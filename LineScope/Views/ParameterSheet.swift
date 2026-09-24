import LineScopeCore
import SwiftUI

/// Collects parameters for a filter or resample, then runs it on the focused variant.
struct ParameterSheet: View {
    let request: OperationRequest
    let session: Session
    @Environment(\.dismiss) private var dismiss

    @State private var radius = 2.0
    @State private var intensity = 0.5
    @State private var noiseLevel = 0.02
    @State private var sharpness = 0.4
    @State private var scale = 0.5

    private static let maxOutputSide = 16_384

    var body: some View {
        let source = session.focusedVariant
        VStack(alignment: .leading, spacing: 14) {
            Text(request.title).font(.headline)
            Text("Applies to: \(source.name) (\(source.buffer.width)×\(source.buffer.height))")
                .font(.caption)
                .foregroundStyle(.secondary)

            Form { fields(source: source) }

            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Apply") {
                    session.perform(operation)
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(!isValid(source: source))
            }
        }
        .padding(20)
        .frame(width: 380)
    }

    @ViewBuilder
    private func fields(source: ImageVariant) -> some View {
        switch request {
        case .gaussianBlur, .boxBlur:
            number("Radius (px)", $radius, range: 0...100)
        case .unsharpMask:
            number("Radius (px)", $radius, range: 0...100)
            number("Intensity", $intensity, range: 0...10)
        case .noiseReduction:
            number("Noise level", $noiseLevel, range: 0...0.1)
            number("Sharpness", $sharpness, range: 0...2)
        case .resample:
            LabeledContent("Scale factor") {
                HStack {
                    TextField("Scale", value: $scale, format: .number.precision(.fractionLength(0...4)))
                        .frame(width: 80)
                    Menu("Presets") {
                        ForEach([0.25, 0.5, 0.75, 1.5, 2, 4], id: \.self) { s in
                            Button("×\(s.formatted())") { scale = s }
                        }
                    }
                    .fixedSize()
                }
            }
            LabeledContent("Result") {
                Text("\(Resampler.outputSize(for: source.buffer.width, scale: scale)) × \(Resampler.outputSize(for: source.buffer.height, scale: scale)) px")
                    .monospacedDigit()
            }
        }
    }

    private func number(_ label: String, _ value: Binding<Double>, range: ClosedRange<Double>) -> some View {
        LabeledContent(label) {
            HStack {
                Slider(value: value, in: range)
                TextField(label, value: value, format: .number.precision(.fractionLength(0...3)))
                    .frame(width: 60)
                    .labelsHidden()
            }
        }
    }

    private func isValid(source: ImageVariant) -> Bool {
        guard case .resample = request else { return true }
        let w = Resampler.outputSize(for: source.buffer.width, scale: scale)
        let h = Resampler.outputSize(for: source.buffer.height, scale: scale)
        return scale > 0 && max(w, h) <= Self.maxOutputSide
    }

    private var operation: ImageOperation {
        switch request {
        case .gaussianBlur: .filter(.gaussianBlur(radius: radius))
        case .boxBlur: .filter(.boxBlur(radius: radius))
        case .unsharpMask: .filter(.unsharpMask(radius: radius, intensity: intensity))
        case .noiseReduction: .filter(.noiseReduction(level: noiseLevel, sharpness: sharpness))
        case .resample(let method): .resample(method, scale: scale)
        }
    }
}
