# LineScope as a literate program.
#   make check    tangle the web and verify it reproduces the project's sources exactly
#   make pdf      weave the web and typeset linescope.pdf (needs tectonic; a copy is committed)
#   make tangle   write the tangled sources to build/tangled/ without touching the project
#   make clean

PYTHON  ?= python3
TECTONIC ?= tectonic
WEB     := $(sort $(wildcard chapters/*.nw))
ROOT    := ..
REQUIRE := --require 'Packages/LineScopeCore/Package.swift' \
           --require 'Packages/LineScopeCore/Sources/**/*.swift' \
           --require 'Packages/LineScopeCore/Tests/**/*.swift' \
           --require 'LineScope/**/*.swift' \
           --require 'project.yml' \
           --require 'Windows/LineScope.sln' \
           --require 'Windows/*/*.csproj' \
           --require 'Windows/LineScope.Core/*.cs' \
           --require 'Windows/LineScope.Core.Tests/*.cs' \
           --require 'Windows/LineScope.App/*.cs' \
           --require 'Windows/LineScope.App/*.xaml' \
           --require 'Windows/LineScope.App/app.manifest' \
           --require 'Windows/LineScope.App/Model/*.cs' \
           --require 'Windows/LineScope.App/Views/*.cs'
# (The Windows globs stop short of bin/ and obj/, which hold generated C# files.)

.PHONY: all pdf check tangle clean

all: check pdf

check:
	$(PYTHON) lit.py check --root $(ROOT) $(REQUIRE) $(WEB)

tangle:
	$(PYTHON) lit.py tangle -o build/tangled $(WEB)

build/body.tex build/chunkindex.tex: $(WEB) lit.py
	$(PYTHON) lit.py weave -o build $(WEB)

pdf: linescope.pdf

linescope.pdf: build/linescope.pdf
	cp build/linescope.pdf linescope.pdf

build/linescope.pdf: linescope.tex build/body.tex build/chunkindex.tex
	$(TECTONIC) --keep-logs --outdir build linescope.tex

clean:
	rm -rf build
