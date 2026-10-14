(function (root) {
  "use strict";
  var MAX_FILE = 64 * 1024 * 1024, MAX_LIBRARY = 512 * 1024 * 1024, database;
  function open() {
    if (database) return database;
    database = new Promise(function (resolve, reject) {
      var request = indexedDB.open("spectralis-local-audio", 2);
      request.onupgradeneeded = function () {
        var db = request.result;
        if (!db.objectStoreNames.contains("tracks")) db.createObjectStore("tracks", { keyPath: "id" });
        if (!db.objectStoreNames.contains("files")) db.createObjectStore("files");
        if (!db.objectStoreNames.contains("recent")) db.createObjectStore("recent", { keyPath: "id" });
      };
      request.onsuccess = function () { request.result.onversionchange = function () { request.result.close(); database = null; }; resolve(request.result); };
      request.onerror = function () { database = null; reject(new Error("This browser could not open your local library. Check its site-storage settings.")); };
      request.onblocked = function () { database = null; reject(new Error("Close other Spectralis tabs and try again.")); };
    });
    return database;
  }
  async function read(store, id) {
    var db = await open();
    return new Promise(function (resolve, reject) {
      var request = db.transaction(store).objectStore(store)[id === undefined ? "getAll" : "get"](id);
      request.onsuccess = function () { resolve(request.result); };
      request.onerror = function () { reject(request.error); };
    });
  }
  async function write(stores, action) {
    var db = await open();
    return new Promise(function (resolve, reject) {
      var tx = db.transaction(stores, "readwrite"), failure;
      var fail = function (message) { failure = new Error(message); tx.abort(); };
      try { action(tx, fail); } catch (err) { failure = err; tx.abort(); }
      tx.oncomplete = resolve;
      tx.onabort = tx.onerror = function () { reject(failure || new Error("Your browser could not save this change. Check its available site storage.")); };
    });
  }
  function link(url, title) {
    var parsed;
    try { parsed = new URL(url.trim()); } catch (_) { throw new Error("Paste an http, https or Spotify track link."); }
    if (!["http:", "https:", "spotify:"].includes(parsed.protocol)) throw new Error("Paste an http, https or Spotify track link.");
    return { kind: "link", url: parsed.href, title: (title || "").trim().slice(0, 240) || parsed.href, size: 0 };
  }
  function forAccount(subject) {
    if (typeof subject !== "string" || !subject.trim()) throw new Error("Sign in to use your library.");
    var owner = subject;
    var key = function (id) { return JSON.stringify([owner, id]); };
    var owns = function (item) { return item && item.owner === owner; };
    async function list() { return (await read("tracks")).filter(owns).sort(function (a, b) { return b.addedAt - a.addedAt; }); }
    async function add(file) {
      if (!/\.(mp3|m4a|wav|flac|ogg|opus|aac|aiff?)$/i.test(file.name)) throw new Error("Choose an MP3, M4A, WAV, FLAC, Ogg, Opus, AAC or AIFF file.");
      if (!file.size || file.size > MAX_FILE) throw new Error("Audio files must be between 1 byte and 64 MB.");
      var hash = await crypto.subtle.digest("SHA-256", await file.arrayBuffer());
      var id = key(Array.from(new Uint8Array(hash), function (b) { return b.toString(16).padStart(2, "0"); }).join(""));
      var track = { id: id, owner: owner, kind: "file", name: file.name, title: file.name.replace(/\.[^.]+$/, ""), size: file.size, addedAt: Date.now() };
      await write(["tracks", "files"], function (tx, fail) {
        // One transaction serializes imports across tabs; the storage cap is browser-wide.
        var tracks = tx.objectStore("tracks"), check = tracks.getAll();
        check.onsuccess = function () {
          var existing = check.result.find(function (t) { return t.id === id; });
          if (existing) { track = existing; return; }
          if (check.result.reduce(function (sum, t) { return sum + (t.size || 0); }, 0) + file.size > MAX_LIBRARY) return fail("Your local library is full (512 MB). Remove a few tracks before importing more.");
          tracks.put(track); tx.objectStore("files").put(file, id);
        };
      });
      return track;
    }
    async function saveLink(url, title) {
      var item = Object.assign(link(url, title), { owner: owner, addedAt: Date.now() }); item.id = key(item.url);
      await write(["tracks"], function (tx) { tx.objectStore("tracks").put(item); });
      return item;
    }
    async function remember(item) {
      var entry = item.kind === "link" ? link(item.url, item.title) : item;
      if (entry.kind !== "link" && !owns(entry)) throw new Error("This track belongs to another library.");
      entry = Object.assign({}, entry, { id: entry.kind === "link" ? key(entry.url) : entry.id, owner: owner, putOnAt: Date.now() });
      await write(["recent"], function (tx) {
        var store = tx.objectStore("recent"), check = store.getAll();
        check.onsuccess = function () {
          check.result.filter(function (t) { return owns(t) && t.id !== entry.id; }).sort(function (a, b) { return b.putOnAt - a.putOnAt; }).slice(49).forEach(function (t) { store.delete(t.id); });
          store.put(entry);
        };
      });
    }
    // Version 1 files had no account owner. Recovery is explicit on shared browsers.
    async function recover() {
      await write(["tracks", "files"], function (tx) {
        var tracks = tx.objectStore("tracks"), files = tx.objectStore("files"), check = tracks.getAll();
        check.onsuccess = function () { check.result.filter(function (t) { return !t.owner; }).forEach(function (t) {
          var oldId = t.id, file = files.get(oldId);
          file.onsuccess = function () {
            if (file.result) { t.id = key(oldId); t.owner = owner; t.kind = "file"; tracks.put(t); files.put(file.result, t.id); }
            tracks.delete(oldId); files.delete(oldId);
          };
        }); };
      });
    }
    return {
      list: list, add: add, saveLink: saveLink, remember: remember, recover: recover,
      hasLegacy: async function () { return (await read("tracks")).some(function (t) { return !t.owner; }); },
      recent: async function () { return (await read("recent")).filter(owns).sort(function (a, b) { return b.putOnAt - a.putOnAt; }); },
      file: async function (id) { return owns(await read("tracks", id)) ? read("files", id) : undefined; },
      remove: async function (id) {
        if (!owns(await read("tracks", id))) return;
        await write(["tracks", "files", "recent"], function (tx) { tx.objectStore("tracks").delete(id); tx.objectStore("files").delete(id); tx.objectStore("recent").delete(id); });
      },
      clearRecent: async function () {
        await write(["recent"], function (tx) { var store = tx.objectStore("recent"), check = store.getAll(); check.onsuccess = function () { check.result.filter(owns).forEach(function (t) { store.delete(t.id); }); }; });
      }
    };
  }
  root.LocalAudioLibrary = { forAccount: forAccount, link: link };
})(window);
