import CoreImage
import CoreImage.CIFilterBuiltins
import Foundation

public enum FilterKind: Sendable, Equatable {
    case gaussianBlur(radius: Double)
    case boxBlur(radius: Double)
    case median
    case unsharpMask(radius: Double, intensity: Double)
    case noiseReduction(level: Double, sharpness: Double)
    case sobel
    case laplacian
    case emboss
    case grayscale
    case invert

    public var displayName: String {
        switch self {
        case .gaussianBlur(let r): "Gaussian r=\(fmt(r))"
        case .boxBlur(let r): "Box r=\(fmt(r))"
        case .median: "Median 3×3"
        case .unsharpMask(let r, let i): "Sharpen r=\(fmt(r)) i=\(fmt(i))"
        case .noiseReduction(let l, let s): "Denoise \(fmt(l))/\(fmt(s))"
        case .sobel: "Sobel"
        case .laplacian: "Laplacian"
        case .emboss: "Emboss"
        case .grayscale: "Grayscale"
        case .invert: "Invert"
        }
    }
}

func fmt(_ v: Double) -> String {
    v.formatted(.number.precision(.fractionLength(0...3)))
}

public enum Filters {
    public static func apply(_ filter: FilterKind, to src: PixelBuffer) -> PixelBuffer {
        switch filter {
        case .gaussianBlur(let radius):
            return applyCI(src) { img in
                let f = CIFilter.gaussianBlur()
                f.inputImage = img
                f.radius = Float(radius)
                return f.outputImage
            }
        case .boxBlur(let radius):
            return applyCI(src) { img in
                let f = CIFilter.boxBlur()
                f.inputImage = img
                f.radius = Float(radius)
                return f.outputImage
            }
        case .median:
            return applyCI(src) { img in
                let f = CIFilter.median()
                f.inputImage = img
                return f.outputImage
            }
        case .unsharpMask(let radius, let intensity):
            return applyCI(src) { img in
                let f = CIFilter.unsharpMask()
                f.inputImage = img
                f.radius = Float(radius)
                f.intensity = Float(intensity)
                return f.outputImage
            }
        case .noiseReduction(let level, let sharpness):
            return applyCI(src) { img in
                let f = CIFilter.noiseReduction()
                f.inputImage = img
                f.noiseLevel = Float(level)
                f.sharpness = Float(sharpness)
                return f.outputImage
            }
        case .sobel:
            return sobel(src)
        case .laplacian:
            // Absolute response so the edges are visible.
            return convolve3x3(src, kernel: [0, 1, 0, 1, -4, 1, 0, 1, 0], absolute: true)
        case .emboss:
            return convolve3x3(src, kernel: [-2, -1, 0, -1, 1, 1, 0, 1, 2], absolute: false)
        case .grayscale:
            return mapRGB(src) { c in
                let y = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b
                return RGBA(r: y, g: y, b: y, a: c.a)
            }
        case .invert:
            return mapRGB(src) { c in RGBA(r: 1 - c.r, g: 1 - c.g, b: 1 - c.b, a: c.a) }
        }
    }

    // MARK: - Core Image

    /// Works directly on encoded values (no color management) so results are consistent with the CPU filters.
    private static let context = CIContext(options: [
        .workingColorSpace: NSNull(),
        .outputColorSpace: NSNull(),
        .cacheIntermediates: false,
    ])

    private static func applyCI(_ src: PixelBuffer, _ build: (CIImage) -> CIImage?) -> PixelBuffer {
        let w = src.width
        let h = src.height
        let premul = src.premultiplied()
        let data = premul.withUnsafeBytes { Data($0) }
        let input = CIImage(
            bitmapData: data, bytesPerRow: w * 16, size: CGSize(width: w, height: h),
            format: .RGBAf, colorSpace: nil
        )
        let extent = input.extent
        guard let output = build(input.clampedToExtent())?.cropped(to: extent) else { return src }
        var result = [Float](repeating: 0, count: w * h * 4)
        result.withUnsafeMutableBytes { raw in
            context.render(
                output, toBitmap: raw.baseAddress!, rowBytes: w * 16, bounds: extent,
                format: .RGBAf, colorSpace: nil
            )
        }
        return PixelBuffer.fromPremultiplied(width: w, height: h, result)
    }

    // MARK: - CPU filters

    private static func mapRGB(_ src: PixelBuffer, _ f: (RGBA) -> RGBA) -> PixelBuffer {
        var out = src
        for y in 0..<src.height {
            for x in 0..<src.width {
                out[x, y] = f(src[x, y])
            }
        }
        return out
    }

    /// 3×3 convolution on RGB with clamped edges; alpha is preserved.
    static func convolve3x3(_ src: PixelBuffer, kernel k: [Float], absolute: Bool) -> PixelBuffer {
        var out = src
        let w = src.width
        let h = src.height
        src.data.withUnsafeBufferPointer { s in
            out.data.withUnsafeMutableBufferPointer { o in
                for y in 0..<h {
                    for x in 0..<w {
                        var acc: (Float, Float, Float) = (0, 0, 0)
                        for ky in -1...1 {
                            let yy = min(max(y + ky, 0), h - 1)
                            for kx in -1...1 {
                                let xx = min(max(x + kx, 0), w - 1)
                                let wgt = k[(ky + 1) * 3 + (kx + 1)]
                                let i = (yy * w + xx) * 4
                                acc.0 += s[i] * wgt
                                acc.1 += s[i + 1] * wgt
                                acc.2 += s[i + 2] * wgt
                            }
                        }
                        if absolute { acc = (abs(acc.0), abs(acc.1), abs(acc.2)) }
                        let i = (y * w + x) * 4
                        o[i] = min(max(acc.0, 0), 1)
                        o[i + 1] = min(max(acc.1, 0), 1)
                        o[i + 2] = min(max(acc.2, 0), 1)
                    }
                }
            }
        }
        return out
    }

    /// Per-channel Sobel gradient magnitude.
    static func sobel(_ src: PixelBuffer) -> PixelBuffer {
        let gx: [Float] = [-1, 0, 1, -2, 0, 2, -1, 0, 1]
        let gy: [Float] = [-1, -2, -1, 0, 0, 0, 1, 2, 1]
        var out = src
        let w = src.width
        let h = src.height
        src.data.withUnsafeBufferPointer { s in
            out.data.withUnsafeMutableBufferPointer { o in
                for y in 0..<h {
                    for x in 0..<w {
                        var sx: (Float, Float, Float) = (0, 0, 0)
                        var sy: (Float, Float, Float) = (0, 0, 0)
                        for ky in -1...1 {
                            let yy = min(max(y + ky, 0), h - 1)
                            for kx in -1...1 {
                                let xx = min(max(x + kx, 0), w - 1)
                                let ki = (ky + 1) * 3 + (kx + 1)
                                let i = (yy * w + xx) * 4
                                sx.0 += s[i] * gx[ki]; sy.0 += s[i] * gy[ki]
                                sx.1 += s[i + 1] * gx[ki]; sy.1 += s[i + 1] * gy[ki]
                                sx.2 += s[i + 2] * gx[ki]; sy.2 += s[i + 2] * gy[ki]
                            }
                        }
                        let i = (y * w + x) * 4
                        o[i] = min((sx.0 * sx.0 + sy.0 * sy.0).squareRoot(), 1)
                        o[i + 1] = min((sx.1 * sx.1 + sy.1 * sy.1).squareRoot(), 1)
                        o[i + 2] = min((sx.2 * sx.2 + sy.2 * sy.2).squareRoot(), 1)
                    }
                }
            }
        }
        return out
    }
}
