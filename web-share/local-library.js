(function (root) {
  "use strict";
  var MAX_FILE = 64 * 1024 * 1024, MAX_LIBRARY = 512 * 1024 * 1024;
  var database;
  function open() {
    if (database) return database;
    database = new Promise(function (resolve, reject) {
      var request = indexedDB.open("spectralis-local-audio", 1);
      request.onupgradeneeded = function () {
        request.result.createObjectStore("tracks", { keyPath: "id" });
        request.result.createObjectStore("files");
      };
      request.onsuccess = function () { request.result.onversionchange = function () { request.result.close(); database = null; }; resolve(request.result); };
      request.onerror = function () { database = null; reject(new Error("This browser could not open your local library. Check its site-storage settings.")); };
      request.onblocked = function () { reject(new Error("Close other Spectralis tabs and try again.")); };
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
  async function add(file) {
    if (!/\.(mp3|m4a|wav|flac|ogg|opus|aac|aiff?)$/i.test(file.name)) throw new Error("Choose an MP3, M4A, WAV, FLAC, Ogg, Opus, AAC or AIFF file.");
    if (!file.size || file.size > MAX_FILE) throw new Error("Audio files must be between 1 byte and 64 MB.");
    var hash = await crypto.subtle.digest("SHA-256", await file.arrayBuffer());
    var id = Array.from(new Uint8Array(hash), function (b) { return b.toString(16).padStart(2, "0"); }).join("");
    var track = { id: id, name: file.name, title: file.name.replace(/\.[^.]+$/, ""), size: file.size, addedAt: Date.now() };
    var db = await open();
    return new Promise(function (resolve, reject) {
      // One read/write transaction also serializes imports from other open tabs.
      var tx = db.transaction(["tracks", "files"], "readwrite"), failure;
      var tracks = tx.objectStore("tracks"), check = tracks.getAll();
      check.onsuccess = function () {
        if (check.result.some(function (t) { return t.id === id; })) return;
        if (check.result.reduce(function (sum, t) { return sum + t.size; }, 0) + file.size > MAX_LIBRARY) {
          failure = new Error("Your local library is full (512 MB). Remove a few tracks before importing more."); tx.abort(); return;
        }
        tracks.put(track); tx.objectStore("files").put(file, id);
      };
      tx.oncomplete = function () { resolve(track); };
      tx.onabort = tx.onerror = function () { reject(failure || new Error("Your browser could not save this file. Free some site storage and try again.")); };
    });
  }
  async function remove(id) {
    var db = await open();
    return new Promise(function (resolve, reject) {
      var tx = db.transaction(["tracks", "files"], "readwrite");
      tx.objectStore("tracks").delete(id); tx.objectStore("files").delete(id);
      tx.oncomplete = resolve; tx.onabort = tx.onerror = function () { reject(tx.error); };
    });
  }
  root.LocalAudioLibrary = { list: function () { return read("tracks"); }, file: function (id) { return read("files", id); }, add: add, remove: remove };
})(window);
