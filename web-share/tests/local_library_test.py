"""Real IndexedDB checks for account isolation, v1 recovery, deduplication and history."""
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

source = Path(__file__).resolve().parents[1] / "local-library.js"
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, **({"executable_path": os.environ["CHROMIUM_PATH"]} if os.environ.get("CHROMIUM_PATH") else {}))
    page = browser.new_page()
    page.route("**/*", lambda route: route.fulfill(content_type="text/html", body="<!doctype html><title>Isolated library test</title>"))
    page.goto("https://library.test/")
    page.evaluate("""() => new Promise((resolve, reject) => {
        const request = indexedDB.open('spectralis-local-audio', 1);
        request.onupgradeneeded = () => { request.result.createObjectStore('tracks', {keyPath:'id'}); request.result.createObjectStore('files'); };
        request.onsuccess = () => {
            const db = request.result, tx = db.transaction(['tracks','files'],'readwrite');
            tx.objectStore('tracks').put({id:'legacy',name:'old.wav',title:'Old favourite',size:3,addedAt:1});
            tx.objectStore('files').put(new File(['old'],'old.wav'), 'legacy');
            tx.oncomplete = () => { db.close(); resolve(); }; tx.onerror = reject;
        }; request.onerror = reject;
    })""")
    page.add_script_tag(content=source.read_text(encoding="utf-8"))
    page.evaluate("""async () => {
        const assert = (ok, message) => { if (!ok) throw new Error(message); };
        const a = LocalAudioLibrary.forAccount('a'), b = LocalAudioLibrary.forAccount('b');
        assert(!(await a.list()).length && !(await b.list()).length, 'legacy files must stay hidden');
        assert(await a.hasLegacy(), 'legacy recovery available');
        await a.recover();
        const old = (await a.list())[0];
        assert(await (await a.file(old.id)).text() === 'old', 'recovery preserves Blob');
        assert(!(await b.hasLegacy()) && !(await b.list()).length, 'recovered files have one owner');
        await b.remove(old.id);
        assert(await a.file(old.id), 'another account cannot remove a file');
        assert(!await b.file(old.id), 'another account cannot read a file');
        const file = new File(['audio bytes'], 'new.wav');
        await Promise.all([a.add(file), a.add(file)]);
        assert((await a.list()).length === 2, 'concurrent duplicate imports deduplicate');
        const saved = await a.saveLink('https://example.com/song', 'Song');
        await a.saveLink('https://example.com/song', 'Song renamed');
        assert((await a.list()).length === 3, 'saved links deduplicate');
        for (let i=0;i<53;i++) await a.remember(LocalAudioLibrary.link('https://example.com/'+i));
        assert((await a.recent()).length === 50, 'history is bounded');
        await a.remember(saved); await a.remember(saved);
        assert((await a.recent()).filter(t=>t.url===saved.url).length === 1, 'repeated picks deduplicate');
        assert(!(await b.recent()).length, 'history isolated');
        await a.remember(old); await a.remove(old.id);
        assert(!(await a.recent()).some(t=>t.id===old.id), 'removed files cannot be requeued from history');
        await a.clearRecent();
        assert(!(await a.recent()).length && (await a.list()).length === 2, 'clearing history keeps library');
        let rejected = false;
        try { await a.saveLink('javascript:alert(1)'); } catch { rejected = true; }
        assert(rejected, 'unsafe link schemes rejected');
    }""")
    page.reload()
    page.add_script_tag(content=source.read_text(encoding="utf-8"))
    assert page.evaluate("async () => (await LocalAudioLibrary.forAccount('a').list()).length") == 2
    browser.close()
print("Library storage passed: v1 recovery, account isolation, Blob persistence, concurrent deduplication, saved links, bounded history and safe URLs.")
