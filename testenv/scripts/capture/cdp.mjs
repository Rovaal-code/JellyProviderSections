// Minimal CDP driver: launches the Playwright-cached Chromium and talks to it
// over the DevTools protocol using Node 22's built-in WebSocket.
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { setTimeout as delay } from 'node:timers/promises';

const CHROME = process.env.JPS_CHROME
  || '/home/alvaro/.cache/ms-playwright/chromium-1228/chrome-linux64/chrome';
const PORT = 9333;
const PROFILE = '/tmp/jps-capture-profile';

export async function launch({ width = 1440, height = 900 } = {}) {
  mkdirSync(PROFILE, { recursive: true });
  const proc = spawn(CHROME, [
    `--remote-debugging-port=${PORT}`,
    `--user-data-dir=${PROFILE}`,
    '--headless=new',
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-gpu',
    '--hide-scrollbars',
    '--force-device-scale-factor=1',
    `--window-size=${width},${height}`,
    'about:blank',
  ], { stdio: 'ignore', detached: false });

  let list = null;
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${PORT}/json/list`);
      list = await r.json();
      if (list.some(t => t.type === 'page')) break;
    } catch { /* not up yet */ }
    await delay(250);
  }
  if (!list) throw new Error('Chromium did not expose the DevTools endpoint');
  const page = list.find(t => t.type === 'page');
  const session = await connect(page.webSocketDebuggerUrl);
  session.proc = proc;
  return session;
}

async function connect(url) {
  const ws = new WebSocket(url);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });

  let id = 0;
  const pending = new Map();
  const listeners = [];

  ws.onmessage = (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
      const { res, rej } = pending.get(msg.id);
      pending.delete(msg.id);
      msg.error ? rej(new Error(JSON.stringify(msg.error))) : res(msg.result);
    } else if (msg.method) {
      listeners.forEach(fn => fn(msg));
    }
  };

  const send = (method, params = {}) => new Promise((res, rej) => {
    const mid = ++id;
    pending.set(mid, { res, rej });
    ws.send(JSON.stringify({ id: mid, method, params }));
  });

  const api = {
    send,
    on: (fn) => listeners.push(fn),
    close: () => ws.close(),

    async evaluate(expression) {
      const r = await send('Runtime.evaluate', {
        expression: `(async () => { ${expression} })()`,
        awaitPromise: true,
        returnByValue: true,
      });
      if (r.exceptionDetails) {
        throw new Error(r.exceptionDetails.exception?.description || JSON.stringify(r.exceptionDetails));
      }
      return r.result.value;
    },

    async navigate(url, { waitMs = 2500 } = {}) {
      await send('Page.navigate', { url });
      await delay(waitMs);
    },

    async setViewport(width, height) {
      await send('Emulation.setDeviceMetricsOverride', {
        width, height, deviceScaleFactor: 1, mobile: width < 700,
      });
    },

    async screenshot(path, { fullPage = false, clip = null } = {}) {
      const params = { format: 'png' };
      if (fullPage) params.captureBeyondViewport = true;
      if (clip) params.clip = { ...clip, scale: clip.scale ?? 1 };
      const { data } = await send('Page.captureScreenshot', params);
      writeFileSync(path, Buffer.from(data, 'base64'));
      return path;
    },

    async waitFor(expression, { timeoutMs = 20000, everyMs = 300 } = {}) {
      const deadline = Date.now() + timeoutMs;
      while (Date.now() < deadline) {
        try {
          if (await api.evaluate(`return !!(${expression});`)) return true;
        } catch { /* page mid-navigation */ }
        await delay(everyMs);
      }
      throw new Error(`Timed out waiting for: ${expression}`);
    },
  };

  await send('Page.enable');
  await send('Runtime.enable');
  await send('Network.enable');
  return api;
}

export { delay };
