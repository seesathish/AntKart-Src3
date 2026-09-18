'use strict';
/*
 * build.js — produces the two print-ready Architect's Playbook PDFs.
 *
 *   node build.js                 # build both books
 *   node build.js --shots         # build + write inspection PNGs to ./_inspect/
 *
 * Route: markdown-it (MD→HTML) → mermaid (diagrams→SVG, in-browser) → Paged.js (CSS Paged
 * Media pagination) → headless Chrome (Chrome/Edge) prints to PDF. No LaTeX/pandoc needed.
 */

const fs = require('fs');
const path = require('path');
const MarkdownIt = require('markdown-it');
const puppeteer = require('puppeteer-core');

const ROOT = path.resolve(__dirname, '..', '..');
const SRC = path.join(ROOT, 'docs', 'ARCHITECT-PLAYBOOK.md');
const OUT_DIR = path.join(ROOT, 'build', 'playbook-pdf');
const INSPECT_DIR = path.join(__dirname, '_inspect');
const CSS = fs.readFileSync(path.join(__dirname, 'template.css'), 'utf8');
const MERMAID_JS = fs.readFileSync(path.join(__dirname, 'node_modules', 'mermaid', 'dist', 'mermaid.min.js'), 'utf8');
const PAGEDJS_JS = fs.readFileSync(path.join(__dirname, 'node_modules', 'pagedjs', 'dist', 'paged.polyfill.js'), 'utf8');
const SHOTS = process.argv.includes('--shots');

const CHROME = [
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
].find(p => fs.existsSync(p));
if (!CHROME) { console.error('No Chrome/Edge found.'); process.exit(1); }

const BOOKS = [
  {
    id: 1, out: 'Book1-Platform-and-Infrastructure.pdf', title: 'Platform and Infrastructure',
    sections: [1, 2, 3, 4, 5],
    names: ['Platform — architecture and patterns', 'Infrastructure as code', 'Azure services', 'Kubernetes', 'Security and identity'],
    other: 'Book 2 — Delivery and Practice covers Observability, GitOps, DevOps, and Architecture practice (Sections 6–9).',
  },
  {
    id: 2, out: 'Book2-Delivery-and-Practice.pdf', title: 'Delivery and Practice',
    sections: [6, 7, 8, 9],
    names: ['Observability', 'GitOps', 'DevOps', 'Architecture practice'],
    other: 'Book 1 — Platform and Infrastructure covers Platform, Infrastructure as code, Azure services, Kubernetes, and Security and identity (Sections 1–5).',
  },
];

const escapeHtml = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
function slugify(s) {
  return String(s).toLowerCase()
    .replace(/[\u{1F000}-\u{1FAFF}\u{2600}-\u{27BF}\u2190-\u21FF\u2B00-\u2BFF]/gu, '') // emoji/symbols
    .replace(/&[a-z]+;/g, '')
    .replace(/[^\w\s-]/g, '')
    .trim().replace(/\s+/g, '-').replace(/-+/g, '-') || 'x';
}
function uniqueSlug(text, map) {
  // prefix so ids never start with a digit (invalid in querySelector, which Paged.js uses)
  let base = 'pb-' + slugify(text), s = base, i = 2;
  while (map.has(s)) { s = `${base}-${i}`; i++; }
  map.set(s, true); return s;
}
// strip trailing status emoji + escape, for TOC/title display
function cleanText(t) {
  return escapeHtml(t.replace(/\s*[\u{1F7E1}\u{1F535}\u{1F7E2}]\s*$/u, '').trim());
}

function makeMd() {
  const md = new MarkdownIt({ html: true, linkify: true, typographer: false });
  const defFence = md.renderer.rules.fence.bind(md.renderer.rules);
  md.renderer.rules.fence = (tokens, idx, opts, env, self) => {
    const t = tokens[idx];
    if ((t.info || '').trim() === 'mermaid')
      return `<figure class="diagram"><pre class="mermaid">${escapeHtml(t.content)}</pre></figure>\n`;
    return defFence(tokens, idx, opts, env, self);
  };
  md.renderer.rules.heading_open = (tokens, idx, opts, env, self) => {
    const tag = tokens[idx].tag;
    const text = tokens[idx + 1] && tokens[idx + 1].type === 'inline' ? tokens[idx + 1].content : '';
    const slug = uniqueSlug(text, env.slugs);
    tokens[idx].attrSet('id', slug);
    env.headings.push({ tag, text, slug });
    return self.renderToken(tokens, idx, opts);
  };
  return md;
}

function splitSource() {
  const lines = fs.readFileSync(SRC, 'utf8').split(/\r?\n/);
  // locate section header lines, skipping code fences
  const secLine = {};
  let inFence = false;
  for (let i = 0; i < lines.length; i++) {
    if (/^```/.test(lines[i])) inFence = !inFence;
    if (inFence) continue;
    const m = lines[i].match(/^#\s+(\d+)\.\s/);
    if (m) secLine[+m[1]] = i;
  }
  const frontMatter = lines.slice(0, secLine[1]).join('\n');
  const sectionBlock = n => {
    const start = secLine[n];
    const next = secLine[n + 1] !== undefined ? secLine[n + 1] : lines.length;
    return lines.slice(start, next).join('\n');
  };
  return { frontMatter, sectionBlock };
}

function buildFrontMatterHtml(book, srcFront) {
  // Title page
  const secList = book.sections.map((n, i) => `${n} &middot; ${escapeHtml(book.names[i])}`).join('<br>');
  const titlePage = `<div class="titlepage">
    <div class="kicker">The Architect's Playbook</div>
    <div class="book-title">${escapeHtml(book.title)}</div>
    <div class="book-sub">Book ${book.id} of 2</div>
    <div class="sections">${secList}</div>
    <div class="foot">AntKart &middot; a printable desk reference &middot; A4, double-sided, spiral-bound</div>
  </div>`;

  // Render source front matter (drop its top H1 title; keep intro + how-to + status + progress + study order)
  const md = makeMd();
  const frontMd = srcFront.replace(/^#\s+The Architect's Playbook.*$/m, '');
  const fmHtml = md.render(frontMd, { slugs: new Map(), headings: [] });

  return { titlePage, fmHtml };
}

function buildBookHtml(book, srcFront, sectionBlock) {
  const { titlePage, fmHtml } = buildFrontMatterHtml(book, srcFront);

  // Render this book's body (all its sections) in one pass for stable ids + TOC order.
  const md = makeMd();
  const env = { slugs: new Map(), headings: [] };
  const bodyMd = book.sections.map(sectionBlock).join('\n\n');
  const bodyHtml = md.render(bodyMd, env);

  // Wrap each section: a title page (Section N + H1) then the body.
  const chunks = bodyHtml.split(/(?=<h1\b)/).filter(c => c.trim());
  const sectionsHtml = chunks.map((chunk, i) => {
    const m = chunk.match(/^(<h1\b[^>]*>)([\s\S]*?)(<\/h1>)([\s\S]*)$/);
    if (!m) return chunk;
    const [, h1open, h1text, h1close, rest] = m;
    const num = (h1text.match(/^\s*(\d+)\./) || [])[1] || '';
    const first = i === 0 ? ' first' : '';   // first section resets the arabic page counter
    return `<section class="section">
  <div class="section-title${first}"><div class="snum">Section ${num}</div>${h1open}${h1text}${h1close}</div>
  <div class="section-body">${rest}</div>
</section>`;
  }).join('\n');

  // TOC from collected headings (sections = h1, concepts = h3).
  let toc = `<nav class="toc"><h1>Contents</h1><p class="otherbook">${escapeHtml(book.other)}</p><ul>`;
  for (const h of env.headings) {
    if (h.tag === 'h1')
      toc += `<li class="sec"><a href="#${h.slug}"><span class="t">${cleanText(h.text)}</span><span class="leader"></span></a></li>`;
    else if (h.tag === 'h3')
      toc += `<li class="con"><a href="#${h.slug}"><span class="t">${cleanText(h.text)}</span><span class="leader"></span></a></li>`;
  }
  toc += `</ul></nav>`;

  const css = CSS.replace(/__BOOK_TITLE__/g, book.title.replace(/"/g, '\\"'));

  return `<!DOCTYPE html><html><head><meta charset="utf-8"><title>${escapeHtml(book.title)}</title>
<style>${css}</style></head><body>
<div class="frontmatter">
${titlePage}
<div class="fmbody">${fmHtml}</div>
${toc}
</div>
<div class="mainmatter">
${sectionsHtml}
</div>
<script>window.PagedConfig = { auto: false };</script>
<script>${MERMAID_JS}</script>
<script>${PAGEDJS_JS}</script>
<script>
(async () => {
  try {
    mermaid.initialize({ startOnLoad:false, theme:'neutral', securityLevel:'loose',
      flowchart:{ htmlLabels:true, useMaxWidth:true, padding:8 }, themeVariables:{ fontSize:'16px' } });
    const nodes = Array.from(document.querySelectorAll('pre.mermaid'));
    let ok=0, fail=0; const failed=[];
    for (const n of nodes) {
      try { await mermaid.run({ nodes:[n] }); ok++; }
      catch(e){ fail++; failed.push((n.textContent||'').slice(0,60)); n.innerHTML='<div style="color:#b00;font-size:9pt">[diagram render error]</div>'; }
    }
    window.__mermaid = { total: nodes.length, ok, fail, failed };
    const result = await window.PagedPolyfill.preview();
    // Strip furniture from any empty page (recto-forcing leaves blanks that Paged.js does not
    // always classify as :blank), so no header or stray page number appears on a blank leaf.
    let emptied = 0;
    document.querySelectorAll('.pagedjs_page').forEach(p => {
      const c = p.querySelector('.pagedjs_page_content');
      if (!c) return;
      // structural wrappers (section/div/nav) can straddle a forced break as empty fragments;
      // only real content elements count, so a recto-forcing blank is still detected as empty.
      const hasBlock = c.querySelector('h1,h2,h3,h4,h5,h6,p,ul,ol,li,table,pre,figure,svg,img,blockquote,code');
      if (!hasBlock && c.textContent.trim() === '') { p.classList.add('ak-empty'); emptied++; }
    });
    window.__emptied = emptied;
    // Footer page numbers: Paged.js's counter(page) in the margin box does not honour the
    // main-matter counter-reset the way target-counter (the TOC) does, so the two disagree.
    // Re-label the main-matter footers to match the TOC: arabic restarting at the main-matter
    // start, blank on section-title pages and inserted blanks. Front matter (roman) is left as is.
    // Paged.js renders counter(page) as a pseudo-element and does not reset it consistently.
    // The CSS kills that pseudo; here we set every footer number ourselves: roman for the
    // front matter, arabic restarting at the main matter, blank on title/section/empty pages.
    const allPages = Array.from(document.querySelectorAll('.pagedjs_page'));
    const firstMain = allPages.findIndex(p => p.querySelector('.mainmatter, .section'));
    const roman = n => { const t = [[1000, 'm'], [900, 'cm'], [500, 'd'], [400, 'cd'], [100, 'c'], [90, 'xc'], [50, 'l'], [40, 'xl'], [10, 'x'], [9, 'ix'], [5, 'v'], [4, 'iv'], [1, 'i']]; let s = ''; for (const [v, sym] of t) { while (n >= v) { s += sym; n -= v; } } return s; };
    let romanN = 0, arabicN = 0, relabelled = 0;
    allPages.forEach((p, i) => {
      const empty = p.classList.contains('ak-empty');
      const isTitle = !!p.querySelector('.titlepage');
      const isSecTitle = !!p.querySelector('.section-title');
      let label;
      if (firstMain < 0 || i < firstMain) { romanN++; label = (empty || isTitle) ? '' : roman(romanN); }
      else { arabicN++; label = (empty || isSecTitle) ? '' : String(arabicN); }
      const box = p.querySelector('.pagedjs_margin-bottom-center .pagedjs_margin-content');
      if (box) { box.textContent = label; relabelled++; }
    });
    window.__relabelled = relabelled;
    window.__pages = result && result.total ? result.total : document.querySelectorAll('.pagedjs_page').length;
    window.__renderDone = true;
  } catch (e) { window.__error = String(e && e.stack || e); window.__renderDone = true; }
})();
</script>
</body></html>`;
}

async function inspect(page, book) {
  fs.mkdirSync(INSPECT_DIR, { recursive: true });
  // front matter pages 1..6 + TOC, and the busiest-diagram page
  const targets = await page.evaluate(() => {
    const pages = Array.from(document.querySelectorAll('.pagedjs_page'));
    const idxOf = el => { const p = el.closest('.pagedjs_page'); return pages.indexOf(p); };
    // busiest diagram = svg with most descendants
    let best = null, bestN = -1;
    document.querySelectorAll('.pagedjs_page svg').forEach(svg => {
      const n = svg.querySelectorAll('*').length;
      if (n > bestN) { bestN = n; best = svg; }
    });
    const out = { total: pages.length, busy: best ? idxOf(best) : -1, busyN: bestN };
    return out;
  });
  // locate the TOC page and measure mirrored margins on one odd + one even content page
  const extra = await page.evaluate(() => {
    const pages = Array.from(document.querySelectorAll('.pagedjs_page'));
    const idxOf = el => pages.indexOf(el.closest('.pagedjs_page'));
    const tocEl = document.querySelector('nav.toc');
    const tocPage = tocEl ? idxOf(tocEl) : -1;
    const measure = i => {
      const p = pages[i]; if (!p) return null;
      const area = p.querySelector('.pagedjs_area') || p.querySelector('.pagedjs_page_content');
      if (!area) return null;
      const pr = p.getBoundingClientRect(), ar = area.getBoundingClientRect();
      const mm = px => +(px / (96 / 25.4)).toFixed(1);
      return { left: mm(ar.left - pr.left), right: mm(pr.right - ar.right) };
    };
    // pick a mid-book odd and even page (past front matter)
    const oddIdx = 60, evenIdx = 61;
    return { tocPage, oddIdx, evenIdx, oddMargins: measure(oddIdx), evenMargins: measure(evenIdx),
             oddIsRight: pages[oddIdx] && pages[oddIdx].classList.contains('pagedjs_right_page'),
             evenIsRight: pages[evenIdx] && pages[evenIdx].classList.contains('pagedjs_right_page') };
  });
  console.log(`     margins: page${extra.oddIdx + 1} L=${extra.oddMargins && extra.oddMargins.left} R=${extra.oddMargins && extra.oddMargins.right} (right-page=${extra.oddIsRight}); page${extra.evenIdx + 1} L=${extra.evenMargins && extra.evenMargins.left} R=${extra.evenMargins && extra.evenMargins.right} (right-page=${extra.evenIsRight})`);

  const secInfo = await page.evaluate(() => {
    const pages = Array.from(document.querySelectorAll('.pagedjs_page'));
    return Array.from(document.querySelectorAll('.section-title')).map(st => {
      const p = st.closest('.pagedjs_page');
      return { idx: pages.indexOf(p) + 1, right: p.classList.contains('pagedjs_right_page'), title: (st.querySelector('h1') || {}).textContent };
    });
  });
  console.log('     section title pages: ' + secInfo.map(s => `p${s.idx}${s.right ? 'R' : 'L'}`).join(' '));
  const allRight = secInfo.every(s => s.right);
  console.log('     all sections on RIGHT-hand pages: ' + allRight);
  const blanks = await page.evaluate(() => Array.from(document.querySelectorAll('.pagedjs_page')).map((p, i) => p.classList.contains('pagedjs_blank_page') ? i + 1 : null).filter(Boolean));
  console.log('     blank pages: ' + JSON.stringify(blanks) + '; page-before-§1 class: ' + await page.evaluate(i => { const p = document.querySelectorAll('.pagedjs_page')[i]; return p ? p.className.replace(/pagedjs_/g, '') : ''; }, (secInfo[0] ? secInfo[0].idx - 2 : 7)));

  const want = new Set([0, 1, 2, 3, 4, 5]);
  if (targets.busy >= 0) want.add(targets.busy);
  if (extra.tocPage >= 0) { want.add(extra.tocPage); want.add(extra.tocPage + 1); }
  want.add(extra.oddIdx); want.add(extra.evenIdx);
  if (secInfo[0]) { want.add(secInfo[0].idx - 1); want.add(secInfo[0].idx - 2); want.add(secInfo[0].idx); }
  for (const i of [...want].filter(i => i >= 0 && i < targets.total)) {
    const clip = await page.evaluate((i) => {
      const p = document.querySelectorAll('.pagedjs_page')[i];
      p.scrollIntoView();
      const r = p.getBoundingClientRect();
      return { x: r.x + window.scrollX, y: r.y + window.scrollY, width: r.width, height: r.height };
    }, i);
    await page.screenshot({ path: path.join(INSPECT_DIR, `book${book.id}-page${String(i + 1).padStart(3, '0')}.png`), clip, captureBeyondViewport: true });
  }
  return targets;
}

async function buildBook(browser, book, srcFront, sectionBlock) {
  const html = buildBookHtml(book, srcFront, sectionBlock);
  const htmlPath = path.join(OUT_DIR, `_${book.out}.html`);
  fs.writeFileSync(htmlPath, html, 'utf8');

  const page = await browser.newPage();
  await page.setViewport({ width: 900, height: 1400, deviceScaleFactor: SHOTS ? 2 : 1 });
  page.on('console', m => { if (m.type() === 'error') console.log(`  [console.error] ${m.text()}`.slice(0, 200)); });
  await page.goto('file:///' + htmlPath.replace(/\\/g, '/'), { waitUntil: 'load', timeout: 120000 });
  await page.waitForFunction('window.__renderDone === true', { timeout: 900000 });

  const stats = await page.evaluate(() => ({ m: window.__mermaid, pages: window.__pages, error: window.__error }));
  if (stats.error) console.log(`  !! page error: ${stats.error}`);
  console.log(`  Book ${book.id}: ${stats.pages} pages, diagrams ${stats.m ? stats.m.ok + '/' + stats.m.total + ' ok, ' + stats.m.fail + ' failed' : 'n/a'}`);
  if (stats.m && stats.m.failed && stats.m.failed.length) console.log(`     failed: ${JSON.stringify(stats.m.failed)}`);

  let insp = null;
  if (SHOTS) { insp = await inspect(page, book); console.log(`     busiest diagram on page ${insp.busy + 1} (${insp.busyN} svg nodes)`); }

  const pdfPath = path.join(OUT_DIR, book.out);
  await page.pdf({ path: pdfPath, printBackground: true, preferCSSPageSize: true,
    margin: { top: 0, right: 0, bottom: 0, left: 0 }, displayHeaderFooter: false, timeout: 0 });
  const sizeMB = (fs.statSync(pdfPath).size / 1048576).toFixed(2);
  console.log(`     wrote ${book.out} (${sizeMB} MB)`);
  fs.unlinkSync(htmlPath);
  await page.close();
  return { pages: stats.pages, mermaid: stats.m };
}

(async () => {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const { frontMatter, sectionBlock } = splitSource();
  const browser = await puppeteer.launch({ executablePath: CHROME, headless: 'new', args: ['--no-sandbox', '--font-render-hinting=none'] });
  const summary = [];
  for (const book of BOOKS) {
    console.log(`Building Book ${book.id} — ${book.title} ...`);
    summary.push({ book, ...(await buildBook(browser, book, frontMatter, sectionBlock)) });
  }
  await browser.close();
  console.log('\n=== SUMMARY ===');
  for (const s of summary)
    console.log(`Book ${s.book.id} (${s.book.title}): ${s.pages} pages; diagrams ${s.mermaid ? s.mermaid.ok + '/' + s.mermaid.total : '?'} rendered.`);
  console.log(`Output: ${OUT_DIR}`);
})().catch(e => { console.error(e); process.exit(1); });
