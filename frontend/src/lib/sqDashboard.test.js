import assert from 'node:assert/strict'
import { test } from 'node:test'
import { computeAnalytics, describeTrack, moveInOrder, normalizeBaseUrl, splitRoom } from './sqDashboard.js'

const NOW = Date.parse('2026-10-03T12:30:00Z')
const sub = (over) => ({ id: 'x', status: 'queued', tier: 'normal', displayName: 'A', submittedAtUtc: '2026-10-03T12:00:00Z', ...over })

test('normalizeBaseUrl keeps http(s) origins and drops trailing slashes', () => {
  assert.equal(normalizeBaseUrl(' https://api.example.com/// '), 'https://api.example.com')
  assert.equal(normalizeBaseUrl('http://localhost:3000/'), 'http://localhost:3000')
  assert.equal(normalizeBaseUrl('javascript:alert(1)'), '')
  assert.equal(normalizeBaseUrl('api.example.com'), '')
  assert.equal(normalizeBaseUrl(undefined), '')
})

test('analytics counts statuses, tiers and active items', () => {
  const stats = computeAnalytics(
    [
      sub({ id: '1', status: 'played' }),
      sub({ id: '2', status: 'queued', tier: 'skip' }),
      sub({ id: '3', status: 'pending' }),
      sub({ id: '4', status: 'rejected' }),
      sub({ id: '5', status: 'skipped', tier: 'super_skip' }),
    ],
    NOW,
  )

  assert.equal(stats.total, 5)
  assert.equal(stats.active, 2) // queued + pending
  assert.equal(stats.played, 1)
  assert.equal(stats.rejected, 1)
  assert.deepEqual(stats.byTier, { normal: 3, skip: 1, super_skip: 1 })
  assert.equal(stats.paidShare, 40)
})

test('approval rate ignores submissions nobody has decided on yet', () => {
  const stats = computeAnalytics(
    [sub({ status: 'played' }), sub({ status: 'queued' }), sub({ status: 'rejected' }), sub({ status: 'pending' }), sub({ status: 'pending' })],
    NOW,
  )
  // 3 decided, 1 rejected -> 2/3
  assert.equal(stats.approvalRate, 67)
})

test('empty room produces zeros, not NaN', () => {
  const stats = computeAnalytics([], NOW)

  assert.equal(stats.total, 0)
  assert.equal(stats.approvalRate, 0)
  assert.equal(stats.paidShare, 0)
  assert.equal(stats.peakHourCount, 0)
  assert.deepEqual(stats.topRequesters, [])
  assert.equal(stats.perHour.length, 24)
})

test('top requesters are ranked, capped at five, and ties break alphabetically', () => {
  const names = ['Zed', 'Zed', 'Amy', 'Amy', 'Bob', 'Cat', 'Dan', 'Eve', 'Fay']
  const stats = computeAnalytics(names.map((displayName, i) => sub({ id: String(i), displayName })), NOW)

  assert.equal(stats.topRequesters.length, 5)
  assert.deepEqual(stats.topRequesters.slice(0, 2), [
    { name: 'Amy', count: 2 },
    { name: 'Zed', count: 2 },
  ])
})

test('per-hour buckets are trailing 60-minute windows, oldest first, and ignore anything older than a day', () => {
  // NOW is 12:30, so bucket 23 is 11:30-12:30, bucket 22 is 10:30-11:30, and so on.
  const stats = computeAnalytics(
    [
      sub({ submittedAtUtc: '2026-10-03T12:10:00Z' }), // 20 min ago -> last window
      sub({ submittedAtUtc: '2026-10-03T12:20:00Z' }), // 10 min ago -> last window
      sub({ submittedAtUtc: '2026-10-03T10:40:00Z' }), // 1h50m ago -> the window before
      sub({ submittedAtUtc: '2026-10-01T00:00:00Z' }), // too old
      sub({ submittedAtUtc: 'not a date' }),
    ],
    NOW,
  )

  assert.equal(stats.perHour[23], 2)
  assert.equal(stats.perHour[22], 1)
  assert.equal(stats.perHour.reduce((a, b) => a + b, 0), 3)
  assert.equal(stats.peakHourCount, 2)
})

test('moveInOrder moves one place and refuses to fall off either end', () => {
  const order = ['a', 'b', 'c']

  assert.deepEqual(moveInOrder(order, 'b', -1), ['b', 'a', 'c'])
  assert.deepEqual(moveInOrder(order, 'b', 1), ['a', 'c', 'b'])
  assert.equal(moveInOrder(order, 'a', -1), order)
  assert.equal(moveInOrder(order, 'c', 1), order)
  assert.equal(moveInOrder(order, 'nope', 1), order)
  assert.deepEqual(order, ['a', 'b', 'c']) // never mutates its input
})

test('splitRoom separates now playing, pending approval and the live queue', () => {
  const room = {
    nowPlayingId: 'n',
    submissions: [sub({ id: 'n', status: 'playing' }), sub({ id: 'p', status: 'pending' }), sub({ id: 'q', status: 'queued' })],
    orderedQueue: [sub({ id: 'q', status: 'queued' }), sub({ id: 'p', status: 'pending' })],
  }

  const { nowPlaying, pending, queue } = splitRoom(room)

  assert.equal(nowPlaying.id, 'n')
  assert.deepEqual(pending.map((s) => s.id), ['p'])
  assert.deepEqual(queue.map((s) => s.id), ['q'])
  assert.deepEqual(splitRoom(null), { nowPlaying: null, pending: [], queue: [] })
})

test('describeTrack falls back from title to url to a placeholder', () => {
  assert.equal(describeTrack({ title: 'Song', artist: 'Band' }), 'Band - Song')
  assert.equal(describeTrack({ title: '', url: 'https://x.test/a' }), 'https://x.test/a')
  assert.equal(describeTrack({}), 'Untitled')
})
