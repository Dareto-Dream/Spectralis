import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createSqClient, SqApiError } from './sqClient.js'

function harness(respond = () => new Response('{}')) {
  const calls = []
  const fetchImpl = async (url, init = {}) => {
    calls.push({ url, method: init.method || 'GET', body: init.body ? JSON.parse(init.body) : undefined })
    return respond()
  }
  const client = createSqClient({ baseUrl: 'https://api.test', roomId: 'room 1', ownerToken: 'tok/en', fetchImpl })
  return { client, calls }
}

test('getRoom puts the owner token in the query, escaped', async () => {
  const { client, calls } = harness()
  await client.getRoom()
  assert.equal(calls[0].url, 'https://api.test/streamer-queue/v1/rooms/room%201?ownerToken=tok%2Fen')
  assert.equal(calls[0].method, 'GET')
})

test('moderation calls hit the right routes with the owner token in the body', async () => {
  const { client, calls } = harness()

  await client.approve('s1')
  await client.reject('s2')
  await client.setStatus('s3', 'played')
  await client.setOrder(['b', 'a'])
  await client.setNowPlaying('s4')
  await client.setAccepting(false)

  assert.deepEqual(
    calls.map((c) => [c.method, c.url.replace('https://api.test/streamer-queue/v1/rooms/room%201', ''), c.body]),
    [
      ['POST', '/submissions/s1/approve', { ownerToken: 'tok/en' }],
      ['POST', '/submissions/s2/reject', { ownerToken: 'tok/en' }],
      ['POST', '/submissions/s3/status', { ownerToken: 'tok/en', status: 'played' }],
      ['PUT', '/order', { ownerToken: 'tok/en', order: ['b', 'a'] }],
      ['POST', '/now-playing', { ownerToken: 'tok/en', submissionId: 's4' }],
      ['PUT', '/settings', { ownerToken: 'tok/en', acceptingSubmissions: false }],
    ],
  )
})

test('remove is a DELETE with the token in the query', async () => {
  const { client, calls } = harness()
  await client.remove('s 1')
  assert.equal(calls[0].method, 'DELETE')
  assert.ok(calls[0].url.endsWith('/submissions/s%201?ownerToken=tok%2Fen'))
})

test('webhook key management uses the v2 route', async () => {
  const { client, calls } = harness(() => new Response('{"webhookKey":"wk_1"}'))
  const result = await client.webhookKey('rotate')

  assert.equal(result.webhookKey, 'wk_1')
  assert.equal(calls[0].url, 'https://api.test/streamer-queue/v2/rooms/room%201/webhook/key')
  assert.deepEqual(calls[0].body, { ownerToken: 'tok/en', action: 'rotate' })
})

test('errors carry the status and the server message', async () => {
  const { client } = harness(() => new Response('{"error":"Owner token invalid.","status":403}', { status: 403 }))

  await assert.rejects(client.getRoom(), (err) => err instanceof SqApiError && err.status === 403 && err.message === 'Owner token invalid.')
})

test('non-json failures still produce a readable message', async () => {
  const { client } = harness(() => new Response('bad gateway', { status: 502 }))
  await assert.rejects(client.getRoom(), (err) => err.status === 502 && err.message === 'bad gateway')
})
