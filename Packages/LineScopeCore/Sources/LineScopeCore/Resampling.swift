import Foundation

public enum ResampleMethod: String, CaseIterable, Identifiable, Sendable {
    case nearest
    case tent
    case catmullRom
    case mitchell
    case lanczos3
    case sinc
    case area

    public var id: String { rawValue }

    public var displayName: String {
        switch self {
        case .nearest: "Nearest Neighbor"
        case .tent: "Tent (Bilinear)"
        case .catmullRom: "Catmull-Rom (Bicubic)"
        case .mitchell: "Mitchell-Netravali"
        case .lanczos3: "Lanczos-3"
        case .sinc: "Sinc (truncated, r=\(Int(Resampler.sincRadius)))"
        case .area: "Area Average"
        }
    }

    public var shortName: String {
        switch self {
        case .nearest: "Nearest"
        case .tent: "Tent"
        case .catmullRom: "Catmull-Rom"
        case .mitchell: "Mitchell"
        case .lanczos3: "Lanczos"
        case .sinc: "Sinc"
        case .area: "Area"
        }
    }
}

/// Separable resampling with explicit, textbook kernels.
///
/// Coordinates follow the pixel-center convention: input pixel `j` covers `[j, j+1)` and its center is `j + 0.5`.
/// Output pixel `i` maps to input position `(i + 0.5) / scale`.
/// - `nearest` picks one source pixel and does no prefiltering, so it aliases when downscaling.
/// - The kernel methods (tent, Catmull-Rom, Mitchell, Lanczos, sinc) widen their kernel by `1/scale`
///   when downscaling (antialiased).
/// - `area` weights source pixels by how much of the output pixel's footprint they cover.
public enum Resampler {
    /// Half-width of the truncated sinc kernel, in source pixels (before widening).
    public static let sincRadius = 8.0

    public static func outputSize(for size: Int, scale: Double) -> Int {
        max(1, Int((Double(size) * scale).rounded()))
    }

    public static func resample(_ src: PixelBuffer, scale: Double, method: ResampleMethod) -> PixelBuffer {
        resample(
            src,
            width: outputSize(for: src.width, scale: scale),
            height: outputSize(for: src.height, scale: scale),
            method: method
        )
    }

    public static func resample(_ src: PixelBuffer, width: Int, height: Int, method: ResampleMethod) -> PixelBuffer {
        let premul = src.premultiplied()
        let xContrib = contributions(inSize: src.width, outSize: width, method: method)
        let yContrib = contributions(inSize: src.height, outSize: height, method: method)

        // Horizontal pass: (src.width x src.height) -> (width x src.height)
        var tmp = [Float](repeating: 0, count: width * src.height * 4)
        premul.withUnsafeBufferPointer { s in
            tmp.withUnsafeMutableBufferPointer { t in
                for y in 0..<src.height {
                    let srcRow = y * src.width * 4
                    let dstRow = y * width * 4
                    for x in 0..<width {
                        let c = xContrib[x]
                        var r: Float = 0, g: Float = 0, b: Float = 0, a: Float = 0
                        for k in 0..<c.weights.count {
                            let w = c.weights[k]
                            let si = srcRow + (c.start + k) * 4
                            r += s[si] * w
                            g += s[si + 1] * w
                            b += s[si + 2] * w
                            a += s[si + 3] * w
                        }
                        let di = dstRow + x * 4
                        t[di] = r
                        t[di + 1] = g
                        t[di + 2] = b
                        t[di + 3] = a
                    }
                }
            }
        }

        // Vertical pass: (width x src.height) -> (width x height)
        var out = [Float](repeating: 0, count: width * height * 4)
        tmp.withUnsafeBufferPointer { t in
            out.withUnsafeMutableBufferPointer { o in
                let rowStride = width * 4
                for y in 0..<height {
                    let c = yContrib[y]
                    let dstRow = y * rowStride
                    for k in 0..<c.weights.count {
                        let w = c.weights[k]
                        let srcRow = (c.start + k) * rowStride
                        for i in 0..<rowStride {
                            o[dstRow + i] += t[srcRow + i] * w
                        }
                    }
                }
            }
        }

        return PixelBuffer.fromPremultiplied(width: width, height: height, out)
    }

    // MARK: - Kernels

    struct Contribution {
        var start: Int
        var weights: [Float]
    }

    static func contributions(inSize: Int, outSize: Int, method: ResampleMethod) -> [Contribution] {
        let scale = Double(outSize) / Double(inSize)
        var result: [Contribution] = []
        result.reserveCapacity(outSize)

        for i in 0..<outSize {
            let center = (Double(i) + 0.5) / scale
            switch method {
            case .nearest:
                let j = min(max(Int(center.rounded(.down)), 0), inSize - 1)
                result.append(Contribution(start: j, weights: [1]))

            case .area:
                // Output pixel footprint in input coordinates, exact coverage weights.
                let half = 0.5 / scale
                let lo = center - half
                let hi = center + half
                let first = max(0, Int(lo.rounded(.down)))
                let last = min(inSize - 1, Int(hi.rounded(.up)) - 1)
                var weights: [Float] = []
                if last >= first {
                    for j in first...last {
                        let overlap = min(hi, Double(j + 1)) - max(lo, Double(j))
                        weights.append(Float(max(0, overlap)))
                    }
                }
                result.append(normalized(start: first, weights: weights, fallback: center, inSize: inSize))

            case .tent, .catmullRom, .mitchell, .lanczos3, .sinc:
                let filterScale = max(1, 1 / scale)
                let support = kernelSupport(method) * filterScale
                let first = max(0, Int((center - support).rounded(.down)))
                let last = min(inSize - 1, Int((center + support).rounded(.up)))
                var weights: [Float] = []
                if last >= first {
                    for j in first...last {
                        let x = (Double(j) + 0.5 - center) / filterScale
                        weights.append(Float(kernel(method, x)))
                    }
                }
                result.append(normalized(start: first, weights: weights, fallback: center, inSize: inSize))
            }
        }
        return result
    }

    private static func normalized(start: Int, weights: [Float], fallback center: Double, inSize: Int) -> Contribution {
        // Trim zero weights at both ends to keep inner loops short.
        var first = 0
        var last = weights.count - 1
        while first <= last && weights[first] == 0 { first += 1 }
        while last >= first && weights[last] == 0 { last -= 1 }
        guard first <= last else {
            let j = min(max(Int(center.rounded(.down)), 0), inSize - 1)
            return Contribution(start: j, weights: [1])
        }
        let trimmed = Array(weights[first...last])
        let sum = trimmed.reduce(0, +)
        guard abs(sum) > 1e-12 else {
            let j = min(max(Int(center.rounded(.down)), 0), inSize - 1)
            return Contribution(start: j, weights: [1])
        }
        return Contribution(start: start + first, weights: trimmed.map { $0 / sum })
    }

    static func kernelSupport(_ method: ResampleMethod) -> Double {
        switch method {
        case .nearest, .area: 0.5
        case .tent: 1
        case .catmullRom, .mitchell: 2
        case .lanczos3: 3
        case .sinc: sincRadius
        }
    }

    private static func sinc(_ x: Double) -> Double {
        if abs(x) < 1e-9 { return 1 }
        let px = Double.pi * x
        return sin(px) / px
    }

    /// Mitchell-Netravali / Keys family: (B, C) = (1/3, 1/3) is Mitchell, (0, 1/2) is Catmull-Rom.
    private static func bcSpline(_ ax: Double, b: Double, c: Double) -> Double {
        if ax < 1 {
            return ((12 - 9 * b - 6 * c) * ax * ax * ax + (-18 + 12 * b + 6 * c) * ax * ax + (6 - 2 * b)) / 6
        }
        if ax < 2 {
            return ((-b - 6 * c) * ax * ax * ax + (6 * b + 30 * c) * ax * ax
                + (-12 * b - 48 * c) * ax + (8 * b + 24 * c)) / 6
        }
        return 0
    }

    static func kernel(_ method: ResampleMethod, _ x: Double) -> Double {
        let ax = abs(x)
        switch method {
        case .nearest, .area:
            return ax < 0.5 ? 1 : 0
        case .tent:
            return max(0, 1 - ax)
        case .catmullRom:
            return bcSpline(ax, b: 0, c: 0.5)
        case .mitchell:
            return bcSpline(ax, b: 1.0 / 3, c: 1.0 / 3)
        case .lanczos3:
            return ax < 3 ? sinc(ax) * sinc(ax / 3) : 0
        case .sinc:
            // Ideal low-pass, truncated (rectangular window): expect ringing near edges.
            return ax < sincRadius ? sinc(ax) : 0
        }
    }
}
