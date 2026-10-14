const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const script = readFileSync(require('node:path').join(__dirname, '../Spectralis.App/Assets/untitled-cache.js'), 'utf8');
const source = 'https://untitled.stream/library/track/test-track';
const mediaUrl = 'https://sb.untitled.stream/storage/v1/object/sign/private-transcoded-audio/owner/clip.mp3?token=synthetic';

async function run({ bodyBytes = 70003, expectedBytes = bodyBytes, type = 'audio/mpeg', signedUrl = mediaUrl,
    missingTrack = false, signingFailures = 0 } = {}) {
    const data = Uint8Array.from({ length: bodyBytes }, (_, i) => i % 256);
    const messages = [];
    const calls = [];
    let paused = false;
    const page = {
        __remixContext: { state: { loaderData: { 'routes/library.track.$trackSlug': { track: {
            slug: 'test-track', title: 'captured title', username: 'captured artist', duration: 93.14,
            audio_fallback_url: 'https://sb.untitled.stream/storage/v1/object/private-transcoded-audio/owner/clip.mp3'
        } } } } }
    };
    const trackContext = page.__remixContext;
    if (missingTrack) page.__remixContext = undefined;
    let now = 10000;
    page.chrome = { webview: { postMessage(message) {
        messages.push(message);
        if (message.kind !== 'error' && message.kind !== 'reset')
            queueMicrotask(() => page.spectralisUntitledCacheAck(message.session, message.sequence));
    } } };
    const context = vm.createContext({
        window: page, location: new URL(source), URL, AbortSignal, setTimeout, clearTimeout, Date: { now: () => now },
        btoa, document: { querySelectorAll: () => [{ pause() { paused = true; } }] },
        fetch: async (url, options) => {
            calls.push({ url, options });
            if (String(url).startsWith('/api/storage/')) {
                if (signingFailures-- > 0) return { ok: false, status: 403 };
                return { ok: true, status: 200, json: async () => ({ url: signedUrl }) };
            }
            assert.equal(url, mediaUrl);
            return {
                ok: true, status: 206,
                // HAR storage responses do not expose Content-Range to cross-origin fetch.
                headers: new Headers({ 'content-type': type, 'content-length': String(expectedBytes) }),
                body: new ReadableStream({ start(controller) {
                    controller.enqueue(data.subarray(0, 33333));
                    controller.enqueue(data.subarray(33333));
                    controller.close();
                } })
            };
        }
    });
    vm.runInContext(script, context);
    await page.spectralisCacheUntitled(source, 'synthetic-session');
    return { messages, calls, data, paused,
        completeChallenge: () => { page.__remixContext = trackContext; },
        retry: async () => { now += 4000; vm.runInContext(script, context); await page.spectralisCacheUntitled(source, 'synthetic-session'); }
    };
}

test('follows captured signing flow and transfers every byte with acknowledgements', async () => {
    const { messages, calls, data, paused } = await run();
    assert.equal(calls[0].url,
        '/api/storage/buckets/private-transcoded-audio/objects/owner%2Fclip.mp3/signedUrl?durationInSeconds=10800&cacheBufferInSeconds=600');
    assert.equal(calls[0].options.credentials, 'same-origin');
    assert.equal(calls[1].options.credentials, 'include');
    assert.equal(calls[1].options.headers.Range, 'bytes=0-');
    assert.equal(messages[0].kind, 'reset');
    assert.equal(messages[1].kind, 'begin');
    assert.equal(messages[1].artist, 'captured artist');
    assert.equal(messages.at(-1).kind, 'end');
    assert.equal(messages.at(-1).bytes, data.length);
    const chunks = messages.filter(message => message.kind === 'chunk');
    assert.ok(chunks.every(message => Buffer.from(message.data, 'base64').length <= 24 * 1024));
    assert.deepEqual(Buffer.concat(chunks.map(message => Buffer.from(message.data, 'base64'))), Buffer.from(data));
    assert.ok(messages.slice(1).every((message, index) => message.sequence === index));
    assert.equal(paused, true);
});

test('does not accept a browser-check HTML response as audio', async () => {
    const { messages } = await run({ type: 'text/html' });
    assert.equal(messages.length, 1);
    assert.equal(messages[0].kind, 'error');
});

test('rejects truncated audio rather than handing off partial cache', async () => {
    const { messages } = await run({ expectedBytes: 90000 });
    assert.equal(messages.at(-1).kind, 'error');
    assert.equal(messages.some(message => message.kind === 'end'), false);
});

test('does not fetch a signed URL from another host', async () => {
    const { messages, calls } = await run({ signedUrl: 'https://example.com/audio.mp3' });
    assert.equal(calls.length, 1);
    assert.equal(messages[0].kind, 'error');
});

test('detects captcha completion on a later probe without requiring navigation', async () => {
    const state = await run({ missingTrack: true });
    assert.equal(state.messages.length, 0);
    assert.equal(state.calls.length, 0);
    state.completeChallenge();
    await state.retry();
    assert.equal(state.messages.at(-1).kind, 'end');
    assert.equal(state.messages.at(-1).bytes, state.data.length);
});

test('retries a signing challenge and successfully hands off after clearance', async () => {
    const state = await run({ signingFailures: 1 });
    assert.equal(state.messages[0].kind, 'error');
    assert.equal(state.messages[0].stage, 'signing');
    assert.equal(state.messages[0].httpStatus, 403);
    assert.equal(state.messages[0].retryable, true);
    await state.retry();
    assert.equal(state.messages.at(-1).kind, 'end');
});
