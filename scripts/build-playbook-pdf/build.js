'use strict';
/*
 * build.js — produces ONE print-ready A4 PDF of the Architect's Playbook.
 *
 *   node build.js                 # build the PDF
 *   node build.js --shots         # build + write inspection PNGs to ./_inspect/
 *
 * Route: markdown-it (MD→HTML) → mermaid (diagrams→SVG, in-browser) → Paged.js (CSS Paged
 * Media pagination) → headless Chrome (Chrome/Edge) prints to PDF. No LaTeX/pandoc needed.
 *
 * Single volume, standard (non-mirrored) 18mm margins, one TOC, sections start on a fresh
 * page with no dedicated title page and no forced blanks, diagrams sized to their natural
 * (readable) size capped to the page width.
 */

const fs = require('node:fs');
const path = require('node:path');
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
const OUT_FILE = 'ARCHITECT-PLAYBOOK.pdf';
const SECTIONS = [1, 2, 3, 4, 5, 6, 7, 8, 9];

const CHROME = [
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
].find(p => fs.existsSync(p));
if (!CHROME) { console.error('No Chrome/Edge found.'); process.exit(1); }

const escapeHtml = s => s.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
function slugify(s) {
  return String(s).toLowerCase()
    .replace(/[\u{1F000}-\u{1FAFF}\u{2600}-\u{27BF}\u2190-\u21FF\u2B00-\u2BFF]/gu, '')
    .replace(/&[a-z]+;/g, '')
    .replace(/[^\w\s-]/g, '')
    .trim().replaceAll(/\s+/g, '-').replaceAll(/-+/g, '-') || 'x';
}
function uniqueSlug(text, map) {
  let base = 'pb-' + slugify(text), s = base, i = 2;         // never start an id with a digit
  while (map.has(s)) { s = `${base}-${i}`; i++; }
  map.set(s, true); return s;
}
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
    const text = tokens[idx + 1]?.type === 'inline' ? tokens[idx + 1].content : '';
    const slug = uniqueSlug(text, env.slugs);
    tokens[idx].attrSet('id', slug);
    env.headings.push({ tag, text, slug });
    return self.renderToken(tokens, idx, opts);
  };
  return md;
}

function splitSource() {
  const lines = fs.readFileSync(SRC, 'utf8').split(/\r?\n/);
  const secLine = {};
  let inFence = false;
  for (let i = 0; i < lines.length; i++) {
    if (/^```/.test(lines[i])) inFence = !inFence;
    if (inFence) continue;
    const m = /^#\s+(\d+)\.\s/.exec(lines[i]);
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

function buildHtml(frontMatter, sectionBlock) {
  // Front matter: title page + rendered source front matter (minus its top H1) + generated TOC.
  const titlePage = `<div class="titlepage">
    <div class="kicker">AntKart</div>
    <div class="book-title">The Architect&rsquo;s Playbook</div>
    <div class="book-sub">127 concepts across nine sections</div>
    <div class="foot">A printable desk reference &middot; A4 &middot; single volume</div>
  </div>`;
  const fmMd = makeMd();
  const fmHtml = fmMd.render(frontMatter.replace(/^#\s+The Architect's Playbook.*$/m, ''), { slugs: new Map(), headings: [] });

  // Body: all nine sections, each a <section> with its H1 at the top (no dedicated title page).
  const md = makeMd();
  const env = { slugs: new Map(), headings: [] };
  const bodyHtml = md.render(SECTIONS.map(sectionBlock).join('\n\n'), env);
  const chunks = bodyHtml.split(/(?=<h1\b)/).filter(c => c.trim());
  const sectionsHtml = chunks.map(c => `<section class="section">\n${c}\n</section>`).join('\n');

  // One TOC of every section (h1) and concept (h3), page numbers via target-counter.
  let toc = `<nav class="toc"><h1>Contents</h1><ul>`;
  for (const h of env.headings) {
    if (h.tag === 'h1') toc += `<li class="sec"><a href="#${h.slug}"><span class="t">${cleanText(h.text)}</span><span class="leader"></span></a></li>`;
    else if (h.tag === 'h3') toc += `<li class="con"><a href="#${h.slug}"><span class="t">${cleanText(h.text)}</span><span class="leader"></span></a></li>`;
  }
  toc += `</ul></nav>`;

  return `<!DOCTYPE html><html><head><meta charset="utf-8"><title>The Architect's Playbook</title>
<style>${CSS}</style></head><body>
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
    // Strip furniture from any empty page Paged.js may leave.
    document.querySelectorAll('.pagedjs_page').forEach(p => {
      const c = p.querySelector('.pagedjs_page_content');
      if (!c) return;
      const hasBlock = c.querySelector('h1,h2,h3,h4,h5,h6,p,ul,ol,li,table,pre,figure,svg,img,blockquote,code');
      if (!hasBlock && c.textContent.trim() === '') p.classList.add('ak-empty');
    });
    // Footer page numbers: roman for the front matter, arabic restarting at the main matter.
    const allPages = Array.from(document.querySelectorAll('.pagedjs_page'));
    const firstMain = allPages.findIndex(p => p.querySelector('.mainmatter, .section'));
    const roman = n => { const t=[[1000,'m'],[900,'cm'],[500,'d'],[400,'cd'],[100,'c'],[90,'xc'],[50,'l'],[40,'xl'],[10,'x'],[9,'ix'],[5,'v'],[4,'iv'],[1,'i']]; let s=''; for(const [v,sym] of t){ while(n>=v){ s+=sym; n-=v; } } return s; };
    let romanN=0, arabicN=0;
    allPages.forEach((p, i) => {
      const empty = p.classList.contains('ak-empty');
      const isTitle = !!p.querySelector('.titlepage');
      let label;
      if (firstMain < 0 || i < firstMain) { romanN++; label = (empty || isTitle) ? '' : roman(romanN); }
      else { arabicN++; label = empty ? '' : String(arabicN); }
      const box = p.querySelector('.pagedjs_margin-bottom-center .pagedjs_margin-content');
      if (box) box.textContent = label;
    });
    window.__pages = (result && result.total) ? result.total : allPages.length;
    window.__renderDone = true;
  } catch (e) { window.__error = String(e && e.stack || e); window.__renderDone = true; }
})();
</script>
</body></html>`;
}

async function inspect(page) {
  fs.mkdirSync(INSPECT_DIR, { recursive: true });
  const info = await page.evaluate(() => {
    const pages = Array.from(document.querySelectorAll('.pagedjs_page'));
    const idxOf = el => pages.indexOf(el.closest('.pagedjs_page'));
    // diagram pages by svg node count: smallest, median, largest
    const svgs = Array.from(document.querySelectorAll('.pagedjs_page svg'))
      .map(s => ({ i: idxOf(s), n: s.querySelectorAll('*').length })).sort((a, b) => a.n - b.n);
    const sample = svgs.length ? [svgs[0], svgs[Math.floor(svgs.length / 2)], svgs[svgs.length - 1]] : [];
    // section opener pages
    const secs = Array.from(document.querySelectorAll('.section > h1')).map(h => ({ idx: idxOf(h) + 1, right: h.closest('.pagedjs_page').classList.contains('pagedjs_right_page'), t: h.textContent.slice(0, 30) }));
    const tocEl = document.querySelector('nav.toc');
    return { total: pages.length, sample, secs, toc: tocEl ? idxOf(tocEl) + 1 : -1,
      // any fully blank pages left?
      blanks: pages.map((p, i) => p.classList.contains('ak-empty') ? i + 1 : null).filter(Boolean) };
  });
  console.log('     section openers: ' + info.secs.map(s => `p${s.idx}`).join(' '));
  console.log('     empty pages: ' + JSON.stringify(info.blanks));
  console.log('     diagram sample (idx:svgNodes): ' + info.sample.map(s => `${s.i + 1}:${s.n}`).join(' '));
  const want = new Set([0, 1, 2, 3, 4, 5]);
  if (info.toc > 0) { want.add(info.toc - 1); want.add(info.toc); }
  if (info.secs[0]) want.add(info.secs[0].idx - 1);
  info.sample.forEach(s => want.add(s.i));
  for (const i of [...want].filter(i => i >= 0 && i < info.total)) {
    const clip = await page.evaluate((i) => {
      const p = document.querySelectorAll('.pagedjs_page')[i];
      p.scrollIntoView();
      const r = p.getBoundingClientRect();
      return { x: r.x + window.scrollX, y: r.y + window.scrollY, width: r.width, height: r.height };
    }, i);
    await page.screenshot({ path: path.join(INSPECT_DIR, `page${String(i + 1).padStart(3, '0')}.png`), clip, captureBeyondViewport: true });
  }
}

(async () => {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const { frontMatter, sectionBlock } = splitSource();
  const html = buildHtml(frontMatter, sectionBlock);
  const htmlPath = path.join(OUT_DIR, `_${OUT_FILE}.html`);
  fs.writeFileSync(htmlPath, html, 'utf8');

  const browser = await puppeteer.launch({ executablePath: CHROME, headless: 'new', args: ['--no-sandbox', '--font-render-hinting=none'] });
  const page = await browser.newPage();
  await page.setViewport({ width: 900, height: 1400, deviceScaleFactor: SHOTS ? 2 : 1 });
  await page.goto('file:///' + htmlPath.replace(/\\/g, '/'), { waitUntil: 'load', timeout: 120000 });
  await page.waitForFunction('window.__renderDone === true', { timeout: 900000 });

  const stats = await page.evaluate(() => ({ m: window.__mermaid, pages: window.__pages, error: window.__error }));
  if (stats.error) console.log(`  !! page error: ${stats.error}`);
  console.log(`  ${stats.pages} pages; diagrams ${stats.m ? stats.m.ok + '/' + stats.m.total + ' ok, ' + stats.m.fail + ' failed' : 'n/a'}`);
  if (stats.m?.failed?.length) console.log(`     failed: ${JSON.stringify(stats.m.failed)}`);

  if (SHOTS) await inspect(page);

  const pdfPath = path.join(OUT_DIR, OUT_FILE);
  await page.pdf({ path: pdfPath, printBackground: true, preferCSSPageSize: true, margin: { top: 0, right: 0, bottom: 0, left: 0 }, displayHeaderFooter: false, timeout: 0 });
  await browser.close();
  fs.unlinkSync(htmlPath);

  const sizeMB = (fs.statSync(pdfPath).size / 1048576).toFixed(2);
  console.log(`\nWrote ${OUT_FILE} (${stats.pages} pages, ${sizeMB} MB, ${stats.m ? stats.m.ok : 0} diagrams) to ${OUT_DIR}`);
})().catch(e => { console.error(e); process.exit(1); });
