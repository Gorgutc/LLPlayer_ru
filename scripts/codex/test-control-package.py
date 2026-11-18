"""Exercise the Windows or Linux Avalonia package through HTTP and two MCP stdio clients."""

import argparse
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
import http.client
import json
import os
from pathlib import Path
from queue import Empty, Queue
import shutil
import stat
import subprocess
import sys
import tempfile
import threading
import time
from urllib.parse import urlsplit
from uuid import UUID, uuid4
import wave

sys.dont_write_bytecode = True
import f20_pipeline_fixture


LEGACY = "2025-11-25"
MODERN = "2026-07-28"
CLIENT_INFO = {"name": "llplayer-package-smoke", "version": "1.0"}
TIMEOUT = 25
PROFILE = {"profileId": "default"}
RESTART_PENDING = "restart-pending"
# One concrete operation for each numbered row of the F-20 capability matrix.
MATRIX_OPERATIONS = {
    "media.open": "path",
    "streams.list": "kind",
    "subtitles.read": "slot",
    "recognition.start": "mediaPath",
    "translation.start": "targetLanguage",
    "export.subtitles": "path",
    "batch.scan": "folder",
    "models.list": "kind",
    "words.list": "filter",
    "insights.generate": "jobId",
    "diarization.start": "mediaPath",
    "devices.audio.select": "deviceId",
    "ui.set": "fullscreen",
}
SETTING_FIELDS = {
    "app": "volume",
    "playback": "audioEnabled",
    "recognition": "asrEngine",
    "translation": "targetLanguage",
    "online_video": "ytDlpPath",
    "pdic": "executablePath",
    "batch": "recursive",
    "dubbing": "persistPerLineVoices",
}


class SmokeError(Exception):
    """A packaged application did not satisfy the wire contract."""


def check(condition, message):
    if not condition:
        raise SmokeError(message)


@contextmanager
def cleanup_after(cleanup):
    try:
        yield
    except BaseException:
        try:
            cleanup()
        except Exception as error:
            print(f"Package smoke cleanup: {error}", file=sys.stderr)
        raise
    else:
        cleanup()


def remove_released_fixture(fixture, discovery):
    def metadata(path):
        try:
            return path.lstat()
        except FileNotFoundError:
            return None

    try:
        lock = metadata(discovery.with_name("control.lock"))
        released = (metadata(fixture / RESTART_PENDING) is None and metadata(discovery) is None
                    and (lock is None or (stat.S_ISREG(lock.st_mode) and lock.st_size == 0)))
    except OSError:
        released = False
    check(released, f"Preserved {fixture} for recovery because the control state binding was not confirmed clear")
    check(fixture.resolve(strict=True) == fixture, f"Refusing to remove a replaced fixture directory at {fixture}")
    shutil.rmtree(fixture)


def discovery_path():
    if os.name != "nt":
        return Path.home() / ".local" / "state" / "LLPlayer" / "Control" / "host.json"

    # ControlPaths uses the registered Windows Known Folder for discovery, even with a private
    # LOCALAPPDATA override for the companion's settings and history.
    import ctypes

    folder_id = (ctypes.c_ubyte * 16).from_buffer_copy(
        UUID("f1b32785-6fba-4fcf-9d55-7b8e7f157091").bytes_le
    )
    result = ctypes.c_void_p()
    hr = ctypes.windll.shell32.SHGetKnownFolderPath(ctypes.byref(folder_id), 0, None, ctypes.byref(result))
    check(hr == 0 and result.value, "Windows Local AppData Known Folder is unavailable")
    try:
        return Path(ctypes.wstring_at(result)) / "LLPlayer" / "Control" / "host.json"
    finally:
        ctypes.windll.ole32.CoTaskMemFree(result)


@contextmanager
def run_companion(executable, environment, arguments=(), **streams):
    process = subprocess.Popen(
        [str(executable), *arguments],
        cwd=executable.parent,
        env=environment,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        **streams,
    )
    def close_process():
        if process.poll() is None:
            process.kill()
        process.wait(timeout=5)
        for stream in (process.stdin, process.stdout):
            if stream is not None:
                stream.close()

    with cleanup_after(close_process):
        yield process


class ApiClient:
    def __init__(self, manifest):
        address = urlsplit(manifest["address"])
        check(
            address.scheme == "http" and address.hostname == "127.0.0.1"
            and address.username is None and address.path in ("", "/")
            and not address.query and not address.fragment and address.port,
            "ControlHost did not publish an IPv4 loopback API",
        )
        self.port = address.port
        self.credential = manifest["credential"]
        check(len(self.credential) == 64, "ControlHost did not publish a bearer credential")

    def request(self, method, route, value=None, *, status=200, authorized=True, origin=None):
        headers = {}
        if authorized:
            headers["Authorization"] = f"Bearer {self.credential}"
        if origin is not None:
            headers["Origin"] = origin
        if value is not None:
            headers["Content-Type"] = "application/json"
        connection = http.client.HTTPConnection("127.0.0.1", self.port, timeout=TIMEOUT)
        try:
            connection.request(method, route, json.dumps(value) if value is not None else None, headers)
            response = connection.getresponse()
            check(response.status == status, f"{method} {route}: expected HTTP {status}, received {response.status}")
            body = response.read()
            return json.loads(body) if body else None
        finally:
            connection.close()

    def operation(self, operation, arguments, *, scope=PROFILE, request_id=None, revision=None):
        return self.request("POST", "/v1/operations", {
            "requestId": request_id or str(uuid4()),
            "operationId": operation,
            "scope": scope,
            "expectedRevision": revision,
            "arguments": arguments,
        })


def wait_until(read, message):
    deadline = time.monotonic() + TIMEOUT
    while time.monotonic() < deadline:
        result = read()
        if result is not None:
            return result
        time.sleep(0.1)
    raise SmokeError(message)


def read_manifest(path, process, config, stale_instance=None):
    check(process.poll() is None, f"ControlHost exited before ready (exit {process.returncode})")
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return None
    if manifest["instanceId"] == stale_instance:
        return None
    check(
        manifest["processId"] == process.pid and manifest["apiVersion"] == 1
        and manifest["profileId"] == PROFILE["profileId"]
        and Path(manifest["configDirectory"]).resolve() == config,
        "The discovery file does not belong to the launched package and fixture profile",
    )
    return manifest


def wait_for_job(api, job_id, expected, session_id=None):
    route = f"/v1/jobs/{job_id}"
    if session_id is not None:
        route += f"?sessionId={session_id}"

    def read():
        job = api.request("GET", route)
        if job["status"] == expected:
            return job
        check(job["status"] not in ("completed", "failed", "cancelled", "interrupted", "awaitingCleanup"),
              f"Expected job status {expected}, job ended in {job['status']}")
        return None

    return wait_until(read, f"Job never reached {expected}")


def verify_catalog(api, operations):
    for operation, argument in MATRIX_OPERATIONS.items():
        check(operation in operations, f"Live catalog has no representative for {operation}")
        row = operations[operation]
        argument_schema = row["inputSchema"].get("properties", {}).get(argument)
        result_schema = row["resultSchema"]
        check(isinstance(argument_schema, dict) and argument_schema,
              f"{operation} has no concrete input schema for {argument}")
        check(result_schema.get("properties") or result_schema.get("items"),
              f"{operation} has no concrete result schema")
        check(row["available"] or row["reason"], f"The live catalog does not explain unavailable operation {operation}")

    for family, field in SETTING_FIELDS.items():
        descriptor = api.operation("settings.schema", {"family": family})
        snapshot = api.operation("settings.read", {"family": family})
        check(not descriptor["error"] and not snapshot["error"], f"Settings readers for {family} rejected their own family")
        schema = descriptor["value"]["fields"].get(field, {}).get("schema")
        check(descriptor["value"]["family"] == snapshot["value"]["family"] == family
              and isinstance(schema, dict) and schema and field in snapshot["value"]["requested"]
              and field in snapshot["value"]["effective"], f"Settings owner {family} does not describe and read {field}")

    models = api.operation("models.list", {"kind": "whisper"})
    words = api.operation("words.list", {"limit": 1})
    watcher = api.operation("watch.get", {})
    streams = api.operation("streams.list", {"kind": "audio"})
    check(not models["error"] and models["value"] and all(item.get("id") for item in models["value"]),
          "The packaged model catalog did not return known model IDs")
    check(not words["error"] and words["value"]["words"] == [], "The new profile's word library was not readable")
    check(not watcher["error"] and watcher["value"]["active"] is False, "The new profile's watcher was not idle")
    check(streams["error"]["code"] == "requires_gui", "The headless stream handler did not report its GUI requirement")
    print("Live API inventory passed: 13 capability representatives and eight settings families.")


def verify_http(api, manifest, process, state_directory, config, media):
    identity = api.request("GET", "/v1/identity")
    check(
        identity["apiVersion"] == 1 and identity["instanceId"] == manifest["instanceId"]
        and identity["processId"] == process.pid and Path(identity["stateDirectory"]).resolve() == state_directory,
        "The live identity does not match the test process and state directory",
    )
    api.request("GET", "/v1/identity", status=401, authorized=False)
    api.request("GET", "/v1/identity", status=403, origin="https://llplayer-package-smoke.invalid")
    capabilities = api.request("GET", "/v1/capabilities")
    operations = {row["operation"]["id"]: row for row in capabilities["operations"]}
    check(capabilities["apiVersion"] == 1 and operations["settings.apply"]["available"]
          and operations["media.probe"]["available"], "Required headless capabilities are unavailable")
    check(operations["playback.get"]["reason"] == "requires_gui", "Headless state must report the GUI prerequisite")

    settings = api.operation("settings.read", {"family": "batch"})["value"]
    new_value = not settings["requested"]["recursive"]
    update = {"family": "batch", "fields": {"recursive": new_value}}
    operation_id = str(uuid4())
    second = ApiClient(manifest)
    ready = threading.Barrier(2, timeout=TIMEOUT)

    def submit(client):
        ready.wait()
        return client.operation("settings.apply", update, request_id=operation_id, revision=settings["revision"])

    with ThreadPoolExecutor(max_workers=2) as clients:
        requests = [clients.submit(submit, client) for client in (api, second)]
        first, duplicate = (request.result() for request in requests)
    check(not first["error"] and not duplicate["error"] and first["jobId"] == duplicate["jobId"]
          and first["jobId"], "Concurrent submissions did not share one durable job")
    job_id = first["jobId"]

    job = wait_for_job(second, job_id, "completed")
    check(job["value"]["requested"]["recursive"] == new_value, "The settings job did not apply the new value")
    replay = second.operation("settings.apply", update, request_id=operation_id, revision=settings["revision"])
    check(not replay["error"] and replay["jobId"] == job_id, "A replay did not return the completed job")
    update["fields"]["recursive"] = not new_value
    conflict = api.operation("settings.apply", update, request_id=operation_id, revision=settings["revision"])
    check(conflict["error"]["code"] == "request_conflict", "A changed request reused a durable request ID")
    updated = api.operation("settings.read", {"family": "batch"})["value"]
    check(updated["requested"]["recursive"] == new_value and (config / "LLPlayer.Batch.json").is_file()
          and (state_directory / "Automation" / "jobs.sqlite").is_file(),
          "Batch settings and the job journal were not persisted")
    state = second.request("GET", "/v1/state")
    check(state["instanceId"] == manifest["instanceId"] and state["jobs"] == [job] and not state["sessions"],
          "A second client did not observe the completed job on the same host")

    media.parent.mkdir()
    with wave.open(str(media), "wb") as fixture:
        fixture.setparams((1, 2, 8000, 0, "NONE", "not compressed"))
        fixture.writeframes(bytes(1600))
    probe = api.operation("media.probe", {"mediaPath": str(media)})
    value = probe["value"]
    check(not probe["error"] and value and value["streamIndex"] == 0 and len(value["tracks"]) == 1,
          "Packaged FFmpeg did not probe the generated PCM WAV")
    invalid = api.operation("media.probe", {"mediaPath": 42})
    check(invalid["error"]["code"] == "invalid_arguments", "HTTP accepted an invalid media probe argument")
    verify_catalog(api, operations)
    return probe


def verify_aliased_output(api, media):
    alias = media.parent.with_name("input-alias")
    if os.name == "nt":
        junction = subprocess.run([
            "powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference = 'Stop'; "
            "New-Item -ItemType Junction -Path $env:LLPLAYER_SMOKE_ALIAS "
            "-Target $env:LLPLAYER_SMOKE_INPUT_DIR | Out-Null",
        ], env=dict(os.environ, LLPLAYER_SMOKE_ALIAS=str(alias), LLPLAYER_SMOKE_INPUT_DIR=str(media.parent)),
            creationflags=subprocess.CREATE_NO_WINDOW, capture_output=True, timeout=TIMEOUT)
        if junction.returncode != 0:
            print("Aliased overwrite check skipped: Windows could not create the fixture junction.")
            return
    else:
        alias.symlink_to(media.parent, target_is_directory=True)

    try:
        output = alias / media.name
        check(output.samefile(media), "The overwrite regression did not reach the same file through an alias")
        original = media.read_bytes()
        request = api.operation("workflow.create_subtitles", {
            "files": [{"mediaPath": str(media), "outputPath": str(output), "streamIndex": 0}],
            "targetLanguage": "englishAmerican",
            "overwrite": True,
        })
        check(not request["error"] and request["jobId"], "The aliased output request was not submitted for validation")
        rejected = wait_for_job(api, request["jobId"], "failed")
        check(rejected["error"]["code"] == "output_conflict", "An aliased output passed input-file preflight")
        check(media.read_bytes() == original, "The rejected overwrite changed the input WAV")
        print("Aliased overwrite check passed: the input WAV was preserved.")
    finally:
        if os.name == "nt":
            alias.rmdir()
        else:
            alias.unlink()


class StdioClient:
    def __init__(self, process, version):
        self.process = process
        self.version = version
        self.index = 0
        self.messages = Queue()
        self.reader = threading.Thread(target=self.read_output, daemon=True)
        self.reader.start()

    def read_output(self):
        try:
            for line in self.process.stdout:
                self.messages.put(line)
        finally:
            self.messages.put(None)

    def send(self, method, parameters, *, notification=False):
        request = {"jsonrpc": "2.0", "method": method, "params": dict(parameters)}
        if self.version == MODERN:
            request["params"]["_meta"] = {
                "io.modelcontextprotocol/protocolVersion": self.version,
                "io.modelcontextprotocol/clientInfo": CLIENT_INFO,
                "io.modelcontextprotocol/clientCapabilities": {},
            }
        if not notification:
            self.index += 1
            request["id"] = self.index
        self.process.stdin.write(json.dumps(request).encode("utf-8") + b"\n")
        self.process.stdin.flush()
        if notification:
            return None

        deadline = time.monotonic() + TIMEOUT
        while True:
            try:
                line = self.messages.get(timeout=max(0, deadline - time.monotonic()))
            except Empty as error:
                raise SmokeError(f"MCP {self.version}: no response to {method}") from error
            check(line is not None, f"MCP {self.version}: stdio ended before {method} completed")
            try:
                message = json.loads(line)
            except (UnicodeError, ValueError) as error:
                raise SmokeError("Packaged MCP emitted non-JSON-RPC on stdout") from error
            check(isinstance(message, dict) and message.get("jsonrpc") == "2.0",
                  "Packaged MCP emitted an invalid JSON-RPC envelope")
            if "id" not in message and message.get("method", "").startswith("notifications/"):
                continue
            check(message.get("id") == self.index and "result" in message,
                  f"MCP {self.version}: {method} was rejected or returned the wrong request ID")
            return message["result"]

    def tool(self, name, arguments, *, error=False):
        result = self.send("tools/call", {"name": name, "arguments": arguments})
        check(bool(result.get("isError")) == error, f"MCP {self.version}: unexpected result for {name}")
        check(isinstance(result.get("structuredContent"), dict), f"MCP {name} lacks structured content")
        return result["structuredContent"]


def verify_mcp_input_schema(schema, path):
    """Check the structural JSON Schema subset accepted by the DSH MCP client."""
    allowed = {"type", "oneOf", "properties", "required", "additionalProperties", "items",
               "enum", "const", "description", "title", "default", "examples"}
    check(isinstance(schema, dict), f"{path}: expected a schema object")
    extra = schema.keys() - allowed
    check(not extra, f"{path}: unsupported schema keywords: {', '.join(sorted(extra))}")
    if "oneOf" in schema:
        structural = {"type", "properties", "required", "additionalProperties", "items", "enum", "const"}
        check(not schema.keys() & structural and len(schema["oneOf"]) >= 2, f"{path}: malformed oneOf")
        for index, choice in enumerate(schema["oneOf"]):
            verify_mcp_input_schema(choice, f"{path}.oneOf[{index}]")
        return
    kind = schema.get("type")
    check(kind in (None, "object", "array", "string", "integer", "number", "boolean", "null"),
          f"{path}: expected one scalar type, got {kind!r}")
    scalar_types = {"string": (str,), "integer": (int,), "number": (int, float), "boolean": (bool,), "null": (type(None),)}
    if "enum" in schema:
        check(bool(schema["enum"]) and all(type(item) in scalar_types.get(kind, ()) for item in schema["enum"]),
              f"{path}: enum values must match a declared scalar type")
    if "const" in schema:
        check(type(schema["const"]) in scalar_types.get(kind, ()), f"{path}: const must match a declared scalar type")
    if "additionalProperties" in schema:
        check(kind == "object" and isinstance(schema["additionalProperties"], bool),
              f"{path}: additionalProperties must be boolean")
    properties = schema.get("properties", {})
    check(set(schema.get("required", [])) <= properties.keys(), f"{path}: required names must have property schemas")
    for name, child in properties.items():
        verify_mcp_input_schema(child, f"{path}.{name}")
    if "items" in schema:
        verify_mcp_input_schema(schema["items"], f"{path}.items")


def verify_mcp(executable, environment, version, state, probe, media):
    with run_companion(executable, environment, stdin=subprocess.PIPE, stdout=subprocess.PIPE) as process:
        client = StdioClient(process, version)
        if version == LEGACY:
            result = client.send("initialize", {
                "protocolVersion": version, "capabilities": {}, "clientInfo": CLIENT_INFO,
            })
            check(result["protocolVersion"] == version and result["serverInfo"]["name"] == "LLPlayer",
                  "Legacy initialize did not negotiate LLPlayer MCP 2025-11-25")
            client.send("notifications/initialized", {}, notification=True)
        else:
            result = client.send("server/discover", {})
            check(version in result["supportedVersions"]
                  and result["_meta"]["io.modelcontextprotocol/serverInfo"]["name"] == "LLPlayer",
                  "Modern discovery did not advertise LLPlayer MCP 2026-07-28")
        check("tools" in result["capabilities"], f"MCP {version}: tool capability missing")
        tools = {tool["name"]: tool["inputSchema"] for tool in client.send("tools/list", {})["tools"]}
        expected = {"llplayer_state", "llplayer_media_probe", "llplayer_settings_apply", "llplayer_host_restart"}
        expected.update("llplayer_" + operation.replace(".", "_") for operation in MATRIX_OPERATIONS)
        expected.update(("llplayer_ui_surface_list", "llplayer_ui_surface_open", "llplayer_ui_sidebar_search_focus"))
        check(expected <= tools.keys(),
              f"MCP {version}: tool discovery did not use the active host catalog")
        for name, schema in tools.items():
            check(schema.get("type") == "object", f"MCP {version}: {name} must have an object input root")
            verify_mcp_input_schema(schema, name)
        probe_input = tools["llplayer_media_probe"]["properties"]["input"]
        stream = probe_input["properties"]["streamIndex"]
        check({choice["type"] for choice in stream["oneOf"]} == {"integer", "null"},
              "MCP probe must preserve the nullable stream index")
        export_tool = tools["llplayer_export_subtitles"]
        export_input = export_tool["properties"]["input"]
        format_input = export_input["properties"]["format"]
        check("requestId" in export_tool["required"] and "UUID" in export_tool["properties"]["requestId"]["description"]
              and format_input["type"] == "string" and set(format_input["enum"]) == {"srt", "vtt", "txt"},
              "MCP export must preserve its request ID requirement and format choices")
        surface_open = tools["llplayer_ui_surface_open"]["properties"]["input"]
        surface_input = surface_open["properties"]["surface"]
        check("surface" in surface_open["required"] and surface_input["type"] == "string"
              and {"commandPalette", "debugOverlay"} <= set(surface_input["enum"]),
              "MCP must expose named window surface choices")
        check(all("requestId" in tools[name]["required"] for name in (
                  "llplayer_ui_surface_open", "llplayer_ui_sidebar_search_focus")),
              "MCP window commands must retain the request ID requirement")
        check(client.tool("llplayer_state", {}) == state, f"MCP {version}: state differs from direct HTTP")
        check(client.tool("llplayer_media_probe", {"input": {"mediaPath": str(media)}}) == probe,
              f"MCP {version}: WAV probe differs from direct HTTP")
        invalid = client.tool("llplayer_media_probe", {"input": {"mediaPath": 42}}, error=True)
        check(invalid["error"]["code"] == "invalid_arguments", f"MCP {version}: invalid input was not rejected")


@contextmanager
def attach_callback(api, name):
    session = api.request("POST", "/v1/sessions", dict(PROFILE, name=name))
    session_id = session["sessionId"]
    auth = {"ownerToken": session["ownerToken"]}
    try:
        yield session_id, auth
    finally:
        api.request("DELETE", f"/v1/sessions/{session_id}", auth, status=204)


def verify_attach(api, state):
    with attach_callback(api, "Package smoke callback") as (session_id, auth):
        attached = api.request("GET", "/v1/state")
        check(attached["instanceId"] == state["instanceId"] and attached["jobs"] == state["jobs"]
              and [item["sessionId"] for item in attached["sessions"]] == [session_id],
              "Attaching a callback session did not retain the same background job")
        scope = dict(PROFILE, sessionId=session_id)
        implicit = api.operation("playback.play", {})
        check(implicit["error"]["code"] == "session_required", "A GUI mutation implicitly chose a callback session")

        request_id = str(uuid4())
        queued = api.operation("playback.play", {}, scope=scope, request_id=request_id)
        check(not queued["error"] and queued["jobId"], "The explicit GUI mutation was not accepted")
        job_id = queued["jobId"]
        wait_for_job(api, job_id, "running", session_id)
        api.request("POST", f"/v1/jobs/{job_id}/cancel", {"scope": scope, "jobId": job_id})
        wait_for_job(api, job_id, "cancelled", session_id)

        poll = f"/v1/sessions/{session_id}/poll"
        with ThreadPoolExecutor(max_workers=2) as caller:
            first_id = str(uuid4())
            first = caller.submit(api.operation, "playback.get", {}, scope=scope, request_id=first_id)
            command = api.request("POST", poll, auth)
            check(command["request"]["requestId"] == first_id and not command["cancel"],
                  "The cancelled GUI mutation dispatched before the first live read")
            check(command["request"]["operationId"] == "playback.get"
                  and command["request"]["scope"]["sessionId"] == session_id,
                  "The GUI callback did not receive the scoped playback command")

            second_id = str(uuid4())
            second = caller.submit(api.operation, "playback.get", {}, scope=scope, request_id=second_id)
            next_command = api.request("POST", poll, dict(auth, acknowledgeDeliveryId=command["deliveryId"]))
            check(next_command["request"]["requestId"] == second_id and not next_command["cancel"]
                  and next_command["deliveryId"] != command["deliveryId"], "A delivery ACK did not advance the broker")
            result = {"value": {
                "mediaId": str(uuid4()), "mediaRevision": 1, "opened": False, "playing": False, "opening": False,
                "positionTicks": 0, "durationTicks": 0, "speed": 1, "volume": 50, "maximumVolume": 100, "mute": False,
            }}
            for delivery, invocation in ((command, first), (next_command, second)):
                api.request("POST", f"/v1/sessions/{session_id}/complete",
                            dict(auth, commandId=delivery["commandId"], result=result), status=204)
                check(invocation.result()["value"] == result["value"], "The GUI callback result did not reach the API")

    with attach_callback(api, "Replacement package smoke callback") as (replacement, _):
        check(replacement != session_id, "The replacement callback reused the detached session ID")
        replay = api.operation("playback.play", {}, scope=scope, request_id=request_id)
        check(replay["jobId"] == job_id, "Reattaching changed the previous session's durable request identity")
        stale = api.operation("playback.play", {}, scope=scope)
        stale_job = wait_for_job(api, stale["jobId"], "failed", session_id)
        check(stale_job["error"]["code"] == "session_unavailable", "A stale GUI mutation was rerouted to the new session")

    detached = api.request("GET", "/v1/state")
    check(detached["instanceId"] == state["instanceId"] and not detached["sessions"]
          and all(job in detached["jobs"] for job in state["jobs"]), "Detaching changed the background host or job")


def hold_playback_job(api, session_id):
    scope = dict(PROFILE, sessionId=session_id)
    result = api.operation("playback.play", {}, scope=scope)
    check(not result["error"] and result["jobId"], "The held GUI job was not accepted")
    wait_for_job(api, result["jobId"], "running", session_id)
    return result["jobId"]


def verify_active_state(api):
    with attach_callback(api, "Package smoke active-job callback") as (session_id, auth):
        active_id = hold_playback_job(api, session_id)
        newer_ids = set()
        for index in range(51):
            if index % 10 == 0:
                command = api.request("POST", f"/v1/sessions/{session_id}/poll", auth)
                check(command["request"]["operationId"] == "playback.play" and not command["cancel"],
                      "The callback could not hold the older play job open")
            result = api.operation("settings.apply", {"family": "batch", "fields": {"recursive": index % 2 == 0}})
            check(not result["error"] and result["jobId"], "A newer fixture job was not accepted")
            newer_ids.add(result["jobId"])
            wait_for_job(api, result["jobId"], "completed")
        recent = api.request("GET", "/v1/jobs?limit=50")["items"]
        check(len(newer_ids) == 51 and len(recent) == 50 and all(job["jobId"] in newer_ids for job in recent),
              "The fixture did not push the held job outside the newest 50 durable jobs")
        snapshot = api.request("GET", "/v1/state")["jobs"]
        check(any(job["jobId"] == active_id and job["status"] == "running" for job in snapshot),
              "The state snapshot hid a running job older than the newest 50 durable jobs")
    print("Active-job snapshot check passed after 51 newer jobs.")


@contextmanager
def windows_process(pid, access):
    import ctypes

    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.restype = ctypes.c_void_p
    handle = ctypes.c_void_p(kernel.OpenProcess(access, False, pid))
    check(handle.value, "The autostarted process could not be inspected")
    try:
        yield kernel, handle
    finally:
        kernel.CloseHandle(handle)


def process_image(pid):
    if os.name != "nt":
        return Path(f"/proc/{pid}/exe").resolve(strict=True)

    import ctypes

    with windows_process(pid, 0x1000) as (kernel, handle):  # PROCESS_QUERY_LIMITED_INFORMATION
        image = ctypes.create_unicode_buffer(32768)
        size = ctypes.c_uint(len(image))
        check(kernel.QueryFullProcessImageNameW(handle, 0, image, ctypes.byref(size)),
              "The autostarted process image is unavailable")
        return Path(image.value)


def attest_host(discovery, host, config, state_directory):
    manifest = json.loads(discovery.read_text(encoding="utf-8"))
    pid = manifest["processId"]
    check(type(pid) is int and pid > 0 and manifest["apiVersion"] == 1 and manifest["profileId"] == PROFILE["profileId"]
          and Path(manifest["configDirectory"]).resolve(strict=True) == config,
          "The discovery file does not name the test profile")
    check(process_image(pid).samefile(host), "The discovery file does not name the packaged ControlHost")
    api = ApiClient(manifest)
    identity = api.request("GET", "/v1/identity")
    check(identity["processId"] == pid and identity["instanceId"] == manifest["instanceId"]
          and identity["apiVersion"] == 1 and Path(identity["stateDirectory"]).resolve(strict=True) == state_directory,
          "The ControlHost does not own the fixture state directory")
    return api, identity


def verify_autostart(discovery, host, mcp, config, state_directory, environment, previous_instance):
    check(not os.path.lexists(discovery), "The connect-on-demand fixture requires an idle control profile")
    environment = environment.copy()
    environment.pop("LLPLAYER_CONTROL_HOST", None)
    restart_from = None
    restart_marker = config.parent / RESTART_PENDING

    def replacement_host():
        try:
            published = json.loads(discovery.read_text(encoding="utf-8"))
        except FileNotFoundError:
            return None
        if published["instanceId"] == restart_from["instanceId"]:
            return None
        new_api, identity = attest_host(discovery, host, config, state_directory)
        check(identity["instanceId"] != restart_from["instanceId"] and identity["processId"] != restart_from["processId"],
              "The restart did not replace both the ControlHost PID and instance")
        return new_api, identity

    def stop_owned_host():
        # Stop the attested host before closing the bridge pipes, which it might have inherited.
        if restart_from is None:
            if not os.path.lexists(discovery):
                return
            owned_api, identity = attest_host(discovery, host, config, state_directory)
        else:
            try:
                owned_api, identity = wait_until(replacement_host, "The restarted host was not available for cleanup")
            except SmokeError:
                if not os.path.lexists(discovery):
                    raise
                owned_api, identity = attest_host(discovery, host, config, state_directory)
        pid = identity["processId"]
        if os.name == "nt":
            import ctypes

            # SYNCHRONIZE and PROCESS_QUERY_LIMITED_INFORMATION keep a handle to the exact PID.
            with windows_process(pid, 0x101000) as (kernel, handle):
                owned_api.request("POST", "/v1/stop", status=202)
                check(kernel.WaitForSingleObject(handle, TIMEOUT * 1000) == 0, "The on-demand ControlHost did not exit")
                code = ctypes.c_ulong()
                check(kernel.GetExitCodeProcess(handle, ctypes.byref(code)) and code.value == 0,
                      "The on-demand ControlHost did not exit cleanly")
        else:
            owned_api.request("POST", "/v1/stop", status=202)
            wait_until(lambda: True if not Path(f"/proc/{pid}/exe").exists() else None,
                       "The on-demand ControlHost did not exit")
        check(not os.path.lexists(discovery), "The on-demand ControlHost did not withdraw its discovery file")
        if restart_from is not None and identity["instanceId"] != restart_from["instanceId"]:
            restart_marker.unlink()

    with run_companion(mcp, environment, stdin=subprocess.PIPE, stdout=subprocess.PIPE) as process:
        with cleanup_after(stop_owned_host):
            bridge = StdioClient(process, MODERN)
            bridge.send("server/discover", {})
            state = bridge.tool("llplayer_state", {})
            api, identity = attest_host(discovery, host, config, state_directory)
            check(state["instanceId"] == identity["instanceId"] and identity["instanceId"] != previous_instance,
                  "The bridge did not create a new package host on demand")
            if process.poll() is None:
                process.kill()
            process.wait(timeout=5)
            try:
                end_of_stream = bridge.messages.get(timeout=5)
            except Empty as error:
                raise SmokeError("MCP stdout did not reach EOF while the detached host remained alive") from error
            check(end_of_stream is None, "The disconnected MCP bridge left unexpected stdout data")
            check(api.request("GET", "/v1/identity") == identity,
                  "The on-demand ControlHost did not survive MCP disconnection")
            durable_job = next((job for job in state["jobs"]
                                if job["operationId"] == "settings.apply" and job["status"] == "completed"), None)
            check(durable_job is not None, "The on-demand profile has no completed fixture job to verify after restart")
            # Discovery and the lock can both be temporarily clear while the restart launches.
            restart_marker.touch(exist_ok=False)
            restart_from = identity
            api.request("POST", "/v1/restart", status=202)
            new_api, _ = wait_until(replacement_host, "The restart did not publish a new ControlHost")
            check(new_api.request("GET", f"/v1/jobs/{durable_job['jobId']}") == durable_job,
                  "The restarted ControlHost did not retain the original completed job")
    print("MCP connect-on-demand and host restart passed. Stdout closed and the replacement preserved the job and stopped cleanly.")


def verify_crash_binding(discovery, host, environment):
    check(not os.path.lexists(discovery), "The state-binding fixture requires an idle control profile")
    fixture = Path(tempfile.mkdtemp(prefix="llplayer-package-crash-")).resolve(strict=True)
    config = fixture / "config"
    original = fixture / "original-state"
    foreign = fixture / "different-state"
    lock_file = discovery.with_name("control.lock")
    environment = dict(environment, LLPLAYER_CONFIG_DIR=str(config))
    environment["LOCALAPPDATA" if os.name == "nt" else "XDG_STATE_HOME"] = str(original)
    args = ["--serve", "--config-dir", str(config), "--state-dir"]
    binding_cleared = False
    try:
        with run_companion(host, environment, [*args, str(original)], stdout=subprocess.DEVNULL) as process:
            manifest = wait_until(lambda: read_manifest(discovery, process, config), "The crash fixture did not publish discovery")
            api, identity = attest_host(discovery, host, config, original)
            check(identity["processId"] == process.pid, "Refusing to crash a host other than the directly spawned fixture")
            try:
                session = api.request("POST", "/v1/sessions", dict(PROFILE, name="Package crash callback"))
                session_id = session["sessionId"]
                job_id = hold_playback_job(api, session_id)
                process.kill()
                check(process.wait(timeout=5) != 0, "The crash fixture was expected to terminate without a clean shutdown")
            finally:
                if process.poll() is None:
                    api.request("POST", "/v1/stop", status=202)
                    binding_cleared = (process.wait(timeout=TIMEOUT) == 0 and not os.path.lexists(discovery)
                                       and lock_file.stat().st_size == 0)

        with tempfile.TemporaryFile() as stderr:
            try:
                foreign.mkdir()
                with run_companion(host, environment, [*args, str(foreign)], stdout=subprocess.DEVNULL, stderr=stderr) as wrong:
                    def rejected():
                        if wrong.poll() is not None:
                            return wrong.returncode
                        if os.path.lexists(discovery):
                            new = json.loads(discovery.read_text(encoding="utf-8"))
                            if new["processId"] == wrong.pid:
                                wrong_api, _ = attest_host(discovery, host, config, foreign)
                                wrong_api.request("POST", "/v1/stop", status=202)
                                wrong.wait(timeout=TIMEOUT)
                                raise SmokeError("A different state directory launched after an unclean package host exit")
                        return None

                    code = wait_until(rejected, "The different state directory was not rejected promptly")
                    stderr.seek(0)
                    check(code == 2 and b"[cleanup_pending]" in stderr.read(),
                          "A different state directory did not report cleanup_pending after the crash")
                    check(not (foreign / "Automation").exists(), "The rejected state directory opened its job store")
            finally:
                # Even a failed negative assertion should allow the original owner to clear the binding.
                with run_companion(host, environment, [*args, str(original)], stdout=subprocess.DEVNULL) as recovered:
                    wait_until(lambda: read_manifest(discovery, recovered, config, manifest["instanceId"]),
                               "The original state did not recover its discovery")
                    recovered_api, matching = attest_host(discovery, host, config, original)
                    check(matching["processId"] == recovered.pid and matching["instanceId"] != identity["instanceId"],
                          "The recovered host identity did not match the new process")
                    try:
                        wait_for_job(recovered_api, job_id, "interrupted", session_id)
                    finally:
                        recovered_api.request("POST", "/v1/stop", status=202)
                        check(recovered.wait(timeout=TIMEOUT) == 0, "The recovered ControlHost did not exit cleanly")
                        check(not os.path.lexists(discovery), "The recovered host did not withdraw discovery")
                        check(lock_file.stat().st_size == 0, "The recovered host did not release its retained state binding")
                        binding_cleared = True
        print("State-binding check passed: the different directory was rejected and the original recovered the interrupted job.")
    finally:
        if binding_cleared:
            shutil.rmtree(fixture)
        else:
            print(f"Preserved the state-binding fixture for recovery at {fixture}.")


def smoke(package):
    suffix = ".exe" if os.name == "nt" else ""
    host = package / f"LLPlayer.ControlHost{suffix}"
    mcp = package / "mcp" / f"LLPlayer.Mcp{suffix}"
    check(all(path.is_file() and not path.is_symlink() for path in (host, mcp)), "Missing packaged companion apphost")
    discovery = discovery_path()
    check(not os.path.lexists(discovery), "A discovery file already exists. The package test needs an idle control profile.")

    fixture = Path(tempfile.mkdtemp(prefix="llplayer-package-")).resolve(strict=True)
    with cleanup_after(lambda: remove_released_fixture(fixture, discovery)):
        config = fixture / "config"
        state_root = fixture / "state"
        state_directory = state_root / "LLPlayer"
        media = fixture / "input" / "pcm.wav"
        environment = os.environ.copy()
        for name in ("DISPLAY", "WAYLAND_DISPLAY", "LLPLAYER_FFMPEG_DIR", "LLPLAYER_AUDIO_BACKEND"):
            environment.pop(name, None)
        environment["LLPLAYER_CONFIG_DIR"] = str(config)
        environment["LOCALAPPDATA" if os.name == "nt" else "XDG_STATE_HOME"] = str(state_root)
        # This test owns one apphost directly. A failing bridge must not spawn a detached replacement.
        environment["LLPLAYER_CONTROL_HOST"] = str(fixture / "no-replacement-host")
        args = ["--serve", "--config-dir", str(config), "--state-dir", str(state_directory)]

        with run_companion(host, environment, args, stdout=subprocess.DEVNULL) as process:
            def stop_direct_host():
                if process.poll() is None:
                    owned_api, identity = attest_host(discovery, host, config, state_directory)
                    check(identity["processId"] == process.pid, "Refusing to stop a host other than the directly spawned fixture")
                    owned_api.request("POST", "/v1/stop", status=202)
                    check(process.wait(timeout=TIMEOUT) == 0, "ControlHost did not exit cleanly")

            with cleanup_after(stop_direct_host):
                manifest = wait_until(lambda: read_manifest(discovery, process, config), "ControlHost did not publish discovery")
                api = ApiClient(manifest)
                probe = verify_http(api, manifest, process, state_directory, config, media)
                verify_aliased_output(api, media)
                state = api.request("GET", "/v1/state")
                for version in (LEGACY, MODERN):
                    verify_mcp(mcp, environment, version, state, probe, media)
                verify_attach(api, state)
                f20_pipeline_fixture.verify_pipeline(
                    api, fixture, media, mcp, environment, run_companion, StdioClient, MODERN, wait_for_job, check,
                )
                verify_active_state(api)
            check(not os.path.lexists(discovery), "ControlHost did not withdraw its discovery file")
        verify_autostart(discovery, host, mcp, config, state_directory, environment, manifest["instanceId"])
    verify_crash_binding(discovery, host, environment)
    print("Packaged control smoke passed: headless HTTP, durable settings, MCP legacy and modern, FFmpeg probe, GUI callback.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    args = parser.parse_args()
    try:
        smoke(args.package.resolve(strict=True))
    except (SmokeError, OSError, ValueError, KeyError, subprocess.TimeoutExpired) as error:
        raise SystemExit(f"Packaged control smoke failed: {error}") from None


if __name__ == "__main__":
    main()
