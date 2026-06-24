import { useRef, useEffect, useState } from 'react'

// ── fake audio data ───────────────────────────────────────────────────────────

function generateBands(t, count = 64) {
  return Array.from({ length: count }, (_, i) => {
    const f = i / count
    const env = Math.exp(-f * 2.4) * 0.72 + Math.exp(-Math.pow(f - 0.22, 2) * 14) * 0.28
    const kick = Math.pow(Math.max(0, Math.sin(t * Math.PI * 4)), 7) * (f < 0.12 ? 0.85 : 0)
    const hihat = f > 0.65 ? Math.pow(Math.max(0, Math.sin(t * Math.PI * 8)), 9) * 0.45 : 0
    const a = Math.abs(Math.sin(t * 1.42 + i * 0.27)) * 0.55
    const b = Math.abs(Math.sin(t * 0.88 + i * 0.61)) * 0.35
    const c = Math.abs(Math.sin(t * 2.31 + i * 0.13)) * 0.18
    return Math.min(1, Math.max(0.02, (a + b + c + kick + hihat) * env + 0.02))
  })
}

function sample(arr, i, total) {
  return arr[Math.min(arr.length - 1, Math.floor((i / total) * arr.length))]
}

// ── renderers (matching the actual C# visualizers) ───────────────────────────

const R = '#d42b2b'
const BG = '#060608'

function drawSpectrum(ctx, bands, W, H) {
  const bw = W / bands.length
  bands.forEach((v, i) => {
    const bh = Math.max(2, v * H * 0.92)
    const g = ctx.createLinearGradient(0, H - bh, 0, H)
    g.addColorStop(0, 'rgba(255,80,80,0.9)'); g.addColorStop(1, R)
    ctx.fillStyle = g
    ctx.fillRect(i * bw + 0.5, H - bh, bw - 1, bh)
  })
}

function drawMirrorSpectrum(ctx, bands, W, H) {
  const mid = H / 2, bw = W / bands.length
  bands.forEach((v, i) => {
    const bh = Math.max(1, v * mid * 0.9)
    const g = ctx.createLinearGradient(0, 0, 0, mid)
    g.addColorStop(0, 'rgba(255,80,80,0.3)'); g.addColorStop(1, 'rgba(212,43,43,0.95)')
    ctx.fillStyle = g
    ctx.fillRect(i * bw + 0.5, mid - bh, bw - 1, bh)
    ctx.fillRect(i * bw + 0.5, mid, bw - 1, bh)
  })
  ctx.fillStyle = 'rgba(212,43,43,0.25)'
  ctx.fillRect(0, H / 2 - 0.5, W, 1)
}

function drawWaveform(ctx, t, W, H) {
  const wave = x => {
    const f = x / W
    return H / 2
      + Math.sin(f * 14 + t * 2.3) * H * 0.23
      + Math.sin(f * 5.5 + t * 1.1) * H * 0.1
      + Math.sin(f * 28 + t * 3.9) * H * 0.04
  }
  ctx.beginPath()
  for (let x = 0; x <= W; x++) x === 0 ? ctx.moveTo(x, wave(x)) : ctx.lineTo(x, wave(x))
  ctx.lineTo(W, H); ctx.lineTo(0, H); ctx.closePath()
  const g = ctx.createLinearGradient(0, 0, 0, H)
  g.addColorStop(0, 'rgba(212,43,43,0.72)'); g.addColorStop(1, 'rgba(212,43,43,0.04)')
  ctx.fillStyle = g; ctx.fill()
  ctx.beginPath()
  for (let x = 0; x <= W; x++) x === 0 ? ctx.moveTo(x, wave(x)) : ctx.lineTo(x, wave(x))
  ctx.strokeStyle = R; ctx.lineWidth = 2; ctx.stroke()
}

function drawSpinningDisk(ctx, bands, t, W, H) {
  const cx = W / 2, cy = H / 2, rad = Math.min(W, H) * 0.38
  // Outer bars
  for (let i = 0; i < 48; i++) {
    const angle = (i / 48) * Math.PI * 2 + t * 0.8
    const v = sample(bands, i, 48)
    ctx.strokeStyle = `rgba(212,43,43,${0.3 + v * 0.7})`
    ctx.lineWidth = (2 * Math.PI * rad / 48) * 0.55; ctx.lineCap = 'round'
    ctx.beginPath()
    ctx.moveTo(cx + Math.cos(angle) * (rad + 4), cy + Math.sin(angle) * (rad + 4))
    ctx.lineTo(cx + Math.cos(angle) * (rad + 4 + v * rad * 0.6), cy + Math.sin(angle) * (rad + 4 + v * rad * 0.6))
    ctx.stroke()
  }
  // Disk body
  const dg = ctx.createRadialGradient(cx, cy, 0, cx, cy, rad)
  dg.addColorStop(0, 'rgba(55,50,50,0.95)'); dg.addColorStop(0.4, 'rgba(25,22,22,0.92)')
  dg.addColorStop(0.8, 'rgba(15,12,12,0.96)'); dg.addColorStop(1, 'rgba(8,6,6,0.9)')
  ctx.beginPath(); ctx.arc(cx, cy, rad, 0, Math.PI * 2)
  ctx.fillStyle = dg; ctx.fill()
  // Grooves
  for (let r = rad * 0.32; r < rad * 0.95; r += rad * 0.09) {
    ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2)
    ctx.strokeStyle = 'rgba(255,255,255,0.04)'; ctx.lineWidth = 1; ctx.stroke()
  }
  // Center label
  ctx.beginPath(); ctx.arc(cx, cy, rad * 0.28, 0, Math.PI * 2)
  ctx.fillStyle = 'rgba(212,43,43,0.15)'; ctx.fill()
  ctx.strokeStyle = 'rgba(212,43,43,0.4)'; ctx.lineWidth = 1.5; ctx.stroke()
  // Spindle
  ctx.beginPath(); ctx.arc(cx, cy, 5, 0, Math.PI * 2)
  ctx.fillStyle = 'rgba(255,255,255,0.55)'; ctx.fill()
  // Highlight
  const hl = ctx.createRadialGradient(cx - rad * 0.25, cy - rad * 0.25, 0, cx, cy, rad)
  hl.addColorStop(0, 'rgba(255,255,255,0.1)'); hl.addColorStop(1, 'rgba(255,255,255,0)')
  ctx.beginPath(); ctx.arc(cx, cy, rad, 0, Math.PI * 2)
  ctx.fillStyle = hl; ctx.fill()
}

function drawRadialSpectrum(ctx, bands, t, W, H) {
  const cx = W / 2, cy = H / 2
  const maxR = Math.min(W, H) * 0.42, innerR = maxR * 0.30
  const n = Math.min(64, bands.length)
  const rot = t * 0.85

  for (let i = 0; i < n; i++) {
    const v = sample(bands, i, n)
    const barLen = Math.max(4, (maxR - innerR) * v)
    const angle = (i / n) * Math.PI * 2 + rot - Math.PI / 2
    const cos = Math.cos(angle), sin = Math.sin(angle)
    const x1 = cx + cos * innerR, y1 = cy + sin * innerR
    const x2 = cx + cos * (innerR + barLen), y2 = cy + sin * (innerR + barLen)
    ctx.lineCap = 'round'
    // Glow
    ctx.strokeStyle = `rgba(212,43,43,${0.18 * v})`; ctx.lineWidth = 8
    ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke()
    // Bar
    ctx.strokeStyle = `rgba(212,43,43,${0.5 + v * 0.5})`; ctx.lineWidth = 2.5
    ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke()
  }
  // Inner ring
  ctx.beginPath(); ctx.arc(cx, cy, innerR, 0, Math.PI * 2)
  ctx.strokeStyle = 'rgba(212,43,43,0.22)'; ctx.lineWidth = 1.5; ctx.stroke()
  // Hub + dot
  ctx.beginPath(); ctx.arc(cx, cy, innerR * 0.52, 0, Math.PI * 2)
  ctx.fillStyle = 'rgba(212,43,43,0.18)'; ctx.fill()
  ctx.beginPath(); ctx.arc(cx, cy, innerR * 0.22, 0, Math.PI * 2)
  ctx.fillStyle = 'rgba(255,100,100,0.75)'; ctx.fill()
}

function drawOscilloscope(ctx, t, W, H) {
  ctx.beginPath()
  for (let x = 0; x <= W; x++) {
    const f = x / W
    const y = H / 2
      + Math.sin(f * Math.PI * 8 + t * 4) * H * 0.3
      + Math.sin(f * Math.PI * 3 + t * 1.6) * H * 0.1
      + Math.sin(f * Math.PI * 16 + t * 7) * H * 0.03
    x === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y)
  }
  ctx.shadowBlur = 12; ctx.shadowColor = 'rgba(212,43,43,0.45)'
  ctx.strokeStyle = R; ctx.lineWidth = 2; ctx.stroke(); ctx.shadowBlur = 0
  ctx.strokeStyle = 'rgba(212,43,43,0.1)'; ctx.lineWidth = 1
  ctx.beginPath(); ctx.moveTo(0, H / 2); ctx.lineTo(W, H / 2); ctx.stroke()
}

function drawVUMeter(ctx, bands, W, H) {
  const Lv = bands.slice(0, 32).reduce((a, b) => a + b) / 32
  const Rv = bands.slice(32).reduce((a, b) => a + b) / 32
  const segs = 24, cw = W * 0.28, gx = W * 0.1
  const xL = W / 2 - cw - gx / 2, xR = W / 2 + gx / 2
  ;[Lv, Rv].forEach((v, ch) => {
    const x = ch === 0 ? xL : xR, lit = Math.round(v * segs), sh = H / segs - 3
    for (let s = 0; s < segs; s++) {
      const y = H - (s + 1) * (H / segs) + 1.5
      if (s >= segs - 3)      ctx.fillStyle = s < lit ? '#ef4444' : 'rgba(239,68,68,0.08)'
      else if (s >= segs - 7) ctx.fillStyle = s < lit ? '#f59e0b' : 'rgba(245,158,11,0.08)'
      else                    ctx.fillStyle = s < lit ? R : 'rgba(212,43,43,0.08)'
      ctx.fillRect(x, y, cw, sh)
    }
    ctx.fillStyle = 'rgba(212,43,43,0.4)'
    ctx.font = '500 10px "IBM Plex Mono",monospace'; ctx.textAlign = 'center'
    ctx.fillText(ch === 0 ? 'L' : 'R', x + cw / 2, H - 5)
  })
}

function drawSpectrumWave(ctx, bands, t, W, H) {
  const bw = W / bands.length
  bands.forEach((v, i) => {
    ctx.fillStyle = `rgba(212,43,43,${0.28 + v * 0.44})`
    ctx.fillRect(i * bw + 0.5, H - Math.max(2, v * H * 0.74), bw - 1, Math.max(2, v * H * 0.74))
  })
  ctx.beginPath()
  for (let x = 0; x <= W; x++) {
    const v = bands[Math.min(bands.length - 1, Math.floor((x / W) * bands.length))]
    const y = H - v * H * 0.74 + Math.sin((x / W) * 12 + t * 2.5) * 14
    x === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y)
  }
  ctx.strokeStyle = 'rgba(255,100,100,0.6)'; ctx.lineWidth = 2; ctx.stroke()
}

function drawDancingColors(ctx, bands, t, W, H) {
  const bw = W / bands.length
  bands.forEach((v, i) => {
    const hue = (i / bands.length * 90 + t * 25) % 360
    ctx.fillStyle = `hsla(${hue},85%,58%,${0.7 + v * 0.3})`
    ctx.fillRect(i * bw + 0.5, H - Math.max(2, v * H * 0.94), bw - 1, Math.max(2, v * H * 0.94))
  })
}

function drawGraph3D(ctx, bands, t, W, H) {
  for (let l = 6; l >= 0; l--) {
    const yBase = H * 0.84 - (l / 6) * (H * 0.52)
    const depth = 0.38 + (l / 6) * 0.62
    const alpha = 0.18 + (l / 6) * 0.72
    ctx.beginPath()
    for (let x = 0; x <= W; x++) {
      const v = bands[Math.min(bands.length - 1, Math.floor((x / W) * bands.length))]
      const y = yBase - v * H * 0.52 * depth + Math.sin(t * 0.6 + l * 0.8) * 3
      x === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y)
    }
    ctx.lineTo(W, yBase); ctx.lineTo(0, yBase); ctx.closePath()
    ctx.fillStyle = `rgba(212,43,43,${alpha * 0.2})`; ctx.fill()
    ctx.strokeStyle = `rgba(212,43,43,${alpha})`; ctx.lineWidth = 1.5; ctx.stroke()
  }
}

function drawSphere3D(ctx, bands, t, W, H) {
  const cx = W / 2, cy = H / 2, SR = Math.min(W, H) * 0.36
  const yaw = t * 0.82, pitch = -0.36 + Math.sin(t * 0.37) * 0.08
  const FOV = 5.8

  function proj(px, py, pz) {
    const x1 = px * Math.cos(yaw) + pz * Math.sin(yaw)
    const z1 = -px * Math.sin(yaw) + pz * Math.cos(yaw)
    const y2 = py * Math.cos(pitch) - z1 * Math.sin(pitch)
    const z2 = py * Math.sin(pitch) + z1 * Math.cos(pitch)
    const sc = FOV / (FOV + z2)
    return { x: cx + x1 * sc * SR, y: cy + y2 * sc * SR, depth: z2 }
  }
  function sph(lon, lat) {
    const cl = Math.cos(lat)
    return [Math.cos(lon) * cl * 1.32, Math.sin(lat) * 1.32, Math.sin(lon) * cl * 1.32]
  }

  // Aura
  const aura = ctx.createRadialGradient(cx, cy, 0, cx, cy, SR * 0.65)
  aura.addColorStop(0, 'rgba(212,43,43,0.14)'); aura.addColorStop(1, 'rgba(212,43,43,0)')
  ctx.beginPath(); ctx.arc(cx, cy, SR * 0.65, 0, Math.PI * 2)
  ctx.fillStyle = aura; ctx.fill()

  // Latitude rings
  ;[-0.95, -0.48, 0, 0.48, 0.95].forEach(lat => {
    ctx.beginPath()
    for (let i = 0; i <= 64; i++) {
      const [px, py, pz] = sph((i / 64) * Math.PI * 2, lat)
      const p = proj(px, py, pz)
      i === 0 ? ctx.moveTo(p.x, p.y) : ctx.lineTo(p.x, p.y)
    }
    ctx.strokeStyle = 'rgba(212,43,43,0.28)'; ctx.lineWidth = 1; ctx.stroke()
  })

  // Longitude lines
  for (let li = 0; li < 7; li++) {
    const lon = li * (Math.PI / 3.5)
    ctx.beginPath()
    for (let i = 0; i <= 32; i++) {
      const [px, py, pz] = sph(lon, (i / 32) * Math.PI - Math.PI / 2)
      const p = proj(px, py, pz)
      i === 0 ? ctx.moveTo(p.x, p.y) : ctx.lineTo(p.x, p.y)
    }
    ctx.strokeStyle = 'rgba(212,43,43,0.14)'; ctx.lineWidth = 1; ctx.stroke()
  }

  // Spectrum spikes, sorted back-to-front
  const spikes = Array.from({ length: 36 }, (_, i) => {
    const lon = (i / 36) * Math.PI * 2
    const lat = Math.sin(lon * 2 + t * 0.7) * 0.34
    const v = sample(bands, i, 36)
    const [nx, ny, nz] = sph(lon, lat)
    const nf = 1 / 1.32 // unnormalize
    const tip = 1.32 + 0.18 + v * 0.95
    const [px, py, pz] = [nx * nf, ny * nf, nz * nf]
    const start = proj(nx, ny, nz)
    const end = proj(px * tip, py * tip, pz * tip)
    return { start, end, v, depth: (start.depth + end.depth) / 2 }
  }).sort((a, b) => b.depth - a.depth)

  spikes.forEach(({ start, end, v, depth }) => {
    const alpha = depth > 0.42 ? 0.18 : 0.78
    ctx.strokeStyle = `rgba(212,43,43,${alpha * (0.45 + v * 0.55)})`
    ctx.lineWidth = 2; ctx.lineCap = 'round'
    ctx.beginPath(); ctx.moveTo(start.x, start.y); ctx.lineTo(end.x, end.y); ctx.stroke()
  })

  // Core
  ctx.beginPath(); ctx.arc(cx, cy, SR * 0.11, 0, Math.PI * 2)
  ctx.fillStyle = 'rgba(212,43,43,0.22)'; ctx.fill()
}

// ── types ─────────────────────────────────────────────────────────────────────

const VIZ_TYPES = [
  { id: 'spectrum',     label: 'Spectrum' },
  { id: 'mirror',       label: 'Mirror Spectrum' },
  { id: 'waveform',     label: 'Waveform' },
  { id: 'disk',         label: 'Spinning Disk' },
  { id: 'radial',       label: 'Radial Spectrum' },
  { id: 'oscilloscope', label: 'Oscilloscope' },
  { id: 'vu',           label: 'VU Meter' },
  { id: 'specwave',     label: 'Spectrum Wave' },
  { id: 'graph3d',      label: '3D Graph' },
  { id: 'dancing',      label: 'Dancing Colors' },
  { id: 'sphere3d',     label: '3D Sphere' },
]

// ── component ─────────────────────────────────────────────────────────────────

export function VisualizerDemo() {
  const [active, setActive] = useState('spectrum')
  const canvasRef = useRef(null)
  const rafRef = useRef(null)
  const t0Ref = useRef(null)
  const activeRef = useRef(active)
  useEffect(() => { activeRef.current = active }, [active])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    const W = canvas.width, H = canvas.height

    function frame(ts) {
      if (!t0Ref.current) t0Ref.current = ts
      const t = (ts - t0Ref.current) / 1000
      const bands = generateBands(t)
      ctx.fillStyle = BG; ctx.fillRect(0, 0, W, H)
      switch (activeRef.current) {
        case 'spectrum':     drawSpectrum(ctx, bands, W, H); break
        case 'mirror':       drawMirrorSpectrum(ctx, bands, W, H); break
        case 'waveform':     drawWaveform(ctx, t, W, H); break
        case 'disk':         drawSpinningDisk(ctx, bands, t, W, H); break
        case 'radial':       drawRadialSpectrum(ctx, bands, t, W, H); break
        case 'oscilloscope': drawOscilloscope(ctx, t, W, H); break
        case 'vu':           drawVUMeter(ctx, bands, W, H); break
        case 'specwave':     drawSpectrumWave(ctx, bands, t, W, H); break
        case 'graph3d':      drawGraph3D(ctx, bands, t, W, H); break
        case 'dancing':      drawDancingColors(ctx, bands, t, W, H); break
        case 'sphere3d':     drawSphere3D(ctx, bands, t, W, H); break
      }
      rafRef.current = requestAnimationFrame(frame)
    }
    rafRef.current = requestAnimationFrame(frame)
    return () => cancelAnimationFrame(rafRef.current)
  }, [])

  return (
    <div className="viz-demo">
      <div className="viz-demo__screen">
        <canvas ref={canvasRef} width={960} height={360} className="viz-demo__canvas" />
        <div className="viz-demo__hud">
          <img src="/icon.png" alt="" className="viz-demo__art" />
          <div className="viz-demo__info">
            <p className="viz-demo__track">Unknown Suns</p>
            <p className="viz-demo__artist">Midnight Capsule · Spectralis</p>
          </div>
          <span className="viz-demo__live">LIVE</span>
        </div>
      </div>
      <div className="viz-demo__tabs">
        {VIZ_TYPES.map(({ id, label }) => (
          <button
            key={id}
            className={`viz-demo__tab${active === id ? ' active' : ''}`}
            onClick={() => setActive(id)}
          >
            {label}
          </button>
        ))}
      </div>
    </div>
  )
}
