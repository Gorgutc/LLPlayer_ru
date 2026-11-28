"""Run the installed subtitle workflow with a CPU recognizer and a loopback provider."""

from contextlib import contextmanager
from functools import partial
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import shutil
import subprocess
from threading import Lock, Thread
from uuid import uuid4
import wave


SOURCE = ("First fixture sentence.", "Second fixture sentence.")
GERMAN = ("Erster Testsatz.", "Zweiter Testsatz.")
FRENCH = ("Première phrase de test.", "Deuxième phrase de test.")
RUSSIAN = ("Первая контрольная фраза.", "Вторая контрольная фраза.")
TIMES = ("00:00:00,100 --> 00:00:00,400", "00:00:00,650 --> 00:00:00,950")


def build_recognizer(fixture, check):
    template = Path(__file__).resolve().with_name("control-fixture")
    source = fixture / "recognizer-source"
    source.mkdir()
    project = source / "LLPlayer.FasterWhisperFixture.csproj"
    shutil.copy2(template / project.name, project)
    shutil.copy2(template / "Program.cs", source / "Program.cs")
    output = fixture / "recognizer"
    build = subprocess.run(
        ["dotnet", "build", str(project), "-c", "Release", "--output", str(output),
         "--nologo", "--verbosity", "quiet", "-warnaserror"],
        capture_output=True, text=True, timeout=90,
    )
    check(build.returncode == 0, f"Could not build the test recognizer: {build.stdout}{build.stderr}")
    suffix = ".exe" if os.name == "nt" else ""
    executable = output / f"LLPlayer.FasterWhisperFixture{suffix}"
    check(executable.is_file(), "The test recognizer apphost is missing")
    return executable


def write_wave(path):
    with wave.open(str(path), "wb") as audio:
        audio.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
        audio.writeframes(b"\x00\x20\x00\xe0" * 16000)


@contextmanager
def translation_provider(before_first_reply=None):
    requests = []
    request_lock = Lock()
    translations = {"DE": dict(zip(SOURCE, GERMAN, strict=True)),
                    "RU": dict(zip(SOURCE, RUSSIAN, strict=True))}

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self):
            content = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            with request_lock:
                requests.append((self.path, content))
                if before_first_reply is not None and len(requests) == 1:
                    before_first_reply()
            translation = translations.get(content.get("target_lang"), {}).get(content.get("text"))
            if self.path != "/translate" or translation is None:
                self.send_error(400)
                return
            response = json.dumps({
                "alternatives": [],
                "data": translation,
                "source_lang": content["source_lang"],
                "target_lang": content["target_lang"],
            }).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(response)))
            self.end_headers()
            self.wfile.write(response)

        def log_message(self, _format, *_args):
            pass

    with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
        thread = Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            yield f"http://127.0.0.1:{server.server_port}", requests
        finally:
            server.shutdown()
            thread.join(timeout=5)


def queue_job(response, check):
    check(not response.get("error") and response.get("jobId"),
          f"Subtitle job submission failed: {(response.get('error') or {}).get('code')}")
    return response["jobId"]


def configure(api, family, fields, wait_for_job, check):
    snapshot = api.operation("settings.read", {"family": family})["value"]
    response = api.operation("settings.apply", {"family": family, "fields": fields},
                             revision=snapshot["revision"])
    return wait_for_job(api, queue_job(response, check), "completed")


def check_srt(path, translations, digest, check):
    expected = "\r\n\r\n".join(
        f"{index}\r\n{timestamp}\r\n{text}"
        for index, (timestamp, text) in enumerate(zip(TIMES, translations, strict=True), 1)
    ).encode("utf-8") + b"\r\n"
    content = path.read_bytes()
    check(content == expected, f"{path.name}: wrong translated SRT cues, times, encoding, or line endings")
    check(hashlib.sha256(content).hexdigest() == digest.lower(),
          f"{path.name}: the durable result does not match the exported bytes")


def verify_provider(api, media, fixture, revision, requests, wait_for_job, check):
    output = fixture / "provider.srt"
    arguments = {
        "files": [{"mediaPath": str(media), "outputPath": str(output), "streamIndex": 0,
                   "sourceRevision": revision}],
        "targetLanguage": "german",
        "sourceLanguage": "en",
        "format": "srt",
        "utf8Bom": False,
        "detectAsrOmissions": False,
    }
    request_id = str(uuid4())
    response = api.operation("workflow.create_subtitles", arguments, request_id=request_id)
    job_id = queue_job(response, check)
    job = wait_for_job(api, job_id, "completed")
    [receipt] = job["value"]
    check(Path(receipt["path"]) == output and receipt["cues"] == len(SOURCE),
          "The provider workflow returned the wrong durable receipt")
    omissions = api.operation("recognition.omissions.read", {"jobId": job_id, "fileIndex": 0})["value"]
    summary = receipt["omissionDetection"]
    check(summary["enabled"] is False and summary["status"] == "disabled" and summary["applied"] is False
          and omissions["omissionDetection"] == summary and not omissions["candidates"],
          "The explicit OFF provider workflow did not retain its per-file omission mode")
    check_srt(output, GERMAN, receipt["sha256"], check)
    replay = api.operation("workflow.create_subtitles", arguments, request_id=request_id)
    check(replay["jobId"] == job_id, "Provider workflow replay allocated another job")
    conflict = api.operation("workflow.create_subtitles", dict(arguments, detectAsrOmissions=True), request_id=request_id)
    check(conflict["error"]["code"] == "request_conflict",
          "Changing the omission mode reused an already admitted workflow request")
    check(sorted((path, request["source_lang"], request["target_lang"], request["text"])
                 for path, request in requests) ==
          sorted(("/translate", "EN", "DE", sentence) for sentence in SOURCE),
          "The configured provider did not receive just the two source cues and the requested target language")


def mcp_operation(client, operation, value, *, writable=False, error=False, request_id=None):
    arguments = {"input": value}
    if writable:
        arguments["requestId"] = request_id or str(uuid4())
    return client.tool("llplayer_" + operation.replace(".", "_"), arguments, error=error)


def verify_harness(api, client, media, fixture, source_revision, wait_for_job, check):
    invoke = partial(mcp_operation, client)
    request_id = str(uuid4())
    arguments = {"mediaPath": str(media), "streamIndex": 0, "externalTargetLanguage": "fr",
                 "sourceRevision": source_revision}
    accepted = invoke("recognition.start", arguments, writable=True, request_id=request_id)
    job_id = queue_job(accepted, check)
    awaiting = wait_for_job(api, job_id, "awaitingTranslation")
    omitted = invoke("recognition.omissions.read", {"jobId": job_id})["value"]
    check(omitted["omissionDetection"]["enabled"] is True and
          omitted["omissionDetection"] == awaiting["value"]["omissionDetection"],
          "The generic MCP did not retain the default-enabled recognition report")
    replay = api.operation("recognition.start", arguments, request_id=request_id)
    check(replay["jobId"] == job_id, "HTTP did not recognize the job submitted by MCP")
    draft = {"jobId": job_id, "revision": awaiting["value"]["revision"]}
    first = invoke("translation.document.read", dict(draft, limit=1))["value"]
    second = invoke("translation.document.read", dict(draft, offset=first["nextOffset"]))["value"]
    cues = first["cues"] + second["cues"]
    check(first["targetLanguage"] == "fr" and first["documentId"] == second["documentId"]
          and [cue["text"] for cue in cues] == list(SOURCE)
          and len({cue["cueId"] for cue in cues}) == len(SOURCE),
          "The MCP draft lost the target, source text, or unique ordered cue IDs")

    premature = invoke("translation.document.commit", draft, writable=True, error=True)
    check(premature["error"]["code"] == "translation_incomplete", "An incomplete external draft was committed")
    translations = [dict(cueId=cue["cueId"], text=text) for cue, text in zip(cues, FRENCH, strict=True)]
    stale = invoke("translation.document.apply", dict(draft, revision="stale", cues=translations[:1]),
                   writable=True, error=True)
    check(stale["error"]["code"] == "revision_conflict", "An external draft accepted a stale source revision")
    submission = dict(draft, cues=translations[:1])
    page_request = str(uuid4())
    first_receipt = invoke("translation.document.apply", submission, writable=True, request_id=page_request)
    retry = invoke("translation.document.apply", submission, writable=True, request_id=page_request)
    check(first_receipt == retry and first_receipt["value"]["receivedCount"] == 1,
          "Replaying the same external cue page changed the persisted draft")
    invoke("translation.document.apply", dict(draft, cues=translations[1:]), writable=True)
    commit_id = str(uuid4())
    committed = invoke("translation.document.commit", draft, writable=True, request_id=commit_id)
    retry = invoke("translation.document.commit", draft, writable=True, request_id=commit_id)
    check(committed == retry, "Replaying an external commit changed its result")
    complete = wait_for_job(api, job_id, "completed")
    version = complete["value"]["revision"]
    check(version != draft["revision"], "The completed external document reused the source revision")
    document = invoke("subtitles.document.read", {"jobId": job_id, "revision": version})["value"]
    check([cue["cueId"] for cue in document["cues"]] == [cue["cueId"] for cue in cues]
          and [(cue["start"], cue["end"]) for cue in document["cues"]] ==
          [(cue["start"], cue["end"]) for cue in cues],
          "Committing external translations changed cue identifiers or timing")

    output = fixture / "harness.srt"
    export = invoke("export.subtitles", {"jobId": job_id, "revision": version, "path": str(output),
                                        "format": "srt", "text": "translatedOnly", "utf8Bom": False},
                    writable=True)
    saved = wait_for_job(api, queue_job(export, check), "completed")
    check_srt(output, FRENCH, saved["value"]["sha256"], check)


def check_range_output(api, job, original_cues, translation, output, wait_for_job, check):
    reference = {"jobId": job["jobId"], "revision": job["value"]["revision"]}
    document = api.operation("subtitles.document.read", reference)["value"]
    expected = [dict(original_cues[0], translatedText=translation), original_cues[1]]
    check(document["cues"] == expected, "Range translation changed cue order, timing, or an unselected cue")
    export = api.operation("export.subtitles", dict(reference, path=str(output), format="srt",
                                                    text="translatedWithFallback", utf8Bom=False))
    saved = wait_for_job(api, queue_job(export, check), "completed")
    check_srt(output, (translation, SOURCE[1]), saved["value"]["sha256"], check)


def verify_ranges(api, client, media, fixture, source_revision, requests, wait_for_job, check):
    recognized = api.operation("recognition.start", {"mediaPath": str(media), "streamIndex": 0,
                                                      "sourceRevision": source_revision})
    source_job = queue_job(recognized, check)
    source = wait_for_job(api, source_job, "completed")
    reference = {"jobId": source_job, "revision": source["value"]["revision"]}
    original = api.operation("subtitles.document.read", reference)["value"]["cues"]
    check([cue["text"] for cue in original] == list(SOURCE) and not any(cue["translatedText"] for cue in original),
          "The ASR source for range translation does not contain the expected untranslated cues")

    invocation = dict(reference, start=0, count=1)
    before = len(requests)
    translated = api.operation("translation.start", dict(invocation, targetLanguage="german", sourceLanguage="en"))
    provider_job = wait_for_job(api, queue_job(translated, check), "completed")
    check(requests[before:] == [("/translate", {"source_lang": "EN", "target_lang": "DE", "text": SOURCE[0]})],
          "The configured provider received more than the selected zero-based range")
    check_range_output(api, provider_job, original, GERMAN[0], fixture / "provider-range.srt", wait_for_job, check)

    invoke = partial(mcp_operation, client)
    begun = invoke("translation.document.begin", dict(invocation, targetLanguage="fr"), writable=True)
    draft_id = queue_job(begun, check)
    awaiting = wait_for_job(api, draft_id, "awaitingTranslation")
    draft = {"jobId": draft_id, "revision": awaiting["value"]["revision"]}
    page = invoke("translation.document.read", draft)["value"]
    check(page["totalCount"] == 1 and page["cues"] == original[:1],
          "MCP exposed an unselected cue in the external translation draft")
    outside = invoke("translation.document.apply", dict(draft, cues=[
        {"cueId": original[1]["cueId"], "text": FRENCH[1]},
    ]), writable=True, error=True)
    check(outside["error"]["code"] == "unknown_cue", "The external draft accepted an unselected cue")
    invoke("translation.document.apply", dict(draft, cues=[
        {"cueId": original[0]["cueId"], "text": FRENCH[0]},
    ]), writable=True)
    invoke("translation.document.commit", draft, writable=True)
    committed = wait_for_job(api, draft_id, "completed")
    check_range_output(api, committed, original, FRENCH[0], fixture / "harness-range.srt", wait_for_job, check)
    check(len(requests) == before + 1, "The external range unexpectedly used the configured provider")


def verify_sidecars(api, media, requests, wait_for_job, check):
    source = media.with_name("Серия.01.wav")
    write_wave(source)
    original_media = source.read_bytes()
    output = source.with_suffix(".ru.srt")
    revision = api.operation("media.probe", {"mediaPath": str(source)})["value"]["sourceRevision"]

    def job(operation, arguments, status, request_id=None):
        response = api.operation(operation, arguments, request_id=request_id)
        return wait_for_job(api, queue_job(response, check), status)

    def rejected(operation, arguments, code):
        failed = job(operation, arguments, "failed")
        check(failed["error"]["code"] == code, f"{operation} did not reject the sidecar with {code}")

    pipeline = {
        "files": [{"mediaPath": str(source), "streamIndex": 0, "sourceRevision": revision}],
        "targetLanguage": "russian", "sourceLanguage": "en", "format": "srt", "utf8Bom": False,
    }
    before = len(requests)
    pipeline_id = str(uuid4())
    durable = job("workflow.create_subtitles", pipeline, "completed", pipeline_id)
    [receipt] = durable["value"]
    check(Path(receipt["path"]) == output and receipt["cues"] == len(SOURCE),
          "The inferred provider destination is not beside the physical source")
    suggestions = api.operation("recognition.omissions.read", {"jobId": durable["jobId"]})["value"]
    check(receipt["omissionDetection"]["enabled"] is True and
          suggestions["omissionDetection"] == receipt["omissionDetection"],
          "The inferred provider workflow lost its default-enabled omission report")
    check_srt(output, RUSSIAN, receipt["sha256"], check)
    replay = api.operation("workflow.create_subtitles", pipeline, request_id=pipeline_id)
    check(replay["jobId"] == durable["jobId"], "Inferred provider output replay did not retain its job")
    check(len(requests) == before + len(SOURCE), "The inferred provider export repeated translation")

    output.write_bytes(b"")
    rejected("workflow.create_subtitles", pipeline, "output_exists")
    rejected("workflow.create_subtitles", dict(pipeline, overwrite=True), "invalid_arguments")
    rejected("workflow.create_subtitles", dict(pipeline, targetLanguage="german"), "invalid_arguments")
    check(output.read_bytes() == b"" and len(requests) == before + len(SOURCE),
          "Inference rejection rewrote an empty sidecar or started another provider translation")

    awaiting = job("recognition.start", {
        "mediaPath": str(source), "streamIndex": 0, "sourceRevision": revision,
        "externalTargetLanguage": "ru",
    }, "awaitingTranslation")
    draft = {"jobId": awaiting["jobId"], "revision": awaiting["value"]["revision"]}
    page = api.operation("translation.document.read", draft)["value"]
    translations = [dict(cueId=cue["cueId"], text=text) for cue, text in zip(page["cues"], RUSSIAN, strict=True)]
    export = dict(draft, format="srt", text="translatedOnly", utf8Bom=False)
    rejected("export.subtitles", export, "translation_incomplete")
    check(not api.operation("translation.document.apply", dict(draft, cues=translations)).get("error"),
          "Preparing the inferred external SRT failed")
    check(not api.operation("translation.document.commit", draft).get("error"),
          "Committing the inferred external SRT failed")
    complete = wait_for_job(api, draft["jobId"], "completed")
    export["revision"] = complete["value"]["revision"]
    rejected("export.subtitles", dict(export, overwrite=True), "invalid_arguments")
    rejected("export.subtitles", dict(export, text="original"), "invalid_arguments")
    rejected("export.subtitles", export, "output_exists")
    check(output.read_bytes() == b"", "The inferred external export replaced an existing empty SRT")
    output.unlink()

    source.write_bytes(original_media + b"changed media")
    rejected("export.subtitles", export, "stale_media")
    check(not output.exists(), "A stale recognition source got an inferred output")
    source.write_bytes(original_media)
    export_id = str(uuid4())
    written = job("export.subtitles", export, "completed", export_id)
    check(Path(written["value"]["path"]) == output, "The inferred external destination is not beside its source")
    check_srt(output, RUSSIAN, written["value"]["sha256"], check)
    replay = api.operation("export.subtitles", export, request_id=export_id)
    check(replay["jobId"] == written["jobId"] and source.read_bytes() == original_media,
          "External sidecar replay allocated another job or changed its source")


def verify_sidecar_source_revalidation(api, media, wait_for_job, check):
    source = media.with_name("sidecar-provider-revalidation.wav")
    write_wave(source)
    replacement = source.read_bytes() + b"media replaced during provider response"
    fingerprint = api.operation("media.probe", {"mediaPath": str(source)})["value"]["sourceRevision"]
    output = source.with_suffix(".ru.srt")
    with translation_provider(lambda: source.write_bytes(replacement)) as (endpoint, requests):
        configure(api, "translation", {"providers.deepLX.endpoint": endpoint}, wait_for_job, check)
        accepted = api.operation("workflow.create_subtitles", {
            "files": [{"mediaPath": str(source), "streamIndex": 0, "sourceRevision": fingerprint}],
            "targetLanguage": "russian", "sourceLanguage": "en",
        })
        failed = wait_for_job(api, queue_job(accepted, check), "failed")
    check(requests and failed["error"]["code"] == "stale_media" and not output.exists(),
          "The inferred provider sidecar was not blocked after the translator saw a replaced source")
    check(source.read_bytes() == replacement, "The sidecar operation interfered with the replaced media")


def make_directory_alias(alias, target):
    if os.name != "nt":
        alias.symlink_to(target, target_is_directory=True)
        return True
    command = ("$ErrorActionPreference = 'Stop'; "
               "New-Item -ItemType Junction -Path $env:LLPLAYER_SIDECAR_ALIAS "
               "-Target $env:LLPLAYER_SIDECAR_TARGET | Out-Null")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", command],
                            env=dict(os.environ, LLPLAYER_SIDECAR_ALIAS=str(alias), LLPLAYER_SIDECAR_TARGET=str(target)),
                            creationflags=subprocess.CREATE_NO_WINDOW, capture_output=True, timeout=15)
    return result.returncode == 0


def remove_directory_alias(alias):
    if os.name == "nt":
        alias.rmdir()
    else:
        alias.unlink()


def verify_sidecar_alias_resume(api, media, wait_for_job, check):
    original = media.with_name("sidecar-resume-original")
    replacement = media.with_name("sidecar-resume-replacement")
    alias = media.with_name("sidecar-resume-alias")
    for directory in (original, replacement):
        directory.mkdir()
        write_wave(directory / "input.wav")

    def retarget(target):
        remove_directory_alias(alias)
        check(make_directory_alias(alias, target), "Could not retarget the isolated sidecar input alias")

    if not make_directory_alias(alias, original):
        print("Sidecar alias resume skipped: Windows could not create the fixture junction.")
        return
    try:
        source = alias / "input.wav"
        args = {
            "files": [{"mediaPath": str(source), "streamIndex": 0}],
            "targetLanguage": "russian", "sourceLanguage": "en", "utf8Bom": False,
        }
        with translation_provider(lambda: retarget(replacement)) as (endpoint, requests):
            configure(api, "translation", {"providers.deepLX.endpoint": endpoint}, wait_for_job, check)
            request_id = str(uuid4())
            accepted = api.operation("workflow.create_subtitles", args, request_id=request_id)
            job_id = queue_job(accepted, check)
            interrupted = wait_for_job(api, job_id, "interrupted")
            check(interrupted["error"]["code"] == "input_changed", "Changed alias was not rejected before publication")
            check(api.operation("workflow.create_subtitles", args, request_id=request_id)["jobId"] == job_id,
                  "Replaying an interrupted sidecar allocated another job")
            body = {"jobId": job_id, "scope": {"profileId": "default"}}
            resume_route = f"/v1/jobs/{job_id}/resume"
            check(queue_job(api.request("POST", resume_route, body), check) == job_id,
                  "Resuming an interrupted sidecar allocated another job")
            resumed = wait_for_job(api, job_id, "interrupted")
            before = original / "input.ru.srt"
            after = replacement / "input.ru.srt"
            check(resumed["error"]["code"] == "input_changed" and not before.exists() and not after.exists(),
                  "Resume after alias retarget relocated the sidecar")

            retarget(original)
            check(queue_job(api.request("POST", resume_route, body), check) == job_id,
                  "Restoring the original alias did not resume the existing job")
            completed = wait_for_job(api, job_id, "completed")
            [receipt] = completed["value"]
            check(Path(receipt["path"]) == before and not after.exists() and len(requests) == len(SOURCE),
                  "Resume failed to reuse the original path and translation checkpoint")
            check_srt(before, RUSSIAN, receipt["sha256"], check)
        print("Sidecar alias resume passed: the relocated input was rejected and original checkpoint recovered.")
    finally:
        if alias.exists():
            remove_directory_alias(alias)


def verify_guarded_physical_source(api, media, wait_for_job, check):
    approved = media.with_name("guarded-source-approved")
    original = media.with_name("guarded-source-original")
    replacement = media.with_name("guarded-source-replacement")
    for directory in (approved, replacement):
        directory.mkdir()
        write_wave(directory / "input.wav")

    source = approved / "input.wav"
    options = {"mediaPath": str(source), "requirePhysicalSource": True}
    probe = api.operation("media.probe", options)
    check(not probe.get("error"), "A guarded probe rejected an unchanged physical source")
    fingerprint = probe["value"]["sourceRevision"]
    approved.rename(original)
    if not make_directory_alias(approved, replacement):
        print("Guarded-source check skipped: Windows could not create the fixture junction.")
        return

    try:
        same_bytes = api.operation("media.probe", dict(options, requirePhysicalSource=False))
        check(not same_bytes.get("error") and same_bytes["value"]["sourceRevision"] == fingerprint,
              "The redirected file must match the approved bytes for this regression")
        job_input = dict(options, streamIndex=0, sourceRevision=fingerprint)
        recognized = api.operation("recognition.start", job_input)
        failed = wait_for_job(api, queue_job(recognized, check), "failed")
        check(failed["error"]["code"] == "source_path_changed",
              "Guarded recognition accepted a redirected source between admission and execution")
        rejected = api.operation("media.probe", options)
        check(rejected["error"]["code"] == "source_path_changed", "A guarded probe accepted a redirected source")
        with translation_provider() as (endpoint, requests):
            configure(api, "translation", {"providers.deepLX.endpoint": endpoint}, wait_for_job, check)
            started = api.operation("workflow.create_subtitles", {
                "files": [job_input], "targetLanguage": "russian", "sourceLanguage": "en",
            })
            failed = wait_for_job(api, queue_job(started, check), "failed")
            check(failed["error"]["code"] == "source_path_changed" and not requests,
                  "Guarded provider workflow translated a source that was redirected before execution")
        check(all(not (directory / "input.ru.srt").exists() for directory in (original, replacement)),
              "The redirected guarded workflow published an SRT")
        print("Guarded-source check passed: same-content aliases were rejected at the host boundary.")
    finally:
        remove_directory_alias(approved)


def verify_pipeline(api, fixture, media, mcp, environment, run_companion, stdio_client, modern,
                    wait_for_job, check):
    recognizer = build_recognizer(fixture, check)
    source = media.with_name("subtitle-workflow.wav")
    write_wave(source)
    probe = api.operation("media.probe", {"mediaPath": str(source)})["value"]
    revision = probe["sourceRevision"]
    configure(api, "recognition", {
        "asrEngine": "fasterWhisper",
        "fasterWhisper.useManualEngine": True,
        "fasterWhisper.manualEnginePath": str(recognizer),
        "fasterWhisper.useManualModel": True,
        "fasterWhisper.manualModelDir": str(fixture),
        "fasterWhisper.model": "tiny.en",
        "fasterWhisper.extraArguments": "--device cpu",
    }, wait_for_job, check)
    with translation_provider() as (endpoint, requests):
        configure(api, "translation", {"sentenceProvider": "DeepLX", "targetLanguage": "french",
                                       "providers.deepLX.endpoint": endpoint},
                  wait_for_job, check)
        verify_provider(api, source, fixture, revision, requests, wait_for_job, check)
        with run_companion(mcp, environment, stdin=subprocess.PIPE, stdout=subprocess.PIPE) as process:
            client = stdio_client(process, modern)
            discover = client.send("server/discover", {})
            check(modern in discover["supportedVersions"], "MCP discovery failed before the subtitle workflow")
            verify_harness(api, client, source, fixture, revision, wait_for_job, check)
            check(len(requests) == len(SOURCE), "The external harness path unexpectedly called the configured provider")
            verify_ranges(api, client, source, fixture, revision, requests, wait_for_job, check)
        verify_sidecars(api, source, requests, wait_for_job, check)
    verify_sidecar_source_revalidation(api, source, wait_for_job, check)
    verify_sidecar_alias_resume(api, source, wait_for_job, check)
    verify_guarded_physical_source(api, source, wait_for_job, check)
    print("Subtitle workflow passed: fixture ASR, HTTP and MCP range translation, draft replay and exact SRT sidecars.")
