import Foundation

/// One step that derives a new image from an existing one.
public enum ImageOperation: Sendable, Equatable {
    case filter(FilterKind)
    case resample(ResampleMethod, scale: Double)

    public var displayName: String {
        switch self {
        case .filter(let f): f.displayName
        case .resample(let m, let s): "\(m.shortName) ×\(fmt(s))"
        }
    }

    public func apply(to buffer: PixelBuffer) -> PixelBuffer {
        switch self {
        case .filter(let f): Filters.apply(f, to: buffer)
        case .resample(let m, let s): Resampler.resample(buffer, scale: s, method: m)
        }
    }
}
