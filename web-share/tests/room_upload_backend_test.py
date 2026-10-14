"""Real local backend + isolated Ward stub + Chromium. No live account or server is used.

Build with cargo build --manifest-path backend/Cargo.toml, then run this script.
CHROMIUM_PATH may override Playwright's bundled Chromium.
"""
import io
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import Request, urlopen
from urllib.error import HTTPError
import wave
import zipfile
from playwright.sync_api import sync_playwright, expect

REPO = Path(__file__).resolve().parents[2]
TOKEN = "isolated-test-listener"

class WardStub(BaseHTTPRequestHandler):
    def do_GET(self):
        valid = self.path == "/oauth/userinfo" and self.headers.get("Authorization") == "Bearer " + TOKEN
        self.send_response(200 if valid else 401)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps({"sub": "test-user", "name": "Test listener"} if valid else {"error": "Invalid test account"}).encode())
    def log_message(self, *_): pass

ward = ThreadingHTTPServer(("127.0.0.1", 0), WardStub)
threading.Thread(target=ward.serve_forever, daemon=True).start()
with socket.socket() as free:
    free.bind(("127.0.0.1", 0)); port = free.getsockname()[1]
base = f"http://127.0.0.1:{port}"
env = {**os.environ, "PORT": str(port), "SPECTRALIS_BACKEND_HOST": "127.0.0.1", "SPECTRALIS_ENVIRONMENT": "production",
       "SPECTRALIS_PUBLIC_BASE_URL": base, "SPECTRALIS_WEB_SHARE_ROOT": str(REPO / "web-share"), "WARD_ISSUER": f"http://127.0.0.1:{ward.server_port}"}
env.pop("REDIS_URL", None)
binary = REPO / "backend/target/debug" / ("spectralis-backend.exe" if os.name == "nt" else "spectralis-backend")
audio = io.BytesIO()
with wave.open(audio, "wb") as wav:
    wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(8000); wav.writeframes(b"\0\0" * 8000 * 2)
audio = audio.getvalue()
package = io.BytesIO()
with zipfile.ZipFile(package, "w") as archive: archive.writestr("audio/track.wav", audio)

def request(path, method="GET", body=None, headers=None, binary=False):
    raw = body if isinstance(body, bytes) else json.dumps(body).encode() if body is not None else None
    req = Request(base + path, method=method, data=raw, headers={"Content-Type": "application/json", **(headers or {})})
    with urlopen(req, timeout=15) as response:
        data = response.read()
        return data if binary else json.loads(data)

def rejected(path, method, body, headers, status):
    try: request(path, method, body, headers)
    except HTTPError as error: assert error.code == status, (error.code, error.read()); return
    raise AssertionError("Expected request to be rejected")

with tempfile.TemporaryFile() as log:
    process = subprocess.Popen([str(binary)], cwd=REPO, env=env, stdout=log, stderr=log,
                               creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    try:
        for _ in range(100):
            try: request("/health"); break
            except OSError: time.sleep(.1)
        else: raise RuntimeError("Test backend did not start")
        assert request("/shared-play/v2/config")["accountsEnabled"]
        opted_out = request("/shared-play/v2/sessions", "POST", {"roomCapabilities":{"audioUploads":False}})
        opted_path = "/shared-play/v2/sessions/" + opted_out["roomCode"]
        rejected(opted_path+"/uploads", "POST", b"", {"Authorization":"Bearer "+TOKEN},403)
        request(opted_path+"/end", "POST", {}, {"x-session-key":opted_out["sessionKey"]})
        created = request("/shared-play/v2/sessions", "POST", {"track":{"displayName":"Local room check","durationSeconds":2},"package":{"trackId":"test","audioExtension":".wav"},"playback":{"isPlaying":False,"durationSeconds":2}})
        code = created["roomCode"]; path = f"/shared-play/v2/sessions/{code}"; host = {"x-session-key":created["sessionKey"]}
        request(path+"/package", "PUT", package.getvalue(), host)
        rejected(path+"/uploads", "POST", b"", {}, 401)
        rejected(path+"/uploads", "POST", b"", host, 401)
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True, **({"executable_path":os.environ["CHROMIUM_PATH"]} if os.environ.get("CHROMIUM_PATH") else {}))
            page = browser.new_page(viewport={"width":1440,"height":1000})
            page_errors = []; uploads = []
            page.on("pageerror", lambda error: page_errors.append(str(error)))
            page.on("request", lambda req: uploads.append(req) if req.url.endswith("/uploads") and req.method == "POST" else None)
            page.goto(base+"/spectralis/web-share/?session="+code)
            expect(page.locator("#openAppButton")).to_be_visible()
            page.locator("#libraryButton").click()
            page.locator("#libraryFiles").set_input_files({"name":"A song.wav","mimeType":"audio/wav","buffer":audio})
            expect(page.locator("#libraryList strong")).to_have_text("A song")
            expect(page.get_by_role("button",name="Add to queue",exact=True)).to_be_disabled()
            assert not uploads, "Import must not upload audio"
            # Duplicate import is a single local track, and the Blob survives a page reload.
            page.locator("#libraryFiles").set_input_files({"name":"A song.wav","mimeType":"audio/wav","buffer":audio})
            expect(page.locator("#libraryStatus")).to_contain_text("Nothing was uploaded")
            expect(page.locator("#libraryList strong")).to_have_count(1)
            # Exercise the real device-connect API; only the account site's page is a local fixture.
            page.context.route("https://player.deltavdevs.com/connect/**", lambda route: route.fulfill(content_type="text/html",body="<h1>Isolated test account approval</h1>"))
            with page.expect_popup() as opened:
                page.get_by_role("button",name="Sign in with Ward",exact=True).click()
            popup = opened.value
            popup.wait_for_url("https://player.deltavdevs.com/connect/*")
            request("/player/v1/connect/"+popup.url.rsplit("/",1)[1]+"/approve","POST",{}, {"Authorization":"Bearer "+TOKEN})
            expect(page.locator("#libraryAccount")).to_contain_text("Test listener")
            popup.close()
            page.reload(); page.locator("#libraryButton").click()
            expect(page.locator("#libraryAccount")).to_contain_text("Test listener")
            expect(page.locator("#libraryList strong")).to_have_count(1)
            assert not uploads
            page.get_by_role("button",name="Add to queue",exact=True).click()
            expect(page.locator("#libraryStatus")).to_contain_text("was added to the queue")
            assert len(uploads) == 1
            assert uploads[0].headers["authorization"].startswith("Bearer sp_app_")
            queued = request(path+"/queue")["items"]; assert len(queued) == 1
            item = queued[0]; assert item["addedBy"] == "Test listener" and item["sourceKind"] == "upload"
            source_path = item["url"].removeprefix(base)
            rejected(source_path,"GET",None,{},403)
            assert request(source_path,headers=host,binary=True) == audio
            request(path+f"/uploads/{item['id']}/received","POST",{},host)
            rejected(source_path,"GET",None,host,404)
            # The host's actual socket permission reaches the UI and the HTTP guard.
            page.evaluate("""({url,key}) => new Promise((resolve,reject) => {
                const ws = window.testHost = new WebSocket(url);
                ws.onopen=()=>ws.send(JSON.stringify({v:2,t:'hello',proto:2,role:'host',key,clientId:'test-host',name:'Host'}));
                ws.onmessage=e=>{if(JSON.parse(e.data).t==='welcome')resolve()};ws.onerror=reject;
            })""", {"url":base.replace("http:","ws:")+path+"/socket","key":created["sessionKey"]})
            page.evaluate("testHost.send(JSON.stringify({v:2,t:'caps.set',caps:{audioUploads:false}}))")
            expect(page.get_by_role("button",name="Add to queue",exact=True)).to_be_disabled()
            rejected(path+"/uploads","POST",b"",{"Authorization":"Bearer "+TOKEN},403)
            rejected(path+"/queue/items/"+item["id"],"DELETE",None,{"Authorization":"Bearer "+TOKEN},403)
            # Removing the received request does not remove its browser-library original.
            request(path+"/queue/items/"+item["id"],"DELETE",None,host)
            assert not request(path+"/queue")["items"]
            assert page.evaluate("async () => (await LocalAudioLibrary.file((await LocalAudioLibrary.list())[0].id)).size") == len(audio)
            snapshots = Path(tempfile.gettempdir())/"spectralis-upload-check"; snapshots.mkdir(exist_ok=True)
            for width in [1440,390]:
                page.set_viewport_size({"width":width,"height":900}); page.screenshot(path=str(snapshots/f"library-{width}.png"))
                assert page.evaluate("document.querySelector('#libraryDialog').scrollWidth <= document.querySelector('#libraryDialog').clientWidth+1")
            page.get_by_role("button",name="Remove",exact=True).click()
            expect(page.locator("#libraryList strong")).to_have_count(0)
            assert not page_errors, page_errors
            browser.close()
        request(path+"/end","POST",{},host)
        print("Upload integration passed: guest rejection, browser-only import/dedup/reload, verified account upload, host-only receipt/deletion, live opt-out, queue removal, local file deletion and responsive UI.")
        print("Screenshots:",snapshots)
    except Exception:
        log.seek(0); print(log.read().decode(errors="replace")[-5000:]); raise
    finally:
        process.terminate(); process.wait(timeout=10); ward.shutdown(); ward.server_close()
