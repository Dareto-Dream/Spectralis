// Thin client for the owner side of the Streamer Queue API (see backend/src/main.rs, /streamer-queue/v1
// and /streamer-queue/v2). Every call carries the room's owner token.

export class SqApiError extends Error {
  constructor(status, message) {
    super(message)
    this.name = 'SqApiError'
    this.status = status
  }
}

async function readError(res) {
  const text = await res.text().catch(() => '')
  try {
    const parsed = JSON.parse(text)
    if (parsed && typeof parsed.error === 'string' && parsed.error) return parsed.error
  } catch {
    // not JSON
  }
  return text || `Request failed (${res.status})`
}

export function createSqClient({ baseUrl, roomId, ownerToken, fetchImpl = (...a) => fetch(...a) }) {
  const room = `${baseUrl}/streamer-queue/v1/rooms/${encodeURIComponent(roomId)}`

  async function call(url, { method = 'GET', body } = {}) {
    const res = await fetchImpl(url, {
      method,
      headers: body === undefined ? undefined : { 'content-type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
    if (!res.ok) throw new SqApiError(res.status, await readError(res))
    return res.json()
  }

  const owned = (extra = {}) => ({ ownerToken, ...extra })
  const sub = (id, action) => `${room}/submissions/${encodeURIComponent(id)}/${action}`

  return {
    getRoom: () => call(`${room}?ownerToken=${encodeURIComponent(ownerToken)}`),
    approve: (id) => call(sub(id, 'approve'), { method: 'POST', body: owned() }),
    reject: (id) => call(sub(id, 'reject'), { method: 'POST', body: owned() }),
    setStatus: (id, status) => call(sub(id, 'status'), { method: 'POST', body: owned({ status }) }),
    remove: (id) =>
      call(`${room}/submissions/${encodeURIComponent(id)}?ownerToken=${encodeURIComponent(ownerToken)}`, {
        method: 'DELETE',
      }),
    setOrder: (ids) => call(`${room}/order`, { method: 'PUT', body: owned({ order: ids }) }),
    setNowPlaying: (id) => call(`${room}/now-playing`, { method: 'POST', body: owned({ submissionId: id }) }),
    setAccepting: (accepting) =>
      call(`${room}/settings`, { method: 'PUT', body: owned({ acceptingSubmissions: accepting }) }),
    /** action: "rotate" (returns the new key, shown once) or "revoke". */
    webhookKey: (action) =>
      call(`${baseUrl}/streamer-queue/v2/rooms/${encodeURIComponent(roomId)}/webhook/key`, {
        method: 'POST',
        body: owned({ action }),
      }),
  }
}
