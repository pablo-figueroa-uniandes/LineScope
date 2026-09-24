import LineScopeCore
import SwiftUI

/// Separate window describing the focused variant of the most recently active document window.
struct InspectorView: View {
    static let windowID = "inspector"

    private var active = ActiveSession.shared

    var body: some View {
        Group {
            if let session = active.session {
                InspectorContent(session: session)
            } else {
                ContentUnavailableView("No Image", systemImage: "photo", description: Text("Open an image to inspect it."))
            }
        }
        .frame(minWidth: 300, idealWidth: 340, minHeight: 320, idealHeight: 760)
    }
}

private struct InspectorContent: View {
    let session: Session

    var body: some View {
        let variant = session.focusedVariant
        let profile = session.profile(for: variant)
        let spectrum = session.spectrum(for: profile)
        let info = session.sourceInfo
        let original = session.original.buffer

        Form {
            Section("File") {
                row("Name", session.fileName)
                row("Type", info.fileType)
                row("Stored size", "\(info.pixelWidth) × \(info.pixelHeight) px")
                row("Bit depth", "\(info.bitsPerComponent) bpc, \(info.bitsPerPixel) bpp")
                row("Color space", info.colorSpaceName)
                row("Alpha", info.hasAlpha ? "Yes" : "No")
                if let dpi = info.dpi { row("Resolution", "\(num(dpi, 0)) dpi") }
            }

            Section("Focused Image") {
                row("Tab", variant.name)
                row("Size", "\(variant.buffer.width) × \(variant.buffer.height) px")
                row(
                    "Scale vs. original",
                    "×\(num(Double(variant.buffer.width) / Double(original.width), 4)) · ×\(num(Double(variant.buffer.height) / Double(original.height), 4))"
                )
                row("Working format", "RGBA Float32, sRGB-encoded")
            }

            Section("Operation Chain") {
                Text("Original").foregroundStyle(.secondary)
                ForEach(Array(variant.lineage.enumerated()), id: \.offset) { i, op in
                    Text("\(i + 1). \(op.displayName)")
                }
            }

            Section("Line") {
                let w = Double(variant.buffer.width), h = Double(variant.buffer.height)
                let l = session.line
                row("Start", "(\(num(l.start.x * w, 1)), \(num(l.start.y * h, 1))) px")
                row("End", "(\(num(l.end.x * w, 1)), \(num(l.end.y * h, 1))) px")
                row("Length", "\(num(profile.samples.pixelLength, 1)) px")
                row("Samples", "\(profile.values.count), spacing \(num(profile.samples.spacing, 3)) px")
            }

            Section("\(session.colorModel.displayName) · \(session.channelName)") {
                let v = profile.values
                row("Min", num(Double(v.min() ?? 0), 4))
                row("Max", num(Double(v.max() ?? 0), 4))
                row("Mean", num(Double(v.reduce(0, +)) / Double(max(v.count, 1)), 4))
                if let k = spectrum.peakIndex {
                    row("Peak frequency", "\(num(spectrum.cyclesPerPixel[k], 4)) c/px · \(num(spectrum.cyclesPerLine[k], 2)) c/line")
                    row("Peak amplitude", num(spectrum.amplitudes[k], 4))
                }
            }
        }
        .formStyle(.grouped)
    }

    private func row(_ label: String, _ value: String) -> some View {
        LabeledContent(label) {
            Text(value).textSelection(.enabled).multilineTextAlignment(.trailing)
        }
    }

    private func num(_ v: Double, _ digits: Int) -> String {
        v.formatted(.number.precision(.fractionLength(digits)))
    }
}
