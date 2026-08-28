import { evalTrack } from './ease.js';
import { clamp01 } from './math.js';
import {
  drawOrbShape,
  drawRingShape,
  drawStreakShape,
  drawWheelShape,
  drawShardShape,
  drawAmbientBeamShape,
  drawWordShape,
} from './shapes.js';

export function sectionAt(sections, t) {
  if (!sections || !sections.length) return null;
  for (let i = 0; i < sections.length; i++) {
    if (t < sections[i].end || i === sections.length - 1) return sections[i];
  }
  return sections[0] || null;
}

// Shared by the live preview and the export driver — was two near-identical
// copies in the old tool (studio preview took `ctx` as a param, the export
// driver closed over a module-level `ctx` instead; same math either way).
export function drawSectionWash(ctx, W, H, sec) {
  if (!sec) return;
  const g = ctx.createLinearGradient(0, 0, 0, H);
  g.addColorStop(0, `hsl(${sec.hue},30%,${3 + sec.intensity * 4}%)`);
  g.addColorStop(1, `hsl(${sec.hue2},25%,2%)`);
  ctx.save();
  ctx.globalAlpha = 0.5;
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, W, H);
  ctx.restore();
}

// Show/hide keyframes (Layer.visibleTrack) are "hold" semantics only — no
// easing, a boolean can't tween. Empty/absent track = fall back to the
// static `layer.visible` flag, so old saves render exactly as before.
export function evalVisible(layer, t) {
  const track = layer.visibleTrack;
  if (!track || !track.length) return layer.visible;
  let value = track[0].v;
  for (const kf of track) {
    if (kf.t > t) break;
    value = kf.v;
  }
  return value;
}

// `getLyricWords(layer)` replaces the old hardcoded `layer.params.words` read so the
// same render path works for both the live editor (words live on the layer) and the
// exported capsule driver (words come from a separately embedded LYRIC_WORDS blob).
export function renderLayerAt(ctx, layer, t, W, H, nowMs, beatFlash, getLyricWords, sections) {
  if (!evalVisible(layer, t)) return;
  const x = evalTrack(layer.tracks.x, t, layer.statics.x) * (W / 270);
  const y = evalTrack(layer.tracks.y, t, layer.statics.y) * (H / 480);
  const scale = evalTrack(layer.tracks.scale, t, layer.statics.scale);
  const rotation = evalTrack(layer.tracks.rotation, t, layer.statics.rotation);
  const opacity = clamp01(evalTrack(layer.tracks.opacity, t, layer.statics.opacity));
  const hueA = (evalTrack(layer.tracks.hueA, t, layer.statics.hueA, true) + 360) % 360;
  const hueB = (evalTrack(layer.tracks.hueB, t, layer.statics.hueB, true) + 360) % 360;
  const minSide = Math.min(W, H);
  const p = layer.params;
  switch (layer.type) {
    case 'orb':
      drawOrbShape(ctx, x, y, p.radius * scale * (minSide / 380), hueA, hueB, opacity);
      break;
    case 'ring':
      drawRingShape(ctx, x, y, p.radius * scale * (minSide / 380), hueA, opacity, p.lineWidth);
      break;
    case 'streak':
      drawStreakShape(
        ctx, x, y, rotation,
        p.length * scale * (minSide / 380), p.thickness * scale * (minSide / 380),
        p.mono ? null : hueA, p.mono ? null : hueB, opacity
      );
      break;
    case 'wheel':
      drawWheelShape(
        ctx, x, y, p.radius * scale * (minSide / 380), p.spokes,
        rotation + nowMs * 0.001 * p.spin, hueA, hueB, p.accentIdx, opacity
      );
      break;
    case 'shard':
      drawShardShape(ctx, x, y, p.size * scale * (minSide / 380), rotation + nowMs * 0.001 * p.spin, opacity);
      break;
    case 'ambientBeam':
      drawAmbientBeamShape(ctx, W, y, p.bandHeight * scale * (H / 480), hueA, hueB, opacity);
      break;
    case 'text':
      drawWordShape(
        ctx, p.text, p.fontSize * scale * (minSide / 380), x, y, p.tracking,
        `hsl(${hueA},88%,64%)`, `hsla(${hueA},95%,60%,0.85)`,
        opacity, 1, rotation, 0, 1, 0, layer._seed || (layer._seed = Math.random()), nowMs, beatFlash
      );
      break;
    case 'lyrics':
      renderLyricsLayer(
        ctx, layer, t, x, y, scale, rotation, opacity, hueA, hueB, minSide, nowMs, beatFlash,
        getLyricWords(layer), sections
      );
      break;
  }
}

export function renderLyricsLayer(ctx, layer, t, x, y, scale, rotation, opacity, hueA, hueB, minSide, nowMs, beatFlash, words, sections) {
  words = words || [];
  if (!words.length) return;
  const sec = sectionAt(sections, t);
  if (layer.params.suppressOnNoLyricsSections && sec && sec.noLyrics) return;
  let idx = -1;
  for (let i = 0; i < words.length; i++) {
    if (words[i].time <= t) idx = i;
    else break;
  }
  if (idx < 0) return;
  const w = words[idx];
  const lineAge = t - w.time;
  const dur = w.endTime - w.time;
  const fadeIn = Math.min(1, lineAge / 0.1);
  const fadeOut = lineAge > dur - 0.12 ? Math.max(0, 1 - (lineAge - (dur - 0.12)) / 0.12) : 1;
  const alpha = clamp01(fadeIn * fadeOut) * opacity;
  if (alpha <= 0.01) return;
  const fs = layer.params.fontSize * scale * (minSide / 380);
  const color = w.isKey ? '#fff6dd' : `hsl(${hueA},88%,64%)`;
  const glow = w.isKey ? 'rgba(255,205,110,0.95)' : `hsla(${hueA},95%,60%,0.85)`;
  drawWordShape(ctx, w.text.toUpperCase(), fs, x, y, fs * 0.04, color, glow, alpha, 1, rotation, 0.4, 1, 0, w.seed, nowMs, beatFlash);
}
