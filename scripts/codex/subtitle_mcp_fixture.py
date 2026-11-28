"""Test the subtitle-only MCP catalog and refusals without a ControlHost."""

import hashlib
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys
from tempfile import TemporaryDirectory
from uuid import uuid4

from f20_pipeline_fixture import RUSSIAN, SOURCE, TIMES, queue_job


PREFIX = "llplayer_subtitle_"

def load_fixture():
    spec = importlib.util.spec_from_file_location(
        "control_package", Path(__file__).with_name("test-control-package.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def verify(executable, fixture, version, parent):
    library = parent / "library"
    library.mkdir(exist_ok=True)
    neighbor = parent / "library-other"
    neighbor.mkdir(exist_ok=True)
    media = library / "lesson 1.mp4"
    media.touch()
    outside = neighbor / "outside.mp4"
    outside.touch()
    changed_case = parent / "Library" / media.name
    if os.name != "nt":
        changed_case.parent.mkdir(exist_ok=True)
        changed_case.touch()
    if os.name != "nt" and not (library / "external").is_symlink():
        child = neighbor / "child"
        child.mkdir()
        (child / outside.name).touch()
        (library / "external").symlink_to(neighbor, target_is_directory=True)
        (library / "through").symlink_to(library / "external" / "child", target_is_directory=True)
        (library / "file-link.mp4").symlink_to(outside)
    environment = {
        **os.environ,
        "LLPLAYER_SUBTITLE_ROOT": str(library) + os.sep,
        "LLPLAYER_CONTROL_HOST": str(parent / "no-replacement-host"),
        "LLPLAYER_CONFIG_DIR": str(parent / "subtitles-config"),
    }
    prefix = PREFIX
    known = {prefix + name for name in (
        "media_probe", "recognition_start", "recognition_omissions_read", "translation_document_read",
        "translation_document_apply", "translation_document_commit",
        "export_subtitles", "workflow_create_subtitles", "job_get",
    )}
    identifier = str(uuid4())
    revision = "a" * 64

    with fixture.run_companion(executable, environment, stdin=subprocess.PIPE, stdout=subprocess.PIPE) as process:
        client = fixture.StdioClient(process, version)
        if version == fixture.LEGACY:
            initialization = client.send("initialize", {
                "protocolVersion": version, "capabilities": {}, "clientInfo": fixture.CLIENT_INFO,
            })
            info = initialization["serverInfo"]
            client.send("notifications/initialized", {}, notification=True)
        else:
            initialization = client.send("server/discover", {})
            info = initialization["_meta"]["io.modelcontextprotocol/serverInfo"]
        # This deliberately precedes tools/list. The unrestricted binary must fail without
        # ever consulting ControlHost discovery, even when that host is already running.
        fixture.check(info["name"] == "LLPlayer subtitles", "The subtitle root did not select a distinct MCP profile")
        tools = {tool["name"]: tool for tool in client.send("tools/list", {})["tools"]}
        fixture.check(tools.keys() == known, "The subtitle profile exposed an unexpected tool")
        for name, tool in tools.items():
            schema = tool["inputSchema"]
            fixture.verify_mcp_input_schema(schema, name)
            fixture.check("sessionId" not in schema["properties"], "The subtitle profile exposed a GUI session")
        for name in ("export_subtitles", "workflow_create_subtitles"):
            fields = tools[prefix + name]["inputSchema"]["properties"]["input"]["properties"]
            fixture.check(not ({"path", "outputPath", "overwrite"} & fields.keys()),
                          "An output override was advertised")
        for name in ("media_probe", "recognition_start", "workflow_create_subtitles"):
            fields = tools[prefix + name]["inputSchema"]["properties"]["input"]["properties"]
            if name == "workflow_create_subtitles":
                fields = fields["files"]["items"]["properties"]
            fixture.check("requirePhysicalSource" not in fields, "The physical-source guard was advertised as caller input")
        for name in ("recognition_start", "workflow_create_subtitles"):
            input_schema = tools[prefix + name]["inputSchema"]["properties"]["input"]
            fixture.check("detectAsrOmissions" in input_schema["properties"] and
                          "detectAsrOmissions" not in input_schema.get("required", []),
                          "Omission detection must be optional for a recognition invocation")

        def reject(name, arguments, code):
            reply = client.send("tools/call", {"name": name, "arguments": arguments})
            actual = reply.get("structuredContent", {}).get("error", {}).get("code")
            fixture.check(reply.get("isError") and actual == code, f"{name} should return {code}, got {actual}")

        reject("llplayer_host_stop", {}, "unknown_operation")
        reject("llplayer_export_subtitles", {}, "unknown_operation")
        reject(prefix + "media_probe", {"sessionId": identifier, "input": {"mediaPath": str(media)}}, "invalid_arguments")
        reject(prefix + "media_probe", {"input": {"mediaPath": str(outside)}}, "source_outside_root")
        reject(prefix + "media_probe", {"input": {"mediaPath": str(changed_case)}}, "source_outside_root")
        reject(prefix + "media_probe", {"input": {"mediaPath": str(media), "requirePhysicalSource": False}},
               "invalid_arguments")
        if os.name != "nt":
            for linked in (library / "external" / outside.name, library / "through" / outside.name,
                           library / "file-link.mp4"):
                reject(prefix + "media_probe", {"input": {"mediaPath": str(linked)}}, "source_outside_root")
        reject(prefix + "recognition_start", {"requestId": identifier, "input": {"mediaPath": str(media)}},
               "invalid_arguments")
        reject(prefix + "recognition_start", {"requestId": identifier, "input": {
            "mediaPath": str(media), "sourceRevision": revision, "externalTargetLanguage": "fr",
        }}, "invalid_arguments")
        reject(prefix + "recognition_start", {"requestId": identifier, "input": {
            "mediaPath": str(media), "sourceRevision": revision, "requirePhysicalSource": False,
        }}, "invalid_arguments")
        reject(prefix + "workflow_create_subtitles", {"requestId": identifier, "input": {"files": [{
            "mediaPath": str(media), "sourceRevision": revision, "outputPath": str(outside),
        }]}}, "invalid_arguments")
        reject(prefix + "workflow_create_subtitles", {"requestId": identifier, "input": {"files": [{
            "mediaPath": str(media), "sourceRevision": revision, "requirePhysicalSource": False,
        }]}}, "invalid_arguments")
        reject(prefix + "export_subtitles", {"requestId": identifier, "input": {
            "jobId": identifier, "revision": revision, "path": str(outside), "overwrite": True,
        }}, "invalid_arguments")
        reject(prefix + "job_get", {"jobId": identifier}, "job_not_found")
        reject(prefix + "recognition_omissions_read", {"input": {"jobId": identifier}}, "job_not_found")
        reject(prefix + "translation_document_read", {"input": {"jobId": identifier, "revision": revision}},
               "job_not_found")


def check_scoped_srt(path, digest, check):
    # The scoped bridge uses ControlHost's default UTF-8 BOM.
    body = "\r\n\r\n".join(f"{index}\r\n{timestamp}\r\n{text}" for index, (timestamp, text) in
                             enumerate(zip(TIMES, RUSSIAN, strict=True), 1))
    expected = (body + "\r\n").encode("utf-8-sig")
    content = path.read_bytes()
    check(content == expected, f"{path.name}: wrong translated SRT cues, times, BOM or line endings")
    check(hashlib.sha256(content).hexdigest() == digest.lower(),
          f"{path.name}: the durable result does not match the exported bytes")


def verify_with_host(api, source, executable, environment, run_companion, stdio_client, modern, wait_for_job, check):
    """Exercise the external-translator path on the caller's isolated package fixture."""
    verify_offline(executable, load_fixture())
    library = source.parent / "scoped-subtitle-mcp"
    library.mkdir()
    video = library / "Урок 1. Нож.wav"
    shutil.copyfile(source, video)
    output = video.with_suffix(".ru.srt")
    environment = {**environment, "LLPLAYER_SUBTITLE_ROOT": str(library)}
    request_id = str(uuid4())

    def invoke(client, operation, value, *, writable=False, replay=None):
        arguments = {"input": value}
        if writable:
            arguments["requestId"] = replay or str(uuid4())
        return client.tool(PREFIX + operation, arguments)

    def discover(process):
        client = stdio_client(process, modern)
        result = client.send("server/discover", {})
        check(result["_meta"]["io.modelcontextprotocol/serverInfo"]["name"] == "LLPlayer subtitles",
              "Package MCP did not select the subtitle profile")
        return client

    with run_companion(executable, environment, stdin=subprocess.PIPE, stdout=subprocess.PIPE) as process:
        client = discover(process)
        probe = invoke(client, "media_probe", {"mediaPath": str(video)})["value"]
        input_args = {"mediaPath": str(video), "streamIndex": 0, "sourceRevision": probe["sourceRevision"]}
        accepted = invoke(client, "recognition_start", input_args, writable=True, replay=request_id)
        job_id = queue_job(accepted, check)
        awaiting = wait_for_job(api, job_id, "awaitingTranslation")
        check(client.tool(PREFIX + "job_get", {"jobId": job_id})["value"] == awaiting,
              "The profile did not expose its admitted recognition job")
        summary = awaiting["value"]["omissionDetection"]
        findings = invoke(client, "recognition_omissions_read", {"jobId": job_id})["value"]
        check(summary["enabled"] is True and findings["omissionDetection"] == summary,
              "Omission detection was not enabled by default or the persisted summary disagrees")
        before = api.operation("settings.read", {"family": "recognition"})["value"]
        for enabled in (False, True):
            launched = invoke(client, "recognition_start", dict(input_args, detectAsrOmissions=enabled), writable=True)
            scoped_id = queue_job(launched, check)
            scoped = wait_for_job(api, scoped_id, "awaitingTranslation")["value"]
            candidate_page = invoke(client, "recognition_omissions_read", {"jobId": scoped_id})["value"]
            scoped_summary = scoped["omissionDetection"]
            check(scoped_summary["enabled"] is enabled and candidate_page["omissionDetection"] == scoped_summary,
                  "A recognition launch did not retain the requested omission mode")
            if not enabled:
                check(scoped_summary["status"] == "disabled" and scoped_summary["applied"] is False
                      and scoped_summary["totalCount"] == 0 and not candidate_page["candidates"],
                      "The explicit OFF recognition performed or exposed omission detection")
        check(api.operation("settings.read", {"family": "recognition"})["value"] == before,
              "Job-scoped omission modes changed the common recognition settings")

    with run_companion(executable, environment, stdin=subprocess.PIPE, stdout=subprocess.PIPE) as process:
        client = discover(process)
        denied = client.tool(PREFIX + "job_get", {"jobId": job_id}, error=True)
        check(denied["error"]["code"] == "job_not_found", "A fresh profile exposed a job it did not admit")
        denied = client.tool(PREFIX + "recognition_omissions_read", {"input": {"jobId": job_id}}, error=True)
        check(denied["error"]["code"] == "job_not_found", "A fresh profile exposed suggestions from an unadmitted job")
        replayed = invoke(client, "recognition_start", input_args, writable=True, replay=request_id)
        check(replayed["jobId"] == job_id, "Replaying recognition allocated another underlying job")
        draft = {"jobId": job_id, "revision": awaiting["value"]["revision"]}
        page = invoke(client, "translation_document_read", draft)["value"]
        check(page["targetLanguage"] in ("ru", "russian") and [cue["text"] for cue in page["cues"]] == list(SOURCE),
              "The scoped draft lost the Russian target or source cues")
        translations = [dict(cueId=cue["cueId"], text=text) for cue, text in zip(page["cues"], RUSSIAN, strict=True)]
        invoke(client, "translation_document_apply", dict(draft, cues=translations), writable=True)
        invoke(client, "translation_document_commit", draft, writable=True)
        done = wait_for_job(api, job_id, "completed")
        check(done["value"]["omissionDetection"] == summary and
              invoke(client, "recognition_omissions_read", {"jobId": job_id})["value"] == findings,
              "Committing the source draft lost its separate omission suggestions")
        saved = invoke(client, "export_subtitles", dict(draft, revision=done["value"]["revision"]), writable=True)
        completed = wait_for_job(api, queue_job(saved, check), "completed")
        check(Path(completed["value"]["path"]) == output, "The approved export did not write beside the source")
        check_scoped_srt(output, completed["value"]["sha256"], check)
    print("Scoped subtitle MCP passed: probe, job admission and replay, external translation and nearby SRT.")


def verify_offline(executable, fixture):
    bad_environment = {**os.environ, "LLPLAYER_SUBTITLE_ROOT": "relative", "LLPLAYER_CONTROL_HOST": "no-replacement-host"}
    invalid = subprocess.run([str(executable)], env=bad_environment, stdin=subprocess.DEVNULL,
                             capture_output=True, timeout=fixture.TIMEOUT)
    fixture.check(invalid.returncode == 2 and b"LLPLAYER_SUBTITLE_ROOT" in invalid.stderr and not invalid.stdout,
                  "An invalid subtitle root did not fail closed")
    with TemporaryDirectory(prefix="llplayer-subtitle-mcp-") as temporary:
        directory = Path(temporary).resolve(strict=True)
        for protocol in (fixture.LEGACY, fixture.MODERN):
            verify(executable, fixture, protocol, directory)
    print("PASS: both MCP protocol revisions enforce the offline subtitle-only catalog.")


if __name__ == "__main__":
    verify_offline(Path(sys.argv[1]).resolve(strict=True), load_fixture())
