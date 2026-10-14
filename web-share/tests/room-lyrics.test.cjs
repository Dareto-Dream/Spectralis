const { test } = require("node:test");
const assert = require("node:assert/strict");
const lyrics = require("../room-lyrics.js");

test("missing, empty and malformed content never exposes a lyrics mode", () => {
  for (const value of [
    null,
    undefined,
    "",
    [],
    [{ text: "No timestamp" }],
    [{ timeSeconds: 0, text: " " }],
    [{ timeSeconds: Infinity, text: "Invalid" }],
  ]) {
    assert.equal(lyrics.mode(lyrics.normalize(value)), null);
  }
});

test("legacy line-only manifests lock to line and keep timestamp zero", () => {
  const lines = lyrics.normalize([
    { timeSeconds: 10, text: "Later" },
    { timeSeconds: 0, text: "First" },
  ]);
  assert.equal(lyrics.mode(lines), "line");
  assert.equal(lyrics.indexAt(lines, 0), 0);
  assert.equal(lyrics.indexAt(lines, 9.9), 0);
  assert.equal(lyrics.indexAt(lines, 10), 1);
  assert.equal(lines[0].text, "First");
});

test("word mode uses only supplied timings, including single-word lines", () => {
  const lines = lyrics.normalize([
    {
      timeSeconds: 5,
      text: "Hello world",
      words: [
        { timeSeconds: 5.5, text: "Hello " },
        { timeSeconds: 6, text: "world" },
      ],
    },
  ]);
  assert.equal(lyrics.mode(lines), "word");
  assert.equal(lyrics.indexAt(lines, 4), -1);
  assert.equal(lyrics.indexAt(lines[0].words, 5), -1);
  assert.equal(lyrics.indexAt(lines[0].words, 5.5), 0);
  assert.equal(lyrics.indexAt(lines[0].words, 6), 1);
  assert.equal(lines[0].words.map((w) => w.text).join(""), "Hello world");
  assert.equal(
    lyrics.mode(
      lyrics.normalize([
        {
          timeSeconds: 0,
          text: "Hello",
          words: [{ timeSeconds: 1, text: "Hello" }],
        },
      ]),
    ),
    "word",
  );
});

test("mixed enhanced LRC stays word timed without fabricating words for plain lines", () => {
  const lines = lyrics.normalize([
    { timeSeconds: 1, text: "One line" },
    { timeSeconds: 4, text: "Hi", words: [{ timeSeconds: 4, text: "Hi" }] },
  ]);
  assert.equal(lyrics.mode(lines), "word");
  assert.deepEqual(lines[0].words, []);
  assert.equal(
    lyrics.mode(
      lyrics.normalize([
        { timeSeconds: 0, text: "Plain", words: [{ text: "No timestamp" }] },
      ]),
    ),
    "line",
  );
});
