(function (root) {
  "use strict";
  root.RoomLibrary = function (options) {
    var base = new URL(options.baseUrl), tokenKey = "spectralis.room-account:" + base.origin;
    var token = "", user = null, enabled = false, busy = false, connecting = false, generation = 0, previewUrl = "", renderVersion = 0, view = "all";
    var el = {};
    ["libraryButton", "libraryDialog", "libraryFiles", "libraryImport", "libraryList", "libraryStatus", "librarySummary", "libraryAccount", "librarySignIn", "librarySignOut", "libraryPreview", "libraryCollection", "librarySaveOptions", "libraryAll", "libraryRecent", "libraryClearRecent", "libraryRecover", "librarySaveLink", "libraryLinkTitle", "libraryQueueLink", "libraryLinkHint", "queueAddForm", "queueUrlInput"].forEach(function (id) { el[id] = document.getElementById(id); });
    function headers(url, initial) {
      var result = Object.assign({}, initial || {});
      if (enabled && token && new URL(url, base).origin === base.origin) result.Authorization = "Bearer " + token;
      return result;
    }
    async function request(path, init) {
      var url = new URL(path, base).href;
      var response = await fetch(url, Object.assign({ cache: "no-store", credentials: "omit", signal: AbortSignal.timeout(15000) }, init || {}, { headers: headers(url, init && init.headers) }));
      var data = await response.json().catch(function () { return {}; });
      if (!response.ok) {
        if (response.status === 401 && token) { token = ""; user = null; try { sessionStorage.removeItem(tokenKey); } catch (_) {} render(); }
        throw new Error(data.error || data.message || "The request could not be completed.");
      }
      return data;
    }
    function status(message) { el.libraryStatus.textContent = message; }
    function button(label, action) {
      var b = document.createElement("button"); b.type = "button"; b.textContent = label;
      b.addEventListener("click", function () { Promise.resolve().then(action).catch(function (err) { status(err.message); }); }); return b;
    }
    function stopPreview() {
      el.libraryPreview.pause(); el.libraryPreview.removeAttribute("src"); el.libraryPreview.load(); el.libraryPreview.hidden = true;
      if (previewUrl) URL.revokeObjectURL(previewUrl); previewUrl = "";
    }
    function accountLibrary() { return enabled && user && user.sub ? root.LocalAudioLibrary.forAccount(user.sub) : null; }
    async function run(action) {
      if (busy) return;
      busy = true; render();
      try { await action(); } catch (err) { status(err.message); }
      finally { busy = false; render(); }
    }
    async function remember(db, track) {
      if (!db) return;
      try { await db.remember(track); }
      catch (_) { status("Added to the queue, but this browser could not save it to recent picks."); }
    }
    async function putOn(track) {
      var current = options.room(), db = accountLibrary();
      if (track.kind === "link") {
        if (!current.allowLinks) throw new Error("The host has turned off link requests for you.");
        var result = await options.addLink(track.url, track.title);
        status(result.confirmed ? track.title + " was added to the queue." : "Request sent to the host.");
        if (result.confirmed) await remember(db, track);
      } else {
        if (!db || !current.code || !current.allowUploads) throw new Error("Audio uploads are unavailable in this room.");
        var file = await db.file(track.id);
        if (!file) throw new Error("This file is no longer in your browser library.");
        var body = new FormData(); body.append("file", file, track.name); body.append("title", track.title);
        status("Sending " + track.title + " to the room...");
        var upload = await request("/shared-play/v2/sessions/" + encodeURIComponent(current.code) + "/uploads", { method: "POST", body: body, signal: AbortSignal.timeout(90000) });
        if (options.room().code === current.code) options.onQueue(upload.queue);
        status(track.title + " was added to the queue. Your library copy stays here.");
        await remember(db, track);
      }
    }
    async function render() {
      var version = ++renderVersion, room = options.room(), db = accountLibrary();
      el.libraryAccount.textContent = !enabled ? "Add a link to the room. Account libraries are unavailable in development rooms." : db ? "Signed in as " + (user.name || user.preferred_username || "Listener") : "Share a link, or sign in for your files, saved links and recent picks.";
      el.librarySignIn.hidden = !enabled || !!user;
      if (db && room.code && !room.allowUploads) el.libraryAccount.textContent += " The host has turned off audio uploads.";
      el.librarySignIn.textContent = connecting ? "Cancel sign-in" : "Sign in with Ward";
      el.librarySignOut.hidden = !user; el.librarySignOut.disabled = busy;
      el.libraryCollection.hidden = !db; el.librarySaveOptions.hidden = !db;
      el.libraryQueueLink.disabled = busy || !room.allowLinks;
      el.librarySaveLink.disabled = busy; el.libraryImport.disabled = busy;
      el.libraryClearRecent.disabled = busy; el.libraryRecover.disabled = busy;
      el.libraryLinkHint.textContent = room.allowLinks ? "The host controls what plays next." : "The host is not accepting link requests from you.";
      el.libraryAll.setAttribute("aria-pressed", String(view === "all")); el.libraryRecent.setAttribute("aria-pressed", String(view === "recent"));
      el.libraryClearRecent.hidden = view !== "recent";
      if (!db) { el.libraryList.replaceChildren(); el.librarySummary.textContent = ""; stopPreview(); return; }
      try {
        var saved = await db.list(), tracks = view === "recent" ? await db.recent() : saved, legacy = await db.hasLegacy();
        if (version !== renderVersion) return;
        el.libraryRecover.hidden = !legacy;
        el.librarySummary.textContent = saved.filter(function (t) { return t.kind === "file"; }).length + " files \u00b7 " + saved.filter(function (t) { return t.kind === "link"; }).length + " links \u00b7 " + (saved.reduce(function (sum, t) { return sum + t.size; }, 0) / 1048576).toFixed(1) + " MB";
        el.libraryList.replaceChildren();
        if (!tracks.length) { var empty = document.createElement("li"); empty.className = "queue-empty"; empty.textContent = view === "recent" ? "Your last 50 accepted picks will appear here. Put something on to start." : "A little collection of your favourites. Add audio files or save a link above."; el.libraryList.appendChild(empty); }
        tracks.forEach(function (track) {
          var row = document.createElement("li"), info = document.createElement("div"), title = document.createElement("strong"), detail = document.createElement("span"), actions = document.createElement("div");
          title.textContent = track.title;
          detail.textContent = track.kind === "link" ? track.url : track.name + " \u00b7 " + (track.size / 1048576).toFixed(1) + " MB";
          if (view === "recent") detail.textContent += " \u00b7 " + new Date(track.putOnAt).toLocaleDateString();
          info.append(title, detail); actions.className = "library-track-actions";
          if (track.kind === "file") actions.appendChild(button("Preview", async function () {
            stopPreview(); var file = await db.file(track.id);
            if (!file || !accountLibrary()) return;
            previewUrl = URL.createObjectURL(file); el.libraryPreview.src = previewUrl; el.libraryPreview.hidden = false;
            el.libraryPreview.setAttribute("aria-label", "Preview " + track.title);
            try { await el.libraryPreview.play(); } catch (_) { status("Your browser cannot preview this format. You can still add it to the room."); }
          }));
          var add = button("Add to queue", function () { return run(function () { return putOn(track); }); });
          add.disabled = busy || !room.code || (track.kind === "link" ? !room.allowLinks : !room.allowUploads);
          actions.appendChild(add);
          if (view === "all") {
            var remove = button("Remove", function () { return run(async function () { stopPreview(); await db.remove(track.id); status("Removed from this browser. Tracks already sent to a room stay in its queue."); }); });
            remove.disabled = busy; actions.appendChild(remove);
          } else if (track.kind === "link" && !saved.some(function (t) { return t.id === track.id; })) {
            var save = button("Save link", function () { return run(async function () { await db.saveLink(track.url, track.title); status("Link saved to your library."); }); });
            save.disabled = busy; actions.appendChild(save);
          }
          row.append(info, actions); el.libraryList.appendChild(row);
        });
      } catch (err) { if (version === renderVersion) status(err.message); }
    }
    el.libraryButton.addEventListener("click", function () { el.libraryDialog.showModal(); render(); });
    el.libraryDialog.addEventListener("close", stopPreview);
    el.libraryAll.addEventListener("click", function () { view = "all"; render(); });
    el.libraryRecent.addEventListener("click", function () { view = "recent"; render(); });
    el.libraryClearRecent.addEventListener("click", function () { run(async function () { var db = accountLibrary(); if (db) { await db.clearRecent(); status("Recent picks cleared."); } }); });
    el.libraryRecover.addEventListener("click", function () { run(async function () { var db = accountLibrary(); if (db) { await db.recover(); status("Previous browser files added to your library."); } }); });
    el.queueAddForm.addEventListener("submit", function (event) {
      event.preventDefault(); run(async function () {
        var track = root.LocalAudioLibrary.link(el.queueUrlInput.value, accountLibrary() ? el.libraryLinkTitle.value : "");
        await putOn(track); el.queueUrlInput.value = ""; el.libraryLinkTitle.value = "";
      });
    });
    el.librarySaveLink.addEventListener("click", function () { run(async function () {
      var db = accountLibrary(); if (!db) return;
      await db.saveLink(el.queueUrlInput.value, el.libraryLinkTitle.value);
      el.queueUrlInput.value = ""; el.libraryLinkTitle.value = ""; view = "all"; status("Link saved to your library. Add it to the queue whenever you like.");
    }); });
    el.libraryImport.addEventListener("click", function () { if (accountLibrary()) el.libraryFiles.click(); });
    el.libraryFiles.addEventListener("change", function () {
      var db = accountLibrary(), files = Array.from(el.libraryFiles.files); el.libraryFiles.value = "";
      if (!db) return;
      run(async function () { var imported = 0;
        for (var file of files) { status("Saving " + file.name + " in this browser..."); await db.add(file); imported++; }
        view = "all"; status(imported + " files saved locally. Nothing was uploaded.");
      });
    });
    el.librarySignIn.addEventListener("click", async function () {
      if (!enabled) return;
      if (connecting) { generation++; connecting = false; status("Sign-in cancelled."); render(); return; }
      // Open from the click so popup blockers do not swallow the account step.
      var popup = window.open("about:blank", "spectralis-account");
      if (popup) popup.opener = null;
      connecting = true; var attempt = ++generation; render();
      try {
        var link = await request("/player/v1/connect", { method: "POST" });
        var verification = new URL(link.verificationUrl);
        if (verification.origin !== "https://player.deltavdevs.com" || verification.pathname !== "/connect/" + link.code) throw new Error("The sign-in destination was not recognized.");
        if (!popup) throw new Error("Allow popups for this site, then choose Sign in again.");
        popup.location.replace(verification.href); status("Finish signing in in the account tab. This room will reconnect automatically.");
        for (var i = 0; i < 300 && generation === attempt; i++) {
          await new Promise(function (resolve) { setTimeout(resolve, 2000); });
          var result = await request("/player/v1/connect/" + encodeURIComponent(link.code), { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ secret: link.secret }) });
          if (generation !== attempt) return;
          if (result.status === "connected") {
            token = result.token; user = result.user;
            try { sessionStorage.setItem(tokenKey, token); } catch (_) { /* This tab can still use the in-memory connection. */ }
            status("Account connected. Choose a track to add to the room."); options.onAuth(); return;
          }
        }
        if (generation === attempt) status("Sign-in expired. Try again when you are ready.");
      } catch (err) { if (generation === attempt) status(err.message); }
      finally { if (generation === attempt) connecting = false; render(); }
    });
    el.librarySignOut.addEventListener("click", async function () {
      try { await request("/player/v1/me", { method: "DELETE" }); }
      catch (err) { status(err.message); return; }
      token = ""; user = null; generation++; try { sessionStorage.removeItem(tokenKey); } catch (_) {}
      stopPreview(); view = "all"; el.queueUrlInput.value = ""; el.libraryLinkTitle.value = ""; render(); status("Signed out. Your library will be here when you sign in again."); options.onAuth();
    });
    window.addEventListener("pagehide", function () { generation++; stopPreview(); });
    return {
      init: async function () {
        try {
          var config = await request("/shared-play/v2/config"); enabled = config.accountsEnabled === true;
          if (enabled) { try { token = sessionStorage.getItem(tokenKey) || ""; } catch (_) {} if (token) user = await request("/player/v1/me"); }
        } catch (err) { status(err.message); }
        render();
      }, headers: headers, token: function () { return enabled ? token : ""; }, refresh: function () { if (el.libraryDialog.open) render(); }
    };
  };
})(window);
