import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, extname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { CATEGORIES, POSTERS } from './posters.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const outputRoot = resolve(here, '../../src/Vora.Application/Assets/Posters');
const width = 1000;
const height = 1500;
const jpegQuality = 88;

const args = process.argv.slice(2);
let sheetPath = null;
const wanted = [];
for (let index = 0; index < args.length; index++) {
    if (args[index] === '--sheet') {
        sheetPath = resolve(args[++index] ?? 'posters-sheet.jpg');
    } else {
        wanted.push(args[index]);
    }
}

const contentTypes = {
    '.html': 'text/html; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.jpg': 'image/jpeg',
};

function browserPath() {
    const candidates = [
        process.env.VORA_POSTER_BROWSER,
        'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
        'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
        'C:/Program Files/Google/Chrome/Application/chrome.exe',
        '/usr/bin/microsoft-edge',
        '/usr/bin/google-chrome',
        '/usr/bin/chromium',
        '/usr/bin/chromium-browser',
        '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
        '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
    ];
    const found = candidates.find(candidate => candidate && existsSync(candidate));
    if (!found) throw new Error('No Edge or Chrome found. Set VORA_POSTER_BROWSER to a Chromium browser.');
    return found;
}

function serveFolders() {
    const roots = { '/posters/': outputRoot + sep, '/': here + sep };
    const server = createServer((request, response) => {
        const path = decodeURIComponent(new URL(request.url ?? '/', 'http://localhost').pathname);
        const prefix = Object.keys(roots).find(root => path.startsWith(root));
        const root = prefix ? roots[prefix] : null;
        const file = root ? resolve(root, '.' + path.slice(prefix.length - 1)) : null;
        if (!file || !file.startsWith(root) || !existsSync(file)) {
            response.writeHead(404).end();
            return;
        }
        response.writeHead(200, { 'Content-Type': contentTypes[extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
        response.end(readFileSync(file));
    });
    return new Promise(done => server.listen(0, '127.0.0.1', () => done(server)));
}

async function launchBrowser() {
    const profile = mkdtempSync(join(tmpdir(), 'vora-posters-'));
    const browser = spawn(browserPath(), [
        '--headless=new',
        '--remote-debugging-port=0',
        `--user-data-dir=${profile}`,
        '--hide-scrollbars',
        '--force-device-scale-factor=1',
        '--no-first-run',
        '--no-default-browser-check',
        'about:blank',
    ], { stdio: ['ignore', 'ignore', 'pipe'] });

    const endpoint = await new Promise((done, fail) => {
        let output = '';
        browser.stderr.on('data', chunk => {
            output += chunk.toString();
            const match = output.match(/DevTools listening on (ws:\/\/\S+)/);
            if (match) done(match[1]);
        });
        browser.on('exit', code => fail(new Error(`The browser exited (${code}) before it was ready.`)));
    });

    return { browser, endpoint, profile };
}

function connect(endpoint) {
    const socket = new WebSocket(endpoint);
    const pending = new Map();
    let nextId = 1;

    socket.addEventListener('message', event => {
        const message = JSON.parse(event.data);
        const waiter = pending.get(message.id);
        if (!waiter) return;
        pending.delete(message.id);
        if (message.error) waiter.fail(new Error(message.error.message));
        else waiter.done(message.result);
    });

    const send = (method, params = {}, sessionId) => new Promise((done, fail) => {
        const id = nextId++;
        pending.set(id, { done, fail });
        socket.send(JSON.stringify({ id, method, params, sessionId }));
    });

    return new Promise(done => socket.addEventListener('open', () => done({ send, close: () => socket.close() })));
}

async function openPage(cdp, pageWidth, pageHeight) {
    const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
    const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
    const send = (method, params) => cdp.send(method, params, sessionId);
    await send('Page.enable');
    await send('Emulation.setDeviceMetricsOverride', { width: pageWidth, height: pageHeight, deviceScaleFactor: 1, mobile: false });
    return send;
}

async function capture(send, url, clipWidth, clipHeight) {
    await send('Page.navigate', { url });
    const started = Date.now();
    for (;;) {
        const { result } = await send('Runtime.evaluate', { expression: 'window.__posterState ?? null', returnByValue: true });
        if (result.value === 'ready') break;
        if (typeof result.value === 'string' && result.value.startsWith('error')) throw new Error(`${url}: ${result.value}`);
        if (Date.now() - started > 30000) throw new Error(`${url} did not finish drawing.`);
        await new Promise(done => setTimeout(done, 50));
    }
    const { data } = await send('Page.captureScreenshot', {
        format: 'jpeg',
        quality: jpegQuality,
        clip: { x: 0, y: 0, width: clipWidth, height: clipHeight, scale: 1 },
    });
    return Buffer.from(data, 'base64');
}

function versionOf(bytes) {
    return createHash('sha256').update(bytes).digest('hex').slice(0, 12);
}

function writeManifest() {
    const missing = POSTERS.filter(poster => !existsSync(join(outputRoot, poster.category, `${poster.id}.jpg`)));
    if (missing.length > 0) {
        console.log(`posters.json not written: ${missing.length} poster(s) not drawn yet (${missing.map(poster => poster.id).join(', ')}).`);
        return;
    }
    const posters = POSTERS.map(poster => {
        const file = `${poster.category}/${poster.id}.jpg`;
        const path = join(outputRoot, file);
        return {
            id: poster.id,
            category: poster.category,
            title: poster.title,
            aliases: poster.aliases,
            file,
            version: versionOf(readFileSync(path)),
        };
    });
    const manifest = { categories: CATEGORIES.map(({ id, name }) => ({ id, name })), posters };
    writeFileSync(join(outputRoot, 'posters.json'), JSON.stringify(manifest, null, 2) + '\n');
}

const server = await serveFolders();
const base = `http://127.0.0.1:${server.address().port}`;
const { browser, endpoint, profile } = await launchBrowser();

try {
    const cdp = await connect(endpoint);

    if (sheetPath) {
        const ids = wanted.length > 0 ? wanted : POSTERS.map(poster => poster.id);
        const columns = Math.min(8, ids.length);
        const rows = Math.ceil(ids.length / columns);
        const tileWidth = 250;
        const sheetWidth = columns * tileWidth + (columns + 1) * 16;
        const sheetHeight = rows * tileWidth * 1.5 + (rows + 1) * 16;
        const send = await openPage(cdp, sheetWidth, sheetHeight);
        const bytes = await capture(send, `${base}/sheet.html?ids=${ids.join(',')}&columns=${columns}`, sheetWidth, sheetHeight);
        writeFileSync(sheetPath, bytes);
        console.log(`Wrote ${sheetPath}`);
    } else {
        const unknown = wanted.filter(id => !POSTERS.some(poster => poster.id === id));
        if (unknown.length > 0) throw new Error(`Unknown posters: ${unknown.join(', ')}`);

        const send = await openPage(cdp, width, height);
        const chosen = wanted.length > 0 ? POSTERS.filter(poster => wanted.includes(poster.id)) : POSTERS;
        for (const poster of chosen) {
            const bytes = await capture(send, `${base}/poster.html?id=${poster.id}`, width, height);
            const file = join(outputRoot, poster.category, `${poster.id}.jpg`);
            mkdirSync(dirname(file), { recursive: true });
            writeFileSync(file, bytes);
            console.log(`${poster.category}/${poster.id}.jpg  ${Math.round(bytes.length / 1024)} KB`);
        }
        writeManifest();
    }

    cdp.close();
} finally {
    browser.kill();
    server.close();
    await new Promise(done => setTimeout(done, 300));
    rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
