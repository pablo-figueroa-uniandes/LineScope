#!/usr/bin/env python3
"""Renders Docs/LineScope-Theory-and-Code.md to PDF with headless Chrome (or Chromium, or Edge).

The Markdown is converted in the page by marked, the $…$ / $$…$$ math by KaTeX and the
```mermaid block by Mermaid (all from jsdelivr, so a network connection is needed). Math is
cut out before Markdown parsing so underscores and backslashes survive.

    python3 Docs/tools/build_pdf.py        # writes Docs/LineScope-Theory-and-Code.pdf
"""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

DOCS = Path(__file__).resolve().parent.parent
SOURCE = DOCS / "LineScope-Theory-and-Code.md"
OUTPUT = DOCS / "LineScope-Theory-and-Code.pdf"
PAGE = DOCS / ".print.html"
# Any Chromium-based browser can print the page. $CHROME overrides the search.
BROWSERS = [
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    "google-chrome", "chromium", "chromium-browser",
]


def find_browser() -> str:
    for candidate in ([os.environ["CHROME"]] if "CHROME" in os.environ else []) + BROWSERS:
        if Path(candidate).is_file() or shutil.which(candidate):
            return candidate
    raise SystemExit("error: no Chrome, Chromium or Edge found; set $CHROME to the browser's executable")


def protect_math(md: str) -> tuple[str, list[tuple[str, bool]]]:
    """Replaces math outside code with placeholders; returns the text and the formulas."""
    formulas: list[tuple[str, bool]] = []

    def keep(tex: str, display: bool) -> str:
        formulas.append((tex, display))
        return f"@@MATH{len(formulas) - 1}@@"

    out = []
    for i, part in enumerate(re.split(r"(```.*?```)", md, flags=re.S)):
        if i % 2:  # fenced code: untouched
            out.append(part)
            continue
        part = re.sub(r"\$\$(.+?)\$\$", lambda m: keep(m.group(1), True), part, flags=re.S)
        part = re.sub(r"(?<![\\$])\$(?!\s)(.+?)(?<!\s)\$", lambda m: keep(m.group(1), False), part)
        out.append(part)
    return "".join(out), formulas


HTML = """<!doctype html>
<html><head><meta charset="utf-8"><title>LineScope: theory and code</title>
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.css">
<script src="https://cdn.jsdelivr.net/npm/katex@0.16.11/dist/katex.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/marked@12.0.2/marked.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/mermaid@10.9.1/dist/mermaid.min.js"></script>
<style>
  @page { size: A4; margin: 18mm 17mm; }
  :root { color-scheme: light; }
  body { font: 10.5pt/1.5 -apple-system, "Helvetica Neue", Helvetica, "Segoe UI", Arial, sans-serif; color: #1b1b1a; }
  h1 { font-size: 22pt; margin: 0 0 .4em; }
  h2 { font-size: 15pt; margin-top: 1.6em; border-bottom: 1px solid #ddd; padding-bottom: .2em; break-after: avoid; }
  h3 { font-size: 12pt; margin-top: 1.2em; break-after: avoid; }
  a { color: #1c5cab; text-decoration: none; }
  code { font: 9pt Menlo, Consolas, monospace; background: #f3f2ef; padding: 0 .25em; border-radius: 3px; }
  pre { background: #f3f2ef; padding: .7em 1em; border-radius: 5px; overflow: hidden; white-space: pre-wrap; }
  pre code { background: none; padding: 0; }
  table { border-collapse: collapse; margin: .8em 0; font-size: 9.5pt; break-inside: avoid; }
  th, td { border: 1px solid #d9d8d4; padding: .3em .55em; vertical-align: top; text-align: left; }
  th { background: #f3f2ef; }
  img { max-width: 100%; display: block; margin: .8em auto; break-inside: avoid; }
  hr { border: 0; border-top: 1px solid #e4e3df; margin: 1.5em 0; }
  .katex-display { margin: .7em 0; font-size: .86em; }
  .mermaid { text-align: center; margin: 1em 0; break-inside: avoid; }
</style></head>
<body><main id="doc"></main>
<script>
const md = __MD__;
const math = __MATH__;
let html = marked.parse(md);
html = html.replace(/@@MATH(\\d+)@@/g, (_, i) =>
  katex.renderToString(math[i][0], { displayMode: math[i][1], throwOnError: false }));
document.getElementById("doc").innerHTML = html;
for (const code of document.querySelectorAll("code.language-mermaid")) {
  const div = document.createElement("div");
  div.className = "mermaid";
  div.textContent = code.textContent;
  code.parentElement.replaceWith(div);
}
mermaid.initialize({ startOnLoad: false, theme: "neutral" });
mermaid.run();
</script></body></html>
"""


def main() -> int:
    md, formulas = protect_math(SOURCE.read_text(encoding="utf-8"))
    page = HTML.replace("__MD__", json.dumps(md)).replace("__MATH__", json.dumps(formulas))
    PAGE.write_text(page, encoding="utf-8")
    try:
        subprocess.run(
            [find_browser(), "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
             "--virtual-time-budget=20000", "--run-all-compositor-stages-before-draw",
             f"--print-to-pdf={OUTPUT}", PAGE.as_uri()],
            check=True, capture_output=True,
        )
    finally:
        PAGE.unlink(missing_ok=True)
    print(f"wrote {OUTPUT.relative_to(DOCS.parent)} ({len(formulas)} formulas)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
