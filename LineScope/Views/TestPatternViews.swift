import LineScopeCore
import SwiftUI

/// Analysis window for a generated pattern. Generation runs once per window, not on every view update.
struct PatternWindow: View {
    let spec: TestPatternSpec
    @State private var document: ImageDocument?

    var body: some View {
        Group {
            if let document {
                MainView(document: document, fileName: spec.title)
            } else {
                ProgressView("Generating \(spec.kind.displayName)…")
                    .frame(minWidth: 720, minHeight: 560)
            }
        }
        .navigationTitle(spec.title)
        .task(id: spec) {
            let spec = spec
            document = await Task.detached(priority: .userInitiated) { ImageDocument(pattern: spec) }.value
        }
    }
}

/// Window for choosing a pattern, its size and parameter before creating it.
struct TestPatternGeneratorView: View {
    static let windowID = "pattern-generator"

    @Environment(\.openWindow) private var openWindow
    @State private var spec = TestPatternSpec(kind: .zonePlate)
    @State private var preview: CGImage?

    private static let sizes = [128, 256, 512, 1024, 2048]
    private static let maxSide = 8192
    private static let maxPreviewPixels = 2048 * 2048

    var body: some View {
        HStack(alignment: .top, spacing: 20) {
            Form {
                Picker("Pattern", selection: kindBinding) {
                    ForEach(TestPatternKind.allCases) { Text($0.displayName).tag($0) }
                }
                Text(spec.kind.purpose)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)

                LabeledContent("Size") {
                    HStack {
                        TextField("Width", value: $spec.width, format: .number.grouping(.never))
                            .frame(width: 64)
                        Text("×")
                        TextField("Height", value: $spec.height, format: .number.grouping(.never))
                            .frame(width: 64)
                        Menu("Presets") {
                            ForEach(Self.sizes, id: \.self) { s in
                                Button("\(s) × \(s)") { spec.width = s; spec.height = s }
                            }
                        }
                        .fixedSize()
                    }
                    .labelsHidden()
                }

                if let p = spec.kind.parameter {
                    LabeledContent(p.name) {
                        HStack {
                            if spec.kind != .whiteNoise {
                                Slider(value: $spec.parameter, in: p.range)
                            }
                            TextField(p.name, value: $spec.parameter, format: .number.precision(.fractionLength(0...4)))
                                .frame(width: 70)
                                .labelsHidden()
                        }
                    }
                }

                HStack {
                    Spacer()
                    Button("Create") { openWindow(value: clamped(spec)) }
                        .keyboardShortcut(.defaultAction)
                        .disabled(!isValid)
                }
            }
            .formStyle(.grouped)
            .frame(width: 380)

            previewView
        }
        .padding(16)
        .task(id: spec) {
            guard isValid, spec.width * spec.height <= Self.maxPreviewPixels else { preview = nil; return }
            // Generate the real pattern and show its central pixels at 1:1, so aliasing looks as it will
            // in the analysis window.
            let s = clamped(spec)
            try? await Task.sleep(for: .milliseconds(80))
            guard !Task.isCancelled else { return }
            preview = await Task.detached {
                guard let full = TestPatternGenerator.make(s).makeCGImage() else { return nil }
                let w = min(s.width, 256), h = min(s.height, 256)
                let rect = CGRect(x: (s.width - w) / 2, y: (s.height - h) / 2, width: w, height: h)
                return full.cropping(to: rect)
            }.value
        }
    }

    private var previewView: some View {
        VStack(spacing: 6) {
            Group {
                if let preview {
                    Image(decorative: preview, scale: 1).interpolation(.none)
                } else {
                    Color.clear
                }
            }
            .frame(width: 256, height: 256)
            .background(Color(nsColor: .underPageBackgroundColor))
            .clipShape(RoundedRectangle(cornerRadius: 4))
            Text(spec.width * spec.height <= Self.maxPreviewPixels
                 ? "Preview: center 256 × 256 px at 1:1" : "Too large to preview")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    /// Resets the parameter to the new pattern's default when the kind changes.
    private var kindBinding: Binding<TestPatternKind> {
        Binding(
            get: { spec.kind },
            set: { spec = TestPatternSpec(kind: $0, width: spec.width, height: spec.height) }
        )
    }

    private var isValid: Bool {
        (1...Self.maxSide).contains(spec.width) && (1...Self.maxSide).contains(spec.height)
    }

    private func clamped(_ s: TestPatternSpec) -> TestPatternSpec {
        var s = s
        if let p = s.kind.parameter {
            s.parameter = min(max(s.parameter, p.range.lowerBound), p.range.upperBound)
        }
        return s
    }
}
