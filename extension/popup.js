const autoOpenToggle = document.getElementById("autoOpen");
const openButton = document.getElementById("openButton");
const queueButton = document.getElementById("queueButton");
const candidateText = document.getElementById("candidateText");
const statusText = document.getElementById("status");

let activeTab = null;
let selectedText = "";

init();

async function init() {
  const [{ enabled }, tabs] = await Promise.all([
    sendRuntimeMessage({ type: "getAutoOpen" }),
    chrome.tabs.query({ active: true, currentWindow: true })
  ]);

  autoOpenToggle.checked = Boolean(enabled);
  activeTab = tabs[0] || null;
  selectedText = await getSelectedText(activeTab?.id);

  await refreshCandidateState();
}

autoOpenToggle.addEventListener("change", async () => {
  await sendRuntimeMessage({
    type: "setAutoOpen",
    enabled: autoOpenToggle.checked
  });
  statusText.textContent = autoOpenToggle.checked
    ? "Auto queue is on."
    : "Auto queue is off.";
});

openButton.addEventListener("click", () => handOffCurrentCandidate("play-now"));
queueButton.addEventListener("click", () => handOffCurrentCandidate("queue-next"));

async function handOffCurrentCandidate(intent) {
  const raw = selectedText.trim() || activeTab?.url || "";
  setButtonsEnabled(false);
  statusText.textContent = intent === "queue-next" ? "Queueing..." : "Opening...";

  const result = await sendRuntimeMessage({
    type: "openCandidate",
    raw,
    tabId: activeTab?.id,
    intent,
    pauseSource: true
  });

  statusText.textContent = result.ok
    ? intent === "queue-next"
      ? "Queued in Spectralis and paused this tab."
      : "Sent to Spectralis and paused this tab."
    : result.message;

  const supported = await isSupported(raw);
  setButtonsEnabled(supported);
}

async function refreshCandidateState() {
  const raw = selectedText.trim() || activeTab?.url || "";
  const supported = await isSupported(raw);
  const hasSelection = selectedText.trim().length > 0;

  candidateText.textContent = supported
    ? hasSelection
      ? "Ready to send the selected audio link."
      : "Ready to send this tab."
    : "No supported audio link found here.";

  setButtonsEnabled(supported);
  statusText.textContent = supported
    ? "Use auto queue or the context menu for one-click handoff."
    : "Supported: direct audio files, YouTube, SoundCloud, Suno, and Spotify.";
}

function setButtonsEnabled(enabled) {
  openButton.disabled = !enabled;
  queueButton.disabled = !enabled;
}

async function isSupported(raw) {
  const result = await sendRuntimeMessage({ type: "getSupport", raw });
  return Boolean(result.supported);
}

async function getSelectedText(tabId) {
  if (!Number.isInteger(tabId))
    return "";

  try {
    const response = await chrome.tabs.sendMessage(tabId, { type: "getSelection" });
    return response?.selection || "";
  } catch {
    return "";
  }
}

function sendRuntimeMessage(message) {
  return new Promise(resolve => {
    chrome.runtime.sendMessage(message, response => {
      resolve(response || {});
    });
  });
}
