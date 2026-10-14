// Runs on the real Untitled page so its normal browser session can request playback URLs.
window.spectralisCacheUntitled = async function (sourceUrl, session) {
    if (location.origin !== 'https://untitled.stream' ||
        location.pathname !== new URL(sourceUrl).pathname) return;
    if (window.__untitledCacheSession === session) return;
    if (Date.now() < (window.__untitledCacheRetryAt || 0)) return;
    window.__untitledCacheSession = session;
    let sequence = 0;
    let pending;
    let stage = 'track';
    let httpStatus;
    window.spectralisUntitledCacheAck = function (id, seq) {
        if (id === session && pending && pending.sequence === seq) {
            clearTimeout(pending.timer);
            pending.resolve();
            pending = undefined;
        }
    };
    function send(kind, fields = {}) {
        return new Promise((resolve, reject) => {
            const seq = sequence++;
            pending = { sequence: seq, resolve, timer: setTimeout(() => {
                pending = undefined;
                reject(new Error('Audio cache transfer timed out.'));
            }, 30000) };
            window.chrome.webview.postMessage({ type: 'untitled-cache', session, sequence: seq, kind, ...fields });
        });
    }
    try {
        let track;
        const slug = new URL(sourceUrl).pathname.split('/').filter(Boolean).pop();
        function find(value) {
            if (!value || typeof value !== 'object') return;
            if (value.slug === slug && typeof value.audio_fallback_url === 'string') return value;
            for (const child of Object.values(value)) {
                const match = find(child);
                if (match) return match;
            }
        }
        // The host probes periodically, including after a captcha's document replacement.
        // A challenge page or an unhydrated document is a waiting state, not a failed transfer.
        track = find(window.__remixContext?.state?.loaderData);
        if (!track) {
            window.__untitledCacheSession = undefined;
            return;
        }
        const fallback = new URL(track.audio_fallback_url);
        const prefix = '/storage/v1/object/private-transcoded-audio/';
        if (fallback.origin !== 'https://sb.untitled.stream' || !fallback.pathname.startsWith(prefix))
            throw new Error('Untitled returned an unsupported audio storage path.');
        // Preserve the full storage object path; sign it exactly as the web player does.
        const objectPath = decodeURIComponent(fallback.pathname.slice(prefix.length));
        const signingUrl = '/api/storage/buckets/private-transcoded-audio/objects/' +
            encodeURIComponent(objectPath) + '/signedUrl?durationInSeconds=10800&cacheBufferInSeconds=600';
        stage = 'signing';
        const rights = await fetch(signingUrl, {
            credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
            signal: AbortSignal.timeout(30000)
        });
        httpStatus = rights.status;
        if (!rights.ok) throw new Error('Untitled audio signing returned HTTP ' + rights.status + '.');
        const signed = await rights.json();
        const audioUrl = new URL(signed.url, 'https://sb.untitled.stream');
        if (audioUrl.origin !== 'https://sb.untitled.stream' ||
            !audioUrl.pathname.startsWith('/storage/v1/object/sign/private-transcoded-audio/'))
            throw new Error('Untitled returned an unsupported signed audio URL.');
        stage = 'audio';
        httpStatus = undefined;
        const response = await fetch(audioUrl.href, {
            // Untitled's Audio element uses crossOrigin="use-credentials". The storage
            // request needs the browser's cookies too, including any completed challenge.
            credentials: 'include', headers: { Range: 'bytes=0-' }, signal: AbortSignal.timeout(300000)
        });
        httpStatus = response.status;
        if (!response.ok || !(response.headers.get('content-type') || '').includes('audio/'))
            throw new Error('Untitled audio fetch returned HTTP ' + response.status + ' without audio.');
        // Content-Range isn't CORS-exposed by the storage host in the supplied capture.
        // Check it when available; Content-Length remains readable and checks the transfer.
        const contentRange = response.headers.get('content-range');
        if (response.status === 206 && contentRange) {
            const range = /^bytes 0-(\d+)\/(\d+)$/.exec(contentRange);
            if (!range || Number(range[1]) + 1 !== Number(range[2]))
                throw new Error('Untitled returned an incomplete audio range.');
        }
        const expectedBytes = Number(response.headers.get('content-length')) || undefined;
        stage = 'transfer';
        // A document reload can interrupt an earlier transfer without delivering its error.
        window.chrome.webview.postMessage({ type: 'untitled-cache', session, kind: 'reset' });
        await send('begin', {
            sourceUrl, title: track.title || track.filename || 'Untitled',
            artist: track.display_name || track.username || '', duration: track.duration || 0,
            artworkUrl: track.artwork_url || track.cover_art_url || track.image_url || null,
            expectedBytes
        });
        const reader = response.body.getReader();
        let total = 0;
        try {
            while (true) {
                const { value, done } = await reader.read();
                if (done) break;
                for (let offset = 0; offset < value.length; offset += 24 * 1024) {
                    const part = value.subarray(offset, offset + 24 * 1024);
                    total += part.length;
                    if (total > 256 * 1024 * 1024) throw new Error('Untitled audio exceeds the cache size limit.');
                    await send('chunk', { data: btoa(String.fromCharCode(...part)) });
                }
            }
        } finally { await reader.cancel().catch(() => {}); }
        if (!total || (expectedBytes && total !== expectedBytes)) throw new Error('Untitled audio download was incomplete.');
        document.querySelectorAll('audio,video').forEach(media => media.pause());
        await send('end', { bytes: total });
    } catch (error) {
        if (pending) clearTimeout(pending.timer);
        pending = undefined;
        window.__untitledCacheSession = undefined;
        window.__untitledCacheRetryAt = Date.now() + 3000;
        window.chrome.webview.postMessage({
            type: 'untitled-cache', session, kind: 'error', retryable: true,
            stage, httpStatus, errorName: error.name
        });
    }
};
