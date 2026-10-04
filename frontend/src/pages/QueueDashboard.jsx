import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { SQ_API_BASE } from '../data/site.jsx'
import { createSqClient } from '../lib/sqClient.js'
import { computeAnalytics, describeTrack, moveInOrder, normalizeBaseUrl, splitRoom } from '../lib/sqDashboard.js'
import './QueueDashboard.css'

const STORAGE_KEY = 'spectralis.sq.dashboard'
const POLL_MS = 4000

// The owner token is a credential, so it only lands in localStorage when the user asks for that;
// otherwise it lives in sessionStorage and dies with the tab.
function loadSaved() {
  for (const store of [window.localStorage, window.sessionStorage]) {
    try {
      const raw = store.getItem(STORAGE_KEY)
      if (raw) return { ...JSON.parse(raw), remember: store === window.localStorage }
    } catch {
      // storage blocked or corrupt: start empty
    }
  }
  return { baseUrl: SQ_API_BASE, roomId: '', ownerToken: '', remember: false }
}

function save(conn) {
  const { remember, ...data } = conn
  try {
    window.localStorage.removeItem(STORAGE_KEY)
    window.sessionStorage.removeItem(STORAGE_KEY)
    ;(remember ? window.localStorage : window.sessionStorage).setItem(STORAGE_KEY, JSON.stringify(data))
  } catch {
    // storage blocked: the dashboard still works, it just won't remember
  }
}

function forget() {
  try {
    window.localStorage.removeItem(STORAGE_KEY)
    window.sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    // ignore
  }
}

export default function QueueDashboard() {
  const [conn, setConn] = useState(loadSaved)
  const [client, setClient] = useState(null)
  const [room, setRoom] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [tab, setTab] = useState('queue')
  const [form, setForm] = useState(conn)
  const busyRef = useRef(false)

  const refresh = useCallback(async (c) => {
    try {
      setRoom(await c.getRoom())
      setError('')
    } catch (e) {
      setError(e.message || 'Could not reach the queue.')
    }
  }, [])

  // Opens the room and proves the token is the owner's (the public view has no submissions list).
  const open = useCallback(async (next) => {
    const baseUrl = normalizeBaseUrl(next.baseUrl)
    if (!baseUrl || !next.roomId.trim() || !next.ownerToken.trim()) {
      throw new Error('Enter the server address, room id and owner token.')
    }
    const cleaned = { ...next, baseUrl, roomId: next.roomId.trim(), ownerToken: next.ownerToken.trim() }
    const c = createSqClient(cleaned)
    const first = await c.getRoom()
    if (!first.submissions) throw new Error('That token is not the owner token for this room.')
    return { cleaned, c, first }
  }, [])

  const adopt = useCallback(({ cleaned, c, first }) => {
    save(cleaned)
    setConn(cleaned)
    setClient(c)
    setRoom(first)
    setError('')
  }, [])

  const connect = useCallback(
    (next) =>
      open(next).then(adopt, (e) => setError(e.message || 'Could not connect.')),
    [open, adopt],
  )

  // Reconnect with whatever was saved last time.
  useEffect(() => {
    if (!conn.roomId || !conn.ownerToken) return undefined
    let cancelled = false
    open(conn).then(
      (opened) => { if (!cancelled) adopt(opened) },
      (e) => { if (!cancelled) setError(e.message || 'Could not reconnect.') },
    )
    return () => { cancelled = true }
    // only on first load: later connects go through the form
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Poll while connected and the tab is visible.
  useEffect(() => {
    if (!client) return undefined
    const tick = () => {
      if (!document.hidden && !busyRef.current) refresh(client)
    }
    const id = window.setInterval(tick, POLL_MS)
    return () => window.clearInterval(id)
  }, [client, refresh])

  const act = useCallback(
    async (fn) => {
      if (!client) return
      busyRef.current = true
      setBusy(true)
      try {
        await fn(client)
        await refresh(client)
      } catch (e) {
        setError(e.message || 'That did not work.')
      } finally {
        busyRef.current = false
        setBusy(false)
      }
    },
    [client, refresh],
  )

  const disconnect = () => {
    forget()
    setClient(null)
    setRoom(null)
    setForm({ baseUrl: SQ_API_BASE, roomId: '', ownerToken: '', remember: false })
    setError('')
  }

  const view = useMemo(() => splitRoom(room), [room])
  const stats = useMemo(() => computeAnalytics(room?.submissions), [room])

  if (!client || !room) {
    return (
      <main className="sqd">
        <div className="sqd__connect">
          <h1>Streamer Queue dashboard</h1>
          <p className="sqd__lede">
            Moderate your queue from any browser. Use the room id and owner token from the Streamer Queue panel
            in the Spectralis app.
          </p>
          <form
            onSubmit={(e) => {
              e.preventDefault()
              connect(form)
            }}
          >
            <label>
              Server
              <input value={form.baseUrl} onChange={(e) => setForm({ ...form, baseUrl: e.target.value })} spellCheck="false" />
            </label>
            <label>
              Room id
              <input value={form.roomId} onChange={(e) => setForm({ ...form, roomId: e.target.value })} spellCheck="false" autoComplete="off" />
            </label>
            <label>
              Owner token
              <input
                type="password"
                value={form.ownerToken}
                onChange={(e) => setForm({ ...form, ownerToken: e.target.value })}
                autoComplete="off"
              />
            </label>
            <label className="sqd__check">
              <input type="checkbox" checked={form.remember} onChange={(e) => setForm({ ...form, remember: e.target.checked })} />
              Remember on this device
            </label>
            <button type="submit" className="sqd__btn sqd__btn--primary">Connect</button>
          </form>
          {error && <p className="sqd__error" role="alert">{error}</p>}
        </div>
      </main>
    )
  }

  const order = view.queue.map((s) => s.id)

  return (
    <main className="sqd">
      <header className="sqd__head">
        <div>
          <h1>Streamer Queue</h1>
          <p className="sqd__sub">
            {stats.active} waiting · {stats.played} played · room <code>{conn.roomId.slice(0, 8)}…</code>
          </p>
        </div>
        <div className="sqd__head-actions">
          <button
            className={`sqd__btn ${room.acceptingSubmissions === false ? '' : 'sqd__btn--on'}`}
            disabled={busy}
            onClick={() => act((c) => c.setAccepting(room.acceptingSubmissions === false))}
          >
            {room.acceptingSubmissions === false ? 'Queue closed — open it' : 'Accepting requests — close it'}
          </button>
          <button className="sqd__btn sqd__btn--ghost" onClick={disconnect}>Disconnect</button>
        </div>
      </header>

      {error && <p className="sqd__error" role="alert">{error}</p>}

      <div className="sqd__tabs" role="tablist">
        {[
          ['queue', 'Queue'],
          ['analytics', 'Analytics'],
          ['integrations', 'Integrations'],
        ].map(([id, label]) => (
          <button key={id} role="tab" aria-selected={tab === id} className={tab === id ? 'is-active' : ''} onClick={() => setTab(id)}>
            {label}
            {id === 'queue' && view.pending.length > 0 && <span className="sqd__badge">{view.pending.length}</span>}
          </button>
        ))}
      </div>

      {tab === 'queue' && (
        <section>
          <div className="sqd__card">
            <h2>Now playing</h2>
            {view.nowPlaying ? (
              <div className="sqd__row">
                <div className="sqd__track">
                  <strong>{describeTrack(view.nowPlaying)}</strong>
                  <span>requested by {view.nowPlaying.displayName}</span>
                </div>
                <button className="sqd__btn" disabled={busy} onClick={() => act((c) => c.setStatus(view.nowPlaying.id, 'played'))}>
                  Mark played
                </button>
              </div>
            ) : (
              <p className="sqd__empty">Nothing playing.</p>
            )}
          </div>

          {view.pending.length > 0 && (
            <div className="sqd__card">
              <h2>Waiting for approval</h2>
              {view.pending.map((s) => (
                <div className="sqd__row" key={s.id}>
                  <div className="sqd__track">
                    <strong>{describeTrack(s)}</strong>
                    <span>{s.displayName}</span>
                  </div>
                  <div className="sqd__actions">
                    <button className="sqd__btn sqd__btn--primary" disabled={busy} onClick={() => act((c) => c.approve(s.id))}>Approve</button>
                    <button className="sqd__btn" disabled={busy} onClick={() => act((c) => c.reject(s.id))}>Reject</button>
                  </div>
                </div>
              ))}
            </div>
          )}

          <div className="sqd__card">
            <h2>Up next ({view.queue.length})</h2>
            {view.queue.length === 0 && <p className="sqd__empty">The queue is empty.</p>}
            {view.queue.map((s, i) => (
              <div className="sqd__row" key={s.id}>
                <span className="sqd__pos">{i + 1}</span>
                <div className="sqd__track">
                  <strong>{describeTrack(s)}</strong>
                  <span>
                    {s.displayName}
                    {s.tier && s.tier !== 'normal' && <em className={`sqd__tier sqd__tier--${s.tier}`}>{s.tier.replace('_', ' ')}</em>}
                  </span>
                </div>
                <div className="sqd__actions">
                  <button aria-label="Move up" className="sqd__btn sqd__btn--icon" disabled={busy || i === 0} onClick={() => act((c) => c.setOrder(moveInOrder(order, s.id, -1)))}>↑</button>
                  <button aria-label="Move down" className="sqd__btn sqd__btn--icon" disabled={busy || i === order.length - 1} onClick={() => act((c) => c.setOrder(moveInOrder(order, s.id, 1)))}>↓</button>
                  <button className="sqd__btn" disabled={busy} onClick={() => act((c) => c.setNowPlaying(s.id))}>Play now</button>
                  <button className="sqd__btn" disabled={busy} onClick={() => act((c) => c.setStatus(s.id, 'skipped'))}>Skip</button>
                  <button className="sqd__btn sqd__btn--danger" disabled={busy} onClick={() => act((c) => c.remove(s.id))}>Remove</button>
                </div>
              </div>
            ))}
          </div>
        </section>
      )}

      {tab === 'analytics' && <Analytics stats={stats} />}
      {tab === 'integrations' && <Integrations conn={conn} act={act} />}
    </main>
  )
}

function Analytics({ stats }) {
  const tiles = [
    ['Requests', stats.total],
    ['Played', stats.played],
    ['Skipped', stats.skipped],
    ['Rejected', stats.rejected],
    ['Approval rate', `${stats.approvalRate}%`],
    ['Paid tiers', `${stats.paidShare}%`],
  ]
  return (
    <section>
      <div className="sqd__tiles">
        {tiles.map(([label, value]) => (
          <div className="sqd__tile" key={label}>
            <strong>{value}</strong>
            <span>{label}</span>
          </div>
        ))}
      </div>

      <div className="sqd__card">
        <h2>Requests, last 24 hours</h2>
        <div className="sqd__bars" role="img" aria-label="Requests per hour over the last 24 hours">
          {stats.perHour.map((n, i) => (
            <span
              key={i}
              title={`${n} request${n === 1 ? '' : 's'}, ${23 - i}h ago`}
              style={{ height: `${stats.peakHourCount ? Math.max(4, (n / stats.peakHourCount) * 100) : 4}%` }}
              className={n ? 'has-data' : ''}
            />
          ))}
        </div>
      </div>

      <div className="sqd__card">
        <h2>Top requesters</h2>
        {stats.topRequesters.length === 0 && <p className="sqd__empty">No requests yet.</p>}
        {stats.topRequesters.map((r) => (
          <div className="sqd__row" key={r.name}>
            <span>{r.name}</span>
            <strong>{r.count}</strong>
          </div>
        ))}
      </div>
    </section>
  )
}

function Integrations({ conn, act }) {
  const [key, setKey] = useState('')
  const [note, setNote] = useState('')

  const rotate = () =>
    act(async (c) => {
      const result = await c.webhookKey('rotate')
      setKey(result.webhookKey || '')
      setNote('Copy this now. It is shown once and can only be replaced, not recovered.')
    })
  const revoke = () =>
    act(async (c) => {
      await c.webhookKey('revoke')
      setKey('')
      setNote('Webhook key revoked.')
    })

  const sample = `curl -X POST ${conn.baseUrl}/streamer-queue/v2/rooms/${conn.roomId}/webhook/submit \\
  -H "content-type: application/json" \\
  -H "x-spectralis-webhook-key: ${key || '<your key>'}" \\
  -d '{"source":"twitch","userId":"123","displayName":"viewer","url":"https://youtu.be/..."}'`

  return (
    <section>
      <div className="sqd__card">
        <h2>Webhook key</h2>
        <p className="sqd__lede">
          Lets chat bots and tools (Twitch and YouTube chat bridge, Streamer.bot, Mixitup, curl) submit songs to
          this queue. It can submit and read status, nothing else.
        </p>
        <div className="sqd__actions">
          <button className="sqd__btn sqd__btn--primary" onClick={rotate}>{key ? 'Rotate again' : 'Create / rotate key'}</button>
          <button className="sqd__btn sqd__btn--danger" onClick={revoke}>Revoke</button>
        </div>
        {key && <input className="sqd__key" readOnly value={key} onFocus={(e) => e.target.select()} aria-label="New webhook key" />}
        {note && <p className="sqd__note">{note}</p>}
      </div>
      <div className="sqd__card">
        <h2>Example</h2>
        <pre className="sqd__code">{sample}</pre>
      </div>
    </section>
  )
}
