import AppKit
import ImageIO
import LineScopeCore
import SwiftUI
import UniformTypeIdentifiers

struct Profile {
    struct Point: Identifiable {
        let id: Int
        let x: Double
        let y: Double
    }

    var points: [Point]
    var values: [Float]
    var samples: LineSamples
}

struct SpectrumPoint: Identifiable {
    let id: Int
    let frequency: Double
    let magnitude: Double
}

/// All state for one document window: the variants, the shared line, and view settings.
@MainActor @Observable
final class Session {
    let fileName: String
    let sourceInfo: SourceInfo

    private(set) var variants: [ImageVariant]
    var line: LineSegment = .initial

    var colorModel: ColorModel = .rgba {
        didSet { if channelIndex >= colorModel.channels.count { channelIndex = 0 } }
    }
    var channelIndex = 0
    var window: WindowFunction = .hann
    var removeMean = true
    var logMagnitude = true
    var frequencyAxis: FrequencyAxis = .cyclesPerPixel
    var useDevicePixels = false

    /// Panes per zone, indexed by `Zone.rawValue`.
    var panes: [[PaneState]]
    /// The variant operations apply to and the inspector describes.
    var focusedVariantID: UUID
    var pendingRequest: OperationRequest?
    private(set) var runningOperations = 0
    var errorMessage: String?

    init(document: ImageDocument, fileName: String) {
        let original = ImageVariant(
            buffer: document.buffer, cgImage: document.displayImage,
            parentID: nil, operation: nil, lineage: []
        )
        self.fileName = fileName
        self.sourceInfo = document.info
        self.variants = [original]
        self.focusedVariantID = original.id
        self.panes = Zone.allCases.map { _ in [PaneState(variantID: original.id)] }
    }

    var original: ImageVariant { variants[0] }

    var focusedVariant: ImageVariant {
        variant(focusedVariantID) ?? original
    }

    func variant(_ id: UUID) -> ImageVariant? {
        variants.first { $0.id == id }
    }

    var channelName: String { colorModel.channels[channelIndex] }

    // MARK: Panes

    func select(_ variantID: UUID, zone: Zone, pane: Int) {
        panes[zone.rawValue][pane].variantID = variantID
        focusedVariantID = variantID
    }

    func split(_ zone: Zone) {
        let shown = Set(panes[zone.rawValue].map(\.variantID))
        let next = variants.first { !shown.contains($0.id) } ?? focusedVariant
        panes[zone.rawValue].append(PaneState(variantID: next.id))
    }

    func unsplit(_ zone: Zone) {
        guard panes[zone.rawValue].count > 1 else { return }
        panes[zone.rawValue].removeLast()
    }

    // MARK: Variants

    func closeVariant(_ id: UUID) {
        guard let index = variants.firstIndex(where: { $0.id == id }), index > 0 else { return }
        variants.remove(at: index)
        for z in panes.indices {
            for p in panes[z].indices where panes[z][p].variantID == id {
                panes[z][p].variantID = original.id
            }
        }
        if focusedVariantID == id { focusedVariantID = original.id }
    }

    /// Applies an operation to the focused variant in the background and adds the result as a new tab.
    func perform(_ operation: ImageOperation) {
        let source = focusedVariant
        let input = source.buffer
        runningOperations += 1
        Task {
            let output = await Task.detached(priority: .userInitiated) {
                operation.apply(to: input)
            }.value
            runningOperations -= 1
            guard let cg = output.makeCGImage() else {
                errorMessage = "Could not render the result of \(operation.displayName)."
                return
            }
            let variant = ImageVariant(
                buffer: output, cgImage: cg, parentID: source.id,
                operation: operation, lineage: source.lineage + [operation]
            )
            variants.append(variant)
            show(variant.id)
        }
    }

    /// Shows a new variant in every zone: in the last pane when split (so the left pane keeps the
    /// comparison baseline), otherwise in the only pane.
    private func show(_ id: UUID) {
        for z in panes.indices {
            panes[z][panes[z].count - 1].variantID = id
        }
        focusedVariantID = id
    }

    // MARK: Analysis

    func profile(for variant: ImageVariant) -> Profile {
        let samples = LineSampler.sample(variant.buffer, along: line)
        let values = samples.colors.map {
            ColorConversion.value(of: $0, model: colorModel, channel: channelIndex)
        }
        let points = values.indices.map { Profile.Point(id: $0, x: samples.positions[$0], y: Double(values[$0])) }
        return Profile(points: points, values: values, samples: samples)
    }

    func spectrum(for profile: Profile) -> SpectrumResult {
        SpectrumAnalyzer.compute(
            profile.values, spacing: max(profile.samples.spacing, 1e-9),
            window: window, removeMean: removeMean
        )
    }

    func spectrumPoints(_ s: SpectrumResult) -> [SpectrumPoint] {
        let freqs = frequencyAxis == .cyclesPerPixel ? s.cyclesPerPixel : s.cyclesPerLine
        return s.amplitudes.indices.map { i in
            let a = s.amplitudes[i]
            return SpectrumPoint(
                id: i, frequency: freqs[i],
                magnitude: logMagnitude ? max(20 * log10(max(a, 1e-12)), Self.dBFloor) : a
            )
        }
    }

    static let dBFloor = -100.0

    // MARK: Export

    func exportFocused() {
        let variant = focusedVariant
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.png]
        let base = (fileName as NSString).deletingPathExtension
        let suffix = variant.isOriginal ? "original" : variant.name
            .replacingOccurrences(of: " → ", with: "_")
            .replacingOccurrences(of: "/", with: "-")
        panel.nameFieldStringValue = "\(base)-\(suffix).png"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        guard
            let dest = CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil)
        else {
            errorMessage = "Could not create \(url.lastPathComponent)."
            return
        }
        CGImageDestinationAddImage(dest, variant.cgImage, nil)
        if !CGImageDestinationFinalize(dest) {
            errorMessage = "Could not write \(url.lastPathComponent)."
        }
    }
}

extension FocusedValues {
    @Entry var session: Session?
}

/// The session of the most recently key document window, for the inspector window (which is itself
/// key while being used, so focused values would not reach it).
@MainActor @Observable
final class ActiveSession {
    static let shared = ActiveSession()
    var session: Session?
}
