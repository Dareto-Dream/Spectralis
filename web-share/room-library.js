(function (root) {
  "use strict";
  root.RoomLibrary = function (options) {
    var base = new URL(options.baseUrl), tokenKey = "spectralis.room-account:" + base.origin;
    var token = "", user = null, enabled = false, busy = false, connecting = false, generation = 0, previewUrl = "", renderVersion = 0;
    var el = {};
    ["libraryButton", "libraryDialog", "libraryFiles", "libraryImport", "libraryList", "libraryStatus", "librarySummary", "libraryAccount", "librarySignIn", "librarySignOut", "libraryPreview"].forEach(function (id) { el[id] = document.getElementById(id); });
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
    async function render() {
      var version = ++renderVersion;
      var room = options.room();
      el.libraryAccount.textContent = !enabled ? "Development rooms have no account sign-in. You can keep a local library; uploading is available in production." : user ? "Signed in as " + (user.name || user.preferred_username || "Listener") : "Sign in to add your audio to the room.";
      el.librarySignIn.hidden = !enabled || !!user;
      if (enabled && room.code && !room.allowUploads) el.libraryAccount.textContent += " The host has turned off audio uploads.";
      el.librarySignIn.textContent = connecting ? "Cancel sign-in" : "Sign in with Ward";
      el.librarySignOut.hidden = !user;
      el.librarySignOut.disabled = busy;
      el.libraryImport.disabled = busy;
      var tracks;
      try { tracks = await root.LocalAudioLibrary.list(); } catch (err) { status(err.message); return; }
      if (version !== renderVersion) return;
      tracks.sort(function (a, b) { return b.addedAt - a.addedAt; });
      el.librarySummary.textContent = tracks.length + (tracks.length === 1 ? " track" : " tracks") + " · " + (tracks.reduce(function (sum, t) { return sum + t.size; }, 0) / 1048576).toFixed(1) + " / 512 MB";
      el.libraryList.replaceChildren();
      if (!tracks.length) { var empty = document.createElement("li"); empty.className = "queue-empty"; empty.textContent = "Bring a few favourites. They stay in this browser until you add them to a room."; el.libraryList.appendChild(empty); }
      tracks.forEach(function (track) {
        var row = document.createElement("li"), info = document.createElement("div"), title = document.createElement("strong"), detail = document.createElement("span"), actions = document.createElement("div");
        title.textContent = track.title; detail.textContent = track.name + " · " + (track.size / 1048576).toFixed(1) + " MB";
        info.append(title, detail); actions.className = "library-track-actions";
        actions.appendChild(button("Preview", async function () {
          stopPreview(); var file = await root.LocalAudioLibrary.file(track.id);
          if (!file) throw new Error("This file is no longer in your browser library.");
          previewUrl = URL.createObjectURL(file); el.libraryPreview.src = previewUrl; el.libraryPreview.hidden = false;
          el.libraryPreview.setAttribute("aria-label", "Preview " + track.title);
          try { await el.libraryPreview.play(); } catch (_) { status("Your browser cannot preview this format. You can still add it to the room."); }
        }));
        var add = button("Add to queue", async function () {
          var current = options.room();
          if (busy || !user || !enabled || !current.code || !current.allowUploads) return;
          busy = true; render(); status("Sending " + track.title + " to the room…");
          try {
            var file = await root.LocalAudioLibrary.file(track.id);
            if (!file) throw new Error("This file is no longer in your browser library.");
            var body = new FormData(); body.append("file", file, track.name); body.append("title", track.title);
            var result = await request("/shared-play/v2/sessions/" + encodeURIComponent(current.code) + "/uploads", { method: "POST", body: body, signal: AbortSignal.timeout(90000) });
            if (options.room().code === current.code) options.onQueue(result.queue);
            status(track.title + " was added to the queue. Your library copy stays here.");
          } finally { busy = false; render(); }
        });
        add.disabled = busy || !user || !enabled || !room.code || !room.allowUploads;
        add.title = !enabled ? "Uploads are unavailable in development rooms" : !user ? "Sign in to upload audio" : !room.allowUploads ? "The host has turned off audio uploads" : "Send a temporary copy to this room";
        var remove = button("Remove", async function () { stopPreview(); await root.LocalAudioLibrary.remove(track.id); status("Removed from this browser. Tracks already sent to a room stay in its queue."); await render(); });
        remove.disabled = busy; actions.append(add, remove); row.append(info, actions); el.libraryList.appendChild(row);
      });
    }
    el.libraryButton.addEventListener("click", function () { el.libraryDialog.showModal(); render(); });
    el.libraryDialog.addEventListener("close", stopPreview);
    el.libraryImport.addEventListener("click", function () { el.libraryFiles.click(); });
    el.libraryFiles.addEventListener("change", async function () {
      busy = true; render(); var files = Array.from(el.libraryFiles.files), imported = 0;
      try { for (var file of files) { status("Saving " + file.name + " in this browser…"); await root.LocalAudioLibrary.add(file); imported++; } status(imported + " files saved locally. Nothing was uploaded."); }
      catch (err) { status((imported ? imported + " files saved. " : "") + err.message); }
      finally { busy = false; el.libraryFiles.value = ""; render(); }
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
      stopPreview(); render(); status("Signed out. Files saved in this browser remain here."); options.onAuth();
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
