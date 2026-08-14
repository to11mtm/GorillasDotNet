// Every sound here is synthesised at runtime. Nothing is sampled or shipped as an asset, which
// keeps the repository free of audio files and suits the chunky retro aesthetic.

const MUTE_KEY = 'gorillas.muted';

let context = null;
let master = null;
let muted = localStorage.getItem(MUTE_KEY) === 'true';
let noiseBuffer = null;

// Lets the end-to-end tests assert that the right sounds fired at the right moments, which is
// otherwise unobservable in a headless browser with no audio device.
const telemetry = { whoosh: 0, explosion: 0, hit: 0, fanfare: 0 };
window.__gorillasAudio = telemetry;

function ensureContext() {
    if (context) {
        return context;
    }

    const Ctor = window.AudioContext || window.webkitAudioContext;
    if (!Ctor) {
        return null;
    }

    try {
        context = new Ctor();
        master = context.createGain();
        master.gain.value = muted ? 0 : 0.35;
        master.connect(context.destination);
    } catch {
        context = null;
    }

    return context;
}

function noise() {
    if (noiseBuffer) {
        return noiseBuffer;
    }

    const length = context.sampleRate * 1.2;
    noiseBuffer = context.createBuffer(1, length, context.sampleRate);
    const data = noiseBuffer.getChannelData(0);

    for (let i = 0; i < length; i++) {
        data[i] = (Math.random() * 2) - 1;
    }

    return noiseBuffer;
}

/// Browsers refuse to start an AudioContext without a user gesture, so unlock on the first one.
export function install() {
    const unlock = () => {
        const ctx = ensureContext();
        if (ctx && ctx.state === 'suspended') {
            ctx.resume().catch(() => { });
        }
    };

    window.addEventListener('pointerdown', unlock, { once: false, passive: true });
    window.addEventListener('keydown', unlock, { once: false, passive: true });
    unlock();
}

export function setMuted(value) {
    muted = !!value;
    localStorage.setItem(MUTE_KEY, muted ? 'true' : 'false');

    if (master) {
        master.gain.value = muted ? 0 : 0.35;
    }

    return muted;
}

export function isMuted() {
    return muted;
}

function canPlay() {
    if (muted) {
        return false;
    }

    const ctx = ensureContext();
    return ctx !== null && ctx.state !== 'suspended';
}

/// A falling whistle that lasts as long as the banana is in the air.
export function whoosh(seconds, speed) {
    telemetry.whoosh++;

    if (!canPlay()) {
        return;
    }

    const rate = speed > 0 ? speed : 1;
    const duration = Math.max(0.15, Math.min(seconds / rate, 4));
    const now = context.currentTime;

    const osc = context.createOscillator();
    osc.type = 'triangle';
    osc.frequency.setValueAtTime(620 * rate, now);
    osc.frequency.exponentialRampToValueAtTime(180 * rate, now + duration);

    const gain = context.createGain();
    gain.gain.setValueAtTime(0.0001, now);
    gain.gain.exponentialRampToValueAtTime(0.12, now + 0.05);
    gain.gain.exponentialRampToValueAtTime(0.0001, now + duration);

    osc.connect(gain).connect(master);
    osc.start(now);
    osc.stop(now + duration + 0.05);
}

/// Filtered noise burst. A direct hit gets a longer, deeper blast plus a falling tone.
export function impact(isHit, speed) {
    if (isHit) {
        telemetry.hit++;
    } else {
        telemetry.explosion++;
    }

    if (!canPlay()) {
        return;
    }

    const rate = speed > 0 ? speed : 1;
    const duration = (isHit ? 0.85 : 0.45) / rate;
    const now = context.currentTime;

    const source = context.createBufferSource();
    source.buffer = noise();

    const filter = context.createBiquadFilter();
    filter.type = 'lowpass';
    filter.frequency.setValueAtTime(isHit ? 1800 : 1200, now);
    filter.frequency.exponentialRampToValueAtTime(120, now + duration);

    const gain = context.createGain();
    gain.gain.setValueAtTime(isHit ? 0.9 : 0.55, now);
    gain.gain.exponentialRampToValueAtTime(0.0001, now + duration);

    source.connect(filter).connect(gain).connect(master);
    source.start(now);
    source.stop(now + duration);

    if (!isHit) {
        return;
    }

    const thud = context.createOscillator();
    thud.type = 'square';
    thud.frequency.setValueAtTime(160, now);
    thud.frequency.exponentialRampToValueAtTime(40, now + duration);

    const thudGain = context.createGain();
    thudGain.gain.setValueAtTime(0.3, now);
    thudGain.gain.exponentialRampToValueAtTime(0.0001, now + duration);

    thud.connect(thudGain).connect(master);
    thud.start(now);
    thud.stop(now + duration + 0.05);
}

/// Rising arpeggio for a round win, longer and higher for taking the match.
export function fanfare(isMatch) {
    telemetry.fanfare++;

    if (!canPlay()) {
        return;
    }

    const now = context.currentTime;
    const notes = isMatch ? [523, 659, 784, 1047] : [392, 523, 659];

    notes.forEach((frequency, index) => {
        const start = now + (index * 0.11);

        const osc = context.createOscillator();
        osc.type = 'square';
        osc.frequency.setValueAtTime(frequency, start);

        const gain = context.createGain();
        gain.gain.setValueAtTime(0.0001, start);
        gain.gain.exponentialRampToValueAtTime(0.18, start + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.16);

        osc.connect(gain).connect(master);
        osc.start(start);
        osc.stop(start + 0.2);
    });
}
