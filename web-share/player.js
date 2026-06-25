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

  var elements = {
    audio: document.getElementById("sharedAudio"),
    albumPlate: document.getElementById("albumPlate"),
    canvas: document.getElementById("meterCanvas"),
    copyButton: document.getElementById("copyButton"),
    channelName: document.getElementById("channelName"),
    channelPanel: document.getElementById("channelPanel"),
    channelStats: document.getElementById("channelStats"),
    channelSharedHours: document.getElementById("channelSharedHours"),
    channelListenerHours: document.getElementById("channelListenerHours"),
    channelPeakListeners: document.getElementById("channelPeakListeners"),
    channelTopTrack: document.getElementById("channelTopTrack"),
    currentTime: document.getElementById("currentTime"),
    durationTime: document.getElementById("durationTime"),
    joinButton: document.getElementById("joinButton"),
    joinForm: document.getElementById("joinForm"),
    experienceKicker: document.getElementById("experienceKicker"),
    experienceTitle: document.getElementById("experienceTitle"),
    listenerCount: document.getElementById("listenerCount"),
    lyricCurrent: document.getElementById("lyricCurrent"),
    lyricNext: document.getElementById("lyricNext"),
    lyricsPanel: document.getElementById("lyricsPanel"),
    networkMode: document.getElementById("networkMode"),
    openAppButton: document.getElementById("openAppButton"),
    queueAddForm: document.getElementById("queueAddForm"),
    queueCount: document.getElementById("queueCount"),
    queueList: document.getElementById("queueList"),
    queueUrlInput: document.getElementById("queueUrlInput"),
    reactionButtons: Array.prototype.slice.call(document.querySelectorAll("[data-reaction]")),
    reactionLayer: document.getElementById("reactionLayer"),
    seekSlider: document.getElementById("seekSlider"),
    sessionInput: document.getElementById("sessionInput"),
    sessionState: document.getElementById("sessionState"),
    statusLine: document.getElementById("statusLine"),
    statusSpinner: document.getElementById("statusSpinner"),
    trackArtist: document.getElementById("trackArtist"),
    trackKicker: document.getElementById("trackKicker"),
    trackTitle: document.getElementById("trackTitle"),
    volumeSlider: document.getElementById("volumeSlider")
  };

  var runtime = {
    analyser: null,
    audioContext: null,
    audioObjectUrl: null,
    audioReady: false,
    clientId: getClientId(),
    channelId: "",
    channelTimer: 0,
    drawing: false,
    hostStateAvailable: false,
    lastPlayback: null,
    activeTrackId: "",
    lyrics: [],
    lyricIndex: -1,
    lastNetworkStatusAt: 0,
    networkMode: "quality",
    networkScore: 0,
    networkStableCount: 0,
    presenceTimer: 0,
    preloadGeneration: 0,
    preloadedPackages: {},
    pollTimer: 0,
    queue: null,
    reactionAmbienceTimer: 0,
    reactionHeat: {
      fire: 0,
      love: 0,
      boost: 0
    },
    reactionsPrimed: false,
    reactionsTimer: 0,
    seenReactionIds: new Set(),
    session: null,
    sessionId: "",
    sourceNode: null,
    switchingTrack: false,
    sessionLoopsActive: false,
    syncTimer: 0,
    userActivated: false
  };

  init();

  function init() {
    applyExternalConfig();
    applyLaunchMode();
    updateNetworkModeUi();
    bindEvents();
    drawMeter();

    var initialChannelId = extractChannelId(window.location.href);
    var initialSessionId = extractSessionId(window.location.href);
    if (initialChannelId) {
      loadChannel(initialChannelId);
    } else if (initialSessionId) {
      elements.sessionInput.value = initialSessionId;
      loadSession(initialSessionId, { fromChannel: false });
    } else {
      setSessionState("Offline");
      setStatus("No session loaded.");
      updateJoinButton();
    }
  }

  function applyLaunchMode() {
    var params = new URLSearchParams(window.location.search);
    var source = stringOrEmpty(params.get("source")).toLowerCase();
    var mode = stringOrEmpty(params.get("mode")).toLowerCase();
    var isDiscord = source === "discord" || mode === "activity";

    document.body.classList.toggle("is-discord-activity", isDiscord);
    if (isDiscord) {
      elements.experienceKicker.textContent = "Spectralis / Discord";
      elements.experienceTitle.textContent = "Listen Together";
      document.title = "Spectralis Listen Together";
    }
  }

  function applyExternalConfig() {
    var external = window.SPECTRALIS_SHARED_PLAY_CONFIG;
    if (!external || typeof external !== "object") {
      return;
    }

    if (typeof external.cdnBaseUrl === "string") {
      config.cdnBaseUrl = normalizeBaseUrl(external.cdnBaseUrl);
    }

    if (typeof external.allowRichPackageFallback === "boolean") {
      config.allowRichPackageFallback = external.allowRichPackageFallback;
    }
  }

  function bindEvents() {
    elements.joinForm.addEventListener("submit", function (event) {
      event.preventDefault();
      leaveChannelMode();
      loadSession(elements.sessionInput.value, { fromChannel: false });
    });

    elements.sessionInput.addEventListener("input", function () {
      updateJoinButton();
    });

    elements.joinButton.addEventListener("click", function () {
      activatePlayback();
    });

    elements.copyButton.addEventListener("click", function () {
      copyJoinLink();
    });

    elements.openAppButton.addEventListener("click", function () {
      openInSpectralis();
    });

    elements.reactionButtons.forEach(function (button) {
      button.addEventListener("click", function () {
        sendReaction(
          stringOrEmpty(button.getAttribute("data-reaction")),
          stringOrEmpty(button.getAttribute("data-label"))
        ).catch(function (error) {
          setStatus(error.message || "Reaction could not be sent.");
        });
      });
    });

    elements.queueAddForm.addEventListener("submit", function (event) {
      event.preventDefault();
      addQueueLink().catch(function (error) {
        setStatus(error.message || "Queue link could not be added.");
      });
    });

    elements.volumeSlider.addEventListener("input", function () {
      elements.audio.volume = Number(elements.volumeSlider.value) / 100;
    });

    elements.seekSlider.addEventListener("input", function () {
      if (!runtime.audioReady || !Number.isFinite(elements.audio.duration)) {
        return;
      }

      if (runtime.hostStateAvailable && runtime.lastPlayback !== null) {
        return;
      }

      var ratio = Number(elements.seekSlider.value) / 1000;
      elements.audio.currentTime = Math.max(0, elements.audio.duration * ratio);
      updateTimeline();
    });

    elements.audio.addEventListener("loadedmetadata", function () {
      runtime.audioReady = true;
      reportNetworkSuccess(0, "audio metadata");
      updateTimeline();
      updateJoinButton();
    });

    elements.audio.addEventListener("timeupdate", updateTimeline);

    elements.audio.addEventListener("canplay", function () {
      reportNetworkSuccess(0, "audio canplay");
    });

    elements.audio.addEventListener("play", function () {
      elements.albumPlate.classList.add("is-playing");
      updateJoinButton();
    });

    elements.audio.addEventListener("playing", function () {
      reportNetworkSuccess(0, "audio playing");
    });

    elements.audio.addEventListener("waiting", function () {
      if (runtime.userActivated && runtime.lastPlayback && runtime.lastPlayback.isPlaying) {
        reportNetworkTrouble(1.5, "Audio is buffering.");
      }
    });

    elements.audio.addEventListener("stalled", function () {
      reportNetworkTrouble(2, "Audio stream stalled.");
    });

    elements.audio.addEventListener("pause", function () {
      elements.albumPlate.classList.remove("is-playing");
      elements.audio.playbackRate = 1;
      updateJoinButton();
    });

    elements.audio.addEventListener("error", function () {
      setSessionState("Error", "error");
      setStatus("Audio could not be loaded for this session.");
    });
  }

  async function loadSession(input, options) {
    options = options || {};
    var sessionId = extractSessionId(input);
    if (!sessionId) {
      setSessionState("Error", "error");
      setStatus("Enter a valid Shared Play session ID or join URL.");
      return;
    }

    stopSessionLoops();
    resetAudio();
    clearPreloadedPackages();
    runtime.session = null;
    runtime.queue = null;
    if (!options.fromChannel) {
      leaveChannelMode();
    }

    runtime.sessionId = sessionId;
    runtime.userActivated = false;
    runtime.reactionsPrimed = false;
    runtime.seenReactionIds = new Set();
    resetReactionAmbience();
    updateListenerCount(0);
    if (elements.reactionLayer) {
      elements.reactionLayer.replaceChildren();
    }
    elements.sessionInput.value = options.fromChannel && runtime.channelId ? runtime.channelId : sessionId;
    elements.joinButton.disabled = true;
    updateReactionButtons();
    setSessionState("Loading");
    setSpinning(true);
    setStatus("Loading Shared Play session...");

    try {
      var sessionPayload = await fetchSession(sessionId);
      runtime.session = normalizeSession(sessionId, sessionPayload);
      runtime.activeTrackId = runtime.session.trackId;
      renderTrack(runtime.session.track);
      await prepareAudio(runtime.session);
      await refreshQueueState();
      await refreshPlaybackState();
      startSessionLoops();
      setSessionState("Ready", "live");
      setSpinning(false);
      setStatus(runtime.hostStateAvailable ? "Session loaded." : "Ready — waiting for host to start...");
      updateSeekSliderInteractivity();
      updateJoinButton();
      updateOpenAppButton();
    } catch (error) {
      setSpinning(false);
      setSessionState("Error", "error");
      setStatus(error.message || "Shared Play session could not be loaded.");
      updateJoinButton();
      updateOpenAppButton();
    }
  }

  async function fetchSession(sessionId) {
    var encoded = encodeURIComponent(sessionId);
    var candidates = [
      "/shared-play/v1/sessions/" + encoded,
      "/shared-play/v1/sessions/" + encoded + "/manifest",
      "/shared-play/v1/join/" + encoded,
      "/shared-play/join/" + encoded,
      "/shared-play/v1/sessions/" + encoded + "/state"
    ];

    var lastError = null;
    for (var index = 0; index < candidates.length; index += 1) {
      try {
        return await fetchJson(toCdnUrl(candidates[index]));
      } catch (error) {
        lastError = error;
      }
    }

    throw lastError || new Error("Shared Play session was not found.");
  }

  async function loadChannel(channelId) {
    channelId = cleanChannelId(channelId);
    if (!channelId) {
      setSessionState("Error", "error");
      setStatus("Enter a valid Live Channel link.");
      return;
    }

    runtime.channelId = channelId;
    enterChannelMode(channelId);
    elements.sessionInput.value = channelId;
    setSessionState("Channel");
    setStatus("Loading Live Channel...");

    await refreshChannel();
    if (runtime.channelTimer) {
      window.clearInterval(runtime.channelTimer);
    }
    runtime.channelTimer = window.setInterval(function () {
      refreshChannel().catch(function () {
        setStatus("Waiting for Live Channel...");
      });
    }, config.channelPollIntervalMs);
  }

  async function refreshChannel() {
    if (!runtime.channelId) {
      return;
    }

    var payload = await fetchJson(toCdnUrl("/shared-play/v1/channels/" + encodeURIComponent(runtime.channelId)));
    updateChannelHeader(payload);
    renderChannelStats(payload.stats);

    if (!payload.isLive || !payload.sessionId) {
      stopSessionLoops();
      resetAudio();
      runtime.session = null;
      runtime.queue = null;
      setSessionState("Waiting");
      elements.trackTitle.textContent = payload.displayName ? payload.displayName : "Live Channel";
      elements.trackArtist.textContent = "Waiting for the host to start sharing.";
      elements.trackKicker.textContent = "Permanent listen link";
      setStatus("This Live Channel is not live right now.");
      return;
    }

    if (!runtime.session || runtime.session.sessionId !== payload.sessionId) {
      await loadSession(payload.sessionId, { fromChannel: true });
    } else if (payload.displayName) {
      updateChannelHeader(payload);
    }
  }

  function enterChannelMode(channelId) {
    document.body.classList.add("is-channel-room");
    if (elements.channelPanel) {
      elements.channelPanel.hidden = false;
    }

    elements.experienceKicker.textContent = "Spectralis";
    elements.experienceTitle.textContent = "Live Channel";
    elements.trackKicker.textContent = "Permanent listen link";
    elements.openAppButton.textContent = "Open App";
    elements.copyButton.textContent = "Copy Channel";
    if (elements.channelName) {
      elements.channelName.textContent = channelId || "Channel";
    }
  }

  function leaveChannelMode() {
    runtime.channelId = "";
    if (runtime.channelTimer) {
      window.clearInterval(runtime.channelTimer);
      runtime.channelTimer = 0;
    }

    document.body.classList.remove("is-channel-room");
    if (elements.channelPanel) {
      elements.channelPanel.hidden = true;
    }

    elements.experienceKicker.textContent = document.body.classList.contains("is-discord-activity")
      ? "Spectralis / Discord"
      : "Spectralis";
    elements.experienceTitle.textContent = document.body.classList.contains("is-discord-activity")
      ? "Listen Together"
      : "Shared Play";
    elements.copyButton.textContent = "Copy Link";
  }

  function updateChannelHeader(payload) {
    if (!payload || typeof payload !== "object") {
      return;
    }

    var name = stringOrEmpty(payload.displayName) || runtime.channelId || "Channel";
    if (elements.channelName) {
      elements.channelName.textContent = name;
    }

    elements.experienceTitle.textContent = name;
    elements.trackKicker.textContent = payload.isLive ? "Live Channel / On air" : "Live Channel / Waiting";
  }

  function normalizeSession(sessionId, payload) {
    var source = payload && payload.session ? payload.session : payload || {};
    var track = source.track || source.package && source.package.track || source.manifest && source.manifest.track || {};
    var trackId = stringOrEmpty(
      source.activeTrackId ||
      source.currentTrackId ||
      source.trackId ||
      payload && (payload.activeTrackId || payload.currentTrackId || payload.trackId) ||
      track.trackId
    );
    var stateUrl = firstUrl(
      source.stateUrl,
      source.links && source.links.state,
      "/shared-play/v1/sessions/" + encodeURIComponent(sessionId) + "/state"
    );
    var browserAudioUrl = firstPlayableUrl(
      source.browserAudioUrl,
      source.sanitizedAudioUrl,
      source.audioUrl,
      source.streamUrl,
      source.media && source.media.audioUrl,
      source.fallback && source.fallback.audioUrl,
      source.assets && source.assets.browserAudio && source.assets.browserAudio.url,
      source.assets && source.assets.audio && source.assets.audio.url
    );
    var packageUrl = firstUrl(
      source.packageUrl,
      source.spectralisPackageUrl,
      source.assetUrl,
      source.package && source.package.assetUrl,
      source.package && source.package.url,
      source.assets && source.assets.spectralisPackage && source.assets.spectralisPackage.url,
      source.assets && source.assets.package && source.assets.package.url,
      findUploadAssetUrl(source.uploads, "spectralis-package")
    );
    var queueUrl = firstUrl(
      source.queueUrl,
      source.links && source.links.queue,
      payload && payload.queueUrl,
      payload && payload.links && payload.links.queue,
      "/shared-play/v1/sessions/" + encodeURIComponent(sessionId) + "/queue"
    );
    var presenceUrl = firstUrl(
      source.presenceUrl,
      source.links && source.links.presence,
      payload && payload.presenceUrl,
      payload && payload.links && payload.links.presence,
      "/shared-play/v1/sessions/" + encodeURIComponent(sessionId) + "/presence"
    );
    var reactionsUrl = firstUrl(
      source.reactionsUrl,
      source.links && source.links.reactions,
      payload && payload.reactionsUrl,
      payload && payload.links && payload.links.reactions,
      "/shared-play/v1/sessions/" + encodeURIComponent(sessionId) + "/reactions"
    );

    return {
      expiresAtUtc: source.expiresAtUtc || payload && payload.expiresAtUtc || null,
      apiJoinUrl: firstUrl(source.joinUrl, payload && payload.joinUrl, "/shared-play/join/" + encodeURIComponent(sessionId)),
      joinUrl: createWebShareJoinUrl(sessionId),
      packageUrl: packageUrl,
      playback: findPlaybackPayload(source) || findPlaybackPayload(payload),
      sessionId: sessionId,
      stateUrl: stateUrl,
      queueUrl: queueUrl,
      presenceUrl: presenceUrl,
      reactionsUrl: reactionsUrl,
      trackId: trackId,
      track: normalizeTrack(track),
      browserAudioUrl: browserAudioUrl
    };
  }

  function normalizeTrack(track) {
    return {
      album: stringOrEmpty(track.album),
      artist: stringOrEmpty(track.artist),
      displayName: stringOrEmpty(track.displayName || track.title || track.name) || "Shared Play Track",
      durationSeconds: finiteNumber(track.durationSeconds || track.duration || 0),
      formatName: stringOrEmpty(track.formatName || track.format),
      hasEmbeddedVisualizer: Boolean(track.hasEmbeddedVisualizer),
      hasLyrics: Boolean(track.hasLyrics),
      lyrics: normalizeLyrics(track.lyrics || track.lyricLines)
    };
  }

  function normalizeLyrics(lines) {
    if (!Array.isArray(lines)) {
      return [];
    }

    return lines.map(function (line) {
      var text = stringOrEmpty(line && (line.text || line.value || line.line)).trim();
      if (!text) {
        return null;
      }

      return {
        timeSeconds: finiteNumber(line.timeSeconds || line.startTime || line.time || 0),
        text: text
      };
    }).filter(Boolean).sort(function (left, right) {
      return left.timeSeconds - right.timeSeconds;
    }).slice(0, 400);
  }

  async function prepareAudio(session) {
    elements.audio.pause();
    elements.audio.removeAttribute("src");
    runtime.audioReady = false;

    if (runtime.audioObjectUrl) {
      URL.revokeObjectURL(runtime.audioObjectUrl);
      runtime.audioObjectUrl = null;
    }

    if (session.browserAudioUrl) {
      elements.audio.src = session.browserAudioUrl;
      elements.audio.load();
      setStatus("Browser audio stream ready.");
      return;
    }

    if (!session.packageUrl || !config.allowRichPackageFallback) {
      throw new Error("This session does not expose a browser-playable audio stream yet.");
    }

    var preloaded = runtime.preloadedPackages[session.packageUrl];
    if (preloaded && preloaded.objectUrl) {
      runtime.audioObjectUrl = preloaded.objectUrl;
      delete runtime.preloadedPackages[session.packageUrl];
      elements.audio.src = runtime.audioObjectUrl;
      elements.audio.load();
      setStatus("Preloaded audio ready.");
      return;
    }

    setStatus("Extracting audio from session package...");
    runtime.audioObjectUrl = await extractAudioObjectUrlFromPackage(session.packageUrl);
    elements.audio.src = runtime.audioObjectUrl;
    elements.audio.load();
  }

  async function refreshPlaybackState() {
    if (!runtime.session) {
      return;
    }

    var payload = await fetchJson(runtime.session.stateUrl);
    var activeTrackId = extractTrackId(payload);
    if (activeTrackId && !runtime.activeTrackId) {
      runtime.activeTrackId = activeTrackId;
      runtime.session.trackId = activeTrackId;
    }
    if (activeTrackId && runtime.activeTrackId && activeTrackId !== runtime.activeTrackId) {
      await switchActiveTrack(activeTrackId, payload);
    }

    var playback = findPlaybackPayload(payload);
    if (!playback) {
      runtime.hostStateAvailable = false;
      runtime.lastPlayback = null;
      updateSeekSliderInteractivity();
      setSessionState("Ready", "live");
      setStatus(runtime.userActivated
        ? "Playing locally — waiting for host..."
        : "Ready — waiting for host to start...");
      return;
    }

    runtime.hostStateAvailable = true;
    runtime.lastPlayback = normalizePlayback(playback, activeTrackId);
    updateSeekSliderInteractivity();
    applyPlaybackSync(false);
  }

  async function switchActiveTrack(trackId, statePayload) {
    if (runtime.switchingTrack || !runtime.session) {
      return;
    }

    runtime.switchingTrack = true;
    setSessionState("Loading");
    setStatus("Loading next track...");

    try {
      var nextSession = normalizeSession(runtime.sessionId, statePayload || {});
      if (!nextSession.packageUrl && !nextSession.browserAudioUrl) {
        nextSession = normalizeSession(runtime.sessionId, await fetchSession(runtime.sessionId));
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
      setSessionState("Ready", "live");
      setStatus("Next track ready.");
      updateOpenAppButton();
      updateJoinButton();
    } finally {
      runtime.switchingTrack = false;
    }
  }

  async function refreshQueueState() {
    if (!runtime.session || !runtime.session.queueUrl) {
      renderQueue(null);
      return;
    }

    var queue = await fetchJson(runtime.session.queueUrl);
    runtime.queue = normalizeQueue(queue);
    renderQueue(runtime.queue);
    preloadUpcomingQueueTracks();
  }

  async function addQueueLink() {
    if (!runtime.session || !runtime.session.queueUrl) {
      throw new Error("Load a session before adding to the queue.");
    }

    var value = stringOrEmpty(elements.queueUrlInput.value).trim();
    if (!value) {
      return;
    }

    var item = buildQueueLinkItem(value);
    var response = await fetch(runtime.session.queueUrl.replace(/\/queue(?:\?.*)?$/, "/queue/items"), {
      method: "POST",
      cache: "no-store",
      credentials: "omit",
      headers: {
        "accept": "application/json",
        "content-type": "application/json"
      },
      body: JSON.stringify({ item: item })
    });

    if (!response.ok) {
      throw new Error("Queue update failed with HTTP " + response.status + ".");
    }

    elements.queueUrlInput.value = "";
    runtime.queue = normalizeQueue(await response.json());
    renderQueue(runtime.queue);
    setStatus("Request sent to host queue.");
  }

  function normalizeQueue(payload) {
    var items = payload && Array.isArray(payload.items) ? payload.items : [];
    return {
      currentIndex: finiteNumber(payload && payload.currentIndex || -1),
      items: items.map(normalizeQueueItem)
    };
  }

  function normalizeQueueItem(item) {
    return {
      id: stringOrEmpty(item && item.id),
      sourceKind: stringOrEmpty(item && item.sourceKind) || "track",
      title: stringOrEmpty(item && (item.title || item.displayName || item.name)) || stringOrEmpty(item && item.url) || "Queued track",
      artist: stringOrEmpty(item && item.artist),
      url: stringOrEmpty(item && item.url),
      trackId: stringOrEmpty(item && item.trackId),
      packageUrl: firstUrl(item && item.packageUrl)
    };
  }

  function renderQueue(queue) {
    var items = queue && queue.items || [];
    elements.queueCount.textContent = items.length === 1 ? "1 track" : items.length + " tracks";
    elements.queueList.replaceChildren();

    if (items.length === 0) {
      var empty = document.createElement("li");
      var title = document.createElement("span");
      title.className = "queue-title";
      title.textContent = "No queued links yet";
      empty.appendChild(title);
      elements.queueList.appendChild(empty);
      return;
    }

    items.slice(0, 30).forEach(function (item, index) {
      var row = document.createElement("li");
      var title = document.createElement("span");
      var kind = document.createElement("span");
      title.className = "queue-title";
      kind.className = "queue-kind";
      title.textContent = (index === queue.currentIndex ? "Now: " : "") + item.title;
      if (item.artist) {
        title.textContent += " / " + item.artist;
      }
      kind.textContent = item.sourceKind;
      row.appendChild(title);
      row.appendChild(kind);
      elements.queueList.appendChild(row);
    });
  }

  function buildQueueLinkItem(value) {
    return {
      id: "web-" + String(Date.now()) + "-" + Math.random().toString(16).slice(2),
      sourceKind: detectQueueSourceKind(value),
      title: buildQueueTitle(value),
      url: value,
      addedBy: "remote",
      addedAtUtc: new Date().toISOString()
    };
  }

  function detectQueueSourceKind(value) {
    var lower = value.toLowerCase();
    if (lower.startsWith("spotify:") || lower.indexOf("open.spotify.com") >= 0 || lower.indexOf("spotify.link") >= 0) return "spotify";
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
    } catch (error) {
      return value;
    }
  }

  function preloadUpcomingQueueTracks() {
    if (!runtime.queue || !runtime.queue.items.length) {
      return;
    }

    var profile = getSyncProfile();
    var keepUrls = {};
    if (runtime.session && runtime.session.packageUrl) {
      keepUrls[runtime.session.packageUrl] = true;
    }

    for (var offset = 1; offset <= profile.preloadLookahead; offset += 1) {
      var nextIndex = Math.max(0, runtime.queue.currentIndex + offset);
      if (nextIndex >= runtime.queue.items.length) {
        break;
      }

      var item = runtime.queue.items[nextIndex];
      if (!item || !item.trackId || !item.packageUrl) {
        continue;
      }

      keepUrls[item.packageUrl] = true;
      preloadPackage(item);
    }

    trimPreloadedPackages(keepUrls);
  }

  function preloadPackage(item) {
    if (runtime.preloadedPackages[item.packageUrl]) {
      return;
    }

    var generation = runtime.preloadGeneration;
    runtime.preloadedPackages[item.packageUrl] = { loading: true, objectUrl: "", trackId: item.trackId };
    extractAudioObjectUrlFromPackage(item.packageUrl).then(function (objectUrl) {
      if (generation !== runtime.preloadGeneration) {
        URL.revokeObjectURL(objectUrl);
        return;
      }

      runtime.preloadedPackages[item.packageUrl] = {
        loading: false,
        objectUrl: objectUrl,
        trackId: item.trackId
      };
    }).catch(function () {
      delete runtime.preloadedPackages[item.packageUrl];
      reportNetworkTrouble(1, "Next track preload failed.");
    });
  }

  function trimPreloadedPackages(keepUrls) {
    var urls = Object.keys(runtime.preloadedPackages);
    var removable = urls.filter(function (url) {
      return !keepUrls[url];
    });
    var overflow = Math.max(0, urls.length - config.maxPreloadedPackages);
    removable.slice(0, overflow || removable.length).forEach(function (url) {
      revokePreloadedPackage(url);
    });
  }

  function clearPreloadedPackages() {
    Object.keys(runtime.preloadedPackages).forEach(revokePreloadedPackage);
    runtime.preloadGeneration += 1;
  }

  function revokePreloadedPackage(url) {
    var cached = runtime.preloadedPackages[url];
    if (cached && cached.objectUrl) {
      URL.revokeObjectURL(cached.objectUrl);
    }

    delete runtime.preloadedPackages[url];
  }

  function normalizePlayback(playback, fallbackTrackId) {
    return {
      durationSeconds: finiteNumber(playback.durationSeconds || runtime.session.track.durationSeconds || elements.audio.duration || 0),
      hostClockUtc: playback.hostClockUtc || playback.updatedAtUtc || playback.createdAtUtc || new Date().toISOString(),
      isPlaying: Boolean(playback.isPlaying),
      positionSeconds: finiteNumber(playback.positionSeconds || playback.position || 0),
      reason: stringOrEmpty(playback.reason),
      trackId: stringOrEmpty(fallbackTrackId || playback.activeTrackId || playback.currentTrackId || playback.trackId)
    };
  }

  function findPlaybackPayload(payload) {
    if (!payload || typeof payload !== "object") {
      return null;
    }

    if (hasPlaybackFields(payload.playback)) {
      return payload.playback;
    }

    if (payload.state && typeof payload.state === "object") {
      if (hasPlaybackFields(payload.state.playback)) {
        return payload.state.playback;
      }

      if (hasPlaybackFields(payload.state)) {
        return payload.state;
      }
    }

    if (payload.session && typeof payload.session === "object" && hasPlaybackFields(payload.session.playback)) {
      return payload.session.playback;
    }

    return hasPlaybackFields(payload) ? payload : null;
  }

  function extractTrackId(payload) {
    if (!payload || typeof payload !== "object") {
      return "";
    }

    return stringOrEmpty(
      payload.activeTrackId ||
      payload.currentTrackId ||
      payload.trackId ||
      payload.playback && (payload.playback.activeTrackId || payload.playback.currentTrackId || payload.playback.trackId) ||
      payload.state && (payload.state.activeTrackId || payload.state.currentTrackId || payload.state.trackId) ||
      payload.session && (payload.session.activeTrackId || payload.session.currentTrackId || payload.session.trackId)
    );
  }

  function hasPlaybackFields(value) {
    if (!value || typeof value !== "object") {
      return false;
    }

    return Object.prototype.hasOwnProperty.call(value, "isPlaying") ||
      Object.prototype.hasOwnProperty.call(value, "positionSeconds") ||
      Object.prototype.hasOwnProperty.call(value, "position") ||
      Object.prototype.hasOwnProperty.call(value, "hostClockUtc");
  }

  function startSessionLoops() {
    stopSessionLoops();
    runtime.sessionLoopsActive = true;
    var profile = getSyncProfile();

    runtime.pollTimer = window.setInterval(function () {
      refreshPlaybackState().catch(function (error) {
        setSessionState("Reconnecting");
        reportNetworkTrouble(1.5, error.message || "Shared Play state request failed.");
        setStatus(error.message || "Waiting for Shared Play state...");
      });
      refreshQueueState().catch(function () {});
    }, profile.pollIntervalMs);

    runtime.syncTimer = window.setInterval(function () {
      applyPlaybackSync(false);
    }, profile.syncIntervalMs);

    postPresence().catch(function () {});
    refreshReactions().catch(function () {});

    runtime.presenceTimer = window.setInterval(function () {
      postPresence().catch(function () {});
    }, runtime.networkMode === "optimized" ? config.presenceIntervalMs * 1.5 : config.presenceIntervalMs);

    runtime.reactionsTimer = window.setInterval(function () {
      refreshReactions().catch(function () {});
    }, runtime.networkMode === "optimized" ? config.reactionIntervalMs * 1.8 : config.reactionIntervalMs);
  }

  function stopSessionLoops() {
    runtime.sessionLoopsActive = false;

    if (runtime.pollTimer) {
      window.clearInterval(runtime.pollTimer);
      runtime.pollTimer = 0;
    }

    if (runtime.syncTimer) {
      window.clearInterval(runtime.syncTimer);
      runtime.syncTimer = 0;
    }

    if (runtime.presenceTimer) {
      window.clearInterval(runtime.presenceTimer);
      runtime.presenceTimer = 0;
    }

    if (runtime.reactionsTimer) {
      window.clearInterval(runtime.reactionsTimer);
      runtime.reactionsTimer = 0;
    }
  }

  function getSyncProfile() {
    return runtime.networkMode === "optimized" ? config.optimizedSync : config.qualitySync;
  }

  function reportNetworkSuccess(elapsedMs, label) {
    if (elapsedMs >= config.networkVerySlowFetchMs) {
      reportNetworkTrouble(2, label || "Network request was very slow.");
      return;
    }

    if (elapsedMs >= config.networkSlowFetchMs) {
      reportNetworkTrouble(1, label || "Network request was slow.");
      return;
    }

    runtime.networkScore = Math.max(0, runtime.networkScore - 0.35);
    if (runtime.networkMode === "optimized") {
      runtime.networkStableCount += 1;
      if (runtime.networkStableCount >= config.networkRecoverSuccesses && runtime.networkScore <= 0.5) {
        setNetworkMode("quality", "Network recovered; using pure sync.");
      }
    }
  }

  function reportNetworkTrouble(weight, reason) {
    runtime.networkScore = Math.min(8, runtime.networkScore + Math.max(0.5, weight || 1));
    runtime.networkStableCount = 0;
    if (runtime.networkScore >= 3 && runtime.networkMode !== "optimized") {
      setNetworkMode("optimized", reason || "Network is rough; using smoother sync.");
    } else if (runtime.networkMode === "optimized") {
      showNetworkStatus(reason || "Network is rough; using smoother sync.");
    }
  }

  function setNetworkMode(mode, reason) {
    if (runtime.networkMode === mode) {
      return;
    }

    runtime.networkMode = mode;
    runtime.networkStableCount = 0;
    if (mode === "quality") {
      runtime.networkScore = 0;
    }

    updateNetworkModeUi();
    showNetworkStatus(reason);

    if (runtime.sessionLoopsActive && runtime.session) {
      startSessionLoops();
      preloadUpcomingQueueTracks();
    }
  }

  function updateNetworkModeUi() {
    var optimized = runtime.networkMode === "optimized";
    document.body.classList.toggle("is-network-optimized", optimized);
    if (elements.networkMode) {
      elements.networkMode.textContent = optimized ? "Optimized" : "Pure sync";
      elements.networkMode.title = optimized
        ? "Bad-network fallback is active: smoother sync, wider drift tolerance, and deeper queue prefetch."
        : "High-quality pure sync is active.";
    }
  }

  function showNetworkStatus(message) {
    if (!message) {
      return;
    }

    var now = Date.now();
    if (now - runtime.lastNetworkStatusAt < 4500) {
      return;
    }

    runtime.lastNetworkStatusAt = now;
    setStatus(message);
  }

  async function postPresence() {
    if (!runtime.session || !runtime.session.presenceUrl) {
      updateListenerCount(0);
      return;
    }

    var payload = await postJson(runtime.session.presenceUrl, {
      clientId: runtime.clientId,
      displayName: "Listener"
    });
    renderPresence(payload);
  }

  function renderPresence(payload) {
    var participants = payload && Array.isArray(payload.participants) ? payload.participants : [];
    var count = finiteNumber(payload && payload.listenerCount || participants.length);
    updateListenerCount(count);
  }

  function updateListenerCount(count) {
    elements.listenerCount.textContent = count === 1 ? "1 listening" : count + " listening";
  }

  async function sendReaction(type, label) {
    if (!runtime.session || !runtime.session.reactionsUrl) {
      throw new Error("Load a session before sending a reaction.");
    }

    var payload = await postJson(runtime.session.reactionsUrl, {
      clientId: runtime.clientId,
      type: type || "spark",
      label: label || ""
    });
    renderReactions(payload, true);
  }

  async function refreshReactions() {
    if (!runtime.session || !runtime.session.reactionsUrl) {
      return;
    }

    renderReactions(await fetchJson(runtime.session.reactionsUrl), false);
  }

  function renderReactions(payload, showNew) {
    var items = payload && Array.isArray(payload.items) ? payload.items : [];
    if (!runtime.reactionsPrimed && !showNew) {
      items.forEach(function (item) {
        var id = stringOrEmpty(item && item.id);
        if (id) {
          runtime.seenReactionIds.add(id);
        }
      });
      runtime.reactionsPrimed = true;
      return;
    }

    runtime.reactionsPrimed = true;
    items.forEach(function (item) {
      var id = stringOrEmpty(item && item.id);
      if (!id || runtime.seenReactionIds.has(id)) {
        return;
      }

      runtime.seenReactionIds.add(id);
      showReactionBurst(
        stringOrEmpty(item && item.label) || "Spark",
        stringOrEmpty(item && item.type)
      );
    });
  }

  function showReactionBurst(label, type) {
    pulseReactionAmbience(type);

    if (!elements.reactionLayer) {
      return;
    }

    var burst = document.createElement("div");
    burst.className = "reaction-burst reaction-" + cleanClassToken(type);
    burst.textContent = label;
    burst.style.left = 12 + Math.random() * 76 + "%";
    elements.reactionLayer.appendChild(burst);
    window.setTimeout(function () {
      burst.remove();
    }, 1700);
  }

  function pulseReactionAmbience(type) {
    var key = normalizeReactionAmbienceKey(type);
    if (!key) {
      return;
    }

    runtime.reactionHeat[key] = Math.min(1, runtime.reactionHeat[key] + reactionAmbienceStep(key));
    applyReactionAmbience();
    ensureReactionAmbienceLoop();
  }

  function normalizeReactionAmbienceKey(type) {
    switch (cleanClassToken(type)) {
      case "fire":
        return "fire";
      case "love":
        return "love";
      case "boost":
      case "wow":
      case "plus":
      case "spark":
        return "boost";
      default:
        return "";
    }
  }

  function reactionAmbienceStep(key) {
    if (key === "fire") {
      return 0.22;
    }

    if (key === "love") {
      return 0.16;
    }

    return 0.12;
  }

  function ensureReactionAmbienceLoop() {
    if (runtime.reactionAmbienceTimer) {
      return;
    }

    runtime.reactionAmbienceTimer = window.setInterval(function () {
      runtime.reactionHeat.fire = Math.max(0, runtime.reactionHeat.fire - 0.045);
      runtime.reactionHeat.love = Math.max(0, runtime.reactionHeat.love - 0.04);
      runtime.reactionHeat.boost = Math.max(0, runtime.reactionHeat.boost - 0.038);
      applyReactionAmbience();

      if (runtime.reactionHeat.fire <= 0 &&
          runtime.reactionHeat.love <= 0 &&
          runtime.reactionHeat.boost <= 0) {
        window.clearInterval(runtime.reactionAmbienceTimer);
        runtime.reactionAmbienceTimer = 0;
      }
    }, 100);
  }

  function applyReactionAmbience() {
    document.body.style.setProperty("--fire-heat", runtime.reactionHeat.fire.toFixed(3));
    document.body.style.setProperty("--love-heat", runtime.reactionHeat.love.toFixed(3));
    document.body.style.setProperty("--boost-heat", runtime.reactionHeat.boost.toFixed(3));
  }

  function resetReactionAmbience() {
    runtime.reactionHeat.fire = 0;
    runtime.reactionHeat.love = 0;
    runtime.reactionHeat.boost = 0;
    if (runtime.reactionAmbienceTimer) {
      window.clearInterval(runtime.reactionAmbienceTimer);
      runtime.reactionAmbienceTimer = 0;
    }

    applyReactionAmbience();
  }

  async function activatePlayback() {
    if (!runtime.audioReady && runtime.session) {
      setStatus("Audio is still loading.");
      return;
    }

    if (!runtime.session) {
      loadSession(elements.sessionInput.value, { fromChannel: Boolean(runtime.channelId) });
      return;
    }

    runtime.userActivated = true;
    await ensureAudioGraph();
    if (!runtime.lastPlayback) {
      await playLocalUntilHostState();
      updateJoinButton();
      return;
    }

    applyPlaybackSync(true);
    updateJoinButton();
  }

  async function playLocalUntilHostState() {
    try {
      await elements.audio.play();
      elements.audio.playbackRate = 1;
      setSessionState("Playing", "live");
      setStatus("Playing locally — waiting for host...");
    } catch (error) {
      setStatus("Click Join Playback to start audio.");
    }
    updateSeekSliderInteractivity();
  }

  function applyPlaybackSync(force) {
    if (runtime.switchingTrack || !runtime.lastPlayback || !runtime.audioReady) {
      return;
    }

    var profile = getSyncProfile();
    var target = getHostPosition(runtime.lastPlayback);
    if (runtime.lastPlayback.isPlaying && profile.targetLagSeconds > 0) {
      target = Math.max(0, target - profile.targetLagSeconds);
    }

    var duration = runtime.lastPlayback.durationSeconds || elements.audio.duration || 0;
    if (duration > 0) {
      target = Math.min(target, duration);
    }

    var drift = target - elements.audio.currentTime;
    if (force || Math.abs(drift) >= profile.hardSeekSeconds) {
      elements.audio.currentTime = Math.max(0, target);
      drift = 0;
    }

    if (runtime.lastPlayback.isPlaying) {
      if (runtime.userActivated && elements.audio.paused) {
        elements.audio.play().catch(function () {
          setStatus("Click Join Playback to start audio.");
        });
      }

      if (!elements.audio.paused) {
        elements.audio.playbackRate = getCorrectionRate(drift);
        setSessionState("Synced", "live");
      }
    } else {
      if (!elements.audio.paused) {
        elements.audio.pause();
      }

      elements.audio.playbackRate = 1;
      if (Math.abs(drift) > 0.35) {
        elements.audio.currentTime = Math.max(0, target);
      }

      setSessionState("Paused");
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
    var absolute = Math.abs(drift);
    if (absolute < profile.softSyncSeconds) {
      return 1;
    }

    var amount = Math.min(profile.maxCorrectionRate, absolute / profile.correctionDivisor);
    return drift > 0 ? 1 + amount : 1 - amount;
  }

  async function ensureAudioGraph() {
    if (runtime.audioContext || !window.AudioContext && !window.webkitAudioContext) {
      return;
    }

    try {
      var AudioContextCtor = window.AudioContext || window.webkitAudioContext;
      runtime.audioContext = new AudioContextCtor();
      runtime.analyser = runtime.audioContext.createAnalyser();
      runtime.analyser.fftSize = 128;
      runtime.sourceNode = runtime.audioContext.createMediaElementSource(elements.audio);
      runtime.sourceNode.connect(runtime.analyser);
      runtime.analyser.connect(runtime.audioContext.destination);
      await runtime.audioContext.resume();
    } catch (error) {
      runtime.analyser = null;
    }
  }

  async function extractAudioObjectUrlFromPackage(packageUrl) {
    var startedAt = performance.now();
    try {
      var response = await fetch(packageUrl, {
        cache: "no-store",
        credentials: "omit"
      });

      if (!response.ok) {
        var httpError = new Error("Rich package download failed with HTTP " + response.status + ".");
        httpError.handledHttp = true;
        if (response.status === 408 || response.status === 429 || response.status >= 500) {
          reportNetworkTrouble(response.status >= 500 ? 1.5 : 1, httpError.message);
          httpError.networkReported = true;
        }

        throw httpError;
      }

      var contentLength = Number(response.headers.get("content-length"));
      if (Number.isFinite(contentLength) && contentLength > config.maxPackageBytes) {
        throw new Error("Rich package is larger than the browser playback limit.");
      }

      var buffer = await response.arrayBuffer();
      if (buffer.byteLength > config.maxPackageBytes) {
        throw new Error("Rich package is larger than the browser playback limit.");
      }

      var elapsedMs = performance.now() - startedAt;
      reportPackageDownloadHealth(buffer.byteLength, elapsedMs);

      var entry = findStoredZipEntry(buffer, /^audio\/track\.[a-z0-9]+$/i);
      if (!entry) {
        throw new Error("Rich package does not contain a playable audio entry.");
      }

      var audioBuffer = buffer.slice(entry.dataStart, entry.dataStart + entry.compressedSize);
      var blob = new Blob([audioBuffer], { type: mimeFromFileName(entry.fileName) });
      return URL.createObjectURL(blob);
    } catch (error) {
      if (error && !error.networkReported && !error.handledHttp) {
        reportNetworkTrouble(1.5, error.message || "Rich package download failed.");
      }

      throw error;
    }
  }

  function reportPackageDownloadHealth(byteLength, elapsedMs) {
    var bytesPerSecond = elapsedMs > 0 ? byteLength / (elapsedMs / 1000) : byteLength;
    if (elapsedMs >= 9000 || bytesPerSecond < 180000) {
      reportNetworkTrouble(2, "Large audio package is downloading slowly; using smoother sync.");
      return;
    }

    reportNetworkSuccess(elapsedMs, "Audio package download was slow.");
  }

  function findStoredZipEntry(buffer, namePattern) {
    var view = new DataView(buffer);
    var bytes = new Uint8Array(buffer);
    var decoder = new TextDecoder("utf-8");
    var eocdOffset = findEndOfCentralDirectory(view);
    if (eocdOffset < 0) {
      throw new Error("Rich package is not a valid ZIP file.");
    }

    var totalEntries = view.getUint16(eocdOffset + 10, true);
    var centralDirectoryOffset = view.getUint32(eocdOffset + 16, true);
    var offset = centralDirectoryOffset;

    for (var index = 0; index < totalEntries; index += 1) {
      if (view.getUint32(offset, true) !== 0x02014b50) {
        break;
      }

      var method = view.getUint16(offset + 10, true);
      var compressedSize = view.getUint32(offset + 20, true);
      var fileNameLength = view.getUint16(offset + 28, true);
      var extraLength = view.getUint16(offset + 30, true);
      var commentLength = view.getUint16(offset + 32, true);
      var localHeaderOffset = view.getUint32(offset + 42, true);
      var fileName = decoder.decode(bytes.subarray(offset + 46, offset + 46 + fileNameLength));

      if (namePattern.test(fileName)) {
        if (method !== 0) {
          throw new Error("Browser playback needs the package audio entry stored without ZIP compression.");
        }

        if (view.getUint32(localHeaderOffset, true) !== 0x04034b50) {
          throw new Error("Rich package audio entry has an invalid local header.");
        }

        var localNameLength = view.getUint16(localHeaderOffset + 26, true);
        var localExtraLength = view.getUint16(localHeaderOffset + 28, true);
        var dataStart = localHeaderOffset + 30 + localNameLength + localExtraLength;
        return {
          compressedSize: compressedSize,
          dataStart: dataStart,
          fileName: fileName
        };
      }

      offset += 46 + fileNameLength + extraLength + commentLength;
    }

    return null;
  }

  function findEndOfCentralDirectory(view) {
    var minOffset = Math.max(0, view.byteLength - 65557);
    for (var offset = view.byteLength - 22; offset >= minOffset; offset -= 1) {
      if (view.getUint32(offset, true) === 0x06054b50) {
        return offset;
      }
    }

    return -1;
  }

  function drawMeter() {
    var canvas = elements.canvas;
    var context = canvas.getContext("2d");
    var rect = canvas.getBoundingClientRect();
    var scale = window.devicePixelRatio || 1;
    var width = Math.max(1, Math.floor(rect.width * scale));
    var height = Math.max(1, Math.floor(rect.height * scale));
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width;
      canvas.height = height;
    }

    context.clearRect(0, 0, width, height);

    var bars = getMeterBars();
    var gap = Math.max(3 * scale, width / 180);
    var barWidth = Math.max(5 * scale, (width - gap * (bars.length - 1)) / bars.length);
    var time = performance.now() / 700;

    for (var index = 0; index < bars.length; index += 1) {
      var synthetic = 0.32 + Math.sin(time + index * 0.54) * 0.18 + Math.sin(time * 0.48 + index) * 0.08;
      var level = runtime.analyser ? bars[index] : synthetic;
      if (!runtime.lastPlayback || !runtime.lastPlayback.isPlaying) {
        level *= 0.35;
      }

      var barHeight = Math.max(4 * scale, level * height * 0.82);
      var x = index * (barWidth + gap);
      var y = (height - barHeight) / 2;
      var alpha = runtime.analyser ? 0.95 : 0.38;
      context.fillStyle = "rgba(216, 161, 58, " + alpha + ")";
      roundRect(context, x, y, barWidth, barHeight, 5 * scale);
      context.fill();
    }

    window.requestAnimationFrame(drawMeter);
  }

  function getMeterBars() {
    var count = 36;
    var bars = new Array(count);
    if (!runtime.analyser) {
      for (var idleIndex = 0; idleIndex < count; idleIndex += 1) {
        bars[idleIndex] = 0.3;
      }

      return bars;
    }

    var data = new Uint8Array(runtime.analyser.frequencyBinCount);
    runtime.analyser.getByteFrequencyData(data);
    for (var index = 0; index < count; index += 1) {
      var sourceIndex = Math.floor(index / count * data.length);
      bars[index] = Math.max(0.05, data[sourceIndex] / 255);
    }

    return bars;
  }

  function roundRect(context, x, y, width, height, radius) {
    var safeRadius = Math.min(radius, width / 2, height / 2);
    context.beginPath();
    context.moveTo(x + safeRadius, y);
    context.lineTo(x + width - safeRadius, y);
    context.quadraticCurveTo(x + width, y, x + width, y + safeRadius);
    context.lineTo(x + width, y + height - safeRadius);
    context.quadraticCurveTo(x + width, y + height, x + width - safeRadius, y + height);
    context.lineTo(x + safeRadius, y + height);
    context.quadraticCurveTo(x, y + height, x, y + height - safeRadius);
    context.lineTo(x, y + safeRadius);
    context.quadraticCurveTo(x, y, x + safeRadius, y);
    context.closePath();
  }

  function renderTrack(track) {
    elements.trackTitle.textContent = track.displayName;
    elements.trackArtist.textContent = [track.artist, track.album].filter(Boolean).join(" / ") || "Shared Play session";
    runtime.lyrics = track.lyrics || [];
    runtime.lyricIndex = -1;

    var badges = [];
    if (track.formatName) {
      badges.push(track.formatName);
    }

    if (track.hasLyrics || runtime.lyrics.length > 0) {
      badges.push("Lyrics");
    }

    if (track.hasEmbeddedVisualizer) {
      badges.push("Embedded visualizer");
    }

    elements.trackKicker.textContent = badges.length ? badges.join(" / ") : "Private listening room";
    renderLyricsForPosition(elements.audio.currentTime || 0);
  }

  function updateTimeline() {
    var duration = finiteNumber(runtime.lastPlayback && runtime.lastPlayback.durationSeconds || elements.audio.duration || 0);
    var position = finiteNumber(elements.audio.currentTime || 0);
    elements.currentTime.textContent = formatTime(position);
    elements.durationTime.textContent = formatTime(duration);
    elements.seekSlider.value = duration > 0 ? String(Math.round(position / duration * 1000)) : "0";
    renderLyricsForPosition(position);
  }

  function renderLyricsForPosition(position) {
    if (!elements.lyricCurrent || !elements.lyricNext) {
      return;
    }

    if (!runtime.lyrics.length) {
      runtime.lyricIndex = -1;
      elements.lyricsPanel.classList.add("is-empty");
      elements.lyricCurrent.textContent = "No synced lyrics in this room";
      elements.lyricNext.textContent = "";
      return;
    }

    elements.lyricsPanel.classList.remove("is-empty");
    var index = findLyricIndex(position);
    runtime.lyricIndex = index;

    if (index < 0) {
      elements.lyricCurrent.textContent = "";
      elements.lyricNext.textContent = runtime.lyrics[0].text;
      return;
    }

    elements.lyricCurrent.textContent = runtime.lyrics[index].text;
    elements.lyricNext.textContent = index + 1 < runtime.lyrics.length ? runtime.lyrics[index + 1].text : "";
  }

  function findLyricIndex(position) {
    var low = 0;
    var high = runtime.lyrics.length - 1;
    var found = -1;
    var target = finiteNumber(position) + 0.05;

    while (low <= high) {
      var mid = Math.floor((low + high) / 2);
      if (runtime.lyrics[mid].timeSeconds <= target) {
        found = mid;
        low = mid + 1;
      } else {
        high = mid - 1;
      }
    }

    return found;
  }

  function updateSeekSliderInteractivity() {
    elements.seekSlider.disabled = runtime.hostStateAvailable && runtime.lastPlayback !== null;
  }

  function updateJoinButton() {
    updateOpenAppButton();
    updateReactionButtons();
    elements.joinButton.disabled = !runtime.session && !elements.sessionInput.value.trim();

    if (!runtime.session) {
      elements.joinButton.textContent = "Load Session";
      return;
    }

    if (!runtime.audioReady) {
      elements.joinButton.textContent = "Loading Audio";
      elements.joinButton.disabled = true;
      return;
    }

    if (!runtime.userActivated) {
      elements.joinButton.textContent = "Join Playback";
      elements.joinButton.disabled = false;
      return;
    }

    if (!runtime.hostStateAvailable) {
      elements.joinButton.textContent = elements.audio.paused ? "Play Audio" : "Playing";
      elements.joinButton.disabled = false;
      return;
    }

    elements.joinButton.textContent = elements.audio.paused ? "Resume Sync" : "Syncing";
    elements.joinButton.disabled = false;
  }

  function updateOpenAppButton() {
    elements.openAppButton.disabled = !getActiveSessionId();
  }

  function updateReactionButtons() {
    var enabled = Boolean(runtime.session && runtime.session.reactionsUrl);
    elements.reactionButtons.forEach(function (button) {
      button.disabled = !enabled;
    });
  }

  function resetAudio() {
    elements.audio.pause();
    elements.audio.removeAttribute("src");
    elements.audio.load();
    elements.albumPlate.classList.remove("is-playing");
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

    updateSeekSliderInteractivity();
    updateTimeline();
    renderLyricsForPosition(0);
  }

  function copyJoinLink() {
    var joinUrl = runtime.channelId
      ? createWebShareChannelUrl(runtime.channelId)
      : runtime.session && runtime.session.joinUrl || "";
    if (!joinUrl) {
      return;
    }

    navigator.clipboard.writeText(joinUrl).then(function () {
      setStatus(runtime.channelId ? "Live Channel link copied." : "Shared Play link copied.");
    }).catch(function () {
      setStatus(joinUrl);
    });
  }

  function openInSpectralis() {
    var sessionId = getActiveSessionId();
    if (!sessionId) {
      return;
    }

    setStatus("Opening Spectralis...");
    window.location.href = createSpectralisJoinUrl(sessionId);
  }

  function getActiveSessionId() {
    return runtime.session && runtime.session.sessionId || extractSessionId(elements.sessionInput.value);
  }

  async function fetchJson(url) {
    var startedAt = performance.now();
    try {
      var response = await fetch(url, {
        cache: "no-store",
        credentials: "omit",
        headers: {
          "accept": "application/json"
        }
      });

      var elapsedMs = performance.now() - startedAt;
      if (!response.ok) {
        var httpError = new Error("CDN request failed with HTTP " + response.status + ".");
        httpError.handledHttp = true;
        if (response.status === 408 || response.status === 429 || response.status >= 500) {
          reportNetworkTrouble(response.status >= 500 ? 1.5 : 1, httpError.message);
          httpError.networkReported = true;
        }

        throw httpError;
      }

      reportNetworkSuccess(elapsedMs, "CDN request was slow.");
      var text = await response.text();
      try {
        return JSON.parse(text);
      } catch (error) {
        var parseError = new Error("CDN response was not JSON.");
        parseError.handledParse = true;
        throw parseError;
      }
    } catch (error) {
      if (error && !error.networkReported && !error.handledHttp && !error.handledParse && error.name !== "SyntaxError") {
        reportNetworkTrouble(1.5, error.message || "CDN request failed.");
      }

      throw error;
    }
  }

  async function postJson(url, payload) {
    var startedAt = performance.now();
    try {
      var response = await fetch(url, {
        method: "POST",
        cache: "no-store",
        credentials: "omit",
        headers: {
          "accept": "application/json",
          "content-type": "application/json"
        },
        body: JSON.stringify(payload || {})
      });

      var elapsedMs = performance.now() - startedAt;
      if (!response.ok) {
        var httpError = new Error("CDN request failed with HTTP " + response.status + ".");
        httpError.handledHttp = true;
        if (response.status === 408 || response.status === 429 || response.status >= 500) {
          reportNetworkTrouble(response.status >= 500 ? 1.5 : 1, httpError.message);
          httpError.networkReported = true;
        }

        throw httpError;
      }

      reportNetworkSuccess(elapsedMs, "CDN request was slow.");
      var text = await response.text();
      try {
        return JSON.parse(text);
      } catch (error) {
        var parseError = new Error("CDN response was not JSON.");
        parseError.handledParse = true;
        throw parseError;
      }
    } catch (error) {
      if (error && !error.networkReported && !error.handledHttp && !error.handledParse && error.name !== "SyntaxError") {
        reportNetworkTrouble(1.5, error.message || "CDN request failed.");
      }

      throw error;
    }
  }

  function getClientId() {
    var key = "spectralisSharedPlayClientId";
    try {
      var existing = window.localStorage.getItem(key);
      if (existing) {
        return existing;
      }

      var created = "web-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);
      window.localStorage.setItem(key, created);
      return created;
    } catch (error) {
      return "web-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);
    }
  }

  function renderChannelStats(stats) {
    if (!elements.channelStats) {
      return;
    }

    if (!stats || typeof stats !== "object") {
      elements.channelStats.hidden = true;
      return;
    }

    elements.channelStats.hidden = false;
    var sharedSeconds = finiteNumber(stats.totalSharedSeconds || 0);
    var listenerSeconds = finiteNumber(stats.totalListenerSeconds || 0);
    var peak = finiteNumber(stats.peakConcurrentListeners || 0);
    var topTrack = Array.isArray(stats.trackStats) && stats.trackStats.length > 0 ? stats.trackStats[0] : null;
    elements.channelSharedHours.textContent = formatHours(sharedSeconds) + " shared";
    elements.channelListenerHours.textContent = formatHours(listenerSeconds) + " listener-hours";
    elements.channelPeakListeners.textContent = peak + " peak";
    elements.channelTopTrack.textContent = topTrack
      ? "Top: " + (stringOrEmpty(topTrack.title) || "Shared Play Track")
      : "No top track yet";
  }

  function extractChannelId(input) {
    var value = stringOrEmpty(input).trim();
    if (!value) {
      return "";
    }

    try {
      var url = new URL(value, window.location.href);
      var queryId = url.searchParams.get("channel") || url.searchParams.get("channelId");
      if (queryId) {
        return cleanChannelId(queryId);
      }

      var parts = url.pathname.split("/").filter(Boolean);
      for (var index = 0; index < parts.length; index += 1) {
        if ((parts[index] === "channel" || parts[index] === "u" || parts[index] === "listen") &&
            parts[index + 1]) {
          return cleanChannelId(parts[index + 1]);
        }
      }
    } catch (error) {
      return cleanChannelId(value);
    }

    return "";
  }

  function cleanChannelId(value) {
    return decodeURIComponent(stringOrEmpty(value))
      .trim()
      .replace(/[^a-zA-Z0-9_-]/g, "");
  }

  function extractSessionId(input) {
    var value = stringOrEmpty(input).trim();
    if (!value) {
      return "";
    }

    var isUrlOrPath = looksLikeUrlOrPath(value);
    try {
      var url = new URL(value, window.location.href);
      var queryId = url.searchParams.get("session") || url.searchParams.get("sessionId") || url.searchParams.get("id");
      if (queryId) {
        return cleanSessionId(queryId);
      }

      var hash = url.hash.replace(/^#\/?/, "");
      if (hash) {
        return cleanSessionId(hash);
      }

      var parts = url.pathname.split("/").filter(Boolean);
      for (var index = 0; index < parts.length; index += 1) {
        if ((parts[index] === "join" || parts[index] === "sessions" || parts[index] === "web-share") &&
            isSessionCandidate(parts[index + 1])) {
          return cleanSessionId(parts[index + 1]);
        }
      }

      if (parts.length === 1 && value.indexOf("/") >= 0) {
        return cleanSessionId(parts[0]);
      }
    } catch (error) {
      return isUrlOrPath ? "" : cleanSessionId(value);
    }

    return isUrlOrPath ? "" : cleanSessionId(value);
  }

  function cleanSessionId(value) {
    return decodeURIComponent(stringOrEmpty(value))
      .trim()
      .replace(/^session=/i, "")
      .replace(/[^a-zA-Z0-9._:-]/g, "");
  }

  function isSessionCandidate(value) {
    var cleaned = cleanSessionId(value);
    return cleaned.length > 0 &&
      !/\.html?$/i.test(cleaned) &&
      cleaned !== "web-share" &&
      cleaned !== "shared-play" &&
      cleaned !== "join" &&
      cleaned !== "sessions";
  }

  function looksLikeUrlOrPath(value) {
    return /^[a-z][a-z0-9+.-]*:/i.test(value) ||
      value.startsWith("//") ||
      value.startsWith("/") ||
      value.indexOf("/") >= 0 ||
      value.indexOf("\\") >= 0;
  }

  function firstUrl() {
    for (var index = 0; index < arguments.length; index += 1) {
      var value = arguments[index];
      if (typeof value !== "string" || !value.trim()) {
        continue;
      }

      var resolved = resolveUrl(value);
      if (resolved) {
        return resolved;
      }
    }

    return "";
  }

  function firstPlayableUrl() {
    for (var index = 0; index < arguments.length; index += 1) {
      var url = firstUrl(arguments[index]);
      if (url && !/\.zip($|\?)/i.test(url)) {
        return url;
      }
    }

    return "";
  }

  function resolveUrl(value) {
    try {
      var url = new URL(value, config.cdnBaseUrl);
      if (url.protocol !== "https:") {
        return "";
      }

      return url.toString();
    } catch (error) {
      return "";
    }
  }

  function toCdnUrl(path) {
    return new URL(path, config.cdnBaseUrl).toString();
  }

  function createWebShareJoinUrl(sessionId) {
    try {
      var url = new URL("/spectralis/web-share/index.html", config.cdnBaseUrl);
      url.searchParams.set("session", sessionId);
      if (document.body.classList.contains("is-discord-activity")) {
        url.searchParams.set("source", "discord");
        url.searchParams.set("mode", "activity");
      }
      return url.toString();
    } catch (error) {
      return "";
    }
  }

  function createWebShareChannelUrl(channelId) {
    try {
      var url = new URL("/spectralis/web-share/index.html", config.cdnBaseUrl);
      url.searchParams.set("channel", channelId);
      return url.toString();
    } catch (error) {
      return "";
    }
  }

  function createSpectralisJoinUrl(sessionId) {
    var query = new URLSearchParams();
    query.set("session", sessionId);
    query.set("cdn", config.cdnBaseUrl);
    return "spectralis://shared-play/join?" + query.toString();
  }

  function normalizeBaseUrl(value) {
    try {
      var url = new URL(value);
      return url.protocol === "https:" ? url.origin : config.cdnBaseUrl;
    } catch (error) {
      return config.cdnBaseUrl;
    }
  }

  function findUploadAssetUrl(uploads, name) {
    if (!Array.isArray(uploads)) {
      return "";
    }

    for (var index = 0; index < uploads.length; index += 1) {
      var upload = uploads[index];
      if (upload && upload.name === name && upload.assetUrl) {
        return upload.assetUrl;
      }
    }

    return "";
  }

  function mimeFromFileName(fileName) {
    var extension = fileName.split(".").pop().toLowerCase();
    var map = {
      aac: "audio/aac",
      aif: "audio/aiff",
      aifc: "audio/aiff",
      aiff: "audio/aiff",
      flac: "audio/flac",
      m4a: "audio/mp4",
      mp3: "audio/mpeg",
      oga: "audio/ogg",
      ogg: "audio/ogg",
      opus: "audio/ogg",
      wav: "audio/wav",
      webm: "audio/webm",
      wma: "audio/x-ms-wma"
    };

    return map[extension] || "application/octet-stream";
  }

  function formatTime(seconds) {
    var safeSeconds = Math.max(0, Math.floor(finiteNumber(seconds)));
    var minutes = Math.floor(safeSeconds / 60);
    var remainder = safeSeconds % 60;
    return minutes + ":" + String(remainder).padStart(2, "0");
  }

  function formatHours(seconds) {
    var hours = finiteNumber(seconds) / 3600;
    if (hours < 10) {
      return hours.toFixed(1) + "h";
    }

    return Math.round(hours) + "h";
  }

  function finiteNumber(value) {
    var number = Number(value);
    return Number.isFinite(number) ? number : 0;
  }

  function stringOrEmpty(value) {
    return typeof value === "string" ? value : "";
  }

  function cleanClassToken(value) {
    return stringOrEmpty(value).toLowerCase().replace(/[^a-z0-9_-]/g, "") || "spark";
  }

  function setStatus(message) {
    elements.statusLine.textContent = message;
  }

  function setSpinning(active) {
    elements.statusSpinner.classList.toggle("is-spinning", active);
  }

  function setSessionState(label, mode) {
    elements.sessionState.textContent = label;
    elements.sessionState.classList.toggle("is-live", mode === "live");
    elements.sessionState.classList.toggle("is-error", mode === "error");
  }
})();
