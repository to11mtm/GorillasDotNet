import * as audio from './gorillas-audio.js';

const VIRTUAL_WIDTH = 320;
const VIRTUAL_HEIGHT = 200;
const EXPLOSION_SECONDS = 0.55;

const themes = {
    retro: {
        sky: '#000038',
        ground: '#101010',
        sun: '#ffd700',
        sunFace: '#000038',
        buildings: ['#0d5a5a', '#7a2f7a', '#8a3d1a'],
        buildingShade: 'rgba(0, 0, 0, 0.35)',
        windowLit: '#ffe94a',
        windowDark: '#2a2a3a',
        gorilla: '#c47a2c',
        gorillaDark: '#8a5218',
        banana: '#ffe94a',
        explosion: ['#ffffff', '#ffe94a', '#ff8c1a', '#c42a00'],
        text: '#e8e8e8',
        accent: '#ffe94a',
        wind: '#ff3b3b',
    },
};

const renderers = new WeakMap();

function themeFor(name) {
    return themes[name] ?? themes.retro;
}

export function init(canvas) {
    if (!canvas) {
        return;
    }

    canvas.width = VIRTUAL_WIDTH;
    canvas.height = VIRTUAL_HEIGHT;

    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = false;

    renderers.set(canvas, {
        ctx,
        scene: null,
        animation: null,
        frameHandle: 0,
    });
}

export function setScene(canvas, scene) {
    const state = renderers.get(canvas);
    if (!state) {
        return;
    }

    state.scene = scene;
    if (!state.animation) {
        draw(state);
    }
}

export function playThrow(canvas, animation, dotNetRef, speed) {
    const state = renderers.get(canvas);
    if (!state) {
        return;
    }

    cancelAnimation(state);

    state.animation = {
        data: animation,
        elapsed: 0,
        explosionElapsed: 0,
        exploding: animation.impactRadius > 0 || animation.isHit,
        dotNetRef,
        speed: speed > 0 ? speed : 1,
        blasted: false,
        lastTimestamp: 0,
    };

    const flightSeconds = (animation.points.length - 1) * animation.stepSeconds;
    audio.whoosh(flightSeconds, state.animation.speed);

    state.frameHandle = requestAnimationFrame((timestamp) => step(state, timestamp));
}

export function dispose(canvas) {
    const state = renderers.get(canvas);
    if (!state) {
        return;
    }

    cancelAnimation(state);
    renderers.delete(canvas);
}

function cancelAnimation(state) {
    if (state.frameHandle) {
        cancelAnimationFrame(state.frameHandle);
        state.frameHandle = 0;
    }

    state.animation = null;
}

function step(state, timestamp) {
    const animation = state.animation;
    if (!animation) {
        return;
    }

    const previous = animation.lastTimestamp || timestamp;
    // Clamp so a backgrounded tab does not teleport the banana on return.
    // Replay playback scales time here so 2x actually looks twice as fast.
    const delta = Math.min((timestamp - previous) / 1000, 0.1) * (animation.speed || 1);
    animation.lastTimestamp = timestamp;

    const flightSeconds = (animation.data.points.length - 1) * animation.data.stepSeconds;
    const finishedFlight = animation.elapsed >= flightSeconds;

    if (!finishedFlight) {
        animation.elapsed += delta;
    } else if (animation.exploding) {
        if (!animation.blasted) {
            animation.blasted = true;
            audio.impact(animation.data.isHit, animation.speed);
        }

        animation.explosionElapsed += delta;
    }

    draw(state);

    const done = animation.elapsed >= flightSeconds &&
        (!animation.exploding || animation.explosionElapsed >= EXPLOSION_SECONDS);

    if (done) {
        const ref = animation.dotNetRef;
        cancelAnimation(state);
        draw(state);
        if (ref) {
            ref.invokeMethodAsync('OnThrowAnimationComplete');
        }
        return;
    }

    state.frameHandle = requestAnimationFrame((next) => step(state, next));
}

function toScreenY(y) {
    return VIRTUAL_HEIGHT - y;
}

function draw(state) {
    const { ctx, scene } = state;
    if (!scene) {
        return;
    }

    const theme = themeFor(scene.theme);

    ctx.fillStyle = theme.sky;
    ctx.fillRect(0, 0, VIRTUAL_WIDTH, VIRTUAL_HEIGHT);

    drawSun(ctx, theme, scene);
    drawCity(ctx, theme, scene);
    drawGorillas(ctx, theme, scene, state.animation);
    drawWind(ctx, theme, scene);
    drawBanana(ctx, theme, state.animation);
    drawExplosion(ctx, theme, state.animation);
    drawBanner(ctx, theme, scene);
}

function drawSun(ctx, theme, scene) {
    const cx = VIRTUAL_WIDTH / 2;
    const cy = 22;

    ctx.fillStyle = theme.sun;
    ctx.beginPath();
    ctx.arc(cx, cy, 10, 0, Math.PI * 2);
    ctx.fill();

    ctx.strokeStyle = theme.sun;
    ctx.lineWidth = 1;
    for (let i = 0; i < 12; i++) {
        const angle = (i / 12) * Math.PI * 2;
        ctx.beginPath();
        ctx.moveTo(cx + Math.cos(angle) * 12, cy + Math.sin(angle) * 12);
        ctx.lineTo(cx + Math.cos(angle) * 15, cy + Math.sin(angle) * 15);
        ctx.stroke();
    }

    ctx.fillStyle = theme.sunFace;
    ctx.fillRect(cx - 4, cy - 3, 2, 2);
    ctx.fillRect(cx + 2, cy - 3, 2, 2);

    if (scene.sunShocked) {
        ctx.beginPath();
        ctx.arc(cx, cy + 3, 3, 0, Math.PI * 2);
        ctx.fill();
    } else {
        ctx.beginPath();
        ctx.arc(cx, cy + 2, 4, 0.15 * Math.PI, 0.85 * Math.PI);
        ctx.strokeStyle = theme.sunFace;
        ctx.stroke();
    }
}

function drawCity(ctx, theme, scene) {
    for (const building of scene.buildings) {
        const top = toScreenY(building.h);
        ctx.fillStyle = theme.buildings[building.colorIndex % theme.buildings.length];
        ctx.fillRect(building.x, top, building.w, building.h);

        ctx.fillStyle = theme.buildingShade;
        ctx.fillRect(building.x, top, 1, building.h);
        ctx.fillRect(building.x + building.w - 1, top, 1, building.h);

        const lit = new Set(building.litWindows);
        const offsetX = building.x + ((building.w - (building.windowCols * 8)) / 2) + 2;

        for (let row = 0; row < building.windowRows; row++) {
            for (let col = 0; col < building.windowCols; col++) {
                const index = (row * building.windowCols) + col;
                ctx.fillStyle = lit.has(index) ? theme.windowLit : theme.windowDark;
                ctx.fillRect(offsetX + (col * 8), top + 4 + (row * 10), 4, 6);
            }
        }
    }

    // Craters are punched out by repainting sky over the damaged area.
    ctx.fillStyle = theme.sky;
    for (const crater of scene.craters) {
        ctx.beginPath();
        ctx.arc(crater.x, toScreenY(crater.y), crater.r, 0, Math.PI * 2);
        ctx.fill();
    }
}

function drawGorillas(ctx, theme, scene, animation) {
    for (const gorilla of scene.gorillas) {
        const throwing = animation && animation.data.slot === gorilla.slot;
        drawGorilla(ctx, theme, gorilla, throwing);

        ctx.fillStyle = gorilla.active ? theme.accent : theme.text;
        ctx.font = '6px monospace';
        ctx.textAlign = 'center';
        ctx.fillText(gorilla.name, gorilla.x, toScreenY(gorilla.y + gorilla.h) - 6);
    }
}

function drawGorilla(ctx, theme, gorilla, armsUp) {
    if (gorilla.defeated) {
        return;
    }

    const left = gorilla.x - (gorilla.w / 2);
    const bottom = toScreenY(gorilla.y);
    const top = bottom - gorilla.h;

    ctx.fillStyle = theme.gorilla;
    ctx.fillRect(left + 3, top, gorilla.w - 6, 5);
    ctx.fillRect(left + 2, top + 5, gorilla.w - 4, 6);
    ctx.fillRect(left + 3, bottom - 3, 3, 3);
    ctx.fillRect(left + gorilla.w - 6, bottom - 3, 3, 3);

    ctx.fillStyle = theme.gorillaDark;
    ctx.fillRect(left + 4, top + 1, 2, 1);
    ctx.fillRect(left + gorilla.w - 6, top + 1, 2, 1);
    ctx.fillRect(left + 5, top + 3, gorilla.w - 10, 1);

    ctx.fillStyle = theme.gorilla;
    if (armsUp) {
        ctx.fillRect(left, top - 3, 2, 8);
        ctx.fillRect(left + gorilla.w - 2, top - 3, 2, 8);
    } else {
        ctx.fillRect(left, top + 5, 2, 6);
        ctx.fillRect(left + gorilla.w - 2, top + 5, 2, 6);
    }
}

function drawWind(ctx, theme, scene) {
    if (!scene.maxWind) {
        return;
    }

    const cx = VIRTUAL_WIDTH / 2;
    const y = VIRTUAL_HEIGHT - 4;
    const length = (scene.wind / scene.maxWind) * 60;

    ctx.strokeStyle = theme.wind;
    ctx.fillStyle = theme.wind;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(cx, y);
    ctx.lineTo(cx + length, y);
    ctx.stroke();

    if (Math.abs(length) > 2) {
        const direction = Math.sign(length);
        ctx.beginPath();
        ctx.moveTo(cx + length, y);
        ctx.lineTo(cx + length - (direction * 4), y - 3);
        ctx.lineTo(cx + length - (direction * 4), y + 3);
        ctx.closePath();
        ctx.fill();
    }
}

function drawBanana(ctx, theme, animation) {
    if (!animation) {
        return;
    }

    const { points, stepSeconds } = animation.data;
    const index = Math.min(points.length - 1, Math.floor(animation.elapsed / stepSeconds));
    if (index >= points.length - 1 && animation.exploding) {
        return;
    }

    const point = points[index];
    const spin = animation.elapsed * 12;

    ctx.save();
    ctx.translate(point[0], toScreenY(point[1]));
    ctx.rotate(spin);
    ctx.fillStyle = theme.banana;
    ctx.fillRect(-3, -1, 6, 2);
    ctx.fillRect(-2, -2, 4, 1);
    ctx.restore();
}

function drawExplosion(ctx, theme, animation) {
    if (!animation || !animation.exploding || animation.explosionElapsed <= 0) {
        return;
    }

    const data = animation.data;
    const progress = Math.min(animation.explosionElapsed / EXPLOSION_SECONDS, 1);
    const maxRadius = Math.max(data.impactRadius, 10);
    const radius = maxRadius * (progress < 0.6 ? progress / 0.6 : 1);
    const x = data.impactX;
    const y = toScreenY(data.impactY);

    const rings = theme.explosion;
    for (let i = 0; i < rings.length; i++) {
        const ringRadius = radius * (1 - (i * 0.22));
        if (ringRadius <= 0) {
            continue;
        }

        ctx.fillStyle = rings[i];
        ctx.globalAlpha = progress > 0.7 ? 1 - ((progress - 0.7) / 0.3) : 1;
        ctx.beginPath();
        ctx.arc(x, y, ringRadius, 0, Math.PI * 2);
        ctx.fill();
    }

    ctx.globalAlpha = 1;
}

function drawBanner(ctx, theme, scene) {
    if (!scene.banner) {
        return;
    }

    const text = scene.banner.text;
    ctx.font = '10px monospace';
    ctx.textAlign = 'center';

    const width = ctx.measureText(text).width + 12;
    const x = VIRTUAL_WIDTH / 2;
    const y = 60;

    ctx.fillStyle = 'rgba(0, 0, 0, 0.75)';
    ctx.fillRect(x - (width / 2), y - 10, width, 16);
    ctx.strokeStyle = scene.banner.tone === 'win' ? theme.accent : theme.text;
    ctx.lineWidth = 1;
    ctx.strokeRect(x - (width / 2), y - 10, width, 16);

    ctx.fillStyle = scene.banner.tone === 'win' ? theme.accent : theme.text;
    ctx.fillText(text, x, y + 2);
}
