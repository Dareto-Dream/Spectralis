'use strict';

// ─── Bootstrap ────────────────────────────────────────────────────────────────

const params  = new URLSearchParams(window.location.search);
const ROOM_ID = (params.get('room') || '').trim().replace(/[^A-Za-z0-9\-]/g, '').slice(0, 40);
const BASE    = window.location.origin;

const $  = id => document.getElementById(id);
const show = id => { const el = $(id); if (el) el.hidden = false; };
const hide = id => { const el = $(id); if (el) el.hidden = true; };
const setText = (id, t) => { const el = $(id); if (el) el.textContent = t; };

let roomData = null;
let stripeClient = null;
let pollTimer = null;
let activeFpCookie = null;

// ─── Fingerprint collection ───────────────────────────────────────────────────

function getFp() {
  if (!activeFpCookie) {
    let cookie = '';
    const match = document.cookie.match(/(?:^|;\s*)_sqfp=([^;]+)/);
    if (match) {
      cookie = match[1];
    } else {
      cookie = crypto.randomUUID ? crypto.randomUUID() : Math.random().toString(36).slice(2) + Date.now().toString(36);
      document.cookie = `_sqfp=${cookie}; max-age=${60 * 60 * 24 * 365}; path=/; SameSite=Lax`;
    }
    activeFpCookie = cookie;
  }
  return {
    fpCookie: activeFpCookie,
    fpUa: navigator.userAgent.slice(0, 200),
    fpScreen: `${screen.width}x${screen.height}x${screen.colorDepth}`,
    fpTz: new Date().getTimezoneOffset(),
    fpLang: navigator.language || '',
  };
}

// ─── Entry point ──────────────────────────────────────────────────────────────

window.addEventListener('DOMContentLoaded', () => {
  if (!ROOM_ID || ROOM_ID.length < 8) {
    showError('No room ID', 'Add ?room=YOUR_ROOM_ID to the URL.');
    return;
  }
  bootstrap();
});

async function bootstrap() {
  try {
    roomData = await apiFetch(`/streamer-queue/v1/rooms/${ROOM_ID}`);
  } catch (e) {
    showError('Room not found', 'This room has expired or the ID is incorrect.');
    return;
  }

  if (!roomData.enabled) {
    show('disabledState');
    return;
  }

  const badge = $('roomBadge');
  if (badge) {
    badge.textContent = ROOM_ID.slice(0, 8) + '…';
    badge.hidden = false;
  }

  if (roomData.stripePublishableKey && typeof Stripe !== 'undefined') {
    stripeClient = Stripe(roomData.stripePublishableKey);
  }

  setupTabs();
  renderSettings();
  bindForms();
  show('mainContent');
  updateNowPlaying();
  pollTimer = setInterval(pollRoom, 15000);
}

// ─── Poll ─────────────────────────────────────────────────────────────────────

async function pollRoom() {
  try {
    roomData = await apiFetch(`/streamer-queue/v1/rooms/${ROOM_ID}`);
    updateNowPlaying();
    const countEl = $('queueCount');
    if (countEl) countEl.textContent = roomData.queueLength > 0 ? `${roomData.queueLength} in queue` : '';
  } catch { /* non-fatal */ }
}

// ─── Settings rendering ───────────────────────────────────────────────────────

function renderSettings() {
  const s = roomData.settings;
  if (!s) return;

  const queueCount = $('queueCount');
  if (queueCount) queueCount.textContent = roomData.queueLength > 0 ? `${roomData.queueLength} in queue` : '';

  // Upload tab visibility
  if (!s.allowLinkSubmissions) {
    hide('requestForm');
    show('uploadForm');
    const uploadTab = $('uploadTab');
    if (uploadTab) uploadTab.hidden = false;
  }
  // Show upload tab button always if file uploads supported (server always accepts)
  const uploadTab = $('uploadTab');
  if (uploadTab) uploadTab.hidden = false;

  // Entry fee
  const queueFee = s.queueEntryFee;
  if (queueFee && queueFee.enabled && queueFee.amount > 0) {
    const label = $('feeLabel');
    if (label) label.textContent = `Queue entry: ${formatAmount(queueFee.amount, queueFee.currency)}`;
    show('feeBlock');
    if (stripeClient) {
      mountStripeElement('stripeElements');
    }
  }

  // Skip sections
  const skipFee = s.skip;
  if (skipFee) {
    show('skipSection');
    const skipLabel = $('skipPriceLabel');
    if (skipLabel) skipLabel.textContent = skipFee.enabled && skipFee.amount > 0
      ? formatAmount(skipFee.amount, skipFee.currency) : 'Free';
    if (skipFee.enabled && skipFee.amount > 0 && stripeClient) {
      show('skipFeeBlock');
      mountStripeElement('skipStripeElements');
    }
  }

  const superSkipFee = s.superSkip;
  if (superSkipFee) {
    show('superSkipSection');
    const ssLabel = $('superSkipPriceLabel');
    if (ssLabel) ssLabel.textContent = superSkipFee.enabled && superSkipFee.amount > 0
      ? formatAmount(superSkipFee.amount, superSkipFee.currency) : 'Free';
    if (superSkipFee.enabled && superSkipFee.amount > 0 && stripeClient) {
      show('superSkipFeeBlock');
      mountStripeElement('superSkipStripeElements');
    }
  }
}

// ─── Tab switching ────────────────────────────────────────────────────────────

function setupTabs() {
  document.querySelectorAll('.sq-tab').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.sq-tab').forEach(b => b.classList.remove('sq-tab-active'));
      btn.classList.add('sq-tab-active');
      const tab = btn.dataset.tab;
      if (tab === 'link') {
        show('requestForm');
        hide('uploadForm');
      } else {
        hide('requestForm');
        show('uploadForm');
      }
    });
  });
}

// ─── Now playing ──────────────────────────────────────────────────────────────

function updateNowPlaying() {
  if (!roomData.nowPlayingId) {
    hide('nowPlaying');
    return;
  }
  const ordered = roomData.orderedQueue || [];
  const np = ordered.find(s => s.id === roomData.nowPlayingId)
    || (roomData.submissions || []).find(s => s.id === roomData.nowPlayingId);
  if (!np) { hide('nowPlaying'); return; }

  setText('npTitle', np.title || '—');
  setText('npArtist', np.artist || '');

  const p2w = roomData.nowPlayingTier === 'skip' || roomData.nowPlayingTier === 'super_skip';
  if (p2w) show('p2wBadge'); else hide('p2wBadge');

  show('nowPlaying');
}

// ─── Metadata autofill ────────────────────────────────────────────────────────

let metaFetchTimer = null;

async function tryAutofillFromUrl(url) {
  if (!url) return;
  clearTimeout(metaFetchTimer);
  metaFetchTimer = setTimeout(async () => {
    show('urlSpinner');
    try {
      // Ask the backend to resolve metadata. Endpoint TBD – use oEmbed fallback client-side for now.
      const oembed = await fetchOembed(url);
      if (oembed) {
        const titleEl = $('inputTitle');
        const artistEl = $('inputArtist');
        if (titleEl && !titleEl.value) titleEl.value = oembed.title || '';
        if (artistEl && !artistEl.value) artistEl.value = oembed.author_name || '';
        show('metaRow');
      } else {
        show('metaRow');
      }
    } catch { show('metaRow'); }
    hide('urlSpinner');
    updateWaitEstimate();
  }, 800);
}

async function fetchOembed(url) {
  const providers = [
    `https://noembed.com/embed?url=${encodeURIComponent(url)}`,
  ];
  for (const endpoint of providers) {
    try {
      const res = await fetch(endpoint);
      if (res.ok) {
        const data = await res.json();
        if (data && data.title) return data;
      }
    } catch { /* try next */ }
  }
  return null;
}

function tryAutofillFromFile(file) {
  // Duration will be filled by the browser via an Audio element
  const audio = document.createElement('audio');
  audio.src = URL.createObjectURL(file);
  audio.addEventListener('loadedmetadata', () => {
    const durEl = $('uploadDuration');
    if (durEl) durEl.value = String(Math.round(audio.duration));
    URL.revokeObjectURL(audio.src);
    updateUploadWaitEstimate(audio.duration);
  });
}

// ─── Wait estimate ────────────────────────────────────────────────────────────

function updateWaitEstimate() {
  const qLen = roomData.queueLength || 0;
  if (qLen <= 0) { hide('waitEstimate'); return; }
  const avgTrackMins = 3.5;
  const reviewMins = 3;
  const lowMins = qLen * (avgTrackMins + reviewMins);
  const highMins = Math.round(lowMins * 1.12);
  setText('waitText', `~${Math.round(lowMins)}–${highMins} min`);
  show('waitEstimate');
}

function updateUploadWaitEstimate(durationSecs) {
  const qLen = roomData.queueLength || 0;
  const trackMins = (durationSecs / 60) || 3.5;
  const lowMins = qLen * (trackMins + 3);
  const highMins = Math.round(lowMins * 1.12);
  setText('uploadWaitText', `~${Math.round(lowMins)}–${highMins} min`);
  show('uploadWait');
}

// ─── Form binding ─────────────────────────────────────────────────────────────

function bindForms() {
  // Link URL → autofill
  const urlInput = $('inputUrl');
  if (urlInput) {
    urlInput.addEventListener('input', () => {
      const val = urlInput.value.trim();
      if (val.startsWith('http')) tryAutofillFromUrl(val);
    });
    urlInput.addEventListener('blur', updateWaitEstimate);
  }

  // Upload file → autofill
  const fileInput = $('uploadFile');
  if (fileInput) {
    fileInput.addEventListener('change', () => {
      const f = fileInput.files[0];
      if (f) tryAutofillFromFile(f);
    });
  }

  // Submit link form
  const requestForm = $('requestForm');
  if (requestForm) {
    requestForm.addEventListener('submit', async e => {
      e.preventDefault();
      await submitLink('normal');
    });
  }

  // Submit upload form
  const uploadForm = $('uploadForm');
  if (uploadForm) {
    uploadForm.addEventListener('submit', async e => {
      e.preventDefault();
      await submitUpload('normal');
    });
  }

  // Skip form
  const skipForm = $('skipForm');
  if (skipForm) {
    skipForm.addEventListener('submit', async e => {
      e.preventDefault();
      await submitLink('skip', { nameId: 'skipName', errorId: 'skipError', spinnerId: 'skipSpinner', btnId: 'skipBtn', btnTextId: 'skipBtnText', successId: 'skipSuccess' });
    });
  }

  // Super skip form
  const ssForm = $('superSkipForm');
  if (ssForm) {
    ssForm.addEventListener('submit', async e => {
      e.preventDefault();
      await submitLink('super_skip', { nameId: 'superSkipName', errorId: 'superSkipError', spinnerId: 'superSkipSpinner', btnId: 'superSkipBtn', btnTextId: 'superSkipBtnText', successId: 'superSkipSuccess' });
    });
  }

  // Request another
  const anotherBtn = $('requestAnotherBtn');
  if (anotherBtn) {
    anotherBtn.addEventListener('click', () => {
      hide('submitSuccess');
      show('requestSection');
      const rf = $('requestForm');
      if (rf) rf.reset();
      hide('metaRow');
      hide('waitEstimate');
    });
  }
}

// ─── Link submission ──────────────────────────────────────────────────────────

async function submitLink(tier, ids = {}) {
  const nameId   = ids.nameId    || 'inputName';
  const errorId  = ids.errorId   || 'submitError';
  const spinnerId= ids.spinnerId || 'submitSpinner';
  const btnId    = ids.btnId     || 'submitBtn';
  const succId   = ids.successId || null;

  hide(errorId);
  show(spinnerId);
  const btn = $(btnId);
  if (btn) btn.disabled = true;

  const fp = getFp();
  const body = {
    url: ($('inputUrl') || {}).value || '',
    displayName: ($(nameId) || {}).value || 'Listener',
    title: ($('inputTitle') || {}).value || null,
    artist: ($('inputArtist') || {}).value || null,
    durationSeconds: null,
    tier,
    ...fp,
  };

  try {
    const result = await apiFetch(`/streamer-queue/v1/rooms/${ROOM_ID}/submit`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });

    if (result.status === 'awaiting_payment' && result.clientSecret && stripeClient) {
      await confirmStripePayment(result.clientSecret, errorId);
      hide(spinnerId);
      if (btn) btn.disabled = false;
      return;
    }

    if (succId) {
      show(succId);
    } else {
      const pos = result.position ? `Position #${result.position}` : '';
      const wait = result.waitEstimateHighMins ? ` — est. ${Math.round(result.waitEstimateLowMins)}–${Math.round(result.waitEstimateHighMins)} min` : '';
      setText('successMsg', 'You\'re in the queue!');
      setText('successWaitText', pos + wait);
      hide('requestSection');
      show('submitSuccess');
    }
    await pollRoom();
  } catch (err) {
    show(errorId);
    setText(errorId, err.message || 'Submission failed. Please try again.');
  } finally {
    hide(spinnerId);
    if (btn) btn.disabled = false;
  }
}

// ─── Upload submission ────────────────────────────────────────────────────────

async function submitUpload(tier) {
  hide('uploadError');
  show('uploadSpinner');
  const btn = $('uploadBtn');
  if (btn) btn.disabled = true;

  const fp = getFp();
  const file = $('uploadFile').files[0];
  if (!file) {
    show('uploadError');
    setText('uploadError', 'Please select a file.');
    hide('uploadSpinner');
    if (btn) btn.disabled = false;
    return;
  }

  const form = new FormData();
  form.append('file', file, file.name);
  form.append('displayName', $('uploadName').value || 'Listener');
  form.append('title', $('uploadTitle').value || '');
  form.append('artist', $('uploadArtist').value || '');
  form.append('tier', tier);
  form.append('fpCookie', fp.fpCookie);
  form.append('fpUa', fp.fpUa);
  form.append('fpScreen', fp.fpScreen);
  form.append('fpTz', String(fp.fpTz));

  try {
    const res = await fetch(`${BASE}/streamer-queue/v1/rooms/${ROOM_ID}/upload`, { method: 'POST', body: form });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: 'Upload failed' }));
      throw new Error(err.error || `HTTP ${res.status}`);
    }
    const result = await res.json();
    const pos = result.position ? `Position #${result.position}` : '';
    const wait = result.waitEstimateHighMins ? ` — est. ${Math.round(result.waitEstimateLowMins)}–${Math.round(result.waitEstimateHighMins)} min` : '';
    setText('successMsg', 'File uploaded!');
    setText('successWaitText', pos + wait);
    hide('requestSection');
    show('submitSuccess');
    await pollRoom();
  } catch (err) {
    show('uploadError');
    setText('uploadError', err.message || 'Upload failed.');
  } finally {
    hide('uploadSpinner');
    if (btn) btn.disabled = false;
  }
}

// ─── Stripe ───────────────────────────────────────────────────────────────────

const stripeElements = {};

function mountStripeElement(containerId) {
  if (!stripeClient || stripeElements[containerId]) return;
  const elements = stripeClient.elements();
  const card = elements.create('card', { style: { base: { color: '#e2e8f0', '::placeholder': { color: '#718096' } } } });
  card.mount(`#${containerId}`);
  stripeElements[containerId] = { elements, card };
}

async function confirmStripePayment(clientSecret, errorId) {
  const se = stripeElements['stripeElements'];
  if (!se) return;
  const result = await stripeClient.confirmCardPayment(clientSecret, {
    payment_method: { card: se.card },
  });
  if (result.error) {
    show(errorId);
    setText(errorId, result.error.message);
  } else {
    hide('requestSection');
    show('submitSuccess');
    setText('successMsg', 'Payment confirmed — you\'re in the queue!');
  }
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

async function apiFetch(path, opts = {}) {
  const res = await fetch(BASE + path, { ...opts });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.error || `HTTP ${res.status}`);
  }
  return res.json();
}

function showError(title, message) {
  hide('mainContent');
  hide('disabledState');
  setText('errorTitle', title);
  setText('errorMessage', message);
  show('errorState');
}

function formatAmount(amount, currency) {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: currency || 'USD' }).format(amount);
}
