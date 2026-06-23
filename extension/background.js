const AUDIO_EXTENSIONS = new Set([
  ".aac",
  ".adts",
  ".aif",
  ".aifc",
  ".aiff",
  ".asf",
  ".flac",
  ".m4a",
  ".m4b",
  ".m4p",
  ".mp3",
  ".mp4",
  ".oga",
  ".ogg",
  ".opus",
  ".wav",
  ".webm",
  ".wma",
  ".3gp"
]);

const SPOTIFY_URI_TYPES = new Set([
  "track",
  "album",
  "playlist",
  "artist",
  "episode"
]);

const AUTO_OPEN_KEY = "autoOpen";
const DEFAULT_INTENT = "play-now";
const AUTO_QUEUE_INTENT = "queue-next";
const recentAutoOpenTabs = new Map();

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    createContextMenu("play-link", "Play link in Spectralis", ["link"]);
    createContextMenu("queue-link", "Queue link in Spectralis", ["link"]);
    createContextMenu("play-selection", "Play selection in Spectralis", ["selection"]);
    createContextMenu("queue-selection", "Queue selection in Spectralis", ["selection"]);
    createContextMenu("play-page", "Play this page in Spectralis", ["page", "audio", "video"]);
    createContextMenu("queue-page", "Queue this page in Spectralis", ["page", "audio", "video"]);
  });
});

function createContextMenu(id, title, contexts) {
  chrome.contextMenus.create({ id, title, contexts });
}

chrome.contextMenus.onClicked.addListener((info, tab) => {
  const raw =
    info.linkUrl ||
    info.srcUrl ||
    info.selectionText ||
    info.pageUrl ||
    tab?.url ||
    "";

  const intent = String(info.menuItemId || "").startsWith("queue-")
    ? AUTO_QUEUE_INTENT
    : DEFAULT_INTENT;

  openCandidate(raw, tab?.id, { intent, pauseSource: true });
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "openCandidate") {
    const tabId = message.tabId ?? sender.tab?.id;
    openCandidate(message.raw || "", tabId, {
      intent: message.intent,
      pauseSource: message.pauseSource !== false,
      fromAutoOpen: Boolean(message.fromAutoOpen)
    }).then(sendResponse);
    return true;
  }

  if (message?.type === "getSupport") {
    sendResponse({ supported: Boolean(normalizeCandidate(message.raw || "")) });
    return false;
  }

  if (message?.type === "setAutoOpen") {
    chrome.storage.sync.set({ [AUTO_OPEN_KEY]: Boolean(message.enabled) }, () => {
      sendResponse({ ok: true, enabled: Boolean(message.enabled) });
    });
    return true;
  }

  if (message?.type === "getAutoOpen") {
    getAutoOpen().then(enabled => sendResponse({ enabled }));
    return true;
  }

  return false;
});

chrome.webNavigation.onBeforeNavigate.addListener(details => {
  if (details.frameId !== 0 || details.tabId < 0)
    return;

  getAutoOpen().then(enabled => {
    if (!enabled)
      return;

    const target = normalizeCandidate(details.url);
    if (!target)
      return;

    const last = recentAutoOpenTabs.get(details.tabId) || 0;
    if (Date.now() - last < 1200)
      return;

    recentAutoOpenTabs.set(details.tabId, Date.now());
    openTarget(target, details.tabId, {
      intent: AUTO_QUEUE_INTENT,
      fromAutoOpen: true,
      pauseSource: true
    });
  });
});

async function getAutoOpen() {
  return new Promise(resolve => {
    chrome.storage.sync.get({ [AUTO_OPEN_KEY]: false }, result => {
      resolve(Boolean(result[AUTO_OPEN_KEY]));
    });
  });
}

async function openCandidate(raw, tabId, options = {}) {
  const target = normalizeCandidate(raw);
  if (!target) {
    return {
      ok: false,
      message: "That does not look like a supported Spectralis audio link."
    };
  }

  try {
    if (options.fromAutoOpen && Number.isInteger(tabId))
      recentAutoOpenTabs.set(tabId, Date.now());

    await openTarget(target, tabId, options);
    return {
      ok: true,
      target,
      intent: normalizeIntent(options.intent)
    };
  } catch {
    return {
      ok: false,
      message: "Chrome could not launch Spectralis for that link."
    };
  }
}

async function openTarget(target, tabId, options = {}) {
  const intent = normalizeIntent(options.intent);
  const deepLink = buildDeepLink(target, intent);
  const createOptions = {
    url: deepLink,
    active: false
  };

  if (Number.isInteger(tabId) && tabId >= 0)
    createOptions.openerTabId = tabId;

  const launcherTab = await chrome.tabs.create(createOptions);
  scheduleLauncherCleanup(launcherTab?.id);

  if (options.pauseSource !== false)
    await pauseTabPlayback(tabId);
}

function buildDeepLink(target, intent) {
  const query = new URLSearchParams({
    source: "chromium-extension",
    url: target,
    intent
  });

  return `spectralis://open?${query.toString()}`;
}

function normalizeIntent(intent) {
  const value = String(intent || "").trim().toLowerCase();
  if (value === "queue" || value === "queue-next" || value === "next")
    return "queue-next";
  if (value === "queue-end" || value === "append" || value === "end")
    return "queue-end";
  if (value === "play" || value === "play-now" || value === "open")
    return "play-now";
  return DEFAULT_INTENT;
}

function scheduleLauncherCleanup(tabId) {
  if (!Number.isInteger(tabId) || tabId < 0)
    return;

  setTimeout(() => {
    chrome.tabs.get(tabId, tab => {
      if (chrome.runtime.lastError || !tab)
        return;

      const url = tab.pendingUrl || tab.url || "";
      if (url.startsWith("spectralis:") || url === "about:blank")
        chrome.tabs.remove(tabId, () => void chrome.runtime.lastError);
    });
  }, 8000);
}

async function pauseTabPlayback(tabId) {
  if (!Number.isInteger(tabId) || tabId < 0)
    return;

  try {
    await chrome.tabs.sendMessage(tabId, { type: "pauseMedia" });
  } catch {
    // Some browser pages cannot receive content-script messages.
  }
}

function normalizeCandidate(raw) {
  const candidates = extractCandidateStrings(raw);
  for (const candidate of candidates) {
    const normalized = normalizeSingleCandidate(candidate);
    if (normalized)
      return normalized;
  }

  return null;
}

function extractCandidateStrings(raw) {
  const value = String(raw || "").trim();
  if (!value)
    return [];

  const matches = value.match(/spotify:[A-Za-z]+:[A-Za-z0-9]+|https?:\/\/[^\s<>"']+/gi) || [];
  return [value, ...matches.map(cleanTrailingPunctuation)];
}

function cleanTrailingPunctuation(value) {
  return value.replace(/[),.;!?]+$/g, "");
}

function normalizeSingleCandidate(raw) {
  const value = cleanTrailingPunctuation(String(raw || "").trim());
  if (!value)
    return null;

  const spotifyUri = normalizeSpotifyUri(value);
  if (spotifyUri)
    return spotifyUri;

  let url;
  try {
    url = new URL(value);
  } catch {
    return null;
  }

  if (url.protocol !== "http:" && url.protocol !== "https:")
    return null;

  if (isSpotifyHost(url.hostname))
    return normalizeSpotifyUrl(url) || url.href;

  if (
    isYouTubeHost(url.hostname) ||
    isSoundCloudHost(url.hostname) ||
    isSunoHost(url.hostname) ||
    isDirectAudioUrl(url)
  ) {
    return url.href;
  }

  return null;
}

function normalizeSpotifyUri(value) {
  const match = /^spotify:([a-z]+):([A-Za-z0-9]+)$/i.exec(value);
  if (!match)
    return null;

  const type = match[1].toLowerCase();
  return SPOTIFY_URI_TYPES.has(type)
    ? `spotify:${type}:${match[2]}`
    : null;
}

function normalizeSpotifyUrl(url) {
  const parts = url.pathname
    .split("/")
    .filter(Boolean)
    .map(part => decodeURIComponent(part));

  for (let index = 0; index + 1 < parts.length; index++) {
    const type = parts[index].toLowerCase();
    if (SPOTIFY_URI_TYPES.has(type))
      return `spotify:${type}:${parts[index + 1]}`;
  }

  return null;
}

function isDirectAudioUrl(url) {
  const path = url.pathname.toLowerCase();
  const dot = path.lastIndexOf(".");
  return dot >= 0 && AUDIO_EXTENSIONS.has(path.slice(dot));
}

function isYouTubeHost(hostname) {
  const host = hostname.toLowerCase();
  return host === "youtu.be" ||
    host === "youtube.com" ||
    host.endsWith(".youtube.com") ||
    host === "youtube-nocookie.com" ||
    host.endsWith(".youtube-nocookie.com");
}

function isSoundCloudHost(hostname) {
  const host = hostname.toLowerCase();
  return host === "soundcloud.com" ||
    host.endsWith(".soundcloud.com") ||
    host === "snd.sc";
}

function isSunoHost(hostname) {
  const host = hostname.toLowerCase();
  return host === "suno.com" ||
    host.endsWith(".suno.com") ||
    host === "suno.ai" ||
    host.endsWith(".suno.ai");
}

function isSpotifyHost(hostname) {
  const host = hostname.toLowerCase();
  return host === "open.spotify.com" ||
    host.endsWith(".open.spotify.com") ||
    host === "spotify.link" ||
    host.endsWith(".spotify.link");
}
