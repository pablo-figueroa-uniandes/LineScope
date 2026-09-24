import Foundation

public enum ColorModel: String, CaseIterable, Identifiable, Sendable {
    case rgba
    case hsl
    case cmyk

    public var id: String { rawValue }

    public var displayName: String {
        switch self {
        case .rgba: "RGBA"
        case .hsl: "HSL"
        case .cmyk: "CMYK"
        }
    }

    public var channels: [String] {
        switch self {
        case .rgba: ["Red", "Green", "Blue", "Alpha"]
        case .hsl: ["Hue", "Saturation", "Lightness"]
        case .cmyk: ["Cyan", "Magenta", "Yellow", "Black"]
        }
    }

    /// Plot range for a channel; hue is in degrees, everything else in 0...1.
    public func range(ofChannel channel: Int) -> ClosedRange<Double> {
        self == .hsl && channel == 0 ? 0...360 : 0...1
    }
}

public enum ColorConversion {
    // MARK: HSL (hue in degrees)

    public static func hsl(from c: RGBA) -> (h: Float, s: Float, l: Float) {
        let maxV = max(c.r, c.g, c.b)
        let minV = min(c.r, c.g, c.b)
        let l = (maxV + minV) / 2
        let d = maxV - minV
        guard d > 1e-7 else { return (0, 0, l) }
        let s = d / (1 - abs(2 * l - 1))
        var h: Float
        if maxV == c.r {
            h = ((c.g - c.b) / d).truncatingRemainder(dividingBy: 6)
        } else if maxV == c.g {
            h = (c.b - c.r) / d + 2
        } else {
            h = (c.r - c.g) / d + 4
        }
        h *= 60
        if h < 0 { h += 360 }
        return (h, min(max(s, 0), 1), l)
    }

    public static func rgb(h: Float, s: Float, l: Float, alpha: Float = 1) -> RGBA {
        let c = (1 - abs(2 * l - 1)) * s
        let hp = h / 60
        let x = c * (1 - abs(hp.truncatingRemainder(dividingBy: 2) - 1))
        let (r, g, b): (Float, Float, Float)
        switch hp {
        case ..<1: (r, g, b) = (c, x, 0)
        case ..<2: (r, g, b) = (x, c, 0)
        case ..<3: (r, g, b) = (0, c, x)
        case ..<4: (r, g, b) = (0, x, c)
        case ..<5: (r, g, b) = (x, 0, c)
        default: (r, g, b) = (c, 0, x)
        }
        let m = l - c / 2
        return RGBA(r: r + m, g: g + m, b: b + m, a: alpha)
    }

    // MARK: CMYK (naive device-independent conversion, K = 1 - max(R,G,B))

    public static func cmyk(from c: RGBA) -> (c: Float, m: Float, y: Float, k: Float) {
        let k = 1 - max(c.r, c.g, c.b)
        guard k < 1 - 1e-7 else { return (0, 0, 0, 1) }
        let d = 1 - k
        return ((1 - c.r - k) / d, (1 - c.g - k) / d, (1 - c.b - k) / d, k)
    }

    public static func rgb(c: Float, m: Float, y: Float, k: Float, alpha: Float = 1) -> RGBA {
        RGBA(r: (1 - c) * (1 - k), g: (1 - m) * (1 - k), b: (1 - y) * (1 - k), a: alpha)
    }

    // MARK: Channel extraction

    public static func value(of color: RGBA, model: ColorModel, channel: Int) -> Float {
        switch model {
        case .rgba:
            return [color.r, color.g, color.b, color.a][channel]
        case .hsl:
            let v = hsl(from: color)
            return [v.h, v.s, v.l][channel]
        case .cmyk:
            let v = cmyk(from: color)
            return [v.c, v.m, v.y, v.k][channel]
        }
    }
}
