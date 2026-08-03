// Home screen evidence: criteria 15, 17, 18, 19, 22, 40, 41, 42.
import { session, login, setTheme, JF, delay } from './jf.mjs';

const OUT = new URL('../../evidence/screenshots/', import.meta.url).pathname;
const theme = process.argv[2] ?? 'dark';
const suffix = theme === 'light' ? 'claro' : 'oscuro';

const s = await session({ width: 1440, height: 1000 });
await login(s);
await setTheme(s, theme);

async function openHome(width, height) {
  await s.setViewport(width, height);
  await s.navigate(`${JF}/web/index.html#/home.html`, { waitMs: 8000 });
  // The rows resolve their TMDb results against the library, so they land
  // several seconds after the rest of the home.
  await s.waitFor(
    `[...document.querySelectorAll('.verticalSection')]
       .filter(v => v.querySelector('.jps-section-title') && v.querySelectorAll('.card').length).length >= 3`,
    { timeoutMs: 90000 });
  await delay(4000);
}

async function scrollToFirstPluginRow(offset = 90) {
  await s.evaluate(`
    const row = [...document.querySelectorAll('.verticalSection')].find(v => v.querySelector('.jps-section-title'));
    row.scrollIntoView({ block: 'start' });
    window.scrollBy(0, -${offset});
    return true;
  `);
  await delay(2500);
}

await openHome(1440, 1000);
await scrollToFirstPluginRow();
await s.screenshot(`${OUT}/01-home-${suffix}-1440.png`);

// Tight crop on each section title: the logo must sit left of the text,
// vertically centred, without pushing the row controls around.
const providers = ['Crunchyroll', 'Netflix', 'Prime Video'];
for (const [i, name] of providers.entries()) {
  // HSS paginates the home, so the same section exists more than once in the
  // DOM. Only the laid-out copy can be cropped.
  const selector = `[...document.querySelectorAll('.jps-section-title')]
      .filter(e => /${name}/.test(e.textContent))
      .find(e => e.getBoundingClientRect().width > 0)`;

  await s.evaluate(`
    const t = ${selector};
    if (t) (t.closest('.sectionTitleContainer') || t.parentElement).scrollIntoView({ block: 'center' });
    return true;
  `);
  // Rows below finish loading while we scroll and push the layout around, so
  // the rect has to be read once everything has settled, not before.
  await delay(2500);

  // Rows keep lazy-loading images and shifting the layout, so only crop once
  // two consecutive measurements agree.
  const measure = () => s.evaluate(`
    const t = ${selector};
    if (!t) return null;
    const head = t.closest('.sectionTitleContainer') || t.parentElement;
    const r = head.getBoundingClientRect();
    if (r.width < 20 || r.height < 10 || r.y < 0) return null;
    // Page.captureScreenshot clips in document coordinates, not viewport ones.
    return {
      x: Math.max(0, r.x + window.scrollX - 10),
      y: Math.max(0, r.y + window.scrollY - 14),
      width: Math.min(760, r.width),
      height: r.height + 28,
    };
  `);

  let box = null;
  for (let attempt = 0; attempt < 6; attempt++) {
    const a = await measure();
    await delay(700);
    const b = await measure();
    if (a && b && a.y === b.y && a.height === b.height) { box = b; break; }
  }
  console.log(name, "recorte:", JSON.stringify(box));
  if (box) {
    const slug = name.toLowerCase().replace(/ /g, '');
    await s.screenshot(`${OUT}/02-titulo-logo-${suffix}-${i + 1}-${slug}.png`, { clip: box });
  }
}

// Local library item next to external TMDb cards, inside the anime row.
const local = await s.evaluate(`
  const row = [...document.querySelectorAll('.verticalSection')]
    .filter(v => /Crunchyroll/.test(v.querySelector('.jps-section-title')?.textContent ?? ''))
    .find(v => v.getBoundingClientRect().width > 0);
  const cards = [...row.querySelectorAll('.card')];
  // Synthetic ids carry the ascii marker 'JPS', which the GUID string form
  // renders as "504a0053"; anything without it came from the local library.
  const localCards = cards.filter(c => !(c.dataset.id || '').includes('504a0053'));
  const target = localCards[0] ?? null;
  if (target) {
    target.scrollIntoView({ block: 'center', inline: 'center' });
    await new Promise(r => setTimeout(r, 1500));
  }
  return {
    rowCards: cards.length,
    localCards: localCards.length,
    localNames: localCards.map(c => c.querySelector('.cardText')?.textContent.trim()).slice(0, 5),
  };
`);
console.log('fila Crunchyroll:', JSON.stringify(local));
await delay(1500);
await s.screenshot(`${OUT}/04-fila-local-vs-externo-${suffix}.png`);

if (theme === 'dark') {
  for (const [w, h, label] of [[1024, 900, 'tablet-1024'], [390, 844, 'movil-390']]) {
    await openHome(w, h);
    await scrollToFirstPluginRow(70);
    await s.screenshot(`${OUT}/03-home-${label}.png`);

    // Criterion 42 is about the page overflowing sideways. The rows are
    // horizontal scrollers by design, so the page itself is what must not move.
    const overflow = await s.evaluate(`
      return { bodyScrollWidth: document.body.scrollWidth, clientWidth: document.documentElement.clientWidth };
    `);
    console.log(label, JSON.stringify(overflow));
  }
}

s.close();
process.exit(0);
