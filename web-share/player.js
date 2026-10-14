(function () {
  "use strict";

  var config = {
    cdnBaseUrl: window.location.origin || "https://spectralis-api.deltavdevs.com",
    channelPollIntervalMs: 5000,
    presenceIntervalMs: 8000,
    reactionIntervalMs: 2500,
    networkSlowFetchMs: 2200,
    networkVerySlowFetchMs: 5000,
    networkRecoverSuccesses: 18,
    qualitySync: {
      correctionDivisor: 18,
      hardSeekSeconds: 1.2,
      maxCorrectionRate: 0.06,
      pollIntervalMs: 1000,
      preloadLookahead: 1,
      softSyncSeconds: 0.18,
      syncIntervalMs: 250,
      targetLagSeconds: 0
    },
    optimizedSync: {
      correctionDivisor: 14,
      hardSeekSeconds: 2.6,
      maxCorrectionRate: 0.1,
      pollIntervalMs: 1800,
      preloadLookahead: 2,
      softSyncSeconds: 0.34,
      syncIntervalMs: 450,
      targetLagSeconds: 0.85
    },
    maxPreloadedPackages: 3,
    maxPackageBytes: 536870912,
    allowRichPackageFallback: true
  };

  var el = {
    audio:                document.getElementById("sharedAudio"),
    backdrop:             document.getElementById("backdrop"),
    backdropImg:          document.getElementById("backdropImg"),
    channelName:          document.getElementById("channelName"),
    channelPanel:         document.getElementById("channelPanel"),
    channelStats:         document.getElementById("channelStats"),
    channelSharedHours:   document.getElementById("channelSharedHours"),
    channelListenerHours: document.getElementById("channelListenerHours"),
    channelPeakListeners: document.getElementById("channelPeakListeners"),
    channelTopTrack:      document.getElementById("channelTopTrack"),
    codeForm:             document.getElementById("codeForm"),
    codeInput:            document.getElementById("codeInput"),
    copyButton:           document.getElementById("copyButton"),
    currentTime:          document.getElementById("currentTime"),
    disc:                 document.getElementById("disc"),
    discArt:              document.getElementById("discArt"),
    durationTime:         document.getElementById("durationTime"),
    landingError:         document.getElementById("landingError"),
    landingScreen:        document.getElementById("landingScreen"),
    listenerCount:        document.getElementById("listenerCount"),
    lyricsPanel:          document.getElementById("lyricsPanel"),
    meterCanvas:          document.getElementById("meterCanvas"),
    openAppButton:        document.getElementById("openAppButton"),
    playerShell:          document.getElementById("playerShell"),
    queueAddForm:         document.getElementById("queueAddForm"),
    queueCount:           document.getElementById("queueCount"),
    queueList:            document.getElementById("queueList"),
    queueUrlInput:        document.getElementById("queueUrlInput"),
    reactionButtons:      Array.prototype.slice.call(document.querySelectorAll("[data-reaction]")),
    reactionLayer:        document.getElementById("reactionLayer"),
    roomBadge:            document.getElementById("roomBadge"),
    rosterList:           document.getElementById("rosterList"),
    seekFill:             document.getElementById("seekFill"),
    seekTrack:            document.getElementById("seekTrack"),
    transportPrev:        document.getElementById("transportPrev"),
    transportPlay:        document.getElementById("transportPlay"),
    transportNext:        document.getElementById("transportNext"),
    transportHint:        document.getElementById("transportHint"),
    voteSkipButton:       document.getElementById("voteSkipButton"),
    statusDot:            document.getElementById("statusDot"),
    statusLine:           document.getElementById("statusLine"),
    trackArtist:          document.getElementById("trackArtist"),
    trackTitle:           document.getElementById("trackTitle"),
    volumeSlider:         document.getElementById("volumeSlider")
  };

  ["roomTitle", "connectionLabel", "trackFormat", "trackAlbum", "mediaTabs", "visualizerTab",
    "lyricsTab", "lyricsTiming", "lyricsLines", "followLyrics", "visualizerPanel", "fullscreenButton", "playIcon",
    "listenButton", "queueHint", "inviteButton", "inviteDialog", "inviteLink", "inviteStatus",
    "settingsButton", "settingsDialog", "nameForm", "listenerName", "settingsStatus",
    "permissionsSummary", "hostSettings", "queuePermission", "transportPermission", "audioUploadPermission"].forEach(function (id) {
    el[id] = document.getElementById(id);
  });

  var runtime = {
    analyser:              null,
    audioContext:          null,
    audioObjectUrl:        null,
    audioReady:            false,
    clientId:              getClientId(),
    channelId:             "",
    channelTimer:          0,
    hostStateAvailable:    false,
    lastPlayback:          null,
    activeRoomCode:        "",
    activeTrackId:         "",
    socket:               null,
    socketWantOpen:       false,
    socketReconnectAt:    0,
    socketAttempt:        0,
    socketPingTimer:      0,
    socketFallbackTimer:  0,
    role:                 "follower",
    caps:                 null,
    roster:               [],
    skipProgress:         null,
    lyrics:                [],
    lyricIndex:            -1,
    lyricWordIndex:        -1,
    lyricMode:             null,
    lyricNodes:            [],
    mediaView:             "visualizer",
    preferredMediaView:    "lyrics",
    followLyrics:          true,
    reducedMotion:         window.matchMedia("(prefers-reduced-motion: reduce)"),
    artworkObjectUrl:      null,
    lastNetworkStatusAt:   0,
    networkMode:           "quality",
    networkScore:          0,
    networkStableCount:    0,
    presenceTimer:         0,
    preloadGeneration:     0,
    preloadedPackages:     {},
    pollTimer:             0,
    queue:                 null,
    reactionAmbienceTimer: 0,
    reactionHeat:          { fire: 0, love: 0, boost: 0 },
    reactionsPrimed:       false,
    reactionsTimer:        0,
    seenReactionIds:       new Set(),
    session:               null,
    sessionLoopsActive:    false,
    sourceNode:            null,
    switchingTrack:        false,
    syncTimer:             0,
    userActivated:         false
  };

  var library;
  init();

  async function init() {
    applyExternalConfig();
    library = window.RoomLibrary({
      baseUrl: config.cdnBaseUrl,
      room: function () { return { code: runtime.activeRoomCode, allowUploads: !!runtime.session && !!runtime.caps && runtime.caps.audioUploads }; },
      onQueue: function (queue) { runtime.queue = queue; renderQueue(queue); },
      onAuth: function () {
        if (runtime.channelId && !runtime.session) {
          loadChannel(runtime.channelId).catch(function (err) { setStatus(err.message || "Could not reload the channel."); });
        } else if (runtime.activeRoomCode) {
          loadSession(runtime.activeRoomCode, { fromChannel: Boolean(runtime.channelId) });
        }
      }
    });
    bindEvents();
    drawMeter();
    await library.init();

    var params = new URLSearchParams(window.location.search);
    var channelId = cleanChannelId(stringOrEmpty(params.get("channel")));
    var rawCode = stringOrEmpty(params.get("session") || params.get("code") || params.get("id"));
    var roomCode = normalizeRoomCode(rawCode);

    if (channelId) {
      showPlayer();
      loadChannel(channelId);
    } else if (roomCode) {
      showPlayer();
      loadSession(roomCode);
    } else {
      showLanding();
    }
  }

  function applyExternalConfig() {
    var external = window.SPECTRALIS_SHARED_PLAY_CONFIG;
    if (!external || typeof external !== "object") return;
    if (typeof external.cdnBaseUrl === "string") {
      config.cdnBaseUrl = normalizeBaseUrl(external.cdnBaseUrl);
    }
    if (typeof external.allowRichPackageFallback === "boolean") {
      config.allowRichPackageFallback = external.allowRichPackageFallback;
    }
  }

  function bindEvents() {
    el.codeForm.addEventListener("submit", function (e) {
      e.preventDefault();
      var raw = stringOrEmpty(el.codeInput.value);
      var code = normalizeRoomCode(raw);
      if (!code) {
        showLandingError("Enter a valid 6-character room code (e.g. X7K-29Q).");
        return;
      }
      showLandingError(null);
      pushRoomCodeToUrl(code);
      showPlayer();
      loadSession(code);
    });

    el.copyButton.addEventListener("click", copyJoinLink);
    el.openAppButton.addEventListener("click", openInSpectralis);
    bindRoomControls();
    el.audio.volume = Number(el.volumeSlider.value) / 100;

    el.reactionButtons.forEach(function (btn) {
      btn.addEventListener("click", function () {
        sendReaction(stringOrEmpty(btn.getAttribute("data-reaction"))).catch(function (err) {
          setStatus(err.message || "Reaction could not be sent.");
        });
      });
    });

    el.queueAddForm.addEventListener("submit", function (e) {
      e.preventDefault();
      addQueueLink().catch(function (err) {
        setStatus(err.message || "Queue link could not be added.");
      });
    });

    el.volumeSlider.addEventListener("input", function () {
      el.audio.volume = Number(el.volumeSlider.value) / 100;
    });

    if (el.transportPlay) el.transportPlay.addEventListener("click", function () {
      handleUserGesture();
      sendTransport(runtime.lastPlayback && runtime.lastPlayback.isPlaying ? "pause" : "play");
    });
    if (el.transportNext) el.transportNext.addEventListener("click", function () { sendTransport("next"); });
    if (el.transportPrev) el.transportPrev.addEventListener("click", function () { sendTransport("prev"); });
    if (el.voteSkipButton) el.voteSkipButton.addEventListener("click", voteSkip);
    if (el.seekTrack) el.seekTrack.addEventListener("click", function (e) {
      if (!canControlTransport()) return;
      var rect = el.seekTrack.getBoundingClientRect();
      var frac = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width));
      var duration = (runtime.lastPlayback && runtime.lastPlayback.durationSeconds) || el.audio.duration || 0;
      if (duration > 0) sendTransport("seek", frac * duration);
    });
    el.seekTrack.addEventListener("keydown", function (event) {
      if (!canControlTransport()) return;
      var duration = (runtime.lastPlayback && runtime.lastPlayback.durationSeconds) || el.audio.duration || 0;
      var position = el.audio.currentTime || 0;
      if (event.key === "ArrowRight") position += 5;
      else if (event.key === "ArrowLeft") position -= 5;
      else if (event.key === "Home") position = 0;
      else if (event.key === "End") position = duration;
      else return;
      event.preventDefault();
      sendTransport("seek", Math.max(0, Math.min(duration, position)));
    });

    el.audio.addEventListener("loadedmetadata", function () {
      runtime.audioReady = true;
      reportNetworkSuccess(0, "audio metadata");
      updateTimeline();
      applyPlaybackSync(true);
    });
    el.audio.addEventListener("timeupdate", updateTimeline);
    el.audio.addEventListener("canplay", function () { reportNetworkSuccess(0, "audio canplay"); });
    el.audio.addEventListener("play", function () {
      if (el.disc) el.disc.classList.add("is-playing");
    });
    el.audio.addEventListener("playing", function () { reportNetworkSuccess(0, "audio playing"); });
    el.audio.addEventListener("waiting", function () {
      if (runtime.userActivated && runtime.lastPlayback && runtime.lastPlayback.isPlaying) {
        reportNetworkTrouble(1.5, "Audio is buffering.");
      }
    });
    el.audio.addEventListener("stalled", function () {
      reportNetworkTrouble(2, "Audio stream stalled.");
    });
    el.audio.addEventListener("pause", function () {
      if (el.disc) el.disc.classList.remove("is-playing");
      el.audio.playbackRate = 1;
    });
    el.audio.addEventListener("error", function () {
      setDotState("error");
      setStatus("Audio could not be loaded for this session.");
    });

    // Listening is explicit: browsing lyrics or room settings must never seek.
    el.listenButton.addEventListener("click", handleUserGesture);
  }

  function handleUserGesture() {
    if (runtime.userActivated) return;
    runtime.userActivated = true;
    el.listenButton.hidden = true;
    ensureAudioGraph().then(function () {
      applyPlaybackSync(true);
    });
  }

  function bindRoomControls() {
    function pauseLyricFollow() {
      if (!runtime.lyrics.length) return;
      runtime.followLyrics = false;
      el.followLyrics.hidden = el.lyricsPanel.hidden;
    }
    el.lyricsPanel.addEventListener("wheel", pauseLyricFollow, { passive: true });
    el.lyricsPanel.addEventListener("touchmove", pauseLyricFollow, { passive: true });
    el.lyricsPanel.addEventListener("keydown", function (event) {
      if (["ArrowUp", "ArrowDown", "PageUp", "PageDown", "Home", "End"].includes(event.key)) pauseLyricFollow();
    });
    el.followLyrics.addEventListener("click", function () {
      runtime.followLyrics = true;
      el.followLyrics.hidden = true;
      scrollToCurrentLyric();
    });
    el.visualizerTab.addEventListener("click", function () { selectMediaView("visualizer", true); });
    el.lyricsTab.addEventListener("click", function () { selectMediaView("lyrics", true); });
    el.mediaTabs.addEventListener("keydown", function (event) {
      if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key) || !runtime.lyrics.length) return;
      event.preventDefault();
      var next = event.key === "Home" ? "visualizer" : event.key === "End" ? "lyrics" : runtime.mediaView === "lyrics" ? "visualizer" : "lyrics";
      selectMediaView(next, true);
      el[next === "lyrics" ? "lyricsTab" : "visualizerTab"].focus();
    });
    el.fullscreenButton.addEventListener("click", async function () {
      try {
        if (document.fullscreenElement) await document.exitFullscreen();
        else if (el.visualizerPanel.requestFullscreen) await el.visualizerPanel.requestFullscreen();
        else setStatus("Fullscreen is unavailable in this browser.");
      } catch (_) { setStatus("Fullscreen could not be opened."); }
    });
    if (!document.fullscreenEnabled) el.fullscreenButton.hidden = true;
    document.addEventListener("fullscreenchange", function () {
      el.fullscreenButton.setAttribute("aria-label", document.fullscreenElement ? "Exit fullscreen" : "Fullscreen visualizer");
    });
    el.inviteButton.addEventListener("click", function () {
      el.inviteLink.value = roomJoinLink();
      el.inviteStatus.textContent = "";
      el.inviteDialog.showModal();
    });
    el.settingsButton.addEventListener("click", function () {
      el.listenerName.value = roomDisplayName();
      el.settingsStatus.textContent = "";
      updateRoomPermissions();
      el.settingsDialog.showModal();
    });
    document.querySelectorAll("[data-close-dialog]").forEach(function (button) {
      button.addEventListener("click", function () { button.closest("dialog").close(); });
    });
    el.nameForm.addEventListener("submit", function (event) {
      event.preventDefault();
      var name = el.listenerName.value.trim().slice(0, 32);
      if (!name) return;
      try { window.localStorage.setItem("spectralisSharedPlayName", name); }
      catch (_) { el.settingsStatus.textContent = "Your browser could not save the name."; return; }
      connectRoomSocket();
      el.settingsStatus.textContent = "Name saved. Rejoining the room…";
    });
    [[el.queuePermission, "queueAdd"], [el.transportPermission, "transport"]].forEach(function (entry) {
      entry[0].addEventListener("change", function () {
        if (runtime.role !== "host") return;
        var caps = {}; caps[entry[1]] = entry[0].value;
        el.settingsStatus.textContent = socketSend({ t: "caps.set", caps: caps }) ? "Permission update requested." : "Reconnecting…";
      });
    });
    el.discArt.addEventListener("error", function () { el.discArt.hidden = true; });
    el.audioUploadPermission.addEventListener("change", function () {
      if (runtime.role !== "host") return;
      el.settingsStatus.textContent = socketSend({ t: "caps.set", caps: { audioUploads: el.audioUploadPermission.checked } }) ? "Permission update requested." : "Reconnecting…";
    });
  }

  function updateRoomPermissions() {
    var canAdd = Boolean(runtime.session && runtime.caps && capAllows(runtime.caps.queueAdd));
    el.queueAddForm.hidden = !canAdd;
    el.queueHint.textContent = canAdd ? "Share a favourite with the room." : runtime.session ? "The host controls who can add tracks." : "Waiting for the host.";
    el.hostSettings.hidden = runtime.role !== "host";
    el.permissionsSummary.textContent = "Your role: " + (runtime.role === "codj" ? "co-DJ" : runtime.role === "host" ? "host" : "listener") + ". " + (canControlTransport() ? "You can control playback." : "Playback follows the host.");
    if (runtime.caps) {
      el.queuePermission.value = runtime.caps.queueAdd;
      el.transportPermission.value = runtime.caps.transport;
      el.audioUploadPermission.checked = runtime.caps.audioUploads;
    }
    if (library) library.refresh();
  }

  // ─── Room code helpers ────────────────────────────────────────────────────

  function normalizeRoomCode(input) {
    if (!input || typeof input !== "string") return "";
    var cleaned = "";
    var chars = input.trim().split("");
    for (var i = 0; i < chars.length && cleaned.length < 7; i++) {
      var c = chars[i];
      if (/[a-zA-Z0-9]/.test(c)) cleaned += c.toUpperCase();
    }
    return cleaned.length === 6 ? cleaned : "";
  }

  function displayRoomCode(code) {
    if (code.length === 6) return code.slice(0, 3) + "-" + code.slice(3);
    return code;
  }

  function pushRoomCodeToUrl(code) {
    try {
      var url = new URL(window.location.href);
      url.searchParams.set("session", code);
      url.searchParams.delete("channel");
      window.history.replaceState(null, "", url.toString());
    } catch (e) { }
  }

  // ─── View transitions ─────────────────────────────────────────────────────

  function showLanding() {
    el.landingScreen.hidden = false;
    el.playerShell.hidden = true;
    el.backdrop.hidden = true;
  }

  function showPlayer() {
    el.landingScreen.hidden = true;
    el.playerShell.hidden = false;
    el.backdrop.hidden = false;
  }

  function showLandingError(message) {
    if (!el.landingError) return;
    if (!message) {
      el.landingError.hidden = true;
      el.landingError.textContent = "";
    } else {
      el.landingError.textContent = message;
      el.landingError.hidden = false;
    }
  }

  // ─── Session load ─────────────────────────────────────────────────────────

  async function loadSession(roomCode, options) {
    options = options || {};
    if (!roomCode) {
      setStatus("Invalid room code.");
      return;
    }

    stopSessionLoops();
    closeRoomSocket();
    resetAudio();
    clearPreloadedPackages();
    runtime.session = null;
    runtime.queue = null;
    runtime.role = "follower";
    runtime.caps = null;
    runtime.roster = [];
    runtime.skipProgress = null;
    renderQueue(null);
    renderRoster();
    updateTransportControls();
    if (!options.fromChannel) leaveChannelMode();

    runtime.activeRoomCode = roomCode;
    runtime.userActivated = false;
    el.listenButton.hidden = false;
    runtime.reactionsPrimed = false;
    runtime.seenReactionIds = new Set();
    resetReactionAmbience();
    updateListenerCount(0);
    if (el.reactionLayer) el.reactionLayer.replaceChildren();
    if (el.roomBadge) el.roomBadge.textContent = displayRoomCode(roomCode);

    setDotState("loading");
    setStatus("Joining room " + displayRoomCode(roomCode) + "…");

    try {
      var payload = await fetchJson(v2Url("sessions/" + encodeURIComponent(roomCode)));
      runtime.session = normalizeSession(roomCode, payload);
      applyCaps((payload.session || payload).capabilities || {});
      if (!runtime.channelId) el.roomTitle.textContent = runtime.session.roomName || "Listening room";
      runtime.activeTrackId = runtime.session.trackId;
      renderTrack(runtime.session.track);
      // Queue and permission updates must work while a track is still preparing,
      // or when this browser cannot decode the current track.
      connectRoomSocket();
      await prepareAudio(runtime.session);
      await refreshQueueState();
      await refreshPlaybackState();
      setDotState("live");
      setStatus(runtime.hostStateAvailable ? "Synced." : "Waiting for host to start…");
      updateReactionButtons();
    } catch (err) {
      setDotState("error");
      setStatus(err.message || "Could not load room.");
    }
  }

  async function loadChannel(channelId) {
    channelId = cleanChannelId(channelId);
    if (!channelId) {
      setStatus("Invalid channel.");
      return;
    }

    runtime.channelId = channelId;
    enterChannelMode(channelId);
    setDotState("loading");
    setStatus("Loading channel…");

    await refreshChannel();
    if (runtime.channelTimer) window.clearInterval(runtime.channelTimer);
    runtime.channelTimer = window.setInterval(function () {
      refreshChannel().catch(function () { setStatus("Waiting for channel…"); });
    }, config.channelPollIntervalMs);
  }

  async function refreshChannel() {
    if (!runtime.channelId) return;

    var payload = await fetchJson(v2Url("channels/" + encodeURIComponent(runtime.channelId)));
    updateChannelHeader(payload);
    renderChannelStats(payload.stats);

    if (!payload.isLive || !payload.roomCode) {
      stopSessionLoops();
      closeRoomSocket();
      resetAudio();
      clearPreloadedPackages();
      runtime.session = null;
      runtime.queue = null;
      runtime.roster = [];
      runtime.caps = null;
      runtime.role = "follower";
      renderQueue(null);
      renderRoster();
      updateListenerCount(0);
      updateTransportControls();
      updateReactionButtons();
      setDotState("waiting");
      el.trackTitle.textContent = payload.displayName || "Live Channel";
      el.trackArtist.textContent = "Waiting for the host to start sharing.";
      setStatus("This channel is not live right now.");
      return;
    }

    if (!runtime.session || runtime.session.roomCode !== payload.roomCode) {
      await loadSession(payload.roomCode, { fromChannel: true });
    } else {
      updateChannelHeader(payload);
    }
  }

  function enterChannelMode(channelId) {
    document.body.classList.add("is-channel-room");
    if (el.channelPanel) el.channelPanel.hidden = false;
    if (el.channelName) el.channelName.textContent = channelId || "Channel";
  }

  function leaveChannelMode() {
    runtime.channelId = "";
    if (runtime.channelTimer) {
      window.clearInterval(runtime.channelTimer);
      runtime.channelTimer = 0;
    }
    document.body.classList.remove("is-channel-room");
    if (el.channelPanel) el.channelPanel.hidden = true;
  }

  function updateChannelHeader(payload) {
    if (!payload || typeof payload !== "object") return;
    var name = stringOrEmpty(payload.displayName) || runtime.channelId || "Channel";
    if (el.channelName) el.channelName.textContent = name;
    el.roomTitle.textContent = name;
  }

  // ─── Session normalization ────────────────────────────────────────────────

  function normalizeSession(roomCode, payload) {
    // The HTTP API wraps its full manifest in `session`; older hosts send it flat.
    var src = payload && payload.session || payload || {};
    var track = src.track || {};
    var trackId = stringOrEmpty(src.trackId || src.activeTrackId || track.trackId);
    var stateUrl = firstUrl(src.stateUrl) || v2Url("sessions/" + encodeURIComponent(roomCode) + "/state");
    var queueUrl = firstUrl(src.queueUrl) || v2Url("sessions/" + encodeURIComponent(roomCode) + "/queue");
    var presenceUrl = firstUrl(src.presenceUrl) || v2Url("sessions/" + encodeURIComponent(roomCode) + "/presence");
    var reactionsUrl = firstUrl(src.reactionsUrl) || v2Url("sessions/" + encodeURIComponent(roomCode) + "/reactions");
    var packageUrl = firstUrl(
      src.packageUrl,
      src.spectralisPackageUrl,
      findUploadAssetUrl(src.uploads, "spectralis-package")
    );
    var browserAudioUrl = firstPlayableUrl(
      src.browserAudioUrl,
      src.sanitizedAudioUrl,
      src.audioUrl,
      src.streamUrl
    );
    var joinUrl = stringOrEmpty(src.joinUrl) || createWebShareJoinUrl(roomCode);
    // No server-side /art endpoint exists — only trust art the manifest actually gives us.
    var albumArtUrl = firstUrl(src.albumArtUrl, src.artUrl, src.coverUrl);

    return {
      roomCode:       stringOrEmpty(src.roomCode || roomCode),
      displayCode:    stringOrEmpty(src.displayCode) || displayRoomCode(roomCode),
      roomName:       stringOrEmpty(src.roomName || src.profile && src.profile.name),
      joinUrl:        joinUrl,
      trackId:        trackId,
      stateUrl:       stateUrl,
      queueUrl:       queueUrl,
      presenceUrl:    presenceUrl,
      reactionsUrl:   reactionsUrl,
      packageUrl:     packageUrl,
      browserAudioUrl: browserAudioUrl,
      albumArtUrl:    albumArtUrl,
      track:          normalizeTrack(track),
      playback:       findPlaybackPayload(src),
      expiresAtUtc:   src.expiresAtUtc || null
    };
  }

  function normalizeTrack(track) {
    return {
      album:                stringOrEmpty(track.album),
      artist:               stringOrEmpty(track.artist),
      displayName:          stringOrEmpty(track.displayName || track.title || track.name) || "Shared Play Track",
      durationSeconds:      finiteNumber(track.durationSeconds || track.duration || 0),
      formatName:           stringOrEmpty(track.formatName || track.format),
      hasEmbeddedVisualizer: Boolean(track.hasEmbeddedVisualizer),
      hasLyrics:            Boolean(track.hasLyrics),
      lyrics:               normalizeLyrics(track.lyrics || track.lyricLines)
    };
  }

  function normalizeLyrics(lines) {
    return window.RoomLyrics.normalize(lines);
  }

  // ─── Audio ────────────────────────────────────────────────────────────────

  async function prepareAudio(session) {
    el.audio.pause();
    el.audio.removeAttribute("src");
    runtime.audioReady = false;
    if (runtime.artworkObjectUrl) URL.revokeObjectURL(runtime.artworkObjectUrl);
    runtime.artworkObjectUrl = null;

    if (runtime.audioObjectUrl) {
      URL.revokeObjectURL(runtime.audioObjectUrl);
      runtime.audioObjectUrl = null;
    }

    if (session.browserAudioUrl) {
      el.audio.src = session.browserAudioUrl;
      el.audio.load();
      setStatus("Browser audio stream ready.");
      return;
    }

    if (!session.packageUrl || !config.allowRichPackageFallback) {
      throw new Error("This room does not expose a browser-playable audio stream yet.");
    }

    var preloaded = runtime.preloadedPackages[session.packageUrl];
    if (preloaded && preloaded.objectUrl) {
      runtime.audioObjectUrl = preloaded.objectUrl;
      runtime.artworkObjectUrl = preloaded.artworkObjectUrl || null;
      delete runtime.preloadedPackages[session.packageUrl];
      el.audio.src = runtime.audioObjectUrl;
      el.audio.load();
      maybeLoadAlbumArt();
      setStatus("Preloaded audio ready.");
      return;
    }

    setStatus("Extracting audio from room package…");
    var assets = await extractAudioObjectUrlFromPackage(session.packageUrl);
    runtime.audioObjectUrl = assets.objectUrl;
    runtime.artworkObjectUrl = assets.artworkObjectUrl;
    el.audio.src = runtime.audioObjectUrl;
    el.audio.load();
    maybeLoadAlbumArt();
  }

  // ─── Playback state ───────────────────────────────────────────────────────

  async function refreshPlaybackState() {
    if (!runtime.session) return;

    var payload = await fetchJson(runtime.session.stateUrl);
    var newTrackId = extractTrackId(payload);

    if (newTrackId && runtime.activeTrackId && newTrackId !== runtime.activeTrackId) {
      await switchActiveTrack(newTrackId, payload);
    } else if (newTrackId && !runtime.activeTrackId) {
      runtime.activeTrackId = newTrackId;
      runtime.session.trackId = newTrackId;
    }

    var playback = findPlaybackPayload(payload);
    if (!playback) {
      runtime.hostStateAvailable = false;
      runtime.lastPlayback = null;
      setDotState("live");
      setStatus(runtime.userActivated ? "Playing locally — waiting for host…" : "Waiting for host to start…");
      return;
    }

    runtime.hostStateAvailable = true;
    runtime.lastPlayback = normalizePlayback(playback, newTrackId);
    applyPlaybackSync(false);
  }

  async function switchActiveTrack(trackId, statePayload) {
    if (runtime.switchingTrack || !runtime.session) return;
    runtime.switchingTrack = true;
    renderLyrics([]);
    el.audio.pause();
    setDotState("loading");
    setStatus("Loading next track…");

    try {
      var roomCode = runtime.activeRoomCode;
      var nextSession = normalizeSession(roomCode, statePayload || {});
      if (!nextSession.packageUrl && !nextSession.browserAudioUrl) {
        nextSession = normalizeSession(roomCode, await fetchJson(v2Url("sessions/" + encodeURIComponent(roomCode))));
      }
      nextSession.stateUrl = nextSession.stateUrl || runtime.session.stateUrl;
      nextSession.queueUrl = nextSession.queueUrl || runtime.session.queueUrl;
      nextSession.presenceUrl = nextSession.presenceUrl || runtime.session.presenceUrl;
      nextSession.reactionsUrl = nextSession.reactionsUrl || runtime.session.reactionsUrl;
      runtime.lastPlayback = null;
      await prepareAudio(nextSession);
      runtime.session = nextSession;
      runtime.activeTrackId = trackId;
      renderTrack(nextSession.track);
      var playback = findPlaybackPayload(statePayload) || nextSession.playback;
      if (playback) {
        runtime.lastPlayback = normalizePlayback(playback, trackId);
        runtime.hostStateAvailable = true;
      }
      setDotState("live");
      setStatus("Next track ready.");
    } finally {
      runtime.switchingTrack = false;
      applyPlaybackSync(true);
    }
  }

  function normalizePlayback(playback, fallbackTrackId) {
    return {
      durationSeconds: finiteNumber(playback.durationSeconds || (runtime.session && runtime.session.track.durationSeconds) || el.audio.duration || 0),
      hostClockUtc:    playback.hostClockUtc || playback.updatedAtUtc || new Date().toISOString(),
      isPlaying:       Boolean(playback.isPlaying),
      positionSeconds: finiteNumber(playback.positionSeconds || playback.position || 0),
      reason:          stringOrEmpty(playback.reason),
      trackId:         stringOrEmpty(fallbackTrackId || playback.trackId || playback.activeTrackId)
    };
  }

  function findPlaybackPayload(payload) {
    if (!payload || typeof payload !== "object") return null;
    if (hasPlaybackFields(payload.playback)) return payload.playback;
    if (payload.state && typeof payload.state === "object") {
      if (hasPlaybackFields(payload.state.playback)) return payload.state.playback;
      if (hasPlaybackFields(payload.state)) return payload.state;
    }
    return hasPlaybackFields(payload) ? payload : null;
  }

  function extractTrackId(payload) {
    if (!payload || typeof payload !== "object") return "";
    return stringOrEmpty(
      payload.activeTrackId || payload.currentTrackId || payload.trackId ||
      (payload.playback && (payload.playback.activeTrackId || payload.playback.trackId)) ||
      (payload.state && (payload.state.activeTrackId || payload.state.trackId))
    );
  }

  function hasPlaybackFields(value) {
    if (!value || typeof value !== "object") return false;
    return Object.prototype.hasOwnProperty.call(value, "isPlaying") ||
      Object.prototype.hasOwnProperty.call(value, "positionSeconds") ||
      Object.prototype.hasOwnProperty.call(value, "hostClockUtc");
  }

  // ─── Sync ─────────────────────────────────────────────────────────────────

  function applyPlaybackSync(force) {
    if (runtime.switchingTrack || !runtime.lastPlayback || !runtime.audioReady) return;

    var profile = getSyncProfile();
    var target = getHostPosition(runtime.lastPlayback);
    if (runtime.lastPlayback.isPlaying && profile.targetLagSeconds > 0) {
      target = Math.max(0, target - profile.targetLagSeconds);
    }

    var duration = runtime.lastPlayback.durationSeconds || el.audio.duration || 0;
    if (duration > 0) target = Math.min(target, duration);

    var drift = target - el.audio.currentTime;
    if (force || Math.abs(drift) >= profile.hardSeekSeconds) {
      el.audio.currentTime = Math.max(0, target);
      drift = 0;
    }

    if (runtime.lastPlayback.isPlaying) {
      if (runtime.userActivated && el.audio.paused) {
        ensureAudioGraph().then(function () {
          el.audio.play().catch(function () {
            runtime.userActivated = false;
            el.listenButton.hidden = false;
            setStatus("Select Start listening to enable audio.");
          });
        });
      }
      if (!el.audio.paused) {
        el.audio.playbackRate = getCorrectionRate(drift);
        setDotState("live");
      }
    } else {
      if (!el.audio.paused) el.audio.pause();
      el.audio.playbackRate = 1;
      if (Math.abs(drift) > 0.35) el.audio.currentTime = Math.max(0, target);
      setDotState("paused");
    }

    updateTimeline();
  }

  function getHostPosition(playback) {
    var position = playback.positionSeconds;
    var clock = Date.parse(playback.hostClockUtc);
    if (playback.isPlaying && Number.isFinite(clock)) {
      position += Math.max(0, Date.now() - clock) / 1000;
    }
    return Math.max(0, position);
  }

  function getCorrectionRate(drift) {
    var profile = getSyncProfile();
    var abs = Math.abs(drift);
    if (abs < profile.softSyncSeconds) return 1;
    var amount = Math.min(profile.maxCorrectionRate, abs / profile.correctionDivisor);
    return drift > 0 ? 1 + amount : 1 - amount;
  }

  function getSyncProfile() {
    return runtime.networkMode === "optimized" ? config.optimizedSync : config.qualitySync;
  }

  // ─── Room WebSocket ───────────────────────────────────────────────────────
  //
  // The realtime path. When connected it replaces the state/queue/presence/
  // reaction polling; startSessionLoops() stays as a fallback for proxies that
  // block WebSockets or when the socket can't hold a connection.

  // Spectralis realtime protocol v2 (backend/src/protocol.rs): the hello says which protocol and
  // optional features this page speaks; a server that needs newer answers `update_required`.
  var ENVELOPE_V = 2;
  var CLIENT_FEATURES = ["roster", "reactions"];

  function roomSocketUrl(roomCode) {
    var base = new URL(config.cdnBaseUrl);
    var scheme = base.protocol === "http:" ? "ws:" : "wss:";
    var u = new URL("/shared-play/v2/sessions/" + encodeURIComponent(roomCode) + "/socket", base);
    u.protocol = scheme;
    u.searchParams.set("clientId", runtime.clientId);
    u.searchParams.set("name", roomDisplayName());
    return u.toString();
  }

  function roomDisplayName() {
    try {
      var stored = window.localStorage.getItem("spectralisSharedPlayName");
      if (stored && stored.trim()) return stored.trim().slice(0, 32);
    } catch (e) { /* ignore */ }
    return "Listener";
  }

  function connectRoomSocket() {
    if (!runtime.activeRoomCode) return;
    runtime.socketWantOpen = true;
    closeRoomSocket(true);

    var ws;
    try {
      ws = new WebSocket(roomSocketUrl(runtime.activeRoomCode));
    } catch (e) {
      scheduleSocketFallback();
      return;
    }
    runtime.socket = ws;

    ws.addEventListener("open", function () {
      runtime.socketAttempt = 0;
      cancelSocketFallback();
      socketSend({
        t: "hello",
        accessToken: library.token(),
        proto: ENVELOPE_V,
        client: { kind: "web", version: String(config.clientVersion || "") },
        features: CLIENT_FEATURES,
        role: "listener",
        clientId: runtime.clientId,
        name: roomDisplayName()
      });
      window.clearInterval(runtime.socketPingTimer);
      runtime.socketPingTimer = window.setInterval(function () { socketSend({ t: "ping" }); }, 20000);
      // The realtime feed supersedes network polling.
      stopSessionLoops();
      setDotState("live");
    });

    ws.addEventListener("message", function (ev) {
      var msg;
      try { msg = JSON.parse(ev.data); } catch (e) { return; }
      handleSocketMessage(msg);
    });

    ws.addEventListener("close", function () {
      window.clearInterval(runtime.socketPingTimer);
      runtime.socketPingTimer = 0;
      if (runtime.socket === ws) runtime.socket = null;
      if (!runtime.socketWantOpen) return;
      runtime.socketAttempt++;
      // Fall back to polling immediately so playback never stalls, and keep
      // trying the socket in the background.
      if (!runtime.sessionLoopsActive && runtime.session) startSessionLoops();
      var delay = Math.min(15000, 500 * Math.pow(2, Math.min(runtime.socketAttempt, 5)));
      window.setTimeout(function () {
        if (runtime.socketWantOpen) connectRoomSocket();
      }, delay);
    });

    ws.addEventListener("error", function () { /* close handler drives reconnect */ });

    scheduleSocketFallback();
  }

  function closeRoomSocket(keepWantOpen) {
    if (!keepWantOpen) runtime.socketWantOpen = false;
    window.clearInterval(runtime.socketPingTimer);
    runtime.socketPingTimer = 0;
    var ws = runtime.socket;
    runtime.socket = null;
    if (ws) {
      try { ws.onclose = null; ws.close(); } catch (e) { /* ignore */ }
    }
  }

  function scheduleSocketFallback() {
    cancelSocketFallback();
    runtime.socketFallbackTimer = window.setTimeout(function () {
      if (!isSocketOpen() && !runtime.sessionLoopsActive && runtime.session) startSessionLoops();
    }, 3500);
  }

  function cancelSocketFallback() {
    if (runtime.socketFallbackTimer) {
      window.clearTimeout(runtime.socketFallbackTimer);
      runtime.socketFallbackTimer = 0;
    }
  }

  function isSocketOpen() {
    return runtime.socket && runtime.socket.readyState === 1;
  }

  function socketSend(obj) {
    if (!isSocketOpen()) return false;
    try {
      obj.v = ENVELOPE_V;
      runtime.socket.send(JSON.stringify(obj));
      return true;
    } catch (e) { return false; }
  }

  function handleSocketMessage(msg) {
    switch (msg && msg.t) {
      case "welcome":
        if (msg.you && msg.you.role) runtime.role = msg.you.role;
        if (msg.caps) applyCaps(msg.caps);
        if (Array.isArray(msg.roster)) applyRoster(msg.roster);
        if (msg.state && typeof msg.state === "object") applyRealtimeState(msg.state);
        if (msg.queue && typeof msg.queue === "object") applyRealtimeQueue(msg.queue);
        break;
      case "state":
        if (msg.playback) applyRealtimeState(msg.playback);
        break;
      case "queue":
        if (msg.queue) applyRealtimeQueue(msg.queue);
        break;
      case "roster":
        if (Array.isArray(msg.members)) applyRoster(msg.members);
        break;
      case "caps":
        if (msg.caps) applyCaps(msg.caps);
        break;
      case "reaction":
        showReactionBurst(emojiForReaction(stringOrEmpty(msg.kind)), stringOrEmpty(msg.kind));
        break;
      case "skip":
        runtime.skipProgress = { votes: finiteNumber(msg.votes), required: finiteNumber(msg.required) };
        renderSkipProgress();
        break;
      case "error":
        if (msg.code === "update_required") {
          // A cached copy of this page is older than the server; reconnecting can't fix that.
          runtime.socketWantOpen = false;
          closeRoomSocket();
          setDotState("error");
          setStatus("This page is out of date. Refresh to keep listening.");
          break;
        }
        setStatus(stringOrEmpty(msg.message) || "That action was not allowed.");
        break;
      case "kicked":
        runtime.socketWantOpen = false;
        closeRoomSocket();
        setDotState("error");
        setStatus("The host removed you from this room.");
        resetAudio();
        clearPreloadedPackages();
        runtime.session = null;
        runtime.queue = null;
        runtime.caps = null;
        runtime.role = "follower";
        runtime.roster = [];
        runtime.activeRoomCode = "";
        leaveChannelMode();
        stopSessionLoops();
        renderQueue(null);
        renderRoster();
        updateListenerCount(0);
        updateTransportControls();
        updateReactionButtons();
        break;
    }
  }

  function applyRealtimeState(playbackPayload) {
    var newTrackId = extractTrackId(playbackPayload) || extractTrackId({ playback: playbackPayload });
    if (newTrackId && runtime.activeTrackId && newTrackId !== runtime.activeTrackId) {
      switchActiveTrack(newTrackId, { playback: playbackPayload }).catch(function (error) {
        setDotState("error");
        setStatus(error.message || "The next track could not be loaded.");
      });
      return;
    }
    if (newTrackId && !runtime.activeTrackId) {
      runtime.activeTrackId = newTrackId;
      if (runtime.session) runtime.session.trackId = newTrackId;
    }
    // Both welcome.state and state.playback can contain the backend's state envelope.
    var playback = findPlaybackPayload(playbackPayload);
    if (!hasPlaybackFields(playback)) {
      runtime.hostStateAvailable = false;
      runtime.lastPlayback = null;
      setStatus(runtime.userActivated ? "Playing locally — waiting for host…" : "Waiting for host to start…");
      return;
    }
    runtime.hostStateAvailable = true;
    runtime.lastPlayback = normalizePlayback(playback, newTrackId);
    applyPlaybackSync(false);
    setStatus("Synced.");
  }

  function applyRealtimeQueue(queuePayload) {
    runtime.queue = normalizeQueue(queuePayload);
    renderQueue(runtime.queue);
    preloadUpcomingQueueTracks();
  }

  // ─── Capabilities / roster / transport ────────────────────────────────────

  function capAllows(cap) {
    var rank = { follower: 0, codj: 1, host: 2 };
    var need = { everyone: 0, codj: 1, host: 2 };
    var mine = rank[runtime.role] || 0;
    return mine >= (need[cap] === undefined ? 2 : need[cap]);
  }

  function canControlTransport() {
    return Boolean(runtime.caps) && capAllows(runtime.caps.transport);
  }

  function applyCaps(caps) {
    runtime.caps = {
      queueAdd: caps.queueAdd || "everyone",
      audioUploads: caps.audioUploads !== false,
      queueRemove: caps.queueRemove || "host",
      queueReorder: caps.queueReorder || "host",
      transport: caps.transport || "host",
      voteSkip: caps.voteSkip !== false,
      skipVotesRequired: finiteNumber(caps.skipVotesRequired) || 3
    };
    updateTransportControls();
    renderSkipProgress();
    renderQueue(runtime.queue);
  }

  function applyRoster(members) {
    runtime.roster = members.map(function (m) {
      return {
        clientId: stringOrEmpty(m.clientId),
        name: stringOrEmpty(m.name) || "Listener",
        role: stringOrEmpty(m.role) || "follower",
        isHost: Boolean(m.isHost)
      };
    });
    var me = runtime.roster.filter(function (m) { return m.clientId === runtime.clientId; })[0];
    if (me) runtime.role = me.role;
    updateListenerCount(runtime.roster.length);
    renderRoster();
    updateTransportControls();
    renderQueue(runtime.queue);
  }

  function renderRoster() {
    if (!el.rosterList) return;
    el.rosterList.replaceChildren();
    runtime.roster.forEach(function (m) {
      var chip = document.createElement("span");
      chip.className = "roster-chip" + (m.isHost ? " is-host" : m.role === "codj" ? " is-codj" : "");
      chip.textContent = m.name + (m.isHost ? " · host" : m.role === "codj" ? " · co-DJ" : "");
      var avatar = document.createElement("span");
      avatar.className = "roster-avatar";
      avatar.setAttribute("aria-hidden", "true");
      avatar.textContent = m.name.slice(0, 2).toUpperCase();
      chip.prepend(avatar);
      el.rosterList.appendChild(chip);
    });
  }

  function updateTransportControls() {
    var can = canControlTransport();
    [el.transportPrev, el.transportPlay, el.transportNext].forEach(function (b) {
      if (b) b.hidden = !can;
    });
    if (el.transportHint) {
      el.transportHint.hidden = can;
    }
    if (el.seekTrack) el.seekTrack.classList.toggle("interactive", can);
    el.seekTrack.setAttribute("role", can ? "slider" : "progressbar");
    el.seekTrack.tabIndex = can ? 0 : -1;
    updateRoomPermissions();
    if (el.voteSkipButton) {
      var showVote = Boolean(runtime.caps && runtime.caps.voteSkip) && !can;
      el.voteSkipButton.hidden = !showVote;
    }
  }

  function renderSkipProgress() {
    if (!el.voteSkipButton) return;
    var p = runtime.skipProgress;
    var required = (runtime.caps && runtime.caps.skipVotesRequired) || (p && p.required) || 3;
    if (p && p.votes > 0) {
      el.voteSkipButton.textContent = "Vote skip · " + p.votes + "/" + required;
    } else {
      el.voteSkipButton.textContent = "Vote skip";
    }
  }

  function sendTransport(action, position) {
    if (!canControlTransport()) return;
    var payload = { t: "transport", action: action };
    if (typeof position === "number") payload.position = position;
    if (!socketSend(payload)) setStatus("Reconnecting…");
  }

  function voteSkip() {
    if (!socketSend({ t: "skip.vote" })) setStatus("Reconnecting…");
  }

  // ─── Session loops ────────────────────────────────────────────────────────

  function startSessionLoops() {
    stopSessionLoops();
    runtime.sessionLoopsActive = true;
    var profile = getSyncProfile();

    runtime.pollTimer = window.setInterval(function () {
      refreshPlaybackState().catch(function (err) {
        setDotState("reconnecting");
        reportNetworkTrouble(1.5, err.message || "State request failed.");
        setStatus(err.message || "Waiting for room state…");
      });
      refreshQueueState().catch(function () { });
    }, profile.pollIntervalMs);

    runtime.syncTimer = window.setInterval(function () {
      applyPlaybackSync(false);
    }, profile.syncIntervalMs);

    postPresence().catch(function () { });
    refreshReactions().catch(function () { });

    runtime.presenceTimer = window.setInterval(function () {
      postPresence().catch(function () { });
    }, config.presenceIntervalMs);

    runtime.reactionsTimer = window.setInterval(function () {
      refreshReactions().catch(function () { });
    }, config.reactionIntervalMs);
  }

  function stopSessionLoops() {
    runtime.sessionLoopsActive = false;
    if (runtime.pollTimer)     { window.clearInterval(runtime.pollTimer);     runtime.pollTimer = 0; }
    if (runtime.syncTimer)     { window.clearInterval(runtime.syncTimer);     runtime.syncTimer = 0; }
    if (runtime.presenceTimer) { window.clearInterval(runtime.presenceTimer); runtime.presenceTimer = 0; }
    if (runtime.reactionsTimer){ window.clearInterval(runtime.reactionsTimer); runtime.reactionsTimer = 0; }
  }

  // ─── Presence ─────────────────────────────────────────────────────────────

  async function postPresence() {
    if (!runtime.session || !runtime.session.presenceUrl) { updateListenerCount(0); return; }
    var payload = await postJson(runtime.session.presenceUrl, { clientId: runtime.clientId, displayName: "Listener" });
    var count = finiteNumber((payload && payload.listenerCount) || (payload && Array.isArray(payload.participants) && payload.participants.length) || 0);
    updateListenerCount(count);
  }

  function updateListenerCount(count) {
    if (!el.listenerCount) return;
    el.listenerCount.textContent = count === 1 ? "1" : String(count);
    el.listenerCount.title = count === 1 ? "1 listener" : count + " listeners";
  }

  // ─── Reactions ────────────────────────────────────────────────────────────

  async function sendReaction(type) {
    if (socketSend({ t: "reaction", kind: type || "spark" })) {
      // Optimistic local burst; the room echoes it back to everyone.
      showReactionBurst(emojiForReaction(type), type);
      return;
    }
    if (!runtime.session || !runtime.session.reactionsUrl) throw new Error("Load a room before sending reactions.");
    var payload = await postJson(runtime.session.reactionsUrl, { clientId: runtime.clientId, type: type || "spark", label: emojiForReaction(type) });
    renderReactions(payload, true);
  }

  async function refreshReactions() {
    if (!runtime.session || !runtime.session.reactionsUrl) return;
    renderReactions(await fetchJson(runtime.session.reactionsUrl), false);
  }

  function renderReactions(payload, showNew) {
    var items = payload && Array.isArray(payload.items) ? payload.items : [];
    if (!runtime.reactionsPrimed && !showNew) {
      items.forEach(function (item) {
        var id = stringOrEmpty(item && item.id);
        if (id) runtime.seenReactionIds.add(id);
      });
      runtime.reactionsPrimed = true;
      return;
    }
    runtime.reactionsPrimed = true;
    items.forEach(function (item) {
      var id = stringOrEmpty(item && item.id);
      if (!id || runtime.seenReactionIds.has(id)) return;
      runtime.seenReactionIds.add(id);
      showReactionBurst(stringOrEmpty(item && item.label) || emojiForReaction(stringOrEmpty(item && item.type)), stringOrEmpty(item && item.type));
    });
  }

  function emojiForReaction(type) {
    var map = { love: "❤️", fire: "🔥", wow: "😮", boost: "⚡", plus: "+1" };
    return map[type] || "✨";
  }

  function showReactionBurst(label, type) {
    pulseReactionAmbience(type);
    if (!el.reactionLayer) return;
    var burst = document.createElement("div");
    burst.className = "reaction-burst reaction-" + cleanClassToken(type);
    burst.textContent = label;
    burst.style.left = 12 + Math.random() * 76 + "%";
    el.reactionLayer.appendChild(burst);
    window.setTimeout(function () { burst.remove(); }, 1700);
  }

  function pulseReactionAmbience(type) {
    var key = normalizeReactionAmbienceKey(type);
    if (!key) return;
    runtime.reactionHeat[key] = Math.min(1, runtime.reactionHeat[key] + reactionAmbienceStep(key));
    applyReactionAmbience();
    ensureReactionAmbienceLoop();
  }

  function normalizeReactionAmbienceKey(type) {
    switch (cleanClassToken(type)) {
      case "fire": return "fire";
      case "love": return "love";
      case "boost": case "wow": case "plus": case "spark": return "boost";
      default: return "";
    }
  }

  function reactionAmbienceStep(key) {
    if (key === "fire") return 0.22;
    if (key === "love") return 0.16;
    return 0.12;
  }

  function ensureReactionAmbienceLoop() {
    if (runtime.reactionAmbienceTimer) return;
    runtime.reactionAmbienceTimer = window.setInterval(function () {
      runtime.reactionHeat.fire  = Math.max(0, runtime.reactionHeat.fire  - 0.045);
      runtime.reactionHeat.love  = Math.max(0, runtime.reactionHeat.love  - 0.04);
      runtime.reactionHeat.boost = Math.max(0, runtime.reactionHeat.boost - 0.038);
      applyReactionAmbience();
      if (runtime.reactionHeat.fire <= 0 && runtime.reactionHeat.love <= 0 && runtime.reactionHeat.boost <= 0) {
        window.clearInterval(runtime.reactionAmbienceTimer);
        runtime.reactionAmbienceTimer = 0;
      }
    }, 100);
  }

  function applyReactionAmbience() {
    document.body.style.setProperty("--fire-heat",  runtime.reactionHeat.fire.toFixed(3));
    document.body.style.setProperty("--love-heat",  runtime.reactionHeat.love.toFixed(3));
    document.body.style.setProperty("--boost-heat", runtime.reactionHeat.boost.toFixed(3));
  }

  function resetReactionAmbience() {
    runtime.reactionHeat.fire = runtime.reactionHeat.love = runtime.reactionHeat.boost = 0;
    if (runtime.reactionAmbienceTimer) {
      window.clearInterval(runtime.reactionAmbienceTimer);
      runtime.reactionAmbienceTimer = 0;
    }
    applyReactionAmbience();
  }

  // ─── Queue ────────────────────────────────────────────────────────────────

  async function refreshQueueState() {
    if (!runtime.session || !runtime.session.queueUrl) { renderQueue(null); return; }
    var queue = await fetchJson(runtime.session.queueUrl);
    runtime.queue = normalizeQueue(queue);
    renderQueue(runtime.queue);
    preloadUpcomingQueueTracks();
  }

  async function addQueueLink() {
    if (!runtime.caps || !capAllows(runtime.caps.queueAdd)) throw new Error("The host has not enabled adding tracks for you.");
    var value = stringOrEmpty(el.queueUrlInput.value).trim();
    if (!value) return;
    var parsed = new URL(value);
    if (parsed.protocol !== "https:" && parsed.protocol !== "http:") throw new Error("Paste an http or https track link.");

    var item = buildQueueLinkItem(value);
    if (socketSend({ t: "queue.add", item: { url: item.url, title: item.title, sourceKind: item.sourceKind } })) {
      el.queueUrlInput.value = "";
      setStatus("Request sent to room queue.");
      return;
    }

    if (!runtime.session || !runtime.session.queueUrl) throw new Error("Load a room first.");
    var itemsUrl = runtime.session.queueUrl.replace(/\/queue(?:\?.*)?$/, "/queue/items");
    var response = await fetch(itemsUrl, {
      method: "POST",
      cache: "no-store",
      credentials: "omit",
      headers: library.headers(itemsUrl, { "accept": "application/json", "content-type": "application/json" }),
      body: JSON.stringify({ item: item })
    });

    if (!response.ok) throw new Error("Queue update failed with HTTP " + response.status + ".");
    el.queueUrlInput.value = "";
    runtime.queue = normalizeQueue(await response.json());
    renderQueue(runtime.queue);
    setStatus("Request sent to room queue.");
  }

  function normalizeQueue(payload) {
    var items = payload && Array.isArray(payload.items) ? payload.items : [];
    return {
      currentIndex: finiteNumber(payload && payload.currentIndex != null ? payload.currentIndex : -1),
      items: items.map(normalizeQueueItem)
    };
  }

  function normalizeQueueItem(item) {
    return {
      id:          stringOrEmpty(item && item.id),
      sourceKind:  stringOrEmpty(item && item.sourceKind) || "track",
      title:       stringOrEmpty(item && (item.title || item.displayName || item.name)) || "Queued track",
      artist:      stringOrEmpty(item && item.artist),
      url:         stringOrEmpty(item && item.url),
      trackId:     stringOrEmpty(item && item.trackId),
      packageUrl:  firstUrl(item && item.packageUrl)
    };
  }

  function renderQueue(queue) {
    var items = queue && queue.items || [];
    if (el.queueCount) el.queueCount.textContent = items.length > 0 ? String(items.length) : "";
    if (!el.queueList) return;
    el.queueList.replaceChildren();

    if (items.length === 0) {
      var empty = document.createElement("li");
      empty.className = "queue-empty";
      empty.textContent = "No queued tracks yet";
      el.queueList.appendChild(empty);
      return;
    }

    items.slice(0, 30).forEach(function (item, index) {
      var row = document.createElement("li");
      row.className = "queue-item" + (index === queue.currentIndex ? " is-current" : "");
      if (index === queue.currentIndex) row.setAttribute("aria-current", "true");
      var number = document.createElement("span");
      number.className = "queue-number";
      number.textContent = index === queue.currentIndex ? "♫" : String(index + 1).padStart(2, "0");
      var info = document.createElement("div");
      info.className = "queue-info";
      var titleEl = document.createElement("span");
      titleEl.className = "queue-title";
      titleEl.textContent = item.title;
      var kindEl = document.createElement("span");
      kindEl.className = "queue-kind";
      kindEl.textContent = item.artist || item.sourceKind;
      info.append(titleEl, kindEl);
      var actions = document.createElement("div");
      actions.className = "queue-actions";
      if (item.id && runtime.caps) {
        if (capAllows(runtime.caps.queueReorder) && index > 0) queueAction(actions, "↑", "Move " + item.title + " up", function () {
          if (capAllows(runtime.caps.queueReorder) && !socketSend({ t: "queue.move", id: item.id, toIndex: index - 1 })) setStatus("Reconnecting…");
        });
        if (capAllows(runtime.caps.queueRemove)) queueAction(actions, "×", "Remove " + item.title, function () {
          if (capAllows(runtime.caps.queueRemove) && !socketSend({ t: "queue.remove", id: item.id })) setStatus("Reconnecting…");
        });
      }
      row.append(number, info, actions);
      el.queueList.appendChild(row);
    });
  }

  function queueAction(container, text, label, handler) {
    var button = document.createElement("button");
    button.type = "button";
    button.textContent = text;
    button.setAttribute("aria-label", label);
    button.addEventListener("click", handler);
    container.appendChild(button);
  }

  function buildQueueLinkItem(value) {
    return {
      id: "web-" + Date.now().toString(36) + "-" + Math.random().toString(16).slice(2),
      sourceKind: detectQueueSourceKind(value),
      title: buildQueueTitle(value),
      url: value,
      addedBy: "remote",
      addedAtUtc: new Date().toISOString()
    };
  }

  function detectQueueSourceKind(value) {
    var lower = value.toLowerCase();
    if (lower.startsWith("spotify:") || lower.indexOf("open.spotify.com") >= 0) return "spotify";
    if (lower.indexOf("youtu.be") >= 0 || lower.indexOf("youtube.com") >= 0) return "youtube";
    if (lower.indexOf("soundcloud.com") >= 0 || lower.indexOf("snd.sc") >= 0) return "soundcloud";
    if (lower.indexOf("suno.com") >= 0 || lower.indexOf("suno.ai") >= 0) return "suno";
    return "url";
  }

  function buildQueueTitle(value) {
    try {
      var url = new URL(value);
      var parts = url.pathname.split("/").filter(Boolean);
      return decodeURIComponent(parts[parts.length - 1] || url.host);
    } catch (e) { return value; }
  }

  // ─── Preloading ───────────────────────────────────────────────────────────

  function preloadUpcomingQueueTracks() {
    if (!runtime.queue || !runtime.queue.items.length) return;
    var profile = getSyncProfile();
    var keepUrls = {};
    if (runtime.session && runtime.session.packageUrl) keepUrls[runtime.session.packageUrl] = true;

    for (var offset = 1; offset <= profile.preloadLookahead; offset++) {
      var nextIdx = Math.max(0, runtime.queue.currentIndex + offset);
      if (nextIdx >= runtime.queue.items.length) break;
      var item = runtime.queue.items[nextIdx];
      if (!item || !item.trackId || !item.packageUrl) continue;
      keepUrls[item.packageUrl] = true;
      preloadPackage(item);
    }

    trimPreloadedPackages(keepUrls);
  }

  function preloadPackage(item) {
    if (runtime.preloadedPackages[item.packageUrl]) return;
    var generation = runtime.preloadGeneration;
    runtime.preloadedPackages[item.packageUrl] = { loading: true, objectUrl: "", trackId: item.trackId };
    extractAudioObjectUrlFromPackage(item.packageUrl).then(function (assets) {
      if (generation !== runtime.preloadGeneration || !runtime.preloadedPackages[item.packageUrl]) {
        URL.revokeObjectURL(assets.objectUrl);
        if (assets.artworkObjectUrl) URL.revokeObjectURL(assets.artworkObjectUrl);
        return;
      }
      runtime.preloadedPackages[item.packageUrl] = { loading: false, objectUrl: assets.objectUrl, artworkObjectUrl: assets.artworkObjectUrl, trackId: item.trackId };
    }).catch(function () {
      delete runtime.preloadedPackages[item.packageUrl];
    });
  }

  function trimPreloadedPackages(keepUrls) {
    var urls = Object.keys(runtime.preloadedPackages);
    var removable = urls.filter(function (url) { return !keepUrls[url]; });
    var overflow = Math.max(0, urls.length - config.maxPreloadedPackages);
    removable.slice(0, overflow || removable.length).forEach(revokePreloadedPackage);
  }

  function clearPreloadedPackages() {
    Object.keys(runtime.preloadedPackages).forEach(revokePreloadedPackage);
    runtime.preloadGeneration++;
  }

  function revokePreloadedPackage(url) {
    var cached = runtime.preloadedPackages[url];
    if (cached && cached.objectUrl) URL.revokeObjectURL(cached.objectUrl);
    if (cached && cached.artworkObjectUrl) URL.revokeObjectURL(cached.artworkObjectUrl);
    delete runtime.preloadedPackages[url];
  }

  // ─── Audio graph ──────────────────────────────────────────────────────────

  async function ensureAudioGraph() {
    if (runtime.audioContext) {
      if (runtime.audioContext.state === "suspended") await runtime.audioContext.resume();
      return;
    }
    if (!window.AudioContext && !window.webkitAudioContext) return;
    try {
      var Ctor = window.AudioContext || window.webkitAudioContext;
      runtime.audioContext = new Ctor();
      runtime.analyser = runtime.audioContext.createAnalyser();
      runtime.analyser.fftSize = 128;
      runtime.sourceNode = runtime.audioContext.createMediaElementSource(el.audio);
      runtime.sourceNode.connect(runtime.analyser);
      runtime.analyser.connect(runtime.audioContext.destination);
      await runtime.audioContext.resume();
    } catch (e) { runtime.analyser = null; }
  }

  // ─── ZIP package extraction ───────────────────────────────────────────────

  async function extractAudioObjectUrlFromPackage(packageUrl) {
    var startedAt = performance.now();
    try {
      var response = await fetch(packageUrl, { cache: "no-store", credentials: "omit", headers: library.headers(packageUrl) });
      if (!response.ok) {
        var httpErr = new Error("Package download failed with HTTP " + response.status + ".");
        httpErr.handledHttp = true;
        if (response.status === 408 || response.status === 429 || response.status >= 500) {
          reportNetworkTrouble(response.status >= 500 ? 1.5 : 1, httpErr.message);
          httpErr.networkReported = true;
        }
        throw httpErr;
      }

      var contentLength = Number(response.headers.get("content-length"));
      if (Number.isFinite(contentLength) && contentLength > config.maxPackageBytes) {
        throw new Error("Package is larger than the browser playback limit.");
      }

      var buffer = await response.arrayBuffer();
      if (buffer.byteLength > config.maxPackageBytes) throw new Error("Package is larger than the browser playback limit.");
      reportPackageDownloadHealth(buffer.byteLength, performance.now() - startedAt);

      var entry = findStoredZipEntry(buffer, /^audio\/track\.[a-z0-9]+$/i);
      if (!entry) throw new Error("Package does not contain a playable audio entry.");

      var audioBuffer = buffer.slice(entry.dataStart, entry.dataStart + entry.compressedSize);
      var blob = new Blob([audioBuffer], { type: mimeFromFileName(entry.fileName) });
      var artworkObjectUrl = null;
      try {
        var cover = findStoredZipEntry(buffer, /^artwork\/cover(?:\.[a-z0-9]+)?$/i);
        if (cover) artworkObjectUrl = URL.createObjectURL(new Blob([buffer.slice(cover.dataStart, cover.dataStart + cover.compressedSize)]));
      } catch (_) { /* Missing artwork must not prevent audio playback. */ }
      return { objectUrl: URL.createObjectURL(blob), artworkObjectUrl: artworkObjectUrl };
    } catch (err) {
      if (err && !err.networkReported && !err.handledHttp) reportNetworkTrouble(1.5, err.message || "Package download failed.");
      throw err;
    }
  }

  function reportPackageDownloadHealth(byteLength, elapsedMs) {
    var bps = elapsedMs > 0 ? byteLength / (elapsedMs / 1000) : byteLength;
    if (elapsedMs >= 9000 || bps < 180000) { reportNetworkTrouble(2, "Package downloading slowly."); return; }
    reportNetworkSuccess(elapsedMs, "Package download OK.");
  }

  function findStoredZipEntry(buffer, namePattern) {
    var view = new DataView(buffer);
    var bytes = new Uint8Array(buffer);
    var decoder = new TextDecoder("utf-8");
    var eocdOffset = findEndOfCentralDirectory(view);
    if (eocdOffset < 0) throw new Error("Package is not a valid ZIP file.");
    var totalEntries = view.getUint16(eocdOffset + 10, true);
    var centralDirOffset = view.getUint32(eocdOffset + 16, true);
    var offset = centralDirOffset;

    for (var i = 0; i < totalEntries; i++) {
      if (view.getUint32(offset, true) !== 0x02014b50) break;
      var method = view.getUint16(offset + 10, true);
      var compressedSize = view.getUint32(offset + 20, true);
      var fileNameLength = view.getUint16(offset + 28, true);
      var extraLength = view.getUint16(offset + 30, true);
      var commentLength = view.getUint16(offset + 32, true);
      var localHeaderOffset = view.getUint32(offset + 42, true);
      var fileName = decoder.decode(bytes.subarray(offset + 46, offset + 46 + fileNameLength));

      if (namePattern.test(fileName)) {
        if (method !== 0) throw new Error("Browser playback requires the audio entry to be stored uncompressed.");
        if (view.getUint32(localHeaderOffset, true) !== 0x04034b50) throw new Error("Invalid local ZIP header.");
        var localNameLen = view.getUint16(localHeaderOffset + 26, true);
        var localExtraLen = view.getUint16(localHeaderOffset + 28, true);
        var dataStart = localHeaderOffset + 30 + localNameLen + localExtraLen;
        return { compressedSize: compressedSize, dataStart: dataStart, fileName: fileName };
      }

      offset += 46 + fileNameLength + extraLength + commentLength;
    }

    return null;
  }

  function findEndOfCentralDirectory(view) {
    var min = Math.max(0, view.byteLength - 65557);
    for (var offset = view.byteLength - 22; offset >= min; offset--) {
      if (view.getUint32(offset, true) === 0x06054b50) return offset;
    }
    return -1;
  }

  // ─── Network quality ──────────────────────────────────────────────────────

  function reportNetworkSuccess(elapsedMs, label) {
    if (elapsedMs >= config.networkVerySlowFetchMs) { reportNetworkTrouble(2, label); return; }
    if (elapsedMs >= config.networkSlowFetchMs) { reportNetworkTrouble(1, label); return; }
    runtime.networkScore = Math.max(0, runtime.networkScore - 0.35);
    if (runtime.networkMode === "optimized") {
      runtime.networkStableCount++;
      if (runtime.networkStableCount >= config.networkRecoverSuccesses && runtime.networkScore <= 0.5) {
        setNetworkMode("quality", "Network recovered.");
      }
    }
  }

  function reportNetworkTrouble(weight, reason) {
    runtime.networkScore = Math.min(8, runtime.networkScore + Math.max(0.5, weight || 1));
    runtime.networkStableCount = 0;
    if (runtime.networkScore >= 3 && runtime.networkMode !== "optimized") {
      setNetworkMode("optimized", reason || "Network is rough; using smoother sync.");
    }
  }

  function setNetworkMode(mode, reason) {
    if (runtime.networkMode === mode) return;
    runtime.networkMode = mode;
    runtime.networkStableCount = 0;
    if (mode === "quality") runtime.networkScore = 0;
    if (reason) {
      var now = Date.now();
      if (now - runtime.lastNetworkStatusAt >= 4500) {
        runtime.lastNetworkStatusAt = now;
        setStatus(reason);
      }
    }
    if (runtime.sessionLoopsActive && runtime.session) {
      startSessionLoops();
      preloadUpcomingQueueTracks();
    }
  }

  // ─── Canvas meter ─────────────────────────────────────────────────────────

  function drawMeter() {
    window.requestAnimationFrame(drawMeter);
    if (document.hidden || el.playerShell.hidden) return;
    renderLyricsForPosition(el.audio.currentTime || 0);
    if (el.visualizerPanel.hidden) return;
    var canvas = el.meterCanvas;
    if (!canvas) return;
    var ctx = canvas.getContext("2d");
    var rect = canvas.getBoundingClientRect();
    var dpr = window.devicePixelRatio || 1;
    var w = Math.max(1, Math.floor(rect.width * dpr));
    var h = Math.max(1, Math.floor(rect.height * dpr));
    if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
    ctx.clearRect(0, 0, w, h);

    var bars = runtime.reducedMotion.matches ? new Array(36).fill(0.08) : getMeterBars();
    var gap = Math.max(3 * dpr, w / 180);
    var barW = Math.max(5 * dpr, (w - gap * (bars.length - 1)) / bars.length);
    var time = performance.now() / 700;

    for (var i = 0; i < bars.length; i++) {
      var synthetic = 0.32 + Math.sin(time + i * 0.54) * 0.18 + Math.sin(time * 0.48 + i) * 0.08;
      var level = runtime.analyser ? bars[i] : runtime.reducedMotion.matches ? 0.1 : synthetic;
      if (!runtime.lastPlayback || !runtime.lastPlayback.isPlaying) level *= 0.35;
      var barH = Math.max(4 * dpr, level * h * 0.82);
      var x = i * (barW + gap);
      var y = (h - barH) / 2;
      ctx.fillStyle = "rgba(212, 239, 117, " + (runtime.analyser ? 0.95 : 0.38) + ")";
      roundRect(ctx, x, y, barW, barH, 0);
      ctx.fill();
    }
  }

  function getMeterBars() {
    var count = 36;
    if (!runtime.analyser) {
      var bars = new Array(count);
      for (var i = 0; i < count; i++) bars[i] = 0.3;
      return bars;
    }
    var data = new Uint8Array(runtime.analyser.frequencyBinCount);
    runtime.analyser.getByteFrequencyData(data);
    var result = new Array(count);
    for (var j = 0; j < count; j++) {
      result[j] = Math.max(0.05, data[Math.floor(j / count * data.length)] / 255);
    }
    return result;
  }

  function roundRect(ctx, x, y, w, h, r) {
    var sr = Math.min(r, w / 2, h / 2);
    ctx.beginPath();
    ctx.moveTo(x + sr, y);
    ctx.lineTo(x + w - sr, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + sr);
    ctx.lineTo(x + w, y + h - sr);
    ctx.quadraticCurveTo(x + w, y + h, x + w - sr, y + h);
    ctx.lineTo(x + sr, y + h);
    ctx.quadraticCurveTo(x, y + h, x, y + h - sr);
    ctx.lineTo(x, y + sr);
    ctx.quadraticCurveTo(x, y, x + sr, y);
    ctx.closePath();
  }

  // ─── Track rendering ──────────────────────────────────────────────────────

  function renderTrack(track) {
    el.trackTitle.textContent = track.displayName;
    el.trackArtist.textContent = track.artist;
    el.trackFormat.textContent = track.formatName;
    el.trackAlbum.textContent = track.album;
    renderLyrics(track.lyrics || []);
    maybeLoadAlbumArt();
  }

  function maybeLoadAlbumArt() {
    var url = runtime.session && runtime.session.albumArtUrl || runtime.artworkObjectUrl || "";
    el.discArt.hidden = !url;
    if (url) el.discArt.src = url;
    else el.discArt.removeAttribute("src");
    el.backdropImg.style.backgroundImage = url ? "url(" + JSON.stringify(url) + ")" : "";
  }

  function selectMediaView(view, remember) {
    if (view === "lyrics" && !runtime.lyrics.length) view = "visualizer";
    if (remember) runtime.preferredMediaView = view;
    runtime.mediaView = view;
    var lyrics = view === "lyrics";
    el.lyricsPanel.hidden = !lyrics;
    el.visualizerPanel.hidden = lyrics;
    el.lyricsTiming.hidden = !lyrics || !runtime.lyricMode;
    el.followLyrics.hidden = !lyrics || !runtime.lyricMode || runtime.followLyrics;
    [el.visualizerTab, el.lyricsTab].forEach(function (tab, index) {
      var selected = index === (lyrics ? 1 : 0);
      tab.setAttribute("aria-selected", String(selected));
      tab.tabIndex = selected ? 0 : -1;
    });
    if (lyrics) scrollToCurrentLyric();
  }

  function renderLyrics(lines) {
    var hadFocus = el.lyricsPanel.contains(document.activeElement) || document.activeElement === el.lyricsTab;
    runtime.lyrics = lines;
    runtime.lyricIndex = -1;
    runtime.lyricWordIndex = -1;
    runtime.followLyrics = true;
    runtime.lyricMode = window.RoomLyrics.mode(lines);
    el.lyricsTab.hidden = !runtime.lyricMode;
    el.lyricsTiming.textContent = runtime.lyricMode === "word" ? "Word timed" : runtime.lyricMode === "line" ? "Line timed" : "";
    el.lyricsTiming.title = runtime.lyricMode ? "Timing provided by this track's lyrics" : "";
    el.lyricsPanel.dataset.timing = runtime.lyricMode || "";
    runtime.lyricNodes = lines.map(function (line) {
      // Plain text nodes, never links/buttons: lyrics cannot control playback.
      var node = document.createElement("p");
      node.className = "lyric-line" + (line.words.length ? " has-words" : "");
      if (line.words.length) {
        line.words.forEach(function (word) {
          var span = document.createElement("span");
          span.className = "lyric-word";
          span.textContent = word.text;
          node.appendChild(span);
        });
      } else node.textContent = line.text;
      return node;
    });
    el.lyricsLines.replaceChildren.apply(el.lyricsLines, runtime.lyricNodes);
    el.lyricsPanel.scrollTop = 0;
    selectMediaView(document.fullscreenElement === el.visualizerPanel ? "visualizer" : runtime.preferredMediaView, false);
    if (hadFocus && !lines.length) el.visualizerTab.focus();
    renderLyricsForPosition(el.audio.currentTime || 0);
  }

  function scrollToCurrentLyric() {
    var node = runtime.lyricNodes[runtime.lyricIndex];
    if (!node || el.lyricsPanel.hidden || !runtime.followLyrics) return;
    el.lyricsPanel.scrollTo({
      top: Math.max(0, node.offsetTop - el.lyricsPanel.clientHeight * 0.35),
      behavior: runtime.reducedMotion.matches ? "instant" : "smooth"
    });
  }

  function updateTimeline() {
    var duration = finiteNumber((runtime.lastPlayback && runtime.lastPlayback.durationSeconds) || el.audio.duration || 0);
    var position = finiteNumber(el.audio.currentTime || 0);
    if (el.currentTime) el.currentTime.textContent = formatTime(position);
    if (el.durationTime) el.durationTime.textContent = formatTime(duration);
    el.seekTrack.setAttribute("aria-valuemax", String(duration || 100));
    el.seekTrack.setAttribute("aria-valuenow", String(position));
    el.seekTrack.setAttribute("aria-valuetext", formatTime(position) + " of " + formatTime(duration));
    if (el.seekFill) {
      var pct = duration > 0 ? (position / duration * 100).toFixed(2) : "0";
      el.seekFill.style.width = pct + "%";
    }
    if (el.transportPlay) {
      var playing = Boolean(runtime.lastPlayback && runtime.lastPlayback.isPlaying);
      el.playIcon.setAttribute("href", playing ? "#i-pause" : "#i-play");
      el.transportPlay.setAttribute("aria-label", playing ? "Pause" : "Play");
    }
    renderLyricsForPosition(position);
  }

  // ─── Lyrics ───────────────────────────────────────────────────────────────

  function renderLyricsForPosition(position) {
    if (!runtime.lyrics.length) return;
    var index = window.RoomLyrics.indexAt(runtime.lyrics, position);
    var lineChanged = index !== runtime.lyricIndex;
    if (lineChanged) {
      runtime.lyricIndex = index;
      runtime.lyricWordIndex = -1;
      runtime.lyricNodes.forEach(function (node, i) {
        node.classList.toggle("is-current", i === index);
        node.classList.toggle("is-past", i < index);
        node.querySelectorAll(".lyric-word").forEach(function (word) {
          word.classList.remove("is-sung", "is-active");
        });
      });
      scrollToCurrentLyric();
    }
    if (index < 0) return;
    var line = runtime.lyrics[index];
    var wordIndex = window.RoomLyrics.indexAt(line.words, position);
    if (lineChanged || wordIndex !== runtime.lyricWordIndex) {
      runtime.lyricWordIndex = wordIndex;
      Array.prototype.forEach.call(runtime.lyricNodes[index].children, function (word, i) {
        word.classList.toggle("is-sung", i <= wordIndex);
        word.classList.toggle("is-active", i === wordIndex);
      });
    }
  }

  function updateReactionButtons() {
    var enabled = Boolean(runtime.session && runtime.session.reactionsUrl);
    el.reactionButtons.forEach(function (btn) { btn.disabled = !enabled; });
  }

  function resetAudio() {
    el.audio.pause();
    el.audio.removeAttribute("src");
    el.audio.load();
    if (el.disc) el.disc.classList.remove("is-playing");
    if (el.discArt) { el.discArt.removeAttribute("src"); el.discArt.hidden = true; }
    if (el.backdropImg) el.backdropImg.style.backgroundImage = "";
    runtime.audioReady = false;
    runtime.hostStateAvailable = false;
    runtime.lastPlayback = null;
    runtime.activeTrackId = "";
    renderLyrics([]);
    if (runtime.artworkObjectUrl) URL.revokeObjectURL(runtime.artworkObjectUrl);
    runtime.artworkObjectUrl = null;
    if (runtime.audioObjectUrl) {
      URL.revokeObjectURL(runtime.audioObjectUrl);
      runtime.audioObjectUrl = null;
    }
    updateTimeline();
  }

  function roomJoinLink() {
    return runtime.channelId ? createWebShareChannelUrl(runtime.channelId) :
      (runtime.session && runtime.session.joinUrl) || createWebShareJoinUrl(runtime.activeRoomCode);
  }

  function copyJoinLink() {
    var joinUrl = roomJoinLink();
    if (!joinUrl) return;
    if (!navigator.clipboard) {
      el.inviteLink.select();
      el.inviteStatus.textContent = "Copy the selected link to invite a friend.";
      return;
    }
    navigator.clipboard.writeText(joinUrl).then(function () {
      el.inviteStatus.textContent = "Link copied.";
      setStatus("Room link copied.");
    }).catch(function () {
      el.inviteLink.select();
      el.inviteStatus.textContent = "Copy the selected link to invite a friend.";
    });
  }

  function openInSpectralis() {
    var code = runtime.activeRoomCode;
    if (!code) return;
    setStatus("Opening Spectralis…");
    window.location.href = createSpectralisJoinUrl(code);
  }

  function setStatus(message) {
    if (el.statusLine) el.statusLine.textContent = message;
  }

  function setDotState(state) {
    if (!el.statusDot) return;
    el.statusDot.dataset.state = state;
    el.statusDot.className = "status-dot dot-" + state;
    el.connectionLabel.textContent = { live: "Live", paused: "Paused", loading: "Connecting", waiting: "Offline", error: "Disconnected" }[state] || state;
  }

  // ─── Channel stats ────────────────────────────────────────────────────────

  function renderChannelStats(stats) {
    if (!el.channelStats) return;
    if (!stats || typeof stats !== "object") { el.channelStats.hidden = true; return; }
    el.channelStats.hidden = false;
    var peak = finiteNumber(stats.peakConcurrentListeners || 0);
    var topTrack = Array.isArray(stats.trackStats) && stats.trackStats.length > 0 ? stats.trackStats[0] : null;
    if (el.channelSharedHours) el.channelSharedHours.textContent = formatHours(finiteNumber(stats.totalSharedSeconds)) + " shared";
    if (el.channelListenerHours) el.channelListenerHours.textContent = formatHours(finiteNumber(stats.totalListenerSeconds)) + " listener-hours";
    if (el.channelPeakListeners) el.channelPeakListeners.textContent = peak + " peak";
    if (el.channelTopTrack) el.channelTopTrack.textContent = topTrack ? "Top: " + (stringOrEmpty(topTrack.title) || "Track") : "";
  }

  // ─── URL helpers ──────────────────────────────────────────────────────────

  function v2Url(path) {
    return new URL("/shared-play/v2/" + path, config.cdnBaseUrl).toString();
  }

  function toCdnUrl(path) {
    return new URL(path, config.cdnBaseUrl).toString();
  }

  function createWebShareJoinUrl(roomCode) {
    try {
      var url = new URL("/spectralis/web-share/", config.cdnBaseUrl);
      url.searchParams.set("session", roomCode);
      return url.toString();
    } catch (e) { return ""; }
  }

  function createWebShareChannelUrl(channelId) {
    try {
      var url = new URL("/spectralis/web-share/", config.cdnBaseUrl);
      url.searchParams.set("channel", channelId);
      return url.toString();
    } catch (e) { return ""; }
  }

  function createSpectralisJoinUrl(roomCode) {
    var q = new URLSearchParams();
    q.set("session", roomCode);
    q.set("cdn", config.cdnBaseUrl);
    return "spectralis://shared-play/join?" + q.toString();
  }

  function normalizeBaseUrl(value) {
    try {
      var url = new URL(value);
      return url.protocol === "https:" ? url.origin : config.cdnBaseUrl;
    } catch (e) { return config.cdnBaseUrl; }
  }

  function cleanChannelId(value) {
    return decodeURIComponent(stringOrEmpty(value)).trim().replace(/[^a-zA-Z0-9_-]/g, "");
  }

  function firstUrl() {
    for (var i = 0; i < arguments.length; i++) {
      var value = arguments[i];
      if (typeof value !== "string" || !value.trim()) continue;
      try {
        var url = new URL(value, config.cdnBaseUrl);
        if (url.protocol === "https:") return url.toString();
      } catch (e) { }
    }
    return "";
  }

  function firstPlayableUrl() {
    for (var i = 0; i < arguments.length; i++) {
      var url = firstUrl(arguments[i]);
      if (url && !/\.zip($|\?)/i.test(url)) return url;
    }
    return "";
  }

  function findUploadAssetUrl(uploads, name) {
    if (!Array.isArray(uploads)) return "";
    for (var i = 0; i < uploads.length; i++) {
      var u = uploads[i];
      if (u && u.name === name && u.assetUrl) return u.assetUrl;
    }
    return "";
  }

  // ─── HTTP helpers ─────────────────────────────────────────────────────────

  async function fetchJson(url) {
    var startedAt = performance.now();
    try {
      var response = await fetch(url, { cache: "no-store", credentials: "omit", headers: library.headers(url, { "accept": "application/json" }) });
      var elapsedMs = performance.now() - startedAt;
      if (!response.ok) {
        var httpErr = new Error("Request failed with HTTP " + response.status + ".");
        httpErr.handledHttp = true;
        if (response.status === 408 || response.status === 429 || response.status >= 500) {
          reportNetworkTrouble(response.status >= 500 ? 1.5 : 1, httpErr.message);
          httpErr.networkReported = true;
        }
        throw httpErr;
      }
      reportNetworkSuccess(elapsedMs, "Request OK.");
      return JSON.parse(await response.text());
    } catch (err) {
      if (err && !err.networkReported && !err.handledHttp && err.name !== "SyntaxError") {
        reportNetworkTrouble(1.5, err.message || "Request failed.");
      }
      throw err;
    }
  }

  async function postJson(url, payload) {
    var startedAt = performance.now();
    try {
      var response = await fetch(url, {
        method: "POST",
        cache: "no-store",
        credentials: "omit",
        headers: library.headers(url, { "accept": "application/json", "content-type": "application/json" }),
        body: JSON.stringify(payload || {})
      });
      var elapsedMs = performance.now() - startedAt;
      if (!response.ok) {
        var httpErr = new Error("Request failed with HTTP " + response.status + ".");
        httpErr.handledHttp = true;
        if (response.status === 408 || response.status === 429 || response.status >= 500) {
          reportNetworkTrouble(response.status >= 500 ? 1.5 : 1, httpErr.message);
          httpErr.networkReported = true;
        }
        throw httpErr;
      }
      reportNetworkSuccess(elapsedMs, "Request OK.");
      return JSON.parse(await response.text());
    } catch (err) {
      if (err && !err.networkReported && !err.handledHttp && err.name !== "SyntaxError") {
        reportNetworkTrouble(1.5, err.message || "Request failed.");
      }
      throw err;
    }
  }

  // ─── Local storage ────────────────────────────────────────────────────────

  function getClientId() {
    var key = "spectralisSharedPlayClientId";
    try {
      var existing = window.localStorage.getItem(key);
      if (existing) return existing;
      var created = "web-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);
      window.localStorage.setItem(key, created);
      return created;
    } catch (e) {
      return "web-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);
    }
  }

  // ─── Format helpers ───────────────────────────────────────────────────────

  function mimeFromFileName(fileName) {
    var ext = fileName.split(".").pop().toLowerCase();
    var map = { aac: "audio/aac", aif: "audio/aiff", aifc: "audio/aiff", aiff: "audio/aiff",
                flac: "audio/flac", m4a: "audio/mp4", mp3: "audio/mpeg", oga: "audio/ogg",
                ogg: "audio/ogg", opus: "audio/ogg", wav: "audio/wav", webm: "audio/webm",
                wma: "audio/x-ms-wma" };
    return map[ext] || "application/octet-stream";
  }

  function formatTime(seconds) {
    var s = Math.max(0, Math.floor(finiteNumber(seconds)));
    return Math.floor(s / 60) + ":" + String(s % 60).padStart(2, "0");
  }

  function formatHours(seconds) {
    var h = finiteNumber(seconds) / 3600;
    return h < 10 ? h.toFixed(1) + "h" : Math.round(h) + "h";
  }

  function finiteNumber(value) {
    var n = Number(value);
    return Number.isFinite(n) ? n : 0;
  }

  function stringOrEmpty(value) {
    return typeof value === "string" ? value : "";
  }

  function cleanClassToken(value) {
    return stringOrEmpty(value).toLowerCase().replace(/[^a-z0-9_-]/g, "") || "spark";
  }
})();
