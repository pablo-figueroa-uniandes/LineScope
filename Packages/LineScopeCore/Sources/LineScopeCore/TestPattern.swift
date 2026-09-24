import Foundation

/// Synthetic images commonly used to evaluate resampling and filtering.
public enum TestPatternKind: String, CaseIterable, Identifiable, Codable, Sendable {
    case zonePlate
    case linearChirp
    case sineGrating
    case squareGrating
    case checkerboard
    case stepEdge
    case slantedEdge
    case impulses
    case siemensStar
    case whiteNoise
    case hueRamp

    public var id: String { rawValue }

    public var displayName: String {
        switch self {
        case .zonePlate: "Zone Plate"
        case .linearChirp: "Linear Chirp"
        case .sineGrating: "Sine Grating"
        case .squareGrating: "Square-Wave Bars"
        case .checkerboard: "Checkerboard"
        case .stepEdge: "Step Edge"
        case .slantedEdge: "Slanted Edge"
        case .impulses: "Impulse Grid"
        case .siemensStar: "Siemens Star"
        case .whiteNoise: "White Noise"
        case .hueRamp: "Hue / Lightness Ramp"
        }
    }

    /// What the pattern is good for, shown in the generator window.
    public var purpose: String {
        switch self {
        case .zonePlate: "Circular chirp: frequency grows with radius up to the chosen maximum. Aliasing appears as false ring centers."
        case .linearChirp: "Horizontal sweep from 0 to the chosen frequency. Put the line across it to read a filter's frequency response."
        case .sineGrating: "Single pure frequency along x. The spectrum should show one peak."
        case .squareGrating: "Bars with odd harmonics. Shows how filters treat harmonics near Nyquist."
        case .checkerboard: "Two-dimensional square wave. Small cells alias badly under nearest-neighbor."
        case .stepEdge: "One hard vertical edge. Reveals ringing (sinc, Lanczos) versus blur (tent, Mitchell)."
        case .slantedEdge: "Edge tilted a few degrees, the standard target for measuring sharpness (MTF)."
        case .impulses: "Isolated single pixels. Each dot shows the filter's point-spread function."
        case .siemensStar: "Radial spokes: spatial frequency rises toward the center, in every orientation."
        case .whiteNoise: "Uniform random values with a flat spectrum. The profile's spectrum shows the filter's transfer curve."
        case .hueRamp: "Hue across x, lightness down y. Use it with the HSL and CMYK channels."
        }
    }

    /// The single tunable parameter, or nil if the pattern has none.
    public var parameter: (name: String, defaultValue: Double, range: ClosedRange<Double>)? {
        switch self {
        case .zonePlate, .linearChirp: ("Max frequency (cycles/px)", 0.5, 0.01...1)
        case .sineGrating: ("Frequency (cycles/px)", 0.1, 0.001...1)
        case .squareGrating: ("Period (px)", 8, 2...512)
        case .checkerboard: ("Cell size (px)", 8, 1...512)
        case .slantedEdge: ("Angle (degrees)", 5, -45...45)
        case .impulses: ("Spacing (px)", 32, 2...1024)
        case .siemensStar: ("Spokes", 36, 4...360)
        case .whiteNoise: ("Seed", 1, 0...1_000_000)
        case .stepEdge, .hueRamp: nil
        }
    }
}

public struct TestPatternSpec: Codable, Hashable, Identifiable, Sendable {
    public var kind: TestPatternKind
    public var width: Int
    public var height: Int
    public var parameter: Double

    public var id: Self { self }

    public init(kind: TestPatternKind, width: Int = 512, height: Int = 512, parameter: Double? = nil) {
        self.kind = kind
        self.width = width
        self.height = height
        self.parameter = parameter ?? kind.parameter?.defaultValue ?? 0
    }

    public var title: String {
        var t = "\(kind.displayName) \(width)×\(height)"
        if let p = kind.parameter, kind != .whiteNoise || parameter != p.defaultValue {
            t += " (\(fmt(parameter)))"
        }
        return t
    }
}

public enum TestPatternGenerator {
    public static func make(_ spec: TestPatternSpec) -> PixelBuffer {
        let w = max(1, spec.width)
        let h = max(1, spec.height)
        let p = spec.parameter
        var buf = PixelBuffer(width: w, height: h)
        let cx = Double(w) / 2
        let cy = Double(h) / 2

        func gray(_ f: (Double, Double) -> Double) {
            for y in 0..<h {
                for x in 0..<w {
                    // Evaluate at pixel centers.
                    let v = Float(min(max(f(Double(x) + 0.5, Double(y) + 0.5), 0), 1))
                    buf[x, y] = RGBA(r: v, g: v, b: v)
                }
            }
        }

        switch spec.kind {
        case .zonePlate:
            // Local frequency k·r reaches p at r = max(w,h)/2 (the left/right edges of the midline).
            let k = p / (Double(max(w, h)) / 2)
            gray { x, y in
                let r2 = (x - cx) * (x - cx) + (y - cy) * (y - cy)
                return 0.5 + 0.5 * cos(Double.pi * k * r2)
            }
        case .linearChirp:
            // Phase 2π·p·x²/(2W): instantaneous frequency p·x/W.
            gray { x, _ in 0.5 + 0.5 * cos(Double.pi * p * x * x / Double(w)) }
        case .sineGrating:
            gray { x, _ in 0.5 + 0.5 * sin(2 * Double.pi * p * x) }
        case .squareGrating:
            let period = max(p, 1)
            gray { x, _ in (x - 0.5).truncatingRemainder(dividingBy: period) < period / 2 ? 1 : 0 }
        case .checkerboard:
            let cell = max(Int(p.rounded()), 1)
            gray { x, y in ((Int(x) / cell) + (Int(y) / cell)) % 2 == 0 ? 1 : 0 }
        case .stepEdge:
            gray { x, _ in x < cx ? 0.1 : 0.9 }
        case .slantedEdge:
            // Dark/light halves split by a line through the center tilted from vertical.
            let a = p * Double.pi / 180
            gray { x, y in ((x - cx) * cos(a) + (y - cy) * sin(a)) < 0 ? 0.1 : 0.9 }
        case .impulses:
            let s = max(Int(p.rounded()), 1)
            let off = s / 2
            gray { x, y in (Int(x) % s == off && Int(y) % s == off) ? 1 : 0 }
        case .siemensStar:
            let spokes = max(p.rounded(), 2)
            let radius = Double(min(w, h)) / 2
            gray { x, y in
                let dx = x - cx, dy = y - cy
                guard dx * dx + dy * dy <= radius * radius else { return 0.5 }
                let theta = atan2(dy, dx)
                return sin(spokes * theta) >= 0 ? 1 : 0
            }
        case .whiteNoise:
            var rng = SplitMix64(seed: UInt64(max(p, 0)))
            for y in 0..<h {
                for x in 0..<w {
                    let v = Float(Double(rng.next() >> 11) / Double(1 << 53))
                    buf[x, y] = RGBA(r: v, g: v, b: v)
                }
            }
        case .hueRamp:
            for y in 0..<h {
                for x in 0..<w {
                    let hue = Float((Double(x) + 0.5) / Double(w) * 360)
                    let light = Float(1 - (Double(y) + 0.5) / Double(h))
                    let c = ColorConversion.rgb(h: min(hue, 359.999), s: 1, l: light)
                    buf[x, y] = RGBA(r: min(max(c.r, 0), 1), g: min(max(c.g, 0), 1), b: min(max(c.b, 0), 1))
                }
            }
        }
        return buf
    }
}

/// Small deterministic generator so noise patterns are reproducible from their seed.
struct SplitMix64 {
    private var state: UInt64

    init(seed: UInt64) { state = seed }

    mutating func next() -> UInt64 {
        state &+= 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }
}
