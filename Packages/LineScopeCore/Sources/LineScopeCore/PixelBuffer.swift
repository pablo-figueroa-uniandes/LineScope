import CoreGraphics
import Foundation

/// A color sample with straight (non-premultiplied) alpha, sRGB-encoded components in 0...1.
public struct RGBA: Sendable, Equatable {
    public var r: Float
    public var g: Float
    public var b: Float
    public var a: Float

    public init(r: Float, g: Float, b: Float, a: Float = 1) {
        self.r = r
        self.g = g
        self.b = b
        self.a = a
    }
}

/// Interleaved RGBA Float32 image. Row 0 is the top row.
/// Components are sRGB-encoded (not linearized) with straight alpha, so values match what is displayed.
public struct PixelBuffer: Sendable, Equatable {
    public let width: Int
    public let height: Int
    public var data: [Float]

    public init(width: Int, height: Int, data: [Float]) {
        precondition(width > 0 && height > 0 && data.count == width * height * 4)
        self.width = width
        self.height = height
        self.data = data
    }

    public init(width: Int, height: Int, fill: RGBA = RGBA(r: 0, g: 0, b: 0, a: 1)) {
        var data = [Float](repeating: 0, count: width * height * 4)
        for i in stride(from: 0, to: data.count, by: 4) {
            data[i] = fill.r
            data[i + 1] = fill.g
            data[i + 2] = fill.b
            data[i + 3] = fill.a
        }
        self.init(width: width, height: height, data: data)
    }

    public subscript(x: Int, y: Int) -> RGBA {
        get {
            let i = (y * width + x) * 4
            return RGBA(r: data[i], g: data[i + 1], b: data[i + 2], a: data[i + 3])
        }
        set {
            let i = (y * width + x) * 4
            data[i] = newValue.r
            data[i + 1] = newValue.g
            data[i + 2] = newValue.b
            data[i + 3] = newValue.a
        }
    }

    // MARK: Premultiplication (used by resampling and Core Image, which expect premultiplied data)

    func premultiplied() -> [Float] {
        var out = data
        for i in stride(from: 0, to: out.count, by: 4) {
            let a = out[i + 3]
            out[i] *= a
            out[i + 1] *= a
            out[i + 2] *= a
        }
        return out
    }

    static func fromPremultiplied(width: Int, height: Int, _ p: [Float]) -> PixelBuffer {
        var out = p
        for i in stride(from: 0, to: out.count, by: 4) {
            let a = min(max(out[i + 3], 0), 1)
            out[i + 3] = a
            if a > 1e-6 {
                out[i] = min(max(out[i] / a, 0), 1)
                out[i + 1] = min(max(out[i + 1] / a, 0), 1)
                out[i + 2] = min(max(out[i + 2] / a, 0), 1)
            } else {
                out[i] = 0
                out[i + 1] = 0
                out[i + 2] = 0
            }
        }
        return PixelBuffer(width: width, height: height, data: out)
    }
}

// MARK: - Core Graphics bridging

extension PixelBuffer {
    static let sRGB = CGColorSpace(name: CGColorSpace.sRGB)!

    /// Decodes any CGImage into 8-bit sRGB and converts it to a float buffer.
    public init?(cgImage: CGImage) {
        let w = cgImage.width
        let h = cgImage.height
        guard w > 0, h > 0 else { return nil }
        var bytes = [UInt8](repeating: 0, count: w * h * 4)
        let drawn = bytes.withUnsafeMutableBytes { raw -> Bool in
            guard let ctx = CGContext(
                data: raw.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                space: PixelBuffer.sRGB, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
            ) else { return false }
            ctx.interpolationQuality = .none
            ctx.draw(cgImage, in: CGRect(x: 0, y: 0, width: w, height: h))
            return true
        }
        guard drawn else { return nil }
        var p = [Float](repeating: 0, count: w * h * 4)
        for i in 0..<p.count { p[i] = Float(bytes[i]) / 255 }
        self = PixelBuffer.fromPremultiplied(width: w, height: h, p)
    }

    /// Renders the buffer as an 8-bit premultiplied sRGB CGImage for display or export.
    public func makeCGImage() -> CGImage? {
        var bytes = [UInt8](repeating: 0, count: data.count)
        for i in stride(from: 0, to: data.count, by: 4) {
            let a = min(max(data[i + 3], 0), 1)
            bytes[i] = UInt8((min(max(data[i], 0), 1) * a * 255).rounded())
            bytes[i + 1] = UInt8((min(max(data[i + 1], 0), 1) * a * 255).rounded())
            bytes[i + 2] = UInt8((min(max(data[i + 2], 0), 1) * a * 255).rounded())
            bytes[i + 3] = UInt8((a * 255).rounded())
        }
        guard let provider = CGDataProvider(data: Data(bytes) as CFData) else { return nil }
        return CGImage(
            width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: width * 4,
            space: PixelBuffer.sRGB, bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent
        )
    }
}
