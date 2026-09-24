# LineScope as a literate program

This directory presents the whole LineScope source code as a literate program, in the sense of Donald Knuth: a book written for people, from which the program for the machine can be extracted. The notation is Norman Ramsey's noweb, handled by the small self-contained tool `lit.py`, so noweb itself isn't needed.

```
chapters/*.nw   the web: LaTeX prose interleaved with named code chunks, read in file-name order
lit.py          tangle (web → sources), weave (web → LaTeX), check (tangled == repository sources)
linescope.tex   LaTeX driver: preamble, chunk markup, bibliography, chunk index
Makefile        make check | make pdf | make tangle | make clean
```

## Commands

| Command | What it does |
|---|---|
| `make check` | Tangles every root chunk and compares it byte for byte with the file of the same path in the repository. It also fails if a project source file isn't produced by any root chunk. |
| `make pdf` | Weaves the web and typesets `build/linescope.pdf` with [tectonic](https://tectonic-typesetting.github.io). |
| `make tangle` | Writes the tangled sources to `build/tangled/` without touching the project. |

Tectonic downloads the LaTeX packages it needs on the first run. Code is set in Menlo, which ships with macOS.

## Web syntax

| Syntax | Meaning |
|---|---|
| `<<name>>=` | Starts a code chunk. Defining the same name again appends to it. |
| `@` | Starts documentation (LaTeX). Text after `@ ` on the same line belongs to it. |
| `<<name>>` | On its own line inside code, includes that chunk. The line's indentation is added to every included line. |
| `[[code]]` | Inline code in the prose. |

A chunk named like a file path (containing `/` or `.`) that no other chunk uses is a root chunk. Tangling writes it to that path.

## Keeping the book and the code in sync

The Swift sources in the repository are still the ones Xcode builds. After changing code, update the matching chunk in `chapters/` and run `make check` until it prints only `ok` lines.
