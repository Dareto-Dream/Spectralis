'use strict';

// ─── Bootstrap ────────────────────────────────────────────────────────────────

const params  = new URLSearchParams(window.location.search);
const SESSION = (params.get('session') || '').trim().replace(/[^A-Z0-9]/gi, '').toUpperCase().slice(0, 6);
const BASE    = window.location.origin;

const $  = id => document.getElementById(id);
const show = id => { const el = $(id); if (el) el.hidden = false; };
const hide = id => { const el = $(id); if (el) el.hidden = true; };

let sqData       = null;   // streamer-queue settings + submissions
let stripeClient = null;   // Stripe instance
let npInterval   = null;   // now-playing poll timer

// ─── Entry point ──────────────────────────────────────────────────────────────

window.addEventListener('DOMContentLoaded', () => {
  if (!SESSION) {
    showError('No session code', 'Add ?session=XXXXXX to the URL.');
    return;
  }
  if (SESSION.length !== 6) {
    showError('Invalid session code', 'Session codes are 6 characters long.');
    return;
  }
  bootstrap();
});

async function bootstrap() {
  try {
    sqData = await apiFetch(`/shared-play/v2/sessions/${SESSION}/streamer-queue`);
  } catch (e) {
    showError('Session not found', 'This session has expired or the code is incorrect.');
    return;
  }

  if (!sqData.enabled) {
    show('disabledState');
    return;
  }

  // Room code badge
  const badge = $('roomBadge');
  if (badge) {
    badge.textContent = SESSION.slice(0, 3) + '-' + SESSION.slice(3);
    badge.hidden = false;
  }

  // Init Stripe if any fee is enabled and publishable key present
  if (sqData.stripePublishableKey && typeof Stripe !== 'undefined') {
    stripeClient = Stripe(sqData.stripePublishableKey);
  }

  renderSettings();
  show('mainContent');

  // Start now-playing poll
  pollNowPlaying();
  npInterval = setInterval(pollNowPlaying, 5000);

  // Wire forms
  $('requestForm').addEventListener('submit', handleSubmit);
  $('skipForm')?.addEventListener('submit', handleSkipRequest);
  $('superSkipForm')?.addEventListener('submit', handleSuperSkip);
  $('requestAnotherBtn')?.addEventListener('click', resetRequestForm);
}

// ─── Render settings ──────────────────────────────────────────────────────────

function renderSettings() {
  const s = sqData.settings || {};
  const sub_count = (sqData.submissions || []).filter(
    s => !['rejected', 'played'].includes(s.status)
  ).length;

  $('queueCount').textContent = sub_count > 0 ? `${sub_count} in queue` : '';

  // Queue entry fee
  if (s.queueEntryFee?.enabled) {
    const feeAmt = formatMoney(s.queueEntryFee.amount, s.queueEntryFee.currency);
    $('feeLabel').textContent = `Queue entry: ${feeAmt}`;
    show('feeBlock');
  }

  // Skip requests
  if (s.skipRequests?.enabled) {
    show('skipSection');
    const skipAmt = s.skipRequests.amount > 0
      ? formatMoney(s.skipRequests.amount, s.skipRequests.currency)
      : 'Free';
    $('skipPriceLabel').textContent = skipAmt;
    if (s.skipRequests.votesRequired > 1) {
      $('skipDescription').textContent =
        `${s.skipRequests.votesRequired} votes needed to trigger a skip.`;
    }
    if (s.skipRequests.amount > 0 && stripeClient) {
      show('skipFeeBlock');
    }
  }

  // Super skips
  if (s.superSkips?.enabled) {
    show('superSkipSection');
    const superAmt = s.superSkips.amount > 0
      ? formatMoney(s.superSkips.amount, s.superSkips.currency)
      : 'Free';
    $('superSkipPriceLabel').textContent = superAmt;
    if (s.superSkips.amount > 0 && stripeClient) {
      show('superSkipFeeBlock');
    }
  }
}

// ─── Now playing ──────────────────────────────────────────────────────────────

async function pollNowPlaying() {
  try {
    const [state, session] = await Promise.all([
      apiFetch(`/shared-play/v2/sessions/${SESSION}/state`).catch(() => null),
      apiFetch(`/shared-play/v2/sessions/${SESSION}`).catch(() => null),
    ]);

    const playback = state?.playback || state?.state;
    const track    = session?.track;

    if (!playback || !track) return;

    $('npTitle').textContent  = track.displayName || track.title || '—';
    $('npArtist').textContent = track.artist || '';

    const art = $('npArt');
    if (track.artworkUrl || track.albumArtUrl) {
      art.src = track.artworkUrl || track.albumArtUrl;
      art.hidden = false;
    }

    const duration = playback.durationSeconds || 1;
    const elapsed  = computeElapsed(playback);
    const pct      = Math.min(100, (elapsed / duration) * 100);
    $('npProgress').style.width = pct + '%';

    const disc = $('npDisc');
    if (playback.isPlaying) {
      disc.classList.add('sq-disc-spinning');
    } else {
      disc.classList.remove('sq-disc-spinning');
    }

    show('nowPlaying');
  } catch (_) {
    // silently ignore - not critical
  }
}

function computeElapsed(playback) {
  if (!playback) return 0;
  const pos     = playback.positionSeconds || 0;
  const clockAt = playback.hostClockUtc ? new Date(playback.hostClockUtc).getTime() : null;
  if (!clockAt || !playback.isPlaying) return pos;
  return pos + (Date.now() - clockAt) / 1000;
}

// ─── Submit form ──────────────────────────────────────────────────────────────

let submitElements = null; // Stripe Elements for the request form

async function handleSubmit(e) {
  e.preventDefault();
  clearError('submitError');

  const name = $('inputName').value.trim() || 'Listener';
  const url  = $('inputUrl').value.trim();

  if (!url) {
    showFieldError('submitError', 'Please enter a track link.');
    return;
  }

  const s       = sqData.settings || {};
  const feeEnabled = s.queueEntryFee?.enabled && s.queueEntryFee?.amount > 0;

  setLoading('submitBtn', 'submitSpinner', true);

  try {
    // Step 1: POST to backend
    const result = await apiFetch(`/shared-play/v2/sessions/${SESSION}/streamer-queue/submit`, {
      method: 'POST',
      body: JSON.stringify({ displayName: name, clientId: getClientId(), url }),
    });

    if (result.status === 'awaiting_payment' && result.clientSecret) {
      // Step 2: Confirm payment with Stripe
      if (!stripeClient) {
        throw new Error('Stripe is not available. Please refresh and try again.');
      }
      if (!submitElements) {
        submitElements = stripeClient.elements({ clientSecret: result.clientSecret });
        const payEl = submitElements.create('payment');
        payEl.mount('#stripeElements');
        // Give Stripe a moment to mount
        await delay(300);
      }
      const { error } = await stripeClient.confirmPayment({
        elements: submitElements,
        redirect: 'if_required',
      });
      if (error) {
        throw new Error(error.message || 'Payment failed. You have not been charged.');
      }
      // Step 3: Poll until payment confirmed by webhook
      await pollUntilPaymentConfirmed(result.submissionId);
    }

    // Success
    show('submitSuccess');
    hide('requestForm');
    const pos = result.position;
    $('successMsg').textContent = pos
      ? `You're #${pos} in the queue!`
      : 'Your request is pending approval.';

    // Refresh queue count
    sqData = await apiFetch(`/shared-play/v2/sessions/${SESSION}/streamer-queue`).catch(() => sqData);
    renderSettings();

  } catch (err) {
    showFieldError('submitError', err.message || 'Something went wrong. Please try again.');
  } finally {
    setLoading('submitBtn', 'submitSpinner', false);
  }
}

async function pollUntilPaymentConfirmed(submissionId, timeoutMs = 30000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    await delay(1500);
    try {
      const fresh = await apiFetch(`/shared-play/v2/sessions/${SESSION}/streamer-queue`);
      const sub = (fresh.submissions || []).find(s => s.id === submissionId);
      if (sub && sub.paymentStatus === 'succeeded') {
        sqData = fresh;
        return;
      }
      if (sub && sub.paymentStatus === 'failed') {
        throw new Error("Payment didn't go through. You haven't been charged.");
      }
    } catch (e) {
      if (e.message.includes("charged")) throw e;
    }
  }
  throw new Error('Payment is taking longer than expected. Check back in a moment.');
}

function resetRequestForm() {
  hide('submitSuccess');
  show('requestForm');
  $('inputUrl').value = '';
  submitElements = null;
  const mountEl = $('stripeElements');
  if (mountEl) mountEl.innerHTML = '';
  clearError('submitError');
}

// ─── Skip request ─────────────────────────────────────────────────────────────

let skipElements = null;

async function handleSkipRequest(e) {
  e.preventDefault();
  clearError('skipError');
  const name = $('skipName').value.trim() || 'Listener';
  setLoading('skipBtn', 'skipSpinner', true);
  try {
    const result = await apiFetch(`/shared-play/v2/sessions/${SESSION}/streamer-queue/skip-request`, {
      method: 'POST',
      body: JSON.stringify({ displayName: name, clientId: getClientId() }),
    });
    if (result.status === 'awaiting_payment' && result.clientSecret && stripeClient) {
      if (!skipElements) {
        skipElements = stripeClient.elements({ clientSecret: result.clientSecret });
        skipElements.create('payment').mount('#skipStripeElements');
        await delay(300);
      }
      const { error } = await stripeClient.confirmPayment({
        elements: skipElements,
        redirect: 'if_required',
      });
      if (error) throw new Error(error.message || 'Payment failed. You have not been charged.');
    }
    show('skipSuccess');
    hide('skipSection');
  } catch (err) {
    showFieldError('skipError', err.message || 'Something went wrong.');
  } finally {
    setLoading('skipBtn', 'skipSpinner', false);
  }
}

// ─── Super skip ───────────────────────────────────────────────────────────────

let superSkipElements = null;

async function handleSuperSkip(e) {
  e.preventDefault();
  clearError('superSkipError');
  const name = $('superSkipName').value.trim() || 'Listener';
  setLoading('superSkipBtn', 'superSkipSpinner', true);
  try {
    const result = await apiFetch(`/shared-play/v2/sessions/${SESSION}/streamer-queue/super-skip`, {
      method: 'POST',
      body: JSON.stringify({ displayName: name, clientId: getClientId() }),
    });
    if (result.status === 'awaiting_payment' && result.clientSecret && stripeClient) {
      if (!superSkipElements) {
        superSkipElements = stripeClient.elements({ clientSecret: result.clientSecret });
        superSkipElements.create('payment').mount('#superSkipStripeElements');
        await delay(300);
      }
      const { error } = await stripeClient.confirmPayment({
        elements: superSkipElements,
        redirect: 'if_required',
      });
      if (error) throw new Error(error.message || 'Payment failed. You have not been charged.');
    }
    show('superSkipSuccess');
    hide('superSkipSection');
  } catch (err) {
    showFieldError('superSkipError', err.message || 'Something went wrong.');
  } finally {
    setLoading('superSkipBtn', 'superSkipSpinner', false);
  }
}

// ─── Utilities ────────────────────────────────────────────────────────────────

async function apiFetch(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options,
  });
  const json = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(json.error || `Request failed (${res.status})`);
  return json;
}

function showError(title, message) {
  hide('mainContent');
  hide('disabledState');
  $('errorTitle').textContent   = title;
  $('errorMessage').textContent = message;
  show('errorState');
}

function showFieldError(id, message) {
  const el = $(id);
  if (!el) return;
  el.textContent = message;
  el.hidden = false;
}

function clearError(id) {
  const el = $(id);
  if (el) { el.textContent = ''; el.hidden = true; }
}

function setLoading(btnId, spinnerId, loading) {
  const btn = $(btnId);
  if (btn) btn.disabled = loading;
  const spinner = $(spinnerId);
  if (spinner) spinner.hidden = !loading;
  const txt = $(btnId + 'Text');
  if (txt) txt.style.opacity = loading ? '0.5' : '1';
}

function formatMoney(amount, currency = 'USD') {
  try {
    return new Intl.NumberFormat('en-US', { style: 'currency', currency }).format(amount);
  } catch (_) {
    return `${currency} ${amount.toFixed(2)}`;
  }
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function getClientId() {
  let id = localStorage.getItem('sq_client_id');
  if (!id) {
    id = Math.random().toString(36).slice(2) + Date.now().toString(36);
    localStorage.setItem('sq_client_id', id);
  }
  return id;
}
