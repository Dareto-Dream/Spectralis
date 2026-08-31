import { evalTrack } from './ease.js';
import { clamp01 } from './math.js';
import { drawVectorLayer, drawBitmapLayer } from './shapes.js';

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

// Only two layer kinds reach this now (bitmap/vector — see types/project.ts)
// so the old per-kind switch collapses to two cases. `getLyricWords`/
// `sections` params are gone: lyrics timing is baked into plain keyframes at
// import time (lib/lyricsImport.ts) instead of read live at render time, so
// there's nothing left here that needs section/lyric-word context.
export function renderLayerAt(ctx, layer, t, W, H, nowMs, beatFlash) {
  if (!evalVisible(layer, t)) return;
  const x = evalTrack(layer.tracks.x, t, layer.statics.x) * (W / 270);
  const y = evalTrack(layer.tracks.y, t, layer.statics.y) * (H / 480);
  const scale = evalTrack(layer.tracks.scale, t, layer.statics.scale);
  const rotation = evalTrack(layer.tracks.rotation, t, layer.statics.rotation);
  const opacity = clamp01(evalTrack(layer.tracks.opacity, t, layer.statics.opacity));
  const hueA = (evalTrack(layer.tracks.hueA, t, layer.statics.hueA, true) + 360) % 360;
  const hueB = (evalTrack(layer.tracks.hueB, t, layer.statics.hueB, true) + 360) % 360;
  const minSide = Math.min(W, H);
  const worldScale = scale * (minSide / 380);
  const p = layer.params;
  if (layer.type === 'vector') {
    ctx.save();
    ctx.translate(x, y);
    ctx.rotate(rotation);
    ctx.scale(worldScale, worldScale);
    drawVectorLayer(ctx, p.shapes, hueA, hueB, opacity, nowMs, beatFlash, t);
    ctx.restore();
  } else if (layer.type === 'bitmap') {
    // Bitmap layers have no authored size — they're the full canvas at
    // scale:1, stretched to the actual W×H (not the minSide/380-normalized
    // worldScale vector shapes use to stay aspect-independent; a full-bleed
    // image should stretch to match the real aspect, not stay "circular").
    ctx.save();
    ctx.translate(x, y);
    ctx.rotate(rotation);
    ctx.scale(scale, scale);
    drawBitmapLayer(ctx, p.dataUrl, W, H, opacity);
    ctx.restore();
  }
}
