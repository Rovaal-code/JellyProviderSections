import { launch, delay } from './cdp.mjs';

export const JF = 'http://localhost:8096';
export const USER = 'admin';
export const PASS = 'TestPass123!';

export async function session({ width = 1440, height = 900 } = {}) {
  const s = await launch({ width, height });
  await s.setViewport(width, height);
  return s;
}

/** Logs in through the real login form if the session is not already authenticated. */
export async function login(s) {
  await s.navigate(`${JF}/web/index.html`, { waitMs: 4000 });

  const needsLogin = await s.evaluate(`return location.hash.includes('login');`);
  if (!needsLogin) return 'already-authenticated';

  await s.waitFor(`document.querySelector('input[type=password]')`);
  await s.evaluate(`
    const set = (el, v) => {
      // Jellyfin uses customised built-ins (<input is="emby-input">), so go
      // straight to the native setter on HTMLInputElement.
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(el, v);
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
    };
    const form = document.querySelector('input[type=password]').closest('form');
    set(form.querySelector('input[type=text]'), ${JSON.stringify(USER)});
    set(form.querySelector('input[type=password]'), ${JSON.stringify(PASS)});
    form.querySelector('button.button-submit').click();
    return true;
  `);

  await delay(6000);
  const url = await s.evaluate(`return location.href;`);
  if (url.includes('login')) throw new Error('Login failed, still on the login page: ' + url);
  return url;
}

/** Switches both the client and dashboard themes through the real settings page. */
export async function setTheme(s, theme) {
  await s.navigate(`${JF}/web/index.html#/mypreferencesdisplay.html`, { waitMs: 7000 });
  await s.waitFor(`document.getElementById('selectTheme')`);
  await s.evaluate(`
    const set = (el, v) => {
      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set.call(el, v);
      el.dispatchEvent(new Event('change', { bubbles: true }));
    };
    set(document.getElementById('selectTheme'), ${JSON.stringify(theme)});
    set(document.getElementById('selectDashboardTheme'), ${JSON.stringify(theme)});
    document.querySelector('form button[type=submit], form .button-submit').click();
    return true;
  `);
  await delay(5000);
}

/** Waits until the home rows the plugin registers have rendered. */
export async function waitForHome(s, { timeoutMs = 45000 } = {}) {
  await s.waitFor(`document.querySelectorAll('.sectionTitle, .verticalSection').length > 0`, { timeoutMs });
  await delay(4000);
}

export { delay };
