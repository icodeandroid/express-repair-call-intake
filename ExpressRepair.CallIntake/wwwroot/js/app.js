// Leads table: fetch, render, repeat.
//
// Polling rather than SignalR. The brief allows either, and at this scale the
// trade-off favours polling: no hub, no connection lifecycle, no client library
// to load, and it degrades to "slightly stale" instead of "silently broken".
// The upgrade path is in the README.

const POLL_INTERVAL_MS = 2000;

const body = document.getElementById('leadsBody');
const liveDot = document.getElementById('liveDot');
const liveText = document.getElementById('liveText');
const simulateResult = document.getElementById('simulateResult');

// Last payload rendered, so an unchanged poll does not repaint the table
// and kill the user's text selection.
let lastSignature = null;

function escapeHtml(value) {
  if (value === null || value === undefined) return '';
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function cell(value) {
  return value === null || value === undefined || value === ''
    ? '<span class="muted">—</span>'
    : escapeHtml(value);
}

function formatTime(iso) {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleString();
}

function statusBadge(call) {
  const label = escapeHtml(call.status);
  const className = `badge badge-${label.toLowerCase()}`;

  // The failure reason is the useful part of a Failed row, so surface it
  // on hover rather than hiding it in the server log.
  const title = call.status === 'Failed' && call.error
    ? ` title="${escapeHtml(call.error)}"`
    : '';

  const attempts = call.attempts > 1 ? ` <span class="muted">×${call.attempts}</span>` : '';

  return `<span class="${className}"${title}>${label}</span>${attempts}`;
}

function urgencyBadge(urgency) {
  if (!urgency) return '<span class="muted">—</span>';
  const safe = escapeHtml(urgency);
  return `<span class="badge badge-${safe}">${safe}</span>`;
}

function render(calls) {
  if (calls.length === 0) {
    body.innerHTML = '<tr><td colspan="8" class="empty">No calls yet. Simulate one above.</td></tr>';
    return;
  }

  body.innerHTML = calls.map(call => `
    <tr class="row-${escapeHtml(call.status.toLowerCase())}">
      <td class="nowrap">${cell(call.phone)}</td>
      <td>${cell(call.customerName)}</td>
      <td>${cell(call.appliance)}</td>
      <td>${cell(call.brand)}</td>
      <td class="problem">${cell(call.problem)}</td>
      <td>${urgencyBadge(call.urgency)}</td>
      <td class="nowrap">${statusBadge(call)}</td>
      <td class="nowrap">${escapeHtml(formatTime(call.receivedAt))}</td>
    </tr>
  `).join('');
}

function setLive(ok, message) {
  liveDot.className = ok ? 'dot dot-ok' : 'dot dot-bad';
  liveText.textContent = message;
}

async function poll() {
  try {
    const response = await fetch('/api/calls', { headers: { 'Accept': 'application/json' } });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);

    const calls = await response.json();

    const signature = JSON.stringify(calls);
    if (signature !== lastSignature) {
      render(calls);
      lastSignature = signature;
    }

    const processing = calls.filter(c => c.status === 'Processing').length;
    setLive(true, processing > 0 ? `live — ${processing} processing` : 'live');
  } catch (error) {
    // A failed poll is not fatal: say so and try again on the next tick.
    setLive(false, 'connection lost — retrying');
    console.error('Poll failed', error);
  }
}

async function simulate(sample) {
  simulateResult.textContent = 'Posting to the webhook…';

  try {
    const response = await fetch(`/api/simulate/${sample}`, { method: 'POST' });
    const data = await response.json();

    if (!response.ok) throw new Error(data.error ?? `HTTP ${response.status}`);

    simulateResult.textContent =
      `Webhook accepted sample ${sample} — call #${data.id} is ${data.status}.`;

    poll(); // Show the new row immediately rather than waiting for the next tick.
  } catch (error) {
    simulateResult.textContent = `Could not simulate the call: ${error.message}`;
  }
}

document.querySelectorAll('button[data-sample]').forEach(button => {
  button.addEventListener('click', () => simulate(Number(button.dataset.sample)));
});

poll();
setInterval(poll, POLL_INTERVAL_MS);
