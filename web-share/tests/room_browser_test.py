"""Local integration checks: python web-share/tests/room_browser_test.py.

Requires Python Playwright + Chromium. Optional CHROMIUM_PATH overrides the browser.
All HTTP/WebSocket traffic is intercepted; no live room or account is touched.
"""
import io
import json
import mimetypes
import os
from pathlib import Path
import tempfile
from urllib.parse import urlparse
import wave
import zipfile

from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[1]
BASE = "https://room.test"
buffer = io.BytesIO()
with wave.open(buffer, "wb") as audio:
    audio.setnchannels(1)
    audio.setsampwidth(2)
    audio.setframerate(8000)
    audio.writeframes(b"\0\0" * 8000 * 30)
AUDIO = buffer.getvalue()
# Tiny PNG kept in the test, not a dependency on the local design assets.
import base64
ART = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=")
buffer = io.BytesIO()
with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_STORED) as package:
    package.writestr("audio/track.wav", AUDIO)
    package.writestr("artwork/cover", ART)
PACKAGE = buffer.getvalue()
WORD = [
    {"timeSeconds": 0, "text": "Before the music"},
    {"timeSeconds": 5, "text": "Hello there", "words": [{"timeSeconds": 5, "text": "Hello "}, {"timeSeconds": 6, "text": "there"}]},
    {"timeSeconds": 12, "text": "The next line"},
]
LINE = [{"timeSeconds": 0, "text": "A line at a time"}, {"timeSeconds": 5, "text": "Let the music play"}, {"timeSeconds": 12, "text": "Stay here a while"}]


class Room:
    def __init__(self, page):
        self.page = page
        self.sent = []
        self.errors = []
        self.unexpected = []
        self.track_id = "word"
        self.lines = WORD
        self.role = "follower"
        self.caps = {"queueAdd": "everyone", "queueRemove": "host", "queueReorder": "host", "transport": "host", "voteSkip": True, "skipVotesRequired": 3}
        self.ws = None
        self.playing = False
        self.package = False
        self.accounts = False
        self.channel_requests = 0
        self.queue = {"currentIndex": 0, "items": [{"id": "q1", "title": "Meteor Shower", "artist": "DeltaWave", "sourceKind": "local"}, {"id": "q2", "title": "KATAMARI", "artist": "femtanyl", "sourceKind": "local"}]}
        page.on("pageerror", lambda error: self.errors.append(str(error)))
        page.route("**/*", self.http)
        page.route_web_socket("**/socket**", self.socket)

    def playback(self):
        return {"trackId": self.track_id, "isPlaying": self.playing, "positionSeconds": 6.2, "durationSeconds": 30, "hostClockUtc": "2099-01-01T00:00:00Z"}

    def manifest(self):
        return {"session": {"roomCode": "ABC123", "roomName": "Kitchen disco", "trackId": self.track_id,
                "track": {"displayName": "Meteor Shower", "artist": "DeltaWave", "album": "Room test", "formatName": "WAV", "durationSeconds": 30, "hasLyrics": True, "lyrics": self.lines},
                "browserAudioUrl": None if self.package else BASE + "/audio.wav",
                "packageUrl": BASE + "/track.zip" if self.package else None,
                "albumArtUrl": None if self.package else BASE + "/cover.png",
                "playback": self.playback()}}

    def state_envelope(self):
        # Match write_playback_state and the welcome snapshot in backend/src/collab.rs.
        return {"protocolVersion": "shared-play-v2", "roomCode": "ABC123",
                "trackId": self.track_id, "activeTrackId": self.track_id,
                "playback": self.playback(), "state": self.playback()}

    def http(self, route):
        path = urlparse(route.request.url).path
        if path == "/shared-play/v2/config":
            route.fulfill(json={"accountsEnabled": self.accounts})
        elif path == "/player/v1/me":
            route.fulfill(json={"sub": "isolated-user", "name": "Test listener"})
        elif path == "/shared-play/v2/channels/local-test":
            self.channel_requests += 1
            route.fulfill(json={"channelId": "local-test", "displayName": "Test channel", "isLive": True, "roomCode": "ABC123"})
        elif path.endswith("/sessions/ABC123"):
            route.fulfill(json=self.manifest())
        elif path.endswith("/state"):
            route.fulfill(json=self.state_envelope())
        elif path.endswith("/queue"):
            route.fulfill(json=self.queue)
        elif path.endswith("/presence"):
            route.fulfill(json={"listenerCount": 2})
        elif path.endswith("/reactions"):
            route.fulfill(json={"items": []})
        elif path == "/audio.wav":
            requested = route.request.headers.get("range", "")
            if requested.startswith("bytes="):
                start, end = requested[6:].split("-", 1)
                start, end = int(start or 0), int(end or len(AUDIO) - 1)
                route.fulfill(status=206, body=AUDIO[start:end + 1], content_type="audio/wav",
                              headers={"accept-ranges": "bytes", "content-range": f"bytes {start}-{end}/{len(AUDIO)}", "content-length": str(end - start + 1)})
            else:
                route.fulfill(body=AUDIO, content_type="audio/wav", headers={"accept-ranges": "bytes", "content-length": str(len(AUDIO))})
        elif path == "/cover.png":
            route.fulfill(body=ART, content_type="image/png")
        elif path == "/track.zip":
            route.fulfill(body=PACKAGE, content_type="application/zip")
        else:
            file = ROOT / (path.rsplit("/", 1)[-1] or "index.html")
            if file.is_file():
                route.fulfill(body=file.read_bytes(), content_type=mimetypes.guess_type(file)[0] or "application/octet-stream")
            else:
                self.unexpected.append(path)
                route.fulfill(status=404)

    def socket(self, ws):
        self.ws = ws
        def receive(raw):
            msg = json.loads(raw)
            self.sent.append(msg)
            if msg["t"] == "hello":
                ws.send(json.dumps({"t": "welcome", "v": 2, "you": {"role": self.role}, "caps": self.caps,
                                    "roster": [{"clientId": msg["clientId"], "name": msg["name"], "role": self.role}, {"clientId": "host", "name": "Alex", "role": "host", "isHost": True}],
                                    "queue": self.queue, "state": self.state_envelope()}))
        ws.on_message(receive)

    def send(self, msg):
        self.ws.send(json.dumps({"v": 2, **msg}))

    def switch(self, name, lines, package=False):
        self.track_id, self.lines, self.package = name, lines, package
        self.send({"t": "state", "playback": self.state_envelope()})
        self.page.wait_for_function("document.querySelector('#sharedAudio').readyState >= 1 && document.querySelector('#statusLine').textContent === 'Next track ready.'")


with sync_playwright() as p:
    launch = {"headless": True}
    if os.environ.get("CHROMIUM_PATH"):
        launch["executable_path"] = os.environ["CHROMIUM_PATH"]
    browser = p.chromium.launch(**launch)
    page = browser.new_page(viewport={"width": 1440, "height": 960}, reduced_motion="reduce")
    room = Room(page)
    page.goto(BASE + "/?session=ABC123")
    expect(page.locator("#queueAddForm")).to_be_visible()
    expect(page.locator("#lyricsTiming")).to_have_text("Word timed")
    expect(page.locator(".lyric-word.is-active")).to_have_text("there")
    assert page.locator("#lyricsPanel button, #lyricsPanel a, [data-timing-mode]").count() == 0
    assert page.locator(".eyebrow, nav").count() == 0
    expect(page.locator("#transportPlay")).to_be_hidden()
    # Clicking later lyrics must not seek locally or send a transport command.
    before = page.locator("#sharedAudio").evaluate("a => a.currentTime")
    page.locator(".lyric-line").last.click()
    page.wait_for_timeout(100)
    assert page.locator("#sharedAudio").evaluate("a => a.currentTime") == before
    assert not any(m["t"] == "transport" for m in room.sent)
    assert page.locator("#sharedAudio").evaluate("a => a.paused")
    page.locator("#lyricsPanel").dispatch_event("wheel", {"deltaY": 100})
    expect(page.locator("#followLyrics")).to_be_visible()
    page.locator("#followLyrics").click()
    expect(page.locator("#followLyrics")).to_be_hidden()
    assert page.locator("#sharedAudio").evaluate("a => a.currentTime") == before
    page.locator("#queueUrlInput").fill("https://example.com/song.mp3")
    page.locator("#queueAddForm button").click()
    page.wait_for_timeout(60)
    assert any(m["t"] == "queue.add" and m["item"]["url"] == "https://example.com/song.mp3" for m in room.sent)
    page.locator("#voteSkipButton").click()
    page.locator('[data-reaction="love"]').click()
    page.wait_for_timeout(60)
    assert any(m["t"] == "skip.vote" for m in room.sent)
    assert any(m["t"] == "reaction" for m in room.sent)
    room.switch("line", LINE)
    expect(page.locator("#lyricsTiming")).to_have_text("Line timed")
    assert page.locator(".lyric-word").count() == 0
    expect(page.locator(".lyric-line.is-current")).to_have_text("Let the music play")
    room.switch("missing", [])
    expect(page.locator("#lyricsTab")).to_be_hidden()
    expect(page.locator("#lyricsTiming")).to_be_hidden()
    expect(page.locator("#lyricsPanel")).to_be_hidden()
    expect(page.locator("#followLyrics")).to_be_hidden()
    expect(page.locator("#visualizerPanel")).to_be_visible()
    assert page.locator(".lyric-line").count() == 0
    room.switch("word-again", WORD, package=True)
    expect(page.locator("#lyricsTiming")).to_have_text("Word timed")
    page.wait_for_function("document.querySelector('#discArt').src.startsWith('blob:') && document.querySelector('#discArt').naturalWidth > 0")
    # A role change updates both transport and queue permissions immediately.
    room.caps.update({"queueAdd": "host", "queueRemove": "codj", "queueReorder": "codj", "transport": "codj"})
    room.send({"t": "caps", "caps": room.caps})
    expect(page.locator("#queueAddForm")).to_be_hidden()
    client_id = next(m["clientId"] for m in room.sent if m["t"] == "hello")
    room.send({"t": "roster", "members": [{"clientId": client_id, "name": "Listener", "role": "codj"}]})
    expect(page.locator("#transportPlay")).to_be_visible()
    page.get_by_role("button", name="Move KATAMARI up", exact=True).click()
    page.get_by_role("button", name="Remove KATAMARI", exact=True).click()
    page.locator("#seekTrack").focus()
    page.keyboard.press("ArrowRight")
    page.wait_for_timeout(60)
    assert any(m["t"] == "queue.move" and m["id"] == "q2" and m["toIndex"] == 0 for m in room.sent)
    assert any(m["t"] == "queue.remove" and m["id"] == "q2" for m in room.sent)
    assert any(m["t"] == "transport" and m["action"] == "seek" for m in room.sent)
    # Even authorized controllers cannot seek through lyrics.
    sent_count = len([m for m in room.sent if m["t"] == "transport"])
    page.locator(".lyric-line").last.click()
    page.wait_for_timeout(50)
    assert sent_count == len([m for m in room.sent if m["t"] == "transport"])
    page.locator("#inviteButton").click()
    expect(page.locator("#inviteLink")).to_have_value(BASE + "/spectralis/web-share/?session=ABC123")
    page.keyboard.press("Escape")
    page.locator("#settingsButton").click()
    expect(page.locator("#permissionsSummary")).to_contain_text("co-DJ")
    expect(page.locator("#hostSettings")).to_be_hidden()
    room.role = "codj"
    page.locator("#listenerName").fill("Jamie")
    page.locator("#nameForm button").click()
    page.wait_for_function("localStorage.getItem('spectralisSharedPlayName') === 'Jamie'")
    expect(page.locator("#rosterList")).to_contain_text("Jamie")
    assert any(m["t"] == "hello" and m["name"] == "Jamie" for m in room.sent)
    room.send({"t": "roster", "members": [{"clientId": client_id, "name": "Jamie", "role": "host", "isHost": True}]})
    expect(page.locator("#hostSettings")).to_be_visible()
    page.locator("#queuePermission").select_option("everyone")
    page.wait_for_timeout(60)
    assert any(m["t"] == "caps.set" and m["caps"] == {"queueAdd": "everyone"} for m in room.sent)
    page.keyboard.press("Escape")
    page.locator("#visualizerTab").click()
    page.locator("#fullscreenButton").click()
    page.wait_for_function("document.fullscreenElement === document.querySelector('#visualizerPanel')")
    rect = page.locator("#visualizerPanel").bounding_box()
    assert rect["width"] == 1440 and rect["height"] == 960
    page.evaluate("document.exitFullscreen()")
    page.locator("#lyricsTab").click()
    screenshots = Path(tempfile.gettempdir()) / "spectralis-shared-room-check"
    screenshots.mkdir(exist_ok=True)
    for width in [1440, 1024, 768, 390, 320]:
        page.set_viewport_size({"width": width, "height": 960})
        assert page.evaluate("document.documentElement.scrollWidth <= innerWidth"), width
        page.screenshot(path=str(screenshots / f"room-{width}.png"), full_page=True)
    # Explicit user activation starts audio; normal lyric interaction never did.
    room.playing = True
    room.send({"t": "state", "playback": room.state_envelope()})
    page.locator("#listenButton").click()
    page.wait_for_function("!document.querySelector('#sharedAudio').paused")
    expect(page.locator("#statusLine")).to_have_text("Synced.")
    before = page.locator("#sharedAudio").evaluate("a => a.currentTime")
    page.wait_for_function("before => document.querySelector('#sharedAudio').currentTime > before + 0.1", arg=before)
    # A reconnect must retain playback even when the welcome snapshot is wrapped.
    room.send({"t": "welcome", "state": room.state_envelope()})
    expect(page.locator("#statusLine")).to_have_text("Synced.")
    room.playing = False
    room.send({"t": "state", "playback": room.state_envelope()})
    page.wait_for_function("document.querySelector('#sharedAudio').paused")
    # Older hosts still send a bare playback snapshot.
    room.playing = True
    room.send({"t": "state", "playback": room.playback()})
    page.wait_for_function("!document.querySelector('#sharedAudio').paused")
    room.send({"t": "kicked"})
    expect(page.locator("#lyricsTab")).to_be_hidden()
    expect(page.locator("#queueAddForm")).to_be_hidden()
    expect(page.locator("#transportPlay")).to_be_hidden()
    assert page.locator("#sharedAudio").evaluate("a => a.paused && !a.hasAttribute('src')")
    assert not room.errors, room.errors
    assert not room.unexpected, room.unexpected
    # Account changes reload permissions/media without leaving a permanent channel.
    channel_page = browser.new_page()
    channel = Room(channel_page)
    channel.accounts = True
    channel_page.add_init_script("sessionStorage.setItem('spectralis.room-account:https://room.test', 'isolated-channel-token')")
    channel_page.goto(BASE + "/spectralis/web-share/?channel=local-test")
    channel_page.locator("#libraryButton").click()
    expect(channel_page.locator("#libraryAccount")).to_contain_text("Test listener")
    channel_page.locator("#librarySignOut").click()
    expect(channel_page.locator("#librarySignIn")).to_be_visible()
    expect(channel_page.locator("#channelPanel")).to_be_visible()
    channel_page.wait_for_function("document.querySelector('#sharedAudio').readyState >= 1")
    before = channel.channel_requests
    channel_page.wait_for_timeout(5200)
    assert channel.channel_requests > before, "Signing out stopped the channel from following its host"
    assert not channel.errors, channel.errors
    assert not channel.unexpected, channel.unexpected
    browser.close()
    print("Room integration passed: real page, lyric modes/track changes, no lyric seeking, audio, permissions, queue commands, artwork, dialogs, fullscreen and 5 widths.")
    print(f"Screenshots: {screenshots}")
