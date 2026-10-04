// Pure helpers for the Streamer Queue dashboard: no React, no network, so they're easy to test.

/** Statuses that still occupy a place in the queue. */
export const ACTIVE_STATUSES = new Set(['pending', 'queued', 'awaiting_payment'])

export function normalizeBaseUrl(value) {
  const trimmed = String(value ?? '').trim().replace(/\/+$/, '')
  if (!/^https?:\/\//i.test(trimmed)) return ''
  return trimmed
}

const pct = (n, d) => (d > 0 ? Math.round((n / d) * 100) : 0)

/**
 * Numbers for the analytics tab, derived from the owner's submission list.
 * `now` is injectable so "last 24 hours" is testable.
 */
export function computeAnalytics(submissions, now = Date.now()) {
  const list = Array.isArray(submissions) ? submissions : []

  const byStatus = {}
  const byTier = { normal: 0, skip: 0, super_skip: 0 }
  const requesters = new Map()
  const hours = Array.from({ length: 24 }, () => 0)

  for (const s of list) {
    const status = s.status || 'queued'
    byStatus[status] = (byStatus[status] || 0) + 1
    byTier[s.tier] = (byTier[s.tier] || 0) + 1

    const name = (s.displayName || 'Listener').trim() || 'Listener'
    requesters.set(name, (requesters.get(name) || 0) + 1)

    const at = Date.parse(s.submittedAtUtc)
    if (Number.isFinite(at)) {
      const hoursAgo = Math.floor((now - at) / 3_600_000)
      if (hoursAgo >= 0 && hoursAgo < 24) hours[23 - hoursAgo] += 1
    }
  }

  const played = byStatus.played || 0
  const skipped = byStatus.skipped || 0
  const rejected = byStatus.rejected || 0
  // Approval only means something for submissions a human actually decided on.
  const decided = list.length - (byStatus.pending || 0) - (byStatus.awaiting_payment || 0)

  return {
    total: list.length,
    active: list.filter((s) => ACTIVE_STATUSES.has(s.status)).length,
    played,
    skipped,
    rejected,
    byStatus,
    byTier,
    paidShare: pct(byTier.skip + byTier.super_skip, list.length),
    approvalRate: pct(decided - rejected, decided),
    topRequesters: [...requesters.entries()]
      .map(([name, count]) => ({ name, count }))
      .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name))
      .slice(0, 5),
    // Oldest hour first, ending with the current hour.
    perHour: hours,
    peakHourCount: Math.max(0, ...hours),
  }
}

/**
 * New manual order after moving `id` by `delta` places (negative = earlier). `order` is the
 * ids of the visible queue; moving past either end is a no-op.
 */
export function moveInOrder(order, id, delta) {
  const from = order.indexOf(id)
  const to = from + delta
  if (from === -1 || to < 0 || to >= order.length || delta === 0) return order
  const next = order.slice()
  next.splice(from, 1)
  next.splice(to, 0, id)
  return next
}

/** Splits the owner's room payload into what each part of the dashboard shows. */
export function splitRoom(room) {
  const submissions = Array.isArray(room?.submissions) ? room.submissions : []
  const queue = Array.isArray(room?.orderedQueue) ? room.orderedQueue : []
  const nowId = room?.nowPlayingId || null
  return {
    nowPlaying: nowId ? submissions.find((s) => s.id === nowId) || null : null,
    pending: submissions.filter((s) => s.status === 'pending'),
    queue: queue.filter((s) => s.status !== 'pending'),
  }
}

export function describeTrack(s) {
  const title = (s?.title || '').trim() || (s?.url || '').trim() || 'Untitled'
  const artist = (s?.artist || '').trim()
  return artist ? `${artist} - ${title}` : title
}
