import assert from 'node:assert/strict';
import { test } from 'node:test';
import { helloFrame, interpretFrame, RoomSocket, socketUrl } from './sqRealtime.ts';
import type { SocketLike } from './sqRealtime.ts';

class FakeSocket implements SocketLike {
  sent: string[] = [];
  closed = false;
  private listeners: Record<string, ((event: { data: unknown }) => void)[]> = {};

  send(data: string) {
    this.sent.push(data);
  }
  close() {
    this.closed = true;
  }
  addEventListener(type: string, listener: (event: { data: unknown }) => void) {
    (this.listeners[type] ??= []).push(listener);
  }
  fire(type: string, data?: unknown) {
    for (const listener of this.listeners[type] ?? []) listener({ data });
  }
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

test('hello announces protocol 2 as a bot', () => {
  const hello = JSON.parse(helloFrame());
  assert.equal(hello.t, 'hello');
  assert.equal(hello.proto, 2);
  assert.equal(hello.client.kind, 'bot');
  assert.deepEqual(hello.features, []);
});

test('socket urls follow the http(s) scheme and escape the room id', () => {
  assert.equal(socketUrl('https://api.test', 'a b'), 'wss://api.test/streamer-queue/v2/rooms/a%20b/socket');
  assert.equal(socketUrl('http://localhost:3000', 'r1'), 'ws://localhost:3000/streamer-queue/v2/rooms/r1/socket');
});

test('only sq.changed and update_required mean anything to the bot', () => {
  assert.deepEqual(interpretFrame('{"t":"sq.changed","status":{}}'), { kind: 'changed' });
  assert.deepEqual(interpretFrame('{"t":"error","code":"update_required","message":"Too old."}'), {
    kind: 'update-required',
    message: 'Too old.',
  });
  assert.deepEqual(interpretFrame('{"t":"welcome"}'), { kind: 'ignore' });
  assert.deepEqual(interpretFrame('{"t":"error","code":"no_room"}'), { kind: 'ignore' });
  assert.deepEqual(interpretFrame('not json'), { kind: 'ignore' });
});

function connected(overrides: { debounceMs?: number } = {}) {
  const sockets: FakeSocket[] = [];
  let changes = 0;
  const logs: string[] = [];
  const client = new RoomSocket({
    baseUrl: 'https://api.test',
    roomId: 'r1',
    onChanged: () => changes++,
    log: (line) => logs.push(line),
    debounceMs: overrides.debounceMs ?? 10,
    createSocket: () => {
      const s = new FakeSocket();
      sockets.push(s);
      return s;
    },
  });
  client.start();
  return { client, sockets, logs, changes: () => changes };
}

test('sends hello on open', () => {
  const { client, sockets } = connected();
  sockets[0].fire('open');

  assert.equal(JSON.parse(sockets[0].sent[0]).proto, 2);
  client.stop();
});

test('a burst of change frames becomes one refresh', async () => {
  const { client, sockets, changes } = connected();
  sockets[0].fire('open');

  for (let i = 0; i < 5; i++) sockets[0].fire('message', '{"t":"sq.changed"}');
  await sleep(40);

  assert.equal(changes(), 1);
  sockets[0].fire('message', '{"t":"sq.changed"}');
  await sleep(40);
  assert.equal(changes(), 2);
  client.stop();
});

test('unrelated frames do not trigger refreshes', async () => {
  const { client, sockets, changes } = connected();
  sockets[0].fire('open');

  sockets[0].fire('message', '{"t":"welcome"}');
  sockets[0].fire('message', '{"t":"pong"}');
  await sleep(40);

  assert.equal(changes(), 0);
  client.stop();
});

test('update_required retires the client for good', async () => {
  const { client, sockets, logs } = connected();
  sockets[0].fire('open');

  sockets[0].fire('message', '{"t":"error","code":"update_required","message":"Update the bot."}');
  sockets[0].fire('close');
  await sleep(1300); // longer than the first reconnect delay

  assert.equal(client.isRetired, true);
  assert.equal(sockets.length, 1, 'must not reconnect');
  assert.ok(logs[0].includes('Update the bot.'));
});

test('a dropped connection reconnects with backoff', async () => {
  const { client, sockets } = connected();
  sockets[0].fire('open');

  sockets[0].fire('close');
  await sleep(1200);

  assert.equal(sockets.length, 2);
  client.stop();
});

test('stop closes the socket and prevents reconnects', async () => {
  const { client, sockets } = connected();
  sockets[0].fire('open');

  client.stop();
  sockets[0].fire('close');
  await sleep(1200);

  assert.equal(sockets[0].closed, true);
  assert.equal(sockets.length, 1);
});
