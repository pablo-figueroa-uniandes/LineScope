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
           --require 'project.yml'

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
