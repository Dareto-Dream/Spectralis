import assert from 'node:assert/strict'
import { test } from 'node:test'
import { compareVersions, normalizeReleases, splitReleases, trustedUrl } from './releases.js'

const base = 'https://github.com/Dareto-Dream/Spectralis/releases/download'
const row = (version, channel = 'stable', platforms = { 'win-x64': { installer: `${base}/v${version}/Spectralis-win-x64-Setup.exe` } }) =>
  ({ version, channel, date: '2026-11-15', notesUrl: `https://github.com/Dareto-Dream/Spectralis/releases/tag/v${version}`, platforms })

test('versions order the way semver says', () => {
  const sorted = ['7.1.1', '7.1.1-beta.10', '7.1.0', '7.1.1-beta.2', '7.10.0'].sort(compareVersions)
  assert.deepEqual(sorted, ['7.1.0', '7.1.1-beta.2', '7.1.1-beta.10', '7.1.1', '7.10.0'])
})

test('only our own download locations are linked', () => {
  assert.equal(trustedUrl(`${base}/v7.1.0/Setup.exe`), `${base}/v7.1.0/Setup.exe`)
  assert.equal(trustedUrl('https://spectralis-cdn.deltavdevs.com/Spectralis-win-x64-Setup.exe'), 'https://spectralis-cdn.deltavdevs.com/Spectralis-win-x64-Setup.exe')
  assert.equal(trustedUrl('https://evil.example/Setup.exe'), '')
  assert.equal(trustedUrl('javascript:alert(1)'), '')
  assert.equal(trustedUrl('https://github.com/Dareto-Dream/Spectralis/releases/../../../evil/x'), '')
  assert.equal(trustedUrl(undefined), '')
})

test('the index is cleaned and sorted newest first', () => {
  const rows = normalizeReleases([
    row('7.0.1'),
    row('7.1.0'),
    { version: 'nope' },
    null,
    row('9.9.9', 'stable', { 'win-x64': { installer: 'https://evil.example/x.exe' } }),
  ])
  assert.deepEqual(rows.map(r => r.version), ['9.9.9', '7.1.0', '7.0.1'])
  assert.deepEqual(rows[0].downloads, {}, 'an untrusted link is dropped, not shown')
  assert.equal(normalizeReleases('garbage').length, 0)
})

test('the page gets the latest, the older ones, and a beta only while it is ahead', () => {
  const ahead = splitReleases(normalizeReleases([row('7.1.1-beta.3', 'beta'), row('7.1.0'), row('7.0.1'), row('7.0.0', 'stable', {})]))
  assert.equal(ahead.latest.version, '7.1.0')
  assert.deepEqual(ahead.older.map(r => r.version), ['7.0.1'], 'a release with nothing to download is not listed')
  assert.equal(ahead.beta.version, '7.1.1-beta.3')

  const overtaken = splitReleases(normalizeReleases([row('7.1.1-beta.3', 'beta'), row('7.1.1')]))
  assert.equal(overtaken.beta, null)

  assert.deepEqual(splitReleases([]), { latest: null, older: [], beta: null })
})
