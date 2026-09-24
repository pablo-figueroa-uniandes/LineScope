import CoreGraphics
import Foundation
import LineScopeCore

/// The original image or one derived copy. Each copy records the chain of operations back to the original.
struct ImageVariant: Identifiable {
    let id = UUID()
    let buffer: PixelBuffer
    let cgImage: CGImage
    let parentID: UUID?
    let operation: ImageOperation?
    /// Operations applied to the original, in order. Empty for the original.
    let lineage: [ImageOperation]

    var isOriginal: Bool { operation == nil }

    var name: String {
        lineage.isEmpty ? "Original" : lineage.map(\.displayName).joined(separator: " → ")
    }
}

/// Which kind of content a zone shows.
enum Zone: Int, CaseIterable, Identifiable {
    case images, profile, spectrum

    var id: Int { rawValue }

    var title: String {
        switch self {
        case .images: "Images"
        case .profile: "Line Profile"
        case .spectrum: "Spectrum"
        }
    }
}

/// One side-by-side pane inside a zone; it shows one variant at a time.
struct PaneState: Identifiable, Equatable {
    let id = UUID()
    var variantID: UUID
}

enum FrequencyAxis: String, CaseIterable, Identifiable {
    case cyclesPerPixel
    case cyclesPerLine

    var id: String { rawValue }
    var displayName: String { self == .cyclesPerPixel ? "cycles/pixel" : "cycles/line" }
}

/// Operations that need parameters before running; shown as a sheet.
enum OperationRequest: Identifiable {
    case gaussianBlur, boxBlur, unsharpMask, noiseReduction
    case resample(ResampleMethod)

    var id: String {
        switch self {
        case .gaussianBlur: "gaussian"
        case .boxBlur: "box"
        case .unsharpMask: "unsharp"
        case .noiseReduction: "noise"
        case .resample(let m): "resample-\(m.rawValue)"
        }
    }

    var title: String {
        switch self {
        case .gaussianBlur: "Gaussian Blur"
        case .boxBlur: "Box Blur"
        case .unsharpMask: "Sharpen (Unsharp Mask)"
        case .noiseReduction: "Noise Reduction"
        case .resample(let m): "Resample: \(m.displayName)"
        }
    }
}
