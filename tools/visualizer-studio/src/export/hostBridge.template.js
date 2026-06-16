var canvas = document.getElementById('c'), ctx = canvas.getContext('2d');
var coverImg = document.getElementById('cover-img');
function resize() { canvas.width = window.innerWidth; canvas.height = window.innerHeight; }
resize();
window.addEventListener('resize', resize);

// host bridge (identical contract to the hand-authored visualizers)
var latestFrame = null, latestTime = 0, hasRealFrame = false, hasRealTime = false;
var beatFlash = 0, smoothPeak = 0, lastBeatT = -999, nowMs = 0, bootTimeMs = 0;
function queryNumber(name) {
  var q = String(window.location.search || '').replace(/^\?/, '').split('&');
  for (var i = 0; i < q.length; i++) {
    var pr = q[i].split('=');
    if (decodeURIComponent(pr[0] || '') === name) return Number(decodeURIComponent(pr[1] || ''));
  }
  return NaN;
}
var allowPreviewClock = window.location.protocol === 'file:';
var fixedTime = queryNumber('t'), hasFixedTime = Number.isFinite(fixedTime);
var rootEl = document.documentElement;
function readHostState() {
  var cs = getComputedStyle(rootEl);
  function num(n) { var v = parseFloat(cs.getPropertyValue(n)); return isFinite(v) ? v : NaN; }
  return {
    time: num('--audio-time'), peak: num('--audio-peak'), rms: num('--audio-rms'),
    duration: num('--spectral-duration'), active: rootEl.classList.contains('audio-active'),
  };
}
function computeTime(nowRaf, host) {
  if (hasFixedTime) return fixedTime;
  if (isFinite(host.time)) return host.time;
  if (hasRealTime) return latestTime;
  if (allowPreviewClock) return ((nowRaf - bootTimeMs) / 1000) % PROJECT.meta.songEnd;
  return 0;
}

var coverAlpha = 0;
function rafLoop(now) {
  requestAnimationFrame(rafLoop);
  if (!bootTimeMs) bootTimeMs = now;
  nowMs = now;
  var W = canvas.width, H = canvas.height;
  var host = readHostState();
  var t = computeTime(now, host);
  var peak = isFinite(host.peak) ? host.peak : 0;
  var isActive = host.active || (allowPreviewClock && !hasRealFrame);
  var pkD = peak - smoothPeak;
  if (isActive && pkD > 0.14 && peak > 0.26 && t - lastBeatT > 0.2) {
    lastBeatT = t;
    beatFlash = 1.0;
  }
  smoothPeak = smoothPeak * 0.83 + peak * 0.17;
  beatFlash *= 0.9;
  ctx.fillStyle = '#000';
  ctx.fillRect(0, 0, W, H);
  drawSectionWash(ctx, W, H, sectionAt(PROJECT.sections, t));
  var coverTarget = 0.08;
  coverAlpha = lerp(coverAlpha, coverTarget, 0.02);
  if (coverAlpha > 0.01 && coverImg.complete && coverImg.naturalWidth > 0) {
    ctx.save();
    ctx.globalAlpha = coverAlpha * 0.3;
    ctx.filter = 'blur(60px) brightness(0.5)';
    var iw = coverImg.naturalWidth, ih = coverImg.naturalHeight;
    var cs = Math.max(W / iw, H / ih) * 1.15, dw = iw * cs, dh = ih * cs;
    ctx.drawImage(coverImg, (W - dw) / 2, (H - dh) / 2, dw, dh);
    ctx.filter = 'none';
    ctx.restore();
  }
  PROJECT.layers.forEach(function (layer) {
    renderLayerAt(ctx, layer, t, W, H, now, beatFlash, getLyricWords, PROJECT.sections);
  });
  if (beatFlash > 0.3) {
    ctx.fillStyle = 'rgba(255,255,255,' + (beatFlash - 0.3) * 0.05 + ')';
    ctx.fillRect(0, 0, W, H);
  }
  var effectiveEnd = isFinite(host.duration) && host.duration > 0 ? host.duration : PROJECT.meta.songEnd;
  if (t > effectiveEnd - 3) {
    var fa = clamp01((t - (effectiveEnd - 3)) / 3);
    ctx.fillStyle = 'rgba(0,0,0,' + fa + ')';
    ctx.fillRect(0, 0, W, H);
  }
}
function applyFrame(frame) { latestFrame = frame; latestTime = frame.time; hasRealFrame = true; hasRealTime = true; }
if (!window.spectral) window.spectral = {};
window.spectral.onPlaybackFrame = applyFrame;
(function hook() {
  if (window.spectral && typeof window.spectral.on === 'function') { window.spectral.on('frame', applyFrame); }
  else { setTimeout(hook, 100); }
})();
window.onAudioTime = function (t) { latestTime = t; hasRealTime = true; };
window.onSpectralisFrame = applyFrame;
requestAnimationFrame(rafLoop);
