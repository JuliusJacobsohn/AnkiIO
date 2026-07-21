const startButton = document.querySelector('#start');
const stopButton = document.querySelector('#stop');
const autoStart = document.querySelector('#autostart');
const status = document.querySelector('#status');
const logBox = document.querySelector('#log');
const playback = document.querySelector('#playback');
const copyButton = document.querySelector('#copy');

let recorder;
let stream;
let chunks = [];
let startedAt;

const supportedTypes = [
  'audio/webm;codecs=opus',
  'audio/webm',
  'audio/mp4'
];

function log(event, details = {}) {
  const entry = {
    at: new Date().toISOString(),
    elapsedMs: Math.round(performance.now()),
    event,
    ...details
  };
  logBox.value += `${JSON.stringify(entry)}\n`;
  logBox.scrollTop = logBox.scrollHeight;
}

function preferredMimeType() {
  return supportedTypes.find(type => MediaRecorder.isTypeSupported(type)) || '';
}

async function begin(trigger) {
  if (recorder?.state === 'recording') return;
  startButton.disabled = true;
  status.textContent = 'Requesting microphone…';
  const permissionStart = performance.now();
  log('start-requested', { trigger, visibility: document.visibilityState });

  try {
    stream = await navigator.mediaDevices.getUserMedia({
      audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true }
    });
    log('microphone-granted', { waitMs: Math.round(performance.now() - permissionStart) });
    chunks = [];
    const mimeType = preferredMimeType();
    recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
    recorder.addEventListener('dataavailable', event => {
      if (event.data.size > 0) {
        chunks.push(event.data);
        if (chunks.length === 1) log('first-audio-chunk', { bytes: event.data.size });
      }
    });
    recorder.addEventListener('stop', upload);
    recorder.start(250);
    startedAt = performance.now();
    status.textContent = `Recording (${recorder.mimeType || 'browser default'})`;
    stopButton.disabled = false;
    log('recording-started', { mimeType: recorder.mimeType, supportedTypes: supportedTypes.filter(MediaRecorder.isTypeSupported) });
  } catch (error) {
    status.textContent = `Could not start: ${error.name}`;
    startButton.disabled = false;
    log('recording-start-failed', { name: error.name, message: error.message });
  }
}

async function upload() {
  const durationMs = Math.round(performance.now() - startedAt);
  const blob = new Blob(chunks, { type: recorder.mimeType || 'application/octet-stream' });
  playback.src = URL.createObjectURL(blob);
  playback.hidden = false;
  stream?.getTracks().forEach(track => track.stop());
  log('recording-stopped', { durationMs, bytes: blob.size, mimeType: blob.type });
  status.textContent = 'Uploading…';
  const uploadStart = performance.now();
  const extension = blob.type.includes('mp4') ? 'm4a' : 'webm';
  const form = new FormData();
  form.append('audio', blob, `capture-${Date.now()}.${extension}`);

  try {
    const response = await fetch('/api/recordings', { method: 'POST', body: form });
    const body = await response.json();
    if (!response.ok) throw new Error(JSON.stringify(body));
    status.textContent = 'Uploaded';
    log('upload-completed', { uploadMs: Math.round(performance.now() - uploadStart), response: body });
  } catch (error) {
    status.textContent = 'Upload failed; recording remains playable on this page';
    log('upload-failed', { uploadMs: Math.round(performance.now() - uploadStart), message: error.message });
  } finally {
    startButton.disabled = false;
  }
}

startButton.addEventListener('click', () => begin('user-click'));
stopButton.addEventListener('click', () => {
  stopButton.disabled = true;
  recorder?.stop();
});
autoStart.checked = localStorage.getItem('capture-poc-autostart') === 'true';
autoStart.addEventListener('change', () => localStorage.setItem('capture-poc-autostart', String(autoStart.checked)));
copyButton.addEventListener('click', async () => navigator.clipboard.writeText(logBox.value));
document.addEventListener('visibilitychange', () => log('visibility-changed', { visibility: document.visibilityState }));

window.addEventListener('load', async () => {
  log('page-loaded', {
    standalone: matchMedia('(display-mode: standalone)').matches,
    userAgent: navigator.userAgent,
    secureContext: window.isSecureContext,
    mediaDevices: Boolean(navigator.mediaDevices),
    preferredMimeType: preferredMimeType()
  });

  if ('serviceWorker' in navigator) {
    try {
      await navigator.serviceWorker.register('/sw.js');
      log('service-worker-registered');
    } catch (error) {
      log('service-worker-failed', { message: error.message });
    }
  }

  if (autoStart.checked) await begin('page-load-opt-in');
});
