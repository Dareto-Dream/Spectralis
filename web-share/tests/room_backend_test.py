"""Exercise a deployed development backend with real HTTP, WebSockets and audio.

python web-share/tests/room_backend_test.py --base-url https://your-staging-backend
Requires Playwright; CHROMIUM_PATH optionally selects its browser. Creates one
temporary test room and ends it in finally. Never uses a real Ward credential.
"""
import argparse
import base64
from datetime import datetime, timezone
import io
import json
import math
import os
import struct
import urllib.error
import urllib.request
from urllib.parse import urlparse
import uuid
import wave
import zipfile

from playwright.sync_api import sync_playwright, expect

parser = argparse.ArgumentParser()
parser.add_argument("--base-url", required=True)
base = parser.parse_args().base_url.rstrip("/")
assert urlparse(base).scheme == "https"
assert urlparse(base).hostname not in ("spectralis-api.deltavdevs.com", "player.deltavdevs.com")


def request(path, method="GET", body=None, headers=None, binary=False):
    data = body if isinstance(body, bytes) else json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base + path, data=data, method=method,
                                 headers={"Content-Type": "application/json", **(headers or {})})
    with urllib.request.urlopen(req, timeout=25) as response:
        result = response.read()
        return result if binary else json.loads(result)


# Check the environment before creating anything.
try:
    request("/player/v1/connect", "POST", {})
    raise AssertionError("Ward must be disabled on the test backend")
except urllib.error.HTTPError as error:
    assert error.code == 403
    assert "temporary rooms" in json.load(error)["error"].lower()

audio_buffer = io.BytesIO()
with wave.open(audio_buffer, "wb") as audio:
    audio.setnchannels(1)
    audio.setsampwidth(2)
    audio.setframerate(16000)
    audio.writeframes(b"".join(struct.pack("<h", int(8000 * math.sin(2 * math.pi * 440 * t / 16000))) for t in range(30 * 16000)))
cover = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=")
package_buffer = io.BytesIO()
with zipfile.ZipFile(package_buffer, "w", compression=zipfile.ZIP_STORED) as package:
    package.writestr("audio/track.wav", audio_buffer.getvalue())
    package.writestr("artwork/cover", cover)
track_id = "dev-smoke-" + uuid.uuid4().hex


def playback(playing=False, position=0):
    return {"trackId": track_id, "isPlaying": playing, "positionSeconds": position,
            "durationSeconds": 30, "hostClockUtc": datetime.now(timezone.utc).isoformat()}


created = request("/shared-play/v2/sessions", "POST", {
    "track": {"displayName": "Development playback check", "artist": "Test tone", "durationSeconds": 30,
              "hasLyrics": True, "lyrics": [{"timeSeconds": 0, "text": "Sound check", "words": [
                  {"timeSeconds": 0, "text": "Sound "}, {"timeSeconds": 1, "text": "check"}]}]},
    "package": {"trackId": track_id, "audioExtension": ".wav"}, "playback": playback()
}, headers={"Authorization": "Bearer obsolete-development-token"})
code = created["roomCode"]
host_headers = {"x-session-key": created["sessionKey"]}
session_path = f"/shared-play/v2/sessions/{code}"
try:
    assert created["temporary"] is True
    assert created["joinUrl"] == base + "/spectralis/web-share/?session=" + code
    request(session_path + "/package", "PUT", package_buffer.getvalue(),
            {**host_headers, "Content-Type": "application/vnd.spectralis.shared-play+zip"})
    manifest = request(session_path)["session"]
    assert manifest["track"]["lyrics"][0]["words"][1]["text"] == "check"
    download = request(urlparse(manifest["packageUrl"]).path, binary=True)
    with zipfile.ZipFile(io.BytesIO(download)) as package:
        assert package.read("audio/track.wav") == audio_buffer.getvalue()
        assert package.read("artwork/cover") == cover
    try:
        request(session_path + "/state", "POST", {"playback": playback(True)})
        raise AssertionError("An anonymous guest changed host playback")
    except urllib.error.HTTPError as error:
        assert error.code in (401, 403)

    with sync_playwright() as p:
        launch = {"headless": True, "args": ["--mute-audio"]}
        if os.environ.get("CHROMIUM_PATH"):
            launch["executable_path"] = os.environ["CHROMIUM_PATH"]
        browser = p.chromium.launch(**launch)
        page = browser.new_page()
        errors = []
        page.on("pageerror", lambda error: errors.append(str(error)))
        # Observe the actual audio graph without changing its routing or playback.
        page.add_init_script("""(() => {
          const Original = window.AudioContext;
          window.AudioContext = class extends Original {
            createAnalyser() { const node = super.createAnalyser(); window.testAnalyser = node; return node; }
          };
        })();""")
        page.goto(created["joinUrl"])
        expect(page.locator("#trackTitle")).to_have_text("Development playback check")
        expect(page.locator("#lyricsTiming")).to_have_text("Word timed")
        expect(page.locator("#statusLine")).to_have_text("Synced.")
        page.wait_for_function("document.querySelector('#discArt').naturalWidth > 0")
        request(session_path + "/state", "POST", {"playback": playback(True)}, host_headers)
        page.locator("#listenButton").click()
        page.wait_for_function("!document.querySelector('#sharedAudio').paused && document.querySelector('#sharedAudio').currentTime > 0.5")
        page.wait_for_function("""() => {
          if (!window.testAnalyser) return false;
          const samples = new Float32Array(testAnalyser.fftSize);
          testAnalyser.getFloatTimeDomainData(samples);
          return samples.some(sample => Math.abs(sample) > 0.01);
        }""")
        # These updates travel through the real server's nested WebSocket envelope.
        request(session_path + "/state", "POST", {"playback": playback(False, 3)}, host_headers)
        page.wait_for_function("document.querySelector('#sharedAudio').paused && Math.abs(document.querySelector('#sharedAudio').currentTime - 3) < 0.4")
        request(session_path + "/state", "POST", {"playback": playback(True, 5)}, host_headers)
        page.wait_for_function("!document.querySelector('#sharedAudio').paused && document.querySelector('#sharedAudio').currentTime >= 5")
        assert not errors, errors
        browser.close()
    print("PASS: Ward disabled; temporary staging invite; anonymous audio/art/lyrics; host authorization; audible audio graph; WebSocket pause/resume.")
finally:
    request(session_path + "/end", "POST", {}, host_headers)
