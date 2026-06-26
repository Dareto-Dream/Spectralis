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

const AUTO_OPEN_KEY = "autoOpen";
let autoQueueEnabled = false;

refreshAutoQueueSetting();

chrome.storage?.onChanged?.addListener((changes, areaName) => {
  if (areaName === "sync" && changes[AUTO_OPEN_KEY])
    autoQueueEnabled = Boolean(changes[AUTO_OPEN_KEY].newValue);
});

document.addEventListener("click", event => {
  if (!autoQueueEnabled ||
      event.defaultPrevented ||
      event.button !== 0 ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      event.shiftKey) {
    return;
  }

  const anchor = event.target?.closest?.("a[href]");
  if (!anchor || !isProbablySupportedCandidate(anchor.href))
    return;

  event.preventDefault();
  event.stopPropagation();

  chrome.runtime.sendMessage({
    type: "openCandidate",
    raw: anchor.href,
    intent: "queue-next",
    pauseSource: true,
    fromAutoOpen: true
  });
}, true);

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === "getSelection") {
    sendResponse({
      selection: window.getSelection()?.toString() || ""
    });
    return false;
  }

  if (message?.type === "pauseMedia") {
    sendResponse(pauseMediaPlayback());
    return false;
  }

  return false;
});

function refreshAutoQueueSetting() {
  chrome.runtime.sendMessage({ type: "getAutoOpen" }, response => {
    autoQueueEnabled = Boolean(response?.enabled);
  });
}

function pauseMediaPlayback() {
  let paused = 0;
  for (const media of document.querySelectorAll("audio, video")) {
    try {
      if (!media.paused) {
        media.pause();
        paused += 1;
      }
    } catch {
      // Ignore media elements controlled by cross-origin frames or site code.
    }
  }

  try {
    if ("mediaSession" in navigator)
      navigator.mediaSession.playbackState = "paused";
  } catch {
    // Some pages expose Media Session as read-only.
  }

  return { ok: true, paused };
}

function isProbablySupportedCandidate(raw) {
  let url;
  try {
    url = new URL(raw);
  } catch {
    return /^spotify:[a-z]+:[A-Za-z0-9]+$/i.test(String(raw || ""));
  }

  if (url.protocol !== "http:" && url.protocol !== "https:")
    return false;

  return isYouTubeHost(url.hostname) ||
    isSoundCloudHost(url.hostname) ||
    isSunoHost(url.hostname) ||
    isSpotifyHost(url.hostname) ||
    isDirectAudioUrl(url);
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
