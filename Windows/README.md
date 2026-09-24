# LineScope for Windows

A native Windows port of LineScope (C# / .NET 9 / WPF). It has the same features and layout as the macOS app:

- three zones: images, the line profile and its spectrum;
- a shared, draggable line;
- chained filters and resampling, with each result in a new tab;
- split panes;
- test patterns;
- an inspector window.

The numeric core is a line-by-line port of `Packages/LineScopeCore`, and its unit tests are ported from the XCTest suite.

## Build and run

You need Windows 10 or 11 and the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0). Visual Studio 2022 (17.12 or later) is optional: open `LineScope.sln` and press F5.

```sh
cd Windows
dotnet build LineScope.sln
dotnet run --project LineScope.App                      # or: dotnet run --project LineScope.App -- image.png
dotnet test LineScope.Core.Tests                        # unit tests for the numeric core
dotnet test LineScope.Core.Tests --filter "FullyQualifiedName~SpectrumTests.SinePeak"   # single test
```

A framework-dependent, single-file build (it needs the .NET 9 Desktop Runtime on the target machine):

```sh
dotnet publish LineScope.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

For a build that runs without an installed runtime, use `--self-contained true`. The output is much larger, because it bundles the runtime.

## Layout

```
LineScope.sln
LineScope.Core/          numeric core (net9.0, no UI dependencies): PixelBuffer, Resampling, Filters,
                         LineSampler, Spectrum (radix-2 FFT), ColorSpaces, TestPattern, ImageOperation
LineScope.Core.Tests/    xUnit tests (port of LineScopeCoreTests.swift, plus tests for the new FFT and filters)
LineScope.App/           WPF app
  App.xaml(.cs)          startup, document windows, the shared Inspector and pattern-generator windows
  Model/                 ImageDocument (WIC decoding), ImageVariant, Session, ActiveSession
  Views/                 MainWindow (menus, zones), ZoneView, PaneView, ImageCanvas, Charts,
                         ParameterDialog, TestPatternWindow, InspectorWindow
```

## How the macOS pieces were replaced

| macOS | Windows |
|---|---|
| SwiftUI `DocumentGroup(viewing:)` | One `MainWindow` per document. Open with File ▸ Open (Ctrl+O), drag and drop, or a command-line argument. The app quits when the last document window closes. |
| Core Graphics / ImageIO decoding | WIC through `BitmapDecoder`, converted to premultiplied BGRA8. Embedded ICC profiles are converted to sRGB. EXIF orientation is still ignored. |
| Core Image filters (Gaussian, box, median, unsharp mask, noise reduction) | CPU implementations in `Filters.cs`, with clamped edges and applied to premultiplied data. See the notes below. |
| vDSP real FFT | An iterative radix-2 complex FFT (`Fft` in `Spectrum.cs`), tested against a naive DFT. The normalization is unchanged. |
| Swift Charts | `ChartView` in `Views/Charts.cs`, drawn directly with WPF's `DrawingContext`. |
| `@Observable` `Session` | A plain class with a `Changed` event that carries what changed (line, settings, variants, panes, focus). |
| Menu commands with `@FocusedValue` | Each window has its own menu bar, so menus act on that window's session. `ActiveSession` (set when a window is activated) still drives the Inspector. |
| Points vs. Retina pixels | 1 image pixel = 1 DIP (1/96 in). View ▸ Actual Device Pixels maps 1 image pixel to 1 screen pixel. The app is per-monitor DPI aware. |

### Keyboard shortcuts

| Action | macOS | Windows |
|---|---|---|
| Open | ⌘O | Ctrl+O |
| Custom test pattern | ⌥⌘N | Ctrl+Alt+N |
| Export image | ⇧⌘E | Ctrl+Shift+E |
| Close tab | ⌘⌫ | Ctrl+Delete |
| Close window | ⌘W | Ctrl+W |
| Show inspector | ⌥⌘I | Ctrl+Alt+I |
| Constrain the line to horizontal or vertical | Shift-drag | Shift-drag |

### Numerical differences from macOS

The resampler, line sampler, color conversions, spectrum and test patterns are the same algorithms as on macOS, and should match it to floating-point precision. The filters that Core Image supplied on macOS are reimplemented here. They follow Core Image's documented behavior, but their output is not bit-identical:

- **Gaussian blur**: separable, σ = radius, kernel truncated at 3σ.
- **Box blur**: box over [−(r+½), r+½], with fractional coverage at the ends, so r = 1 is a 3-pixel box and r = 0 is the identity.
- **Median**: 3×3, per channel.
- **Unsharp mask**: `src + intensity·(src − Gaussian(src, radius))`.
- **Noise reduction**: a threshold filter. Neighbors whose luminance differs from the center pixel by less than the noise level are averaged. Pixels on edges are sharpened by `sharpness` times a 3×3 unsharp mask.

Numbers use the current Windows locale; for example, `0,5` in a Spanish locale. The parameter fields accept either the locale's decimal separator or a period.

## Not ported

- **File associations and window restoration.** macOS registers LineScope as an image viewer and restores pattern windows on relaunch. To open a file on Windows, pass it on the command line (`LineScope.exe image.png`) or drag it onto a window.
- **The literate program and the theory document.** `LiterateP/` and `Docs/` describe and link to the Swift sources. The C# files mirror those sources file by file, so the explanations still apply.
