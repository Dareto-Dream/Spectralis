import { lerpHue } from './math.js';

export function drawOrbShape(ctx, cx, cy, r, hueA, hueB, alpha) {
  if (alpha <= 0.004 || r <= 0.5) return;
  ctx.save();
  const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, r);
  g.addColorStop(0, `hsla(${hueA},85%,80%,${alpha})`);
  g.addColorStop(0.55, `hsla(${hueA},90%,58%,${alpha * 0.9})`);
  g.addColorStop(1, `hsla(${hueB},85%,42%,0)`);
  ctx.fillStyle = g;
  ctx.shadowColor = `hsla(${hueA},95%,60%,0.8)`;
  ctx.shadowBlur = r * 0.5;
  ctx.beginPath();
  ctx.arc(cx, cy, r, 0, 6.2832);
  ctx.fill();
  ctx.restore();
}

export function drawRingShape(ctx, cx, cy, r, hue, alpha, lineWidth) {
  if (alpha <= 0.004 || r <= 0.5) return;
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.strokeStyle = hue == null ? '#ffffff' : `hsl(${hue},85%,70%)`;
  ctx.shadowColor = hue == null ? 'rgba(255,255,255,0.85)' : `hsla(${hue},95%,65%,0.85)`;
  ctx.shadowBlur = 14;
  ctx.lineWidth = lineWidth || 1.5;
  ctx.beginPath();
  ctx.arc(cx, cy, r, 0, 6.2832);
  ctx.stroke();
  ctx.restore();
}

export function drawStreakShape(ctx, cx, cy, angle, length, thickness, hueA, hueB, alpha) {
  if (alpha <= 0.004) return;
  ctx.save();
  ctx.translate(cx, cy);
  ctx.rotate(angle);
  const g = ctx.createLinearGradient(-length / 2, 0, length / 2, 0);
  if (hueA == null) {
    g.addColorStop(0, 'rgba(255,255,255,0)');
    g.addColorStop(0.5, `rgba(255,255,255,${alpha})`);
    g.addColorStop(1, 'rgba(255,255,255,0)');
  } else {
    g.addColorStop(0, `hsla(${hueA},90%,60%,0)`);
    g.addColorStop(0.15, `hsla(${hueA},95%,62%,${alpha})`);
    g.addColorStop(0.5, `hsla(${lerpHue(hueA, hueB, 0.5)},95%,68%,${alpha})`);
    g.addColorStop(0.85, `hsla(${hueB},95%,60%,${alpha})`);
    g.addColorStop(1, `hsla(${hueB},90%,55%,0)`);
  }
  ctx.fillStyle = g;
  ctx.shadowColor = hueA == null ? 'rgba(255,255,255,0.9)' : `hsla(${hueA},95%,65%,0.9)`;
  ctx.shadowBlur = thickness * 1.4;
  ctx.fillRect(-length / 2, -thickness / 2, length, thickness);
  ctx.restore();
}

export function drawWheelShape(ctx, cx, cy, r, spokes, rot, hueA, hueB, accentIdx, alpha) {
  if (alpha <= 0.004 || r <= 1) return;
  ctx.save();
  ctx.globalAlpha = alpha;
  for (let i = 0; i < spokes; i++) {
    const a = rot + i * (6.2832 / spokes);
    const x1 = cx + Math.cos(a) * r * 0.06;
    const y1 = cy + Math.sin(a) * r * 0.06;
    const x2 = cx + Math.cos(a) * r * 1.28;
    const y2 = cy + Math.sin(a) * r * 1.28;
    ctx.beginPath();
    ctx.moveTo(x1, y1);
    ctx.lineTo(x2, y2);
    if (i === accentIdx) {
      const lg = ctx.createLinearGradient(x1, y1, x2, y2);
      lg.addColorStop(0, `hsl(${hueA},90%,62%)`);
      lg.addColorStop(1, `hsl(${hueB},90%,58%)`);
      ctx.strokeStyle = lg;
      ctx.lineWidth = 3.2;
      ctx.shadowColor = `hsla(${hueA},95%,65%,0.9)`;
    } else {
      ctx.strokeStyle = 'rgba(255,255,255,0.85)';
      ctx.lineWidth = 1.4;
      ctx.shadowColor = 'rgba(255,255,255,0.6)';
    }
    ctx.shadowBlur = 10;
    ctx.stroke();
  }
  ctx.restore();
  drawRingShape(ctx, cx, cy, r, null, alpha * 0.95, 2.4);
}

export function drawShardShape(ctx, cx, cy, size, rot, alpha) {
  if (alpha <= 0.004) return;
  ctx.save();
  ctx.translate(cx, cy);
  ctx.rotate(rot);
  ctx.globalAlpha = alpha;
  ctx.fillStyle = '#ffffff';
  ctx.shadowColor = 'rgba(255,255,255,0.95)';
  ctx.shadowBlur = size * 1.6;
  ctx.beginPath();
  for (let i = 0; i < 5; i++) {
    const a = -Math.PI / 2 + i * (6.2832 / 5);
    const px = Math.cos(a) * size;
    const py = Math.sin(a) * size;
    if (i === 0) ctx.moveTo(px, py);
    else ctx.lineTo(px, py);
  }
  ctx.closePath();
  ctx.fill();
  ctx.restore();
}

export function drawAmbientBeamShape(ctx, W, cy, bandH, hueA, hueB, alpha) {
  if (alpha <= 0.004) return;
  ctx.save();
  const strips = 16;
  for (let i = 0; i < strips; i++) {
    const f = i / (strips - 1);
    const edge = 1 - Math.abs(f - 0.5) * 2;
    const a = alpha * Math.pow(edge, 1.6);
    if (a <= 0.003) continue;
    const y = cy - bandH / 2 + f * bandH;
    const g = ctx.createLinearGradient(0, 0, W, 0);
    g.addColorStop(0, `hsla(${hueA},90%,62%,${a})`);
    g.addColorStop(0.5, `hsla(${lerpHue(hueA, hueB, 0.5)},90%,66%,${a})`);
    g.addColorStop(1, `hsla(${hueB},90%,58%,${a})`);
    ctx.fillStyle = g;
    ctx.fillRect(0, y, W, bandH / strips + 1);
  }
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
