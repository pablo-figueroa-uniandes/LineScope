import Foundation

/// A point relative to the image extent: (0,0) is the top-left corner, (1,1) the bottom-right corner.
public struct NormalizedPoint: Sendable, Hashable {
    public var x: Double
    public var y: Double

    public init(x: Double, y: Double) {
        self.x = x
        self.y = y
    }

    public func clamped() -> NormalizedPoint {
        NormalizedPoint(x: min(max(x, 0), 1), y: min(max(y, 0), 1))
    }
}

/// The selection line, in normalized coordinates so it lands on the same content in every resampled copy.
public struct LineSegment: Sendable, Hashable {
    public var start: NormalizedPoint
    public var end: NormalizedPoint

    public init(start: NormalizedPoint, end: NormalizedPoint) {
        self.start = start
        self.end = end
    }

    /// Full-width horizontal line at mid-height.
    public static let initial = LineSegment(start: .init(x: 0, y: 0.5), end: .init(x: 1, y: 0.5))

    public func pixelLength(width: Int, height: Int) -> Double {
        let dx = (end.x - start.x) * Double(width)
        let dy = (end.y - start.y) * Double(height)
        return (dx * dx + dy * dy).squareRoot()
    }
}

public struct LineSamples: Sendable {
    /// Position along the line, 0 at `start` and 1 at `end`.
    public var positions: [Double]
    public var colors: [RGBA]
    /// Line length in the sampled image's pixels.
    public var pixelLength: Double
    /// Distance between consecutive samples, in pixels (≈ 1).
    public var spacing: Double
}

public enum LineSampler {
    /// Samples the line in the buffer's own pixel grid: `ceil(length) + 1` bilinear samples,
    /// so a half-size copy yields about half as many samples.
    public static func sample(_ buffer: PixelBuffer, along line: LineSegment) -> LineSamples {
        let w = Double(buffer.width)
        let h = Double(buffer.height)
        let length = line.pixelLength(width: buffer.width, height: buffer.height)
        let count = max(2, Int(length.rounded(.up)) + 1)
        let x0 = line.start.x * w, y0 = line.start.y * h
        let x1 = line.end.x * w, y1 = line.end.y * h

        var positions = [Double](repeating: 0, count: count)
        var colors = [RGBA](repeating: RGBA(r: 0, g: 0, b: 0, a: 0), count: count)
        for i in 0..<count {
            let t = Double(i) / Double(count - 1)
            positions[i] = t
            colors[i] = bilinear(buffer, x: x0 + (x1 - x0) * t, y: y0 + (y1 - y0) * t)
        }
        return LineSamples(
            positions: positions, colors: colors, pixelLength: length,
            spacing: length / Double(count - 1)
        )
    }

    /// Bilinear interpolation at a continuous position (pixel centers at +0.5), edges clamped.
    public static func bilinear(_ buffer: PixelBuffer, x: Double, y: Double) -> RGBA {
        let fx = x - 0.5
        let fy = y - 0.5
        let ix = Int(fx.rounded(.down))
        let iy = Int(fy.rounded(.down))
        let tx = Float(fx - Double(ix))
        let ty = Float(fy - Double(iy))
        let x0 = min(max(ix, 0), buffer.width - 1)
        let x1 = min(max(ix + 1, 0), buffer.width - 1)
        let y0 = min(max(iy, 0), buffer.height - 1)
        let y1 = min(max(iy + 1, 0), buffer.height - 1)

        let d = buffer.data
        func mix(_ c: Int) -> Float {
            let a = d[(y0 * buffer.width + x0) * 4 + c]
            let b = d[(y0 * buffer.width + x1) * 4 + c]
            let e = d[(y1 * buffer.width + x0) * 4 + c]
            let f = d[(y1 * buffer.width + x1) * 4 + c]
            let top = a + (b - a) * tx
            let bottom = e + (f - e) * tx
            return top + (bottom - top) * ty
        }
        return RGBA(r: mix(0), g: mix(1), b: mix(2), a: mix(3))
    }
}
