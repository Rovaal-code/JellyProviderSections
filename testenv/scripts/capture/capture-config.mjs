// Admin configuration page evidence: criteria 4, 12, 13, 14, 35, 36, 37, 38, 40, 41, 42.
import { session, login, setTheme, JF, delay } from './jf.mjs';

const OUT = new URL('../../evidence/screenshots/', import.meta.url).pathname;
const theme = process.argv[2] ?? 'dark';
const suffix = theme === 'light' ? 'claro' : 'oscuro';
const CONFIG = `${JF}/web/index.html#/configurationpage?name=jellyprovidersections`;

const s = await session({ width: 1440, height: 1200 });
await login(s);
await setTheme(s, theme);

async function openConfig(width, height) {
  await s.setViewport(width, height);
  await s.navigate(CONFIG, { waitMs: 6000 });
  await s.waitFor(`document.querySelectorAll('.jps-card-head').length >= 3`, { timeoutMs: 30000 });
  await delay(2500);
}

const collapseAll = () => s.evaluate(`document.getElementById('jps-collapse-all').click(); return true;`);

await openConfig(1440, 1200);

// 36: every closed card the same height, same column positions.
await collapseAll();
await delay(1200);
await s.screenshot(`${OUT}/05-config-tarjetas-cerradas-${suffix}.png`);

const geometry = await s.evaluate(`
  const heads = [...document.querySelectorAll('.jps-card-head')];
  return heads.map(h => {
    const r = h.getBoundingClientRect();
    return { expanded: h.getAttribute('aria-expanded'), x: Math.round(r.x), width: Math.round(r.width), height: Math.round(r.height) };
  });
`);
console.log('tarjetas cerradas:', JSON.stringify(geometry));

// 37/38: two cards expanded at once, same field order in both.
await s.evaluate(`
  const heads = [...document.querySelectorAll('.jps-card-head')];
  heads[0].click();
  heads[1].click();
  return true;
`);
await delay(1800);
await s.screenshot(`${OUT}/06-config-tarjetas-expandidas-${suffix}.png`);

const expanded = await s.evaluate(`
  const heads = [...document.querySelectorAll('.jps-card-head')];
  const widths = heads.map(h => Math.round(h.getBoundingClientRect().width));
  const bodies = [...document.querySelectorAll('.jps-card-head[aria-expanded="true"]')].map(h => {
    const card = h.closest('.jps-section-card');
    return [...card.querySelectorAll('.jps-subhead-label, .jps-field-label')].map(e => e.textContent.trim());
  });
  return {
    expandedFlags: heads.map(h => h.getAttribute('aria-expanded')),
    widthsIdentical: new Set(widths).size === 1,
    widths,
    fieldOrder: bodies,
  };
`);
console.log('tarjetas expandidas:', JSON.stringify(expanded, null, 2).slice(0, 1200));

// 12/13/14: region and provider selectors, provider logos in the list.
const selectors = await s.evaluate(`
  document.getElementById('jps-new-section').click();
  await new Promise(r => setTimeout(r, 1500));
  const region = document.getElementById('jps-f-region');
  const search = document.getElementById('jps-f-providersearch');
  search.focus();
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(search, 'net');
  search.dispatchEvent(new Event('input', { bubbles: true }));
  await new Promise(r => setTimeout(r, 2000));
  const list = document.getElementById('jps-provider-list');
  return {
    regionOptions: region.options.length,
    regionValue: region.value,
    providerOptions: list ? list.children.length : 0,
    providerLogos: list ? list.querySelectorAll('img').length : 0,
  };
`);
console.log('selectores:', JSON.stringify(selectors));
await delay(1500);
await s.screenshot(`${OUT}/07-config-selector-proveedor-${suffix}.png`);

// 4: diagnostics tab, dependency detection.
await s.navigate(CONFIG, { waitMs: 5000 });
await s.evaluate(`
  [...document.querySelectorAll('.jps-tab-btn')].find(b => b.dataset.tab === 'diagnostics').click();
  return true;
`);
await delay(4000);
await s.screenshot(`${OUT}/08-config-diagnostico-${suffix}.png`);

// Connections tab, showing secrets are never sent back to the browser.
await s.evaluate(`
  [...document.querySelectorAll('.jps-tab-btn')].find(b => b.dataset.tab === 'connections').click();
  return true;
`);
await delay(3000);
await s.screenshot(`${OUT}/09-config-conexiones-${suffix}.png`);

// 42: the admin page on a phone.
if (theme === 'dark') {
  await openConfig(390, 844);
  await collapseAll();
  await delay(1200);
  await s.screenshot(`${OUT}/10-config-movil-390.png`);
  const overflow = await s.evaluate(`
    return { bodyScrollWidth: document.body.scrollWidth, clientWidth: document.documentElement.clientWidth };
  `);
  console.log('config movil:', JSON.stringify(overflow));
}

s.close();
process.exit(0);
