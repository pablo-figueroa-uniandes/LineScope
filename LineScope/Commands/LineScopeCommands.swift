import LineScopeCore
import SwiftUI

struct LineScopeCommands: Commands {
    @FocusedValue(\.session) private var focusedSession
    @Environment(\.openWindow) private var openWindow

    /// Falls back to the last active document so the menus keep working while the inspector is key.
    private var session: Session? { focusedSession ?? ActiveSession.shared.session }

    var body: some Commands {
        CommandGroup(after: .newItem) {
            Menu("New Test Pattern") {
                ForEach(TestPatternKind.allCases) { kind in
                    Button(kind.displayName) { openWindow(value: TestPatternSpec(kind: kind)) }
                }
                Divider()
                Button("Custom…") { openWindow(id: TestPatternGeneratorView.windowID) }
                    .keyboardShortcut("n", modifiers: [.command, .option])
            }
        }

        CommandGroup(after: .saveItem) {
            Button("Export Image…") { session?.exportFocused() }
                .keyboardShortcut("e", modifiers: [.command, .shift])
                .disabled(session == nil)
            Button("Close Tab") {
                if let session { session.closeVariant(session.focusedVariantID) }
            }
            .keyboardShortcut(.delete, modifiers: [.command])
            .disabled(session?.focusedVariant.isOriginal ?? true)
        }

        CommandMenu("Filter") {
            Group {
                Button("Gaussian Blur…") { session?.pendingRequest = .gaussianBlur }
                Button("Box Blur…") { session?.pendingRequest = .boxBlur }
                Button("Median 3×3") { session?.perform(.filter(.median)) }
                Button("Noise Reduction…") { session?.pendingRequest = .noiseReduction }
                Divider()
                Button("Sharpen (Unsharp Mask)…") { session?.pendingRequest = .unsharpMask }
                Button("Edge Detect (Sobel)") { session?.perform(.filter(.sobel)) }
                Button("Laplacian") { session?.perform(.filter(.laplacian)) }
                Button("Emboss") { session?.perform(.filter(.emboss)) }
                Divider()
                Button("Grayscale") { session?.perform(.filter(.grayscale)) }
                Button("Invert") { session?.perform(.filter(.invert)) }
            }
            .disabled(session == nil)
        }

        CommandMenu("Resample") {
            ForEach(ResampleMethod.allCases) { method in
                Button("\(method.displayName)…") { session?.pendingRequest = .resample(method) }
            }
            .disabled(session == nil)
        }

        CommandGroup(before: .toolbar) {
            ForEach(Zone.allCases) { zone in
                Button("Split \(zone.title)") { session?.split(zone) }
                    .disabled(session == nil)
            }
            ForEach(Zone.allCases) { zone in
                Button("Unsplit \(zone.title)") { session?.unsplit(zone) }
                    .disabled((session?.panes[zone.rawValue].count ?? 1) < 2)
            }
            Divider()
            Toggle("Actual Device Pixels", isOn: Binding(
                get: { session?.useDevicePixels ?? false },
                set: { session?.useDevicePixels = $0 }
            ))
            .disabled(session == nil)
            Button("Reset Line") { session?.line = .initial }
                .disabled(session == nil)
            Divider()
            Button("Show Inspector") { openWindow(id: InspectorView.windowID) }
                .keyboardShortcut("i", modifiers: [.command, .option])
            Divider()
        }
    }
}
