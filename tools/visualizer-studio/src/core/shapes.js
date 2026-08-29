import { clamp01 } from './math.js';
import { hash, seeded } from './hash.js';

// The old per-kind procedural draw functions (drawOrbShape/drawRingShape/
// drawStreakShape/drawWheelShape/drawShardShape/drawAmbientBeamShape) lived
// here before the bitmap/vector rework — they're gone now that no runtime
// layer type reaches them (see lib/vectorize.ts, which reuses their geometry/
// gradient math to produce VectorShape[] DATA once, at migration/template-
// build time, instead of calling into canvas-drawing code every frame).

// '$hueA'/'$hueB' resolve against the layer's own resolved hue tracks;
// anything else is a literal CSS color string handed straight through.
function resolveColor(c, hueA, hueB) {
  if (c === '$hueA') return `hsl(${hueA},85%,60%)`;
  if (c === '$hueB') return `hsl(${hueB},85%,60%)`;
  return c;
}

// Exported (not just an internal draw-time helper) — the Workspace canvas's
// selection bounding box (lib/vectorHitTest.ts) reuses this exact geometry so
// the drawn selection outline always matches what actually gets painted.
export function boundsOf(shape) {
  switch (shape.kind) {
    case 'rect':
      return { cx: shape.x + shape.w / 2, cy: shape.y + shape.h / 2, w: shape.w, h: shape.h };
    case 'ellipse':
      return { cx: shape.x, cy: shape.y, w: shape.rx * 2, h: shape.ry * 2 };
    case 'line':
      return { cx: (shape.x1 + shape.x2) / 2, cy: (shape.y1 + shape.y2) / 2, w: Math.abs(shape.x2 - shape.x1), h: Math.abs(shape.y2 - shape.y1) };
    case 'polygon':
      return { cx: shape.x, cy: shape.y, w: shape.radius * 2, h: shape.radius * 2 };
    case 'text':
      return { cx: shape.x, cy: shape.y, w: shape.fontSize * 4, h: shape.fontSize };
    case 'path': {
      if (!shape.points.length) return { cx: 0, cy: 0, w: 0, h: 0 };
      let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
      for (const p of shape.points) {
        minX = Math.min(minX, p.x);
        minY = Math.min(minY, p.y);
        maxX = Math.max(maxX, p.x);
        maxY = Math.max(maxY, p.y);
      }
      return { cx: (minX + maxX) / 2, cy: (minY + maxY) / 2, w: maxX - minX, h: maxY - minY };
    }
    default:
      return { cx: 0, cy: 0, w: 0, h: 0 };
  }
}

// Builds fillStyle/strokeStyle-ready value: a plain resolved color string for
// a flat fill, or a live CanvasGradient for radial/linear (needs `ctx` since
// gradients are context-bound objects, unlike plain color strings).
function paintFor(ctx, fill, bounds, hueA, hueB) {
  if (fill.kind === 'flat') return resolveColor(fill.color, hueA, hueB);
  let grad;
  if (fill.kind === 'radial') {
    const r = Math.max(bounds.w, bounds.h) / 2 || 1;
    grad = ctx.createRadialGradient(bounds.cx, bounds.cy, 0, bounds.cx, bounds.cy, r);
  } else {
    const dx = (Math.cos(fill.angle || 0) * bounds.w) / 2;
    const dy = (Math.sin(fill.angle || 0) * bounds.h) / 2;
    grad = ctx.createLinearGradient(bounds.cx - dx, bounds.cy - dy, bounds.cx + dx, bounds.cy + dy);
  }
  for (const s of fill.stops) grad.addColorStop(clamp01(s.offset), resolveColor(s.color, hueA, hueB));
  return grad;
}

function tracePath(ctx, shape) {
  switch (shape.kind) {
    case 'rect':
      ctx.beginPath();
      if (shape.rotation) {
        const cx = shape.x + shape.w / 2, cy = shape.y + shape.h / 2;
        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate(shape.rotation);
        ctx.rect(-shape.w / 2, -shape.h / 2, shape.w, shape.h);
        ctx.restore();
      } else {
        ctx.rect(shape.x, shape.y, shape.w, shape.h);
      }
      return;
    case 'ellipse':
      ctx.beginPath();
      ctx.ellipse(shape.x, shape.y, Math.max(0, shape.rx), Math.max(0, shape.ry), shape.rotation || 0, 0, Math.PI * 2);
      return;
    case 'line':
      ctx.beginPath();
      ctx.moveTo(shape.x1, shape.y1);
      ctx.lineTo(shape.x2, shape.y2);
      return;
    case 'polygon': {
      ctx.beginPath();
      const rot = (shape.rotation || 0) - Math.PI / 2;
      for (let i = 0; i < shape.sides; i++) {
        const a = rot + i * ((Math.PI * 2) / shape.sides);
        const px = shape.x + Math.cos(a) * shape.radius;
        const py = shape.y + Math.sin(a) * shape.radius;
        if (i === 0) ctx.moveTo(px, py);
        else ctx.lineTo(px, py);
      }
      ctx.closePath();
      return;
    }
    case 'path': {
      ctx.beginPath();
      const pts = shape.points;
      if (!pts.length) return;
      ctx.moveTo(pts[0].x, pts[0].y);
      for (let i = 1; i < pts.length; i++) {
        const prev = pts[i - 1], cur = pts[i];
        const c1 = prev.handleOut ? { x: prev.x + prev.handleOut.x, y: prev.y + prev.handleOut.y } : prev;
        const c2 = cur.handleIn ? { x: cur.x + cur.handleIn.x, y: cur.y + cur.handleIn.y } : cur;
        ctx.bezierCurveTo(c1.x, c1.y, c2.x, c2.y, cur.x, cur.y);
      }
      if (shape.closed && pts.length > 1) {
        const last = pts[pts.length - 1], first = pts[0];
        const c1 = last.handleOut ? { x: last.x + last.handleOut.x, y: last.y + last.handleOut.y } : last;
        const c2 = first.handleIn ? { x: first.x + first.handleIn.x, y: first.y + first.handleIn.y } : first;
        ctx.bezierCurveTo(c1.x, c1.y, c2.x, c2.y, first.x, first.y);
        ctx.closePath();
      }
      return;
    }
  }
}

export function drawVectorShape(ctx, shape, hueA, hueB, alpha, nowMs, beatFlash) {
  if (alpha <= 0.004) return;
  ctx.save();
  ctx.globalAlpha = alpha;
  if (shape.glow) {
    ctx.shadowBlur = shape.glow.blur + (beatFlash || 0) * shape.glow.blur * 0.5;
    ctx.shadowColor = resolveColor(shape.glow.color ?? (shape.fill.kind === 'flat' ? shape.fill.color : '$hueA'), hueA, hueB);
  }
  if (shape.kind === 'text') {
    if (!shape.text) {
      ctx.restore();
      return;
    }
    const seed = seeded(hash(shape.id))();
    const color = resolveColor(shape.fill.kind === 'flat' ? shape.fill.color : '#ffffff', hueA, hueB);
    const glowColor = shape.glow ? resolveColor(shape.glow.color ?? (shape.fill.kind === 'flat' ? shape.fill.color : '$hueA'), hueA, hueB) : color;
    drawWordShape(ctx, shape.text, shape.fontSize, shape.x, shape.y, shape.tracking, color, glowColor, 1, 1, 0, shape.jitter || 0, 1, 0, seed, nowMs, beatFlash);
    ctx.restore();
    return;
  }
  tracePath(ctx, shape);
  const bounds = boundsOf(shape);
  if (shape.kind !== 'line' && !(shape.fill.kind === 'flat' && shape.fill.color === 'none')) {
    ctx.fillStyle = paintFor(ctx, shape.fill, bounds, hueA, hueB);
    ctx.fill();
  }
  if (shape.strokeWidth > 0) {
    ctx.strokeStyle = resolveColor(shape.stroke, hueA, hueB);
    ctx.lineWidth = shape.strokeWidth;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.stroke();
  }
  ctx.restore();
}

export function drawVectorLayer(ctx, shapes, hueA, hueB, alpha, nowMs, beatFlash) {
  for (const shape of shapes) drawVectorShape(ctx, shape, hueA, hueB, alpha, nowMs, beatFlash);
}

// Bitmap layers are self-contained (params.dataUrl carries the actual image
// bytes, see types/project.ts's LayerParamsByType.bitmap doc) — no asset-id
// resolution needed at render time. `Image` decode is async; a not-yet-loaded
// frame just draws nothing and picks up on the next one, since the preview/
// export driver's rAF loop is already running continuously.
const imageCache = new Map();
function getOrLoadImage(dataUrl) {
  if (!dataUrl) return null;
  let img = imageCache.get(dataUrl);
  if (!img) {
    img = new Image();
    img.src = dataUrl;
    imageCache.set(dataUrl, img);
  }
  return img.complete && img.naturalWidth > 0 ? img : null;
}

export function drawBitmapLayer(ctx, dataUrl, w, h, alpha) {
  if (alpha <= 0.004 || !dataUrl) return;
  const img = getOrLoadImage(dataUrl);
  if (!img) return;
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.drawImage(img, -w / 2, -h / 2, w, h);
  ctx.restore();
}

export function wordFontStr(fs) {
  return `900 ${fs}px "Arial Black", Impact, "Segoe UI", sans-serif`;
}

export function measureWordShape(ctx, text, fs, tracking) {
  ctx.font = wordFontStr(fs);
  let total = 0;
  const widths = [];
  for (let i = 0; i < text.length; i++) {
    const cw = ctx.measureText(text[i]).width;
    widths.push(cw);
    total += cw + (i < text.length - 1 ? tracking : 0);
  }
  return { total, widths };
}

export function drawWordShape(
  ctx, text, fs, cx, cy, tracking, color, glowColor, alpha, scale, rot,
  jitterAmt, shadowMul, arcBend, wordSeed, nowMs, beatFlash
) {
  if (alpha <= 0.004) return;
  ctx.save();
  ctx.translate(cx, cy);
  ctx.rotate(rot);
  ctx.scale(scale, scale);
  const m = measureWordShape(ctx, text, fs, tracking);
  ctx.font = wordFontStr(fs);
  ctx.textBaseline = 'middle';
  let x = -m.total / 2;
  const bend = arcBend || 0;
  const seed = wordSeed || 0;
  for (let i = 0; i < text.length; i++) {
    const glyphCenter = x + m.widths[i] / 2;
    const frac = m.total > 0 ? glyphCenter / m.total : 0;
    const charRot = bend ? frac * bend : 0;
    const charLift = bend ? -Math.sin(charRot) * (m.total * 0.5) : 0;
    const jig = jitterAmt ? Math.sin(nowMs * 0.011 + i * 1.7 + seed * 12) * jitterAmt * 7 : 0;
    ctx.save();
    ctx.translate(glyphCenter, charLift + jig);
    ctx.rotate(charRot);
    ctx.textAlign = 'center';
    ctx.globalAlpha = alpha;
    ctx.shadowColor = glowColor;
    ctx.shadowBlur = (shadowMul || 1) * (16 + (beatFlash || 0) * 22);
    ctx.fillStyle = color;
    ctx.fillText(text[i], 0, 0);
    ctx.restore();
    x += m.widths[i] + tracking;
  }
  ctx.restore();
}
