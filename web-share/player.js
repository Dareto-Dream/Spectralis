(function () {
  "use strict";

  var config = {
    cdnBaseUrl: window.location.origin || "https://audioplayer-production-5b83.up.railway.app",
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
    lyricCurrent:         document.getElementById("lyricCurrent"),
    lyricNext:            document.getElementById("lyricNext"),
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
    seekFill:             document.getElementById("seekFill"),
    statusDot:            document.getElementById("statusDot"),
    statusLine:           document.getElementById("statusLine"),
    trackArtist:          document.getElementById("trackArtist"),
    trackKicker:          document.getElementById("trackKicker"),
    trackTitle:           document.getElementById("trackTitle"),
    volumeSlider:         document.getElementById("volumeSlider")
  };

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
    lyrics:                [],
    lyricIndex:            -1,
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

  init();

  function init() {
    applyExternalConfig();
    bindEvents();
    drawMeter();

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

    el.audio.addEventListener("loadedmetadata", function () {
      runtime.audioReady = true;
      reportNetworkSuccess(0, "audio metadata");
      updateTimeline();
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
    resetAudio();
    clearPreloadedPackages();
    runtime.session = null;
    runtime.queue = null;
    if (!options.fromChannel) leaveChannelMode();

    runtime.activeRoomCode = roomCode;
    runtime.userActivated = false;
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
      runtime.activeTrackId = runtime.session.trackId;
      renderTrack(runtime.session.track);
      await prepareAudio(runtime.session);
      await refreshQueueState();
      await refreshPlaybackState();
      startSessionLoops();
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
      resetAudio();
      runtime.session = null;
      runtime.queue = null;
      setDotState("waiting");
      el.trackTitle.textContent = payload.displayName || "Live Channel";
      el.trackArtist.textContent = "Waiting for the host to start sharing.";
      el.trackKicker.textContent = "Permanent listen link";
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
    el.trackKicker.textContent = "Live Channel";
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
    el.trackKicker.textContent = payload.isLive ? "Live Channel · On air" : "Live Channel · Waiting";
  }

  // ─── Session normalization ────────────────────────────────────────────────

  function normalizeSession(roomCode, payload) {
    var src = payload || {};
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
    var albumArtUrl = firstUrl(src.albumArtUrl, src.artUrl, src.coverUrl) ||
      (packageUrl ? v2Url("sessions/" + encodeURIComponent(roomCode) + "/art") : "");

    return {
      roomCode:       stringOrEmpty(src.roomCode || roomCode),
      displayCode:    stringOrEmpty(src.displayCode) || displayRoomCode(roomCode),
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
    if (!Array.isArray(lines)) return [];
    return lines.map(function (line) {
      var text = stringOrEmpty(line && (line.text || line.value || line.line)).trim();
      if (!text) return null;
      return { timeSeconds: finiteNumber(line.timeSeconds || line.startTime || line.time || 0), text: text };
    }).filter(Boolean).sort(function (a, b) {
      return a.timeSeconds - b.timeSeconds;
    }).slice(0, 400);
  }

  // ─── Audio ────────────────────────────────────────────────────────────────

  async function prepareAudio(session) {
    el.audio.pause();
    el.audio.removeAttribute("src");
    runtime.audioReady = false;

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
      delete runtime.preloadedPackages[session.packageUrl];
      el.audio.src = runtime.audioObjectUrl;
      el.audio.load();
      setStatus("Preloaded audio ready.");
      return;
    }

    setStatus("Extracting audio from room package…");
    runtime.audioObjectUrl = await extractAudioObjectUrlFromPackage(session.packageUrl);
    el.audio.src = runtime.audioObjectUrl;
    el.audio.load();
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
      setDotState("live");
      setStatus("Next track ready.");
    } finally {
      runtime.switchingTrack = false;
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
            setStatus("Tap to start audio.");
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
    if (!runtime.session || !runtime.session.queueUrl) throw new Error("Load a room first.");
    var value = stringOrEmpty(el.queueUrlInput.value).trim();
    if (!value) return;

    var itemsUrl = runtime.session.queueUrl.replace(/\/queue(?:\?.*)?$/, "/queue/items");
    var response = await fetch(itemsUrl, {
      method: "POST",
      cache: "no-store",
      credentials: "omit",
      headers: { "accept": "application/json", "content-type": "application/json" },
      body: JSON.stringify({ item: buildQueueLinkItem(value) })
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
      var titleEl = document.createElement("span");
      titleEl.className = "queue-title";
      titleEl.textContent = item.title + (item.artist ? " — " + item.artist : "");
      var kindEl = document.createElement("span");
      kindEl.className = "queue-kind";
      kindEl.textContent = item.sourceKind;
      row.appendChild(titleEl);
      row.appendChild(kindEl);
      el.queueList.appendChild(row);
    });
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
    extractAudioObjectUrlFromPackage(item.packageUrl).then(function (objectUrl) {
      if (generation !== runtime.preloadGeneration) { URL.revokeObjectURL(objectUrl); return; }
      runtime.preloadedPackages[item.packageUrl] = { loading: false, objectUrl: objectUrl, trackId: item.trackId };
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
    delete runtime.preloadedPackages[url];
  }

  // ─── Audio graph ──────────────────────────────────────────────────────────

  async function ensureAudioGraph() {
    if (runtime.audioContext || (!window.AudioContext && !window.webkitAudioContext)) return;
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
      var response = await fetch(packageUrl, { cache: "no-store", credentials: "omit" });
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
      return URL.createObjectURL(blob);
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
    var canvas = el.meterCanvas;
    if (!canvas) { window.requestAnimationFrame(drawMeter); return; }
    var ctx = canvas.getContext("2d");
    var rect = canvas.getBoundingClientRect();
    var dpr = window.devicePixelRatio || 1;
    var w = Math.max(1, Math.floor(rect.width * dpr));
    var h = Math.max(1, Math.floor(rect.height * dpr));
    if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
    ctx.clearRect(0, 0, w, h);

    var bars = getMeterBars();
    var gap = Math.max(3 * dpr, w / 180);
    var barW = Math.max(5 * dpr, (w - gap * (bars.length - 1)) / bars.length);
    var time = performance.now() / 700;

    for (var i = 0; i < bars.length; i++) {
      var synthetic = 0.32 + Math.sin(time + i * 0.54) * 0.18 + Math.sin(time * 0.48 + i) * 0.08;
      var level = runtime.analyser ? bars[i] : synthetic;
      if (!runtime.lastPlayback || !runtime.lastPlayback.isPlaying) level *= 0.35;
      var barH = Math.max(4 * dpr, level * h * 0.82);
      var x = i * (barW + gap);
      var y = (h - barH) / 2;
      ctx.fillStyle = "rgba(216, 161, 58, " + (runtime.analyser ? 0.95 : 0.38) + ")";
      roundRect(ctx, x, y, barW, barH, 5 * dpr);
      ctx.fill();
    }

    window.requestAnimationFrame(drawMeter);
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
    el.trackArtist.textContent = [track.artist, track.album].filter(Boolean).join(" / ") || "";
    runtime.lyrics = track.lyrics || [];
    runtime.lyricIndex = -1;

    var badges = [];
    if (track.formatName) badges.push(track.formatName);
    if (track.hasLyrics || runtime.lyrics.length > 0) badges.push("Lyrics");
    if (track.hasEmbeddedVisualizer) badges.push("Visualizer");
    el.trackKicker.textContent = badges.length ? badges.join(" · ") : "Now Playing";

    if (el.lyricsPanel) {
      el.lyricsPanel.hidden = runtime.lyrics.length === 0;
    }

    renderLyricsForPosition(el.audio.currentTime || 0);
    maybeLoadAlbumArt();
  }

  function maybeLoadAlbumArt() {
    if (!runtime.session || !runtime.session.albumArtUrl) return;
    var url = runtime.session.albumArtUrl;
    if (el.discArt) {
      el.discArt.src = url;
      el.discArt.alt = "";
    }
    if (el.backdropImg) {
      el.backdropImg.style.backgroundImage = "url(" + JSON.stringify(url) + ")";
    }
  }

  // ─── Timeline ─────────────────────────────────────────────────────────────

  function updateTimeline() {
    var duration = finiteNumber((runtime.lastPlayback && runtime.lastPlayback.durationSeconds) || el.audio.duration || 0);
    var position = finiteNumber(el.audio.currentTime || 0);
    if (el.currentTime) el.currentTime.textContent = formatTime(position);
    if (el.durationTime) el.durationTime.textContent = formatTime(duration);
    if (el.seekFill) {
      var pct = duration > 0 ? (position / duration * 100).toFixed(2) : "0";
      el.seekFill.style.width = pct + "%";
    }
    renderLyricsForPosition(position);
  }

  // ─── Lyrics ───────────────────────────────────────────────────────────────

  function renderLyricsForPosition(position) {
    if (!el.lyricCurrent || !el.lyricNext) return;
    if (!runtime.lyrics.length) {
      el.lyricCurrent.textContent = "";
      el.lyricNext.textContent = "";
      return;
    }

    var index = findLyricIndex(position);
    runtime.lyricIndex = index;
    if (index < 0) {
      el.lyricCurrent.textContent = "";
      el.lyricNext.textContent = runtime.lyrics[0].text;
    } else {
      el.lyricCurrent.textContent = runtime.lyrics[index].text;
      el.lyricNext.textContent = index + 1 < runtime.lyrics.length ? runtime.lyrics[index + 1].text : "";
    }
  }

  function findLyricIndex(position) {
    var low = 0;
    var high = runtime.lyrics.length - 1;
    var found = -1;
    var target = finiteNumber(position) + 0.05;
    while (low <= high) {
      var mid = Math.floor((low + high) / 2);
      if (runtime.lyrics[mid].timeSeconds <= target) { found = mid; low = mid + 1; }
      else { high = mid - 1; }
    }
    return found;
  }

  // ─── UI helpers ───────────────────────────────────────────────────────────

  function updateReactionButtons() {
    var enabled = Boolean(runtime.session && runtime.session.reactionsUrl);
    el.reactionButtons.forEach(function (btn) { btn.disabled = !enabled; });
  }

  function resetAudio() {
    el.audio.pause();
    el.audio.removeAttribute("src");
    el.audio.load();
    if (el.disc) el.disc.classList.remove("is-playing");
    if (el.discArt) el.discArt.src = "";
    if (el.backdropImg) el.backdropImg.style.backgroundImage = "";
    runtime.audioReady = false;
    runtime.hostStateAvailable = false;
    runtime.lastPlayback = null;
    runtime.activeTrackId = "";
    runtime.lyrics = [];
    runtime.lyricIndex = -1;
    if (runtime.audioObjectUrl) {
      URL.revokeObjectURL(runtime.audioObjectUrl);
      runtime.audioObjectUrl = null;
    }
    updateTimeline();
  }

  function copyJoinLink() {
    var joinUrl = runtime.channelId
      ? createWebShareChannelUrl(runtime.channelId)
      : (runtime.session && runtime.session.joinUrl) || "";
    if (!joinUrl) return;
    navigator.clipboard.writeText(joinUrl).then(function () {
      setStatus(runtime.channelId ? "Channel link copied." : "Room link copied.");
    }).catch(function () { setStatus(joinUrl); });
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
      var url = new URL("/spectralis/web-share", config.cdnBaseUrl);
      url.searchParams.set("session", roomCode);
      return url.toString();
    } catch (e) { return ""; }
  }

  function createWebShareChannelUrl(channelId) {
    try {
      var url = new URL("/spectralis/web-share", config.cdnBaseUrl);
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
      var response = await fetch(url, { cache: "no-store", credentials: "omit", headers: { "accept": "application/json" } });
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
        headers: { "accept": "application/json", "content-type": "application/json" },
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
