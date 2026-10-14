/* The host parses LRC. The room only renders timestamps it actually supplies. */
(function (root) {
  "use strict";
  function timeOf(value) {
    var time = value && (value.timeSeconds ?? value.startTime ?? value.time);
    return typeof time === "number" && Number.isFinite(time) && time >= 0
      ? time
      : null;
  }
  function normalize(input) {
    if (!Array.isArray(input)) return [];
    return input
      .slice(0, 400)
      .map(function (line) {
        var time = timeOf(line);
        var text =
          line && typeof line.text === "string" ? line.text.trim() : "";
        if (time === null || !text) return null;
        var words = Array.isArray(line.words)
          ? line.words
              .slice(0, 500)
              .map(function (word) {
                var start = timeOf(word);
                return start !== null &&
                  typeof word.text === "string" &&
                  word.text.trim()
                  ? { timeSeconds: start, text: word.text }
                  : null;
              })
              .filter(Boolean)
              .sort(function (a, b) {
                return a.timeSeconds - b.timeSeconds;
              })
          : [];
        return { timeSeconds: time, text: text, words: words };
      })
      .filter(Boolean)
      .sort(function (a, b) {
        return a.timeSeconds - b.timeSeconds;
      });
  }
  function mode(lines) {
    return !lines.length
      ? null
      : lines.some(function (line) {
            return line.words.length > 0;
          })
        ? "word"
        : "line";
  }
  function indexAt(items, seconds) {
    var low = 0,
      high = items.length - 1,
      found = -1;
    while (low <= high) {
      var mid = Math.floor((low + high) / 2);
      if (items[mid].timeSeconds <= seconds + 0.0005) {
        found = mid;
        low = mid + 1;
      } else high = mid - 1;
    }
    return found;
  }
  var api = { normalize: normalize, mode: mode, indexAt: indexAt };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.RoomLyrics = api;
})(typeof window !== "undefined" ? window : globalThis);
