# LineScope as a literate program

This directory presents the whole LineScope source code, both the macOS app (Swift) and the Windows port (C#), as a literate program, in the sense of Donald Knuth: a book written for people, from which the program for the machine can be extracted. The notation is Norman Ramsey's noweb, handled by the small self-contained tool `lit.py`, so noweb itself isn't needed.

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
| `make pdf` | Weaves the web, typesets it with [tectonic](https://tectonic-typesetting.github.io), and copies the result to the committed `linescope.pdf`. |
| `make tangle` | Writes the tangled sources to `build/tangled/` without touching the project. |

Tectonic downloads the LaTeX packages it needs on the first run. Code is set in Menlo, which ships with macOS. On other systems the driver falls back to Consolas (Windows) or DejaVu Sans Mono.

Without `make` (for example on Windows), run the tool directly; `py` is the Windows Python launcher:

```sh
py lit.py check --root .. chapters/*.nw      # add the --require globs from the Makefile for the coverage check
py lit.py weave -o build chapters/*.nw && tectonic --outdir build linescope.tex
```

## Organization

| Chapters | Part | Sources |
|---|---|---|
| `00-introduction` | Introduction | `project.yml`, `Packages/LineScopeCore/Package.swift` |
| `10-pixels` … `40-tests` | I. LineScope for macOS | `Packages/LineScopeCore` (numeric core and XCTest suite) |
| `50-model` … `70-commands` | I. LineScope for macOS | `LineScope/` (SwiftUI app) |
| `80-windows-overview` | II. LineScope for Windows | `Windows/LineScope.sln`, the app's project files and `App.xaml(.cs)` |
| `82-windows-core`, `84-windows-tests` | II. LineScope for Windows | `Windows/LineScope.Core`, `Windows/LineScope.Core.Tests` |
| `86-windows-model`, `88-windows-views` | II. LineScope for Windows | `Windows/LineScope.App/Model`, `Windows/LineScope.App/Views` |

Part II doesn't repeat the theory. It refers back to the matching Part I chapter and explains what the port changes.

## Web syntax

| Syntax | Meaning |
|---|---|
| `<<name>>=` | Starts a code chunk. Defining the same name again appends to it. |
| `@` | Starts documentation (LaTeX). Text after `@ ` on the same line belongs to it. |
| `<<name>>` | On its own line inside code, includes that chunk. The line's indentation is added to every included line. |
| `[[code]]` | Inline code in the prose. |

A chunk named like a file path (containing `/` or `.`) that no other chunk uses is a root chunk. Tangling writes it to that path.

## Keeping the book and the code in sync

The sources in the repository are still the ones Xcode and `dotnet` build. After changing code on either platform, update the matching chunk in `chapters/` and run `make check` until it prints only `ok` lines. The Makefile's `--require` globs list every source directory, so a new file that no chunk tangles is reported as `UNCOVERED`.
