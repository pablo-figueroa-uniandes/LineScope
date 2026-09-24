#!/usr/bin/env python3
"""Generates the SVG figures for Docs/LineScope-Theory-and-Code.md.

Dependency-free: plain Python writes the SVG. The kernel formulas mirror
Packages/LineScopeCore/Sources/LineScopeCore/Resampling.swift, and the window formula mirrors
Spectrum.swift, so the figures show exactly what the program computes.

    python3 Docs/figures/make_figures.py        # writes Docs/figures/*.svg, prints the tables
"""

from __future__ import annotations

import cmath
import math
from pathlib import Path

HERE = Path(__file__).resolve().parent

# ---------------------------------------------------------------- kernels (as in Resampling.swift)

SINC_RADIUS = 8.0


def sinc(x: float) -> float:
    if abs(x) < 1e-9:
        return 1.0
    px = math.pi * x
    return math.sin(px) / px


def bc_spline(ax: float, b: float, c: float) -> float:
    if ax < 1:
        return ((12 - 9 * b - 6 * c) * ax**3 + (-18 + 12 * b + 6 * c) * ax**2 + (6 - 2 * b)) / 6
    if ax < 2:
        return ((-b - 6 * c) * ax**3 + (6 * b + 30 * c) * ax**2 + (-12 * b - 48 * c) * ax + (8 * b + 24 * c)) / 6
    return 0.0


KERNELS = [
    # (name, function, support)
    ("Box (nearest)", lambda x: 1.0 if abs(x) < 0.5 else 0.0, 0.5),
    ("Tent", lambda x: max(0.0, 1 - abs(x)), 1.0),
    ("Catmull-Rom", lambda x: bc_spline(abs(x), 0, 0.5), 2.0),
    ("Mitchell-Netravali", lambda x: bc_spline(abs(x), 1 / 3, 1 / 3), 2.0),
    ("Lanczos-3", lambda x: sinc(x) * sinc(x / 3) if abs(x) < 3 else 0.0, 3.0),
    ("Sinc (r = 8)", lambda x: sinc(x) if abs(x) < SINC_RADIUS else 0.0, SINC_RADIUS),
]


def response(k, support: float, f: float, step: float = 0.001) -> float:
    """K(f) = ∫ k(x) cos(2πfx) dx for a symmetric kernel (midpoint rule)."""
    n = int(round(2 * support / step))
    total = 0.0
    for i in range(n):
        x = -support + (i + 0.5) * step
        total += k(x) * math.cos(2 * math.pi * f * x)
    return total * step


# ---------------------------------------------------------------- tiny SVG plotting

LIGHT = {
    "surface": "#fcfcfb", "text": "#0b0b0b", "text2": "#52514e", "grid": "#e4e3df", "axis": "#b9b8b2",
    "series": ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300"],
}
DARK = {
    "surface": "#1a1a19", "text": "#ffffff", "text2": "#c3c2b7", "grid": "#33332f", "axis": "#5a5954",
    "series": ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300"],
}
DASHES = ["", "7 4", "2 3", "10 3 2 3", "4 4", "1 5"]
FONT = "-apple-system, BlinkMacSystemFont, 'Helvetica Neue', Helvetica, Arial, sans-serif"


def style_block() -> str:
    def vars_(p):
        v = [f"--surface:{p['surface']};--text:{p['text']};--text2:{p['text2']};--grid:{p['grid']};--axis:{p['axis']};"]
        v += [f"--s{i + 1}:{c};" for i, c in enumerate(p["series"])]
        return "".join(v)

    return (
        "<style>"
        f"svg{{{vars_(LIGHT)}}}"
        f"@media (prefers-color-scheme: dark){{svg{{{vars_(DARK)}}}}}"
        f"text{{font-family:{FONT};fill:var(--text2);font-size:12px}}"
        ".title{fill:var(--text);font-size:15px;font-weight:600}"
        ".label{fill:var(--text);font-size:12px}"
        ".grid{stroke:var(--grid);stroke-width:1}"
        ".axis{stroke:var(--axis);stroke-width:1}"
        ".line{fill:none;stroke-width:2;stroke-linejoin:round;stroke-linecap:round}"
        "</style>"
    )


class Plot:
    def __init__(self, x0, y0, w, h, xr, yr):
        self.x0, self.y0, self.w, self.h = x0, y0, w, h
        self.xr, self.yr = xr, yr
        self.parts: list[str] = []

    def sx(self, x):
        return self.x0 + (x - self.xr[0]) / (self.xr[1] - self.xr[0]) * self.w

    def sy(self, y):
        y = min(max(y, self.yr[0]), self.yr[1])
        return self.y0 + self.h - (y - self.yr[0]) / (self.yr[1] - self.yr[0]) * self.h

    def grid(self, xticks, yticks, xfmt=str, yfmt=str, xlabel="", ylabel=""):
        for t in yticks:
            y = self.sy(t)
            self.parts.append(f'<line class="grid" x1="{self.x0}" x2="{self.x0 + self.w}" y1="{y:.1f}" y2="{y:.1f}"/>')
            self.parts.append(f'<text x="{self.x0 - 6}" y="{y + 4:.1f}" text-anchor="end">{yfmt(t)}</text>')
        for t in xticks:
            x = self.sx(t)
            self.parts.append(f'<text x="{x:.1f}" y="{self.y0 + self.h + 16}" text-anchor="middle">{xfmt(t)}</text>')
        self.parts.append(
            f'<line class="axis" x1="{self.x0}" x2="{self.x0 + self.w}" y1="{self.y0 + self.h}" y2="{self.y0 + self.h}"/>'
        )
        if xlabel:
            self.parts.append(
                f'<text x="{self.x0 + self.w / 2}" y="{self.y0 + self.h + 34}" text-anchor="middle">{xlabel}</text>'
            )
        if ylabel:
            cx, cy = self.x0 - 44, self.y0 + self.h / 2
            self.parts.append(f'<text transform="translate({cx},{cy}) rotate(-90)" text-anchor="middle">{ylabel}</text>')

    def line(self, pts, slot, dash=""):
        d = " ".join(f"{'M' if i == 0 else 'L'}{self.sx(x):.2f},{self.sy(y):.2f}" for i, (x, y) in enumerate(pts))
        da = f' stroke-dasharray="{dash}"' if dash else ""
        self.parts.append(f'<path class="line" d="{d}" style="stroke:var(--s{slot})"{da}/>')

    def dots(self, pts, slot):
        for x, y in pts:
            self.parts.append(
                f'<circle cx="{self.sx(x):.2f}" cy="{self.sy(y):.2f}" r="4.5" '
                f'style="fill:var(--s{slot});stroke:var(--surface);stroke-width:2"/>'
            )

    def vrule(self, x, text):
        px = self.sx(x)
        self.parts.append(f'<line class="axis" x1="{px:.1f}" x2="{px:.1f}" y1="{self.y0}" y2="{self.y0 + self.h}"/>')
        self.parts.append(f'<text x="{px + 5:.1f}" y="{self.y0 + 12}">{text}</text>')

    def svg(self):
        return "".join(self.parts)


def legend(items, x, y, row_gap=18):
    """items: (label, slot, dash, kind) with kind 'line' or 'dot'."""
    out = []
    for i, (label, slot, dash, kind) in enumerate(items):
        yy = y + i * row_gap
        if kind == "dot":
            out.append(f'<circle cx="{x + 11}" cy="{yy}" r="4.5" style="fill:var(--s{slot});stroke:var(--surface);stroke-width:2"/>')
        else:
            da = f' stroke-dasharray="{dash}"' if dash else ""
            out.append(f'<line x1="{x}" x2="{x + 22}" y1="{yy}" y2="{yy}" class="line" style="stroke:var(--s{slot})"{da}/>')
        out.append(f'<text class="label" x="{x + 30}" y="{yy + 4}">{label}</text>')
    return "".join(out)


def write(name, width, height, title, desc, body):
    svg = (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}" '
        f'role="img" aria-labelledby="t d">'
        f'<title id="t">{title}</title><desc id="d">{desc}</desc>{style_block()}'
        f'<rect width="100%" height="100%" rx="6" style="fill:var(--surface)"/>'
        f'<text class="title" x="20" y="28">{title}</text>{body}</svg>\n'
    )
    (HERE / name).write_text(svg, encoding="utf-8")
    print(f"wrote {name}")


def fnum(v, digits=2):
    s = f"{v:.{digits}f}".rstrip("0").rstrip(".")
    if s in ("-0", "-"):
        s = "0"
    return "−" + s[1:] if s.startswith("-") else s


# ---------------------------------------------------------------- figure 1: aliasing


def fig_aliasing():
    p = Plot(70, 50, 610, 230, (0, 10), (-1.15, 1.15))
    p.grid(range(0, 11), [-1, 0, 1], xfmt=str, yfmt=fnum, xlabel="position x (pixels)", ylabel="value")
    fine = [(i / 100, math.cos(2 * math.pi * 0.8 * i / 100)) for i in range(0, 1001)]
    alias = [(i / 100, math.cos(2 * math.pi * 0.2 * i / 100)) for i in range(0, 1001)]
    p.line(fine, 1)
    p.line(alias, 2, "7 4")
    p.dots([(n, math.cos(2 * math.pi * 0.8 * n)) for n in range(0, 11)], 1)
    body = p.svg() + legend(
        [("signal, 0.8 cycles/px (above Nyquist)", 1, "", "line"),
         ("samples at integer x", 1, "", "dot"),
         ("alias, 0.2 cycles/px (same samples)", 2, "7 4", "line")],
        470, 310,
    )
    write("aliasing.svg", 720, 380, "Aliasing: 0.8 cycles/px sampled once per pixel",
          "A cosine at 0.8 cycles per pixel and a cosine at 0.2 cycles per pixel pass through the same integer samples.",
          body)


# ---------------------------------------------------------------- figure 2: kernels (small multiples)


def fig_kernels():
    cols, rows = 3, 2
    pw, ph = 190, 120
    parts = []
    for i, (name, k, support) in enumerate(KERNELS):
        c, r = i % cols, i // cols
        x0 = 60 + c * (pw + 50)
        y0 = 95 + r * (ph + 80)
        p = Plot(x0, y0, pw, ph, (-4, 4), (-0.3, 1.1))
        p.grid([-4, -2, 0, 2, 4], [0, 0.5, 1], yfmt=fnum, xlabel="x (source pixels)")
        pts = [(x / 50, k(x / 50)) for x in range(-200, 201)]
        p.line(pts, 1)
        parts.append(p.svg())
        parts.append(f'<text class="label" x="{x0}" y="{y0 - 10}">{name}</text>')
    parts.append('<text x="20" y="48">k(x) on a common scale. The sinc kernel continues to |x| = 8 beyond this window.</text>')
    write("kernels.svg", 760, 495, "Resampling kernels", "Six reconstruction kernels plotted on the same axes.", "".join(parts))


# ---------------------------------------------------------------- figure 3: frequency responses


def fig_responses():
    freqs = [i / 200 for i in range(0, 301)]  # 0 … 1.5 cycles/px
    p = Plot(80, 55, 500, 290, (0, 1.5), (-60, 3))
    p.grid([0, 0.25, 0.5, 0.75, 1, 1.25, 1.5], [0, -10, -20, -30, -40, -50, -60], xfmt=fnum, yfmt=str,
           xlabel="frequency (cycles per source pixel)", ylabel="gain (dB)")
    p.vrule(0.5, "Nyquist")
    table = []
    items = []
    for i, (name, k, support) in enumerate(KERNELS):
        k0 = response(k, support, 0)
        pts = []
        for f in freqs:
            g = abs(response(k, support, f, step=0.004)) / k0
            pts.append((f, 20 * math.log10(max(g, 1e-6))))
        p.line(pts, i + 1, DASHES[i])
        items.append((name, i + 1, DASHES[i], "line"))
        row = [response(k, support, f) / k0 for f in (0.25, 0.5, 0.75, 1.0)]
        table.append((name, row))
    body = p.svg() + legend(items, 600, 70, row_gap=22)
    write("kernel-response.svg", 760, 400, "Frequency response of the kernels",
          "Gain in decibels of each kernel from 0 to 1.5 cycles per pixel, with the Nyquist frequency marked at 0.5.",
          body)
    print("\n| Kernel | K(0.25) | K(0.5) Nyquist | K(0.75) | K(1.0) |\n|---|---|---|---|---|")
    for name, row in table:
        print(f"| {name} | " + " | ".join(fnum(v, 3) for v in row) + " |")


# ---------------------------------------------------------------- figure 4: windows and leakage


def dft_amplitudes(values, fft_size, window_sum):
    n = len(values)
    out = []
    for k in range(fft_size // 2 + 1):
        s = sum(values[t] * cmath.exp(-2j * math.pi * k * t / fft_size) for t in range(n))
        scale = 1 if k in (0, fft_size // 2) else 2
        out.append(scale * abs(s) / window_sum)
    return out


def fig_windows():
    n, size = 64, 1024
    f0 = 10.5 / n  # halfway between two bins of the unpadded DFT: worst case leakage
    signal = [math.sin(2 * math.pi * f0 * t) for t in range(n)]
    hann = [0.5 - 0.5 * math.cos(2 * math.pi * t / (n - 1)) for t in range(n)]
    rect = dft_amplitudes(signal, size, n)
    han = dft_amplitudes([s * w for s, w in zip(signal, hann)], size, sum(hann))
    freqs = [k / size for k in range(size // 2 + 1)]
    p = Plot(80, 55, 560, 270, (0, 0.5), (-100, 5))
    p.grid([0, 0.1, 0.2, 0.3, 0.4, 0.5], [0, -20, -40, -60, -80, -100], xfmt=fnum, yfmt=str,
           xlabel="frequency (cycles/pixel)", ylabel="amplitude (dB)")
    p.line([(f, 20 * math.log10(max(a, 1e-6))) for f, a in zip(freqs, rect)], 1)
    p.line([(f, 20 * math.log10(max(a, 1e-6))) for f, a in zip(freqs, han)], 2, "7 4")
    body = p.svg() + legend([("rectangular (no window)", 1, "", "line"), ("Hann", 2, "7 4", "line")], 470, 80)
    write("windows.svg", 720, 380, "Spectral leakage: rectangular versus Hann window",
          "Amplitude spectrum of a 64-sample sine between two DFT bins, zero-padded to 1024, with and without a Hann window.",
          body)


if __name__ == "__main__":
    fig_aliasing()
    fig_kernels()
    fig_responses()
    fig_windows()
