#!/usr/bin/env python3
"""A small noweb-compatible literate-programming tool: tangle, weave and check.

Web syntax (a subset of Norman Ramsey's noweb, itself a simplification of Knuth's WEB):

    <<chunk name>>=      starts a code chunk (a name may be defined in several pieces;
                         the pieces are concatenated in document order)
    @                    starts a documentation chunk (LaTeX); text after "@ " is part of it
    <<other chunk>>      on a line of its own inside code: include that chunk here, with the
                         reference's indentation prefixed to every included line
    [[code]]             in documentation: inline code

A code chunk whose name contains "/" or "." and that no other chunk references is a *root*:
tangling writes it to the file of that name.

    lit.py tangle -o OUTDIR  FILE.nw...
    lit.py weave  -o OUTDIR  FILE.nw...     (writes body.tex and chunkindex.tex)
    lit.py check  --root DIR [--require GLOB]... FILE.nw...
"""

from __future__ import annotations

import argparse
import difflib
import os
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

DEF_RE = re.compile(r"^<<(.+)>>=\s*$")
DOC_RE = re.compile(r"^@(?:\s(.*))?$")
REF_RE = re.compile(r"^(\s*)<<([^<>]+)>>\s*$")
QUOTE_RE = re.compile(r"\[\[(.+?)\]\]")


@dataclass
class Chunk:
    number: int
    name: str
    lines: list[str]
    source: str


@dataclass
class Web:
    # Items in document order: ("doc", [lines]) or ("code", Chunk)
    items: list[tuple[str, object]] = field(default_factory=list)
    chunks: list[Chunk] = field(default_factory=list)

    def definitions(self, name: str) -> list[Chunk]:
        return [c for c in self.chunks if c.name == name]

    def names(self) -> list[str]:
        seen: dict[str, None] = {}
        for c in self.chunks:
            seen.setdefault(c.name, None)
        return list(seen)

    def references(self) -> dict[str, list[int]]:
        """name -> numbers of the chunks that use it."""
        uses: dict[str, list[int]] = {}
        for c in self.chunks:
            for line in c.lines:
                m = REF_RE.match(line)
                if m:
                    uses.setdefault(m.group(2).strip(), []).append(c.number)
        return uses

    def roots(self) -> list[str]:
        used = self.references()
        return [n for n in self.names() if n not in used and ("/" in n or "." in n)]


def parse(paths: list[str]) -> Web:
    web = Web()
    number = 0
    for path in paths:
        mode = "doc"
        doc: list[str] = []
        code: list[str] = []
        name = ""

        def flush():
            nonlocal doc, code, number
            if mode == "doc":
                web.items.append(("doc", doc))
                doc = []
            else:
                number += 1
                chunk = Chunk(number, name, code, path)
                web.chunks.append(chunk)
                web.items.append(("code", chunk))
                code = []

        with open(path, encoding="utf-8") as f:
            for raw in f:
                line = raw.rstrip("\n")
                d = DEF_RE.match(line)
                if d:
                    flush()
                    mode, name = "code", d.group(1).strip()
                    continue
                e = DOC_RE.match(line)
                if e and mode == "code":
                    flush()
                    mode = "doc"
                    if e.group(1):
                        doc.append(e.group(1))
                    continue
                (code if mode == "code" else doc).append(line)
        flush()
    return web


# ---------------------------------------------------------------- tangle


def expand(web: Web, name: str, indent: str = "", stack: tuple[str, ...] = ()) -> list[str]:
    if name in stack:
        raise SystemExit(f"error: recursive chunk <<{name}>> via {' -> '.join(stack)}")
    defs = web.definitions(name)
    if not defs:
        raise SystemExit(f"error: undefined chunk <<{name}>> (used from <<{stack[-1] if stack else '?'}>>)")
    out: list[str] = []
    for chunk in defs:
        for line in chunk.lines:
            m = REF_RE.match(line)
            if m:
                out.extend(expand(web, m.group(2).strip(), indent + m.group(1), stack + (name,)))
            else:
                out.append(indent + line if line else line)
    return out


def tangle(web: Web) -> dict[str, str]:
    files: dict[str, str] = {}
    for root in web.roots():
        lines = expand(web, root)
        # Drop trailing blank lines introduced by the last chunk; files end with one newline.
        while lines and lines[-1] == "":
            lines.pop()
        files[root] = "\n".join(lines) + "\n"
    return files


def warn_cross_file(web: Web) -> None:
    for name in web.names():
        sources = {c.source for c in web.definitions(name)}
        if len(sources) > 1:
            print(f"warning: <<{name}>> is defined in several files: {', '.join(sorted(sources))}", file=sys.stderr)


# ---------------------------------------------------------------- weave

TEX_SPECIALS = {
    "\\": r"\textbackslash{}", "{": r"\{", "}": r"\}", "$": r"\$", "&": r"\&", "#": r"\#",
    "_": r"\_", "%": r"\%", "~": r"\textasciitilde{}", "^": r"\textasciicircum{}",
    "<": r"\textless{}", ">": r"\textgreater{}",
}


def tex(s: str) -> str:
    return "".join(TEX_SPECIALS.get(ch, ch) for ch in s)


def tex_code(s: str) -> str:
    return r"\nwcode{" + tex(s) + "}"


def tex_name(name: str) -> str:
    """A chunk name may itself contain [[code]]."""
    parts = QUOTE_RE.split(name)
    return "".join(tex_code(p) if i % 2 else tex(p) for i, p in enumerate(parts))


def doc_line(line: str) -> str:
    return QUOTE_RE.sub(lambda m: tex_code(m.group(1)), line)


def languages(web: Web) -> dict[str, str]:
    """Listing language for every chunk, from the extension of the root file it tangles into."""
    result: dict[str, str] = {}

    def visit(name: str, lang: str) -> None:
        if name in result:
            return
        result[name] = lang
        for c in web.definitions(name):
            for line in c.lines:
                m = REF_RE.match(line)
                if m:
                    visit(m.group(2).strip(), lang)

    for root in web.roots():
        visit(root, "Swift" if root.endswith(".swift") else "")
    return result


def links(numbers: list[int]) -> str:
    word = "chunk" if len(numbers) == 1 else "chunks"
    return word + " " + ", ".join(rf"\nwlink{{{n}}}" for n in numbers)


def weave(web: Web) -> tuple[str, str]:
    uses = web.references()
    roots = set(web.roots())
    language = languages(web)
    out: list[str] = []
    for kind, item in web.items:
        if kind == "doc":
            out.extend(doc_line(l) for l in item)  # type: ignore[union-attr]
            continue
        chunk: Chunk = item  # type: ignore[assignment]
        defs = [c.number for c in web.definitions(chunk.name)]
        first = defs[0]
        continued = chunk.number != first
        out.append(
            rf"\nwbegin{{{chunk.number}}}{{{tex_name(chunk.name)}}}{{{first}}}{{{'+' if continued else ''}}}"
        )
        lang = language.get(chunk.name, "")
        out.append(r"\begin{nwlisting}" + ("" if lang == "Swift" else f"[language={{{lang}}}]"))
        for line in chunk.lines:
            m = REF_RE.match(line)
            if m:
                ref = m.group(2).strip()
                targets = web.definitions(ref)
                num = targets[0].number if targets else 0
                out.append(f"{m.group(1)}(*@\\nwref{{{num}}}{{{tex_name(ref)}}}@*)")
            else:
                out.append(line)
        out.append(r"\end{nwlisting}")
        notes: list[str] = []
        later = [n for n in defs if n > chunk.number]
        if later:
            notes.append(f"Continued in {links(later)}.")
        if not continued:
            if chunk.name in roots:
                notes.append(rf"Root chunk, tangled to \nwcode{{{tex(chunk.name)}}}.")
            elif chunk.name in uses:
                notes.append(f"Used in {links(sorted(set(uses[chunk.name])))}.")
            else:
                notes.append("Not used.")
        out.append(rf"\nwend{{{' '.join(notes)}}}")
    body = "\n".join(out) + "\n"

    idx: list[str] = [r"\begin{nwindex}"]
    for name in sorted(web.names(), key=lambda n: n.lower().lstrip("[")):
        defs = [c.number for c in web.definitions(name)]
        used = sorted(set(uses.get(name, [])))
        idx.append(
            rf"\nwindexentry{{{tex_name(name)}}}{{{defs[0]}}}"
            + "{" + ", ".join(rf"\nwlink{{{n}}}" for n in defs) + "}"
            + "{" + (", ".join(rf"\nwlink{{{n}}}" for n in used) or "root") + "}"
        )
    idx.append(r"\end{nwindex}")
    return body, "\n".join(idx) + "\n"


# ---------------------------------------------------------------- main


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    t = sub.add_parser("tangle")
    t.add_argument("-o", "--out", required=True)
    w = sub.add_parser("weave")
    w.add_argument("-o", "--out", required=True)
    c = sub.add_parser("check")
    c.add_argument("--root", required=True, help="directory the tangled paths are relative to")
    c.add_argument("--require", action="append", default=[], help="glob of files that must be covered")
    for p in (t, w, c):
        p.add_argument("files", nargs="+")
    args = ap.parse_args()

    web = parse(args.files)
    warn_cross_file(web)

    if args.cmd == "tangle":
        for path, text in tangle(web).items():
            dest = Path(args.out) / path
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(text, encoding="utf-8")
            print(f"tangled {path}")
        return 0

    if args.cmd == "weave":
        body, index = weave(web)
        os.makedirs(args.out, exist_ok=True)
        Path(args.out, "body.tex").write_text(body, encoding="utf-8")
        Path(args.out, "chunkindex.tex").write_text(index, encoding="utf-8")
        print(f"wove {len(web.chunks)} chunks from {len(args.files)} files")
        return 0

    # check
    root = Path(args.root)
    failures = 0
    files = tangle(web)
    for path, text in files.items():
        target = root / path
        if not target.exists():
            print(f"MISSING  {path} (no such file in {root})")
            failures += 1
            continue
        actual = target.read_text(encoding="utf-8")
        if actual == text:
            print(f"ok       {path}")
        else:
            failures += 1
            print(f"DIFFERS  {path}")
            sys.stdout.writelines(
                difflib.unified_diff(
                    actual.splitlines(keepends=True), text.splitlines(keepends=True),
                    fromfile=f"source/{path}", tofile=f"web/{path}", n=1,
                )
            )
    for pattern in args.require:
        for p in sorted(root.glob(pattern)):
            rel = p.relative_to(root).as_posix()
            if p.is_file() and rel not in files:
                print(f"UNCOVERED {rel} (matches --require {pattern} but no root chunk tangles it)")
                failures += 1
    print(f"{len(files)} files checked, {failures} problem(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
