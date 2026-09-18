# Architect's Playbook — printable PDF build

Builds two **print-ready A4 PDFs** from [`docs/ARCHITECT-PLAYBOOK.md`](../../docs/ARCHITECT-PLAYBOOK.md),
designed for **double-sided printing and spiral binding**:

| Book | Title | Contents |
|------|-------|----------|
| 1 | **Platform and Infrastructure** | front matter + Sections 1–5 (Platform, Infrastructure as code, Azure services, Kubernetes, Security and identity) |
| 2 | **Delivery and Practice** | front matter + Sections 6–9 (Observability, GitOps, DevOps, Architecture practice) |

The source Markdown is **never modified** — it is read and rendered.

## What it produces

- **A4 portrait**, mirrored (facing-page) margins for spiral binding: **inner/binding 25 mm, outer 15 mm, top/bottom 20 mm**. The binding edge alternates — inner on every page — so odd and even pages swap left/right.
- Typography: 11 pt body / 1.5 line height, 9.5 pt bordered code blocks, section H1 22 pt (each on a new right-hand page), H2 16 pt, concept H3 14 pt (never orphaned), 10 pt tables with a shaded header row and visible borders.
- Running header (book title left, current section right) and centred footer page number. Front matter uses **roman numerals**; page numbers **restart** at the main matter. Title pages, section-title pages, and inserted blanks carry no header/footer.
- All **73 Mermaid diagrams rendered as vector SVG** (light theme), scaled to the full text width, and never split across a page — a diagram that will not fit moves whole to the next page.
- A generated **table of contents** with real page numbers for every section and concept, plus a one-line note pointing to the other book.

## Requirements

- **Node.js 18+** (developed on 22).
- **Google Chrome or Microsoft Edge** installed at a standard Windows path (the script auto-detects `chrome.exe` / `msedge.exe`). No Chromium download is needed — `puppeteer-core` drives the installed browser.
- No LaTeX, Pandoc, or Poppler required.

Everything else (markdown-it, mermaid, Paged.js, puppeteer-core) is a local npm dependency.

## Build

```bash
cd scripts/build-playbook-pdf
npm install          # first time only
npm run build        # or: node build.js
```

Output (git-ignored):

```
build/playbook-pdf/Book1-Platform-and-Infrastructure.pdf
build/playbook-pdf/Book2-Delivery-and-Practice.pdf
```

### Inspection mode

```bash
node build.js --shots
```

Also writes PNG screenshots of key pages (title, front matter, TOC, section opener, the
busiest-diagram page, a facing odd/even pair) to `scripts/build-playbook-pdf/_inspect/`
(git-ignored) and logs page counts, per-book diagram tallies, mirrored-margin measurements,
and which pages each section opens on. Use it to verify a change without opening a PDF viewer.

## How it works

1. **Split** — `docs/ARCHITECT-PLAYBOOK.md` is split into shared front matter and the two books by locating the `# 1.`…`# 9.` section headers (fence-aware, so `#` inside code blocks is ignored).
2. **Render** — `markdown-it` turns Markdown into HTML; ` ```mermaid ` fences become `<figure class="diagram">` blocks; headings get stable ids.
3. **Diagrams** — inside headless Chrome, `mermaid.run()` renders every diagram to SVG.
4. **Paginate** — `Paged.js` applies the CSS Paged Media rules in [`template.css`](./template.css) (mirrored margins, running heads, breaks, TOC page numbers via `target-counter`).
5. **Number** — after pagination, `build.js` writes the footer page numbers itself (roman front matter, arabic restarting at the main matter) because Paged.js's `counter(page)` does not reset in step with the TOC; the CSS neutralises Paged.js's own counter pseudo. Empty recto-forcing blanks are detected and stripped of furniture.
6. **Print** — `page.pdf()` writes the A4 PDF (Paged.js owns the page geometry; Chrome margins are zero).

## Files

| File | Purpose |
|------|---------|
| `build.js` | The build script (split → render → paginate → number → PDF), plus `--shots` inspection. |
| `template.css` | The CSS Paged Media print stylesheet (`__BOOK_TITLE__` is substituted per book). |
| `package.json` / `package-lock.json` | Pinned dependencies. |

`node_modules/`, `_inspect/`, and the generated PDFs under `build/playbook-pdf/` are git-ignored.

## Notes

- The 🟡/🔵/🟢 status emoji render natively as colour emoji in Chrome, so **no substitution is needed**; if a future engine lost them, a `.status-fallback` style is already defined in `template.css` as a printed marker.
- Rebuild time is a couple of minutes per book — Paged.js paginates ~180 and ~100 pages in the browser.
