# Architect's Playbook — printable PDF build

Renders [`docs/ARCHITECT-PLAYBOOK.md`](../../docs/ARCHITECT-PLAYBOOK.md) into **one print-ready
A4 PDF** — a single volume covering all 127 concepts across the nine sections, with one table
of contents. Designed for double-sided printing and binding.

The source Markdown is **never modified** — it is read and rendered.

## What it produces

- **A4 portrait**, **standard uniform 18 mm margins** (not mirrored — safe for any binding/printer).
- Typography: 11 pt body / 1.5 line height, 9.5 pt bordered code blocks, section H1 21 pt (each section starts on a fresh page), H2 15 pt, concept H3 13.5 pt (never orphaned), 10 pt tables with a shaded header row and visible borders.
- Running header (document title left, current section right) and centred footer page number. Front matter uses **roman numerals**; page numbers **restart** at the first section.
- One generated **table of contents** with real page numbers for every section and concept.
- All **73 Mermaid diagrams rendered as vector SVG** (light theme). Each **fills the text width for readability but is height-capped (150 mm)** so a tall diagram never covers a whole page — wide diagrams fill the width, tall ones scale down to the cap.
- **No wasted paper**: sections start on a new page but have no dedicated title page, and no blank pages are forced (the build reports the empty-page count, normally zero).

## Requirements

- **Node.js 18+** (developed on 22).
- **Google Chrome or Microsoft Edge** at a standard Windows path (auto-detected). No Chromium download — `puppeteer-core` drives the installed browser.
- No LaTeX, Pandoc, or Poppler required.

## Build

```bash
cd scripts/build-playbook-pdf
npm install          # first time only
npm run build        # or: node build.js
```

Output (git-ignored):

```
build/playbook-pdf/ARCHITECT-PLAYBOOK.pdf
```

### Inspection mode

```bash
node build.js --shots
```

Also writes PNG screenshots of key pages (title, front matter, TOC, first section opener, and
the smallest / median / largest diagrams by node count) to `scripts/build-playbook-pdf/_inspect/`
(git-ignored), and logs the page count, diagram tally, section-opener pages, empty-page count,
and the sampled diagram sizes. Use it to verify a change without opening a PDF viewer.

## How it works

1. **Split** — `docs/ARCHITECT-PLAYBOOK.md` is split into front matter and the nine sections by locating the `# 1.`…`# 9.` headers (fence-aware, so `#` inside code blocks is ignored).
2. **Render** — `markdown-it` turns Markdown into HTML; ` ```mermaid ` fences become `<figure class="diagram">` blocks; headings get stable ids (prefixed so they never start with a digit).
3. **Diagrams** — inside headless Chrome, `mermaid.run()` renders every diagram to SVG.
4. **Paginate** — `Paged.js` applies the CSS Paged Media rules in [`template.css`](./template.css) (margins, running heads, breaks, TOC page numbers via `target-counter`).
5. **Number** — after pagination, `build.js` writes the footer page numbers itself (roman front matter, arabic restarting at the first section), because Paged.js's `counter(page)` does not reset in step with the TOC; the CSS neutralises Paged.js's own counter pseudo. Any empty page is detected and stripped of furniture.
6. **Print** — `page.pdf()` writes the A4 PDF (Paged.js owns the page geometry; Chrome margins are zero).

## Files

| File | Purpose |
|------|---------|
| `build.js` | The build script (split → render → paginate → number → PDF), plus `--shots` inspection. |
| `template.css` | The CSS Paged Media print stylesheet. |
| `package.json` / `package-lock.json` | Pinned dependencies. |

`node_modules/`, `_inspect/`, and the generated PDF under `build/playbook-pdf/` are git-ignored.

## Notes

- The 🟡/🔵/🟢 status emoji render natively as colour emoji in Chrome, so **no substitution is needed**; a `.status-fallback` style is defined in `template.css` should a future engine ever drop them.
- Rebuild time is a couple of minutes — Paged.js paginates ~250 pages in the browser.
