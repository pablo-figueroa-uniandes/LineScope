import XCTest
@testable import LineScopeCore

private func gradient(width: Int, height: Int) -> PixelBuffer {
    var buf = PixelBuffer(width: width, height: height)
    for y in 0..<height {
        for x in 0..<width {
            buf[x, y] = RGBA(r: Float(x) / Float(width), g: Float(y) / Float(height), b: 0.25, a: 1)
        }
    }
    return buf
}

final class SpectrumTests: XCTestCase {
    func testSinePeak() {
        let n = 256
        let k = 16
        let values = (0..<n).map { Float(sin(2 * Double.pi * Double(k) * Double($0) / Double(n))) }
        let s = SpectrumAnalyzer.compute(values, spacing: 1, window: .none, removeMean: true)
        XCTAssertEqual(s.fftSize, 256)
        XCTAssertEqual(s.peakIndex, k)
        XCTAssertEqual(s.amplitudes[k], 1, accuracy: 1e-3)
        XCTAssertEqual(s.cyclesPerPixel[k], Double(k) / Double(n), accuracy: 1e-9)
        XCTAssertEqual(s.cyclesPerPixel.last!, 0.5, accuracy: 1e-9)
    }

    func testDCAmplitude() {
        let s = SpectrumAnalyzer.compute([Float](repeating: 0.3, count: 100), spacing: 1, window: .none, removeMean: false)
        XCTAssertEqual(s.fftSize, 128)
        XCTAssertEqual(s.amplitudes[0], 0.3, accuracy: 1e-4)
    }
}

final class ResamplingTests: XCTestCase {
    func testKernelShapes() {
        // Interpolating kernels are 1 at 0 and 0 at nonzero integers.
        for method in [ResampleMethod.tent, .catmullRom, .lanczos3, .sinc] {
            XCTAssertEqual(Resampler.kernel(method, 0), 1, accuracy: 1e-12, "\(method)")
            for k in 1...3 {
                XCTAssertEqual(Resampler.kernel(method, Double(k)), 0, accuracy: 1e-12, "\(method) at \(k)")
            }
        }
        // Mitchell-Netravali (B = C = 1/3) is approximating: k(0) = 8/9, k(1) = 1/18.
        XCTAssertEqual(Resampler.kernel(.mitchell, 0), 8.0 / 9, accuracy: 1e-12)
        XCTAssertEqual(Resampler.kernel(.mitchell, 1), 1.0 / 18, accuracy: 1e-12)
        XCTAssertEqual(Resampler.kernel(.mitchell, 2), 0, accuracy: 1e-12)
        XCTAssertEqual(Resampler.kernel(.catmullRom, 0.5), 0.5625, accuracy: 1e-12)
        // Sinc is truncated at its radius.
        XCTAssertEqual(Resampler.kernel(.sinc, Resampler.sincRadius + 0.5), 0)
    }

    func testIdentityAtScaleOne() {
        let src = gradient(width: 17, height: 9)
        // Mitchell is approximating (slightly smoothing), so it is not an identity at ×1.
        for method in ResampleMethod.allCases where method != .mitchell {
            let out = Resampler.resample(src, scale: 1, method: method)
            XCTAssertEqual(out.width, 17)
            XCTAssertEqual(out.height, 9)
            for (a, b) in zip(out.data, src.data) {
                XCTAssertEqual(a, b, accuracy: 1e-5, "\(method)")
            }
        }
    }

    func testAreaHalvesAverages() {
        var src = PixelBuffer(width: 4, height: 1)
        let values: [Float] = [0, 1, 0.2, 0.6]
        for (x, v) in values.enumerated() { src[x, 0] = RGBA(r: v, g: v, b: v) }
        let out = Resampler.resample(src, scale: 0.5, method: .area)
        XCTAssertEqual(out.width, 2)
        XCTAssertEqual(out[0, 0].r, 0.5, accuracy: 1e-5)
        XCTAssertEqual(out[1, 0].r, 0.4, accuracy: 1e-5)
    }

    func testConstantImageStaysConstant() {
        let src = PixelBuffer(width: 13, height: 7, fill: RGBA(r: 0.4, g: 0.5, b: 0.6))
        for method in ResampleMethod.allCases {
            for scale in [0.3, 0.5, 2.0, 3.7] {
                let out = Resampler.resample(src, scale: scale, method: method)
                for i in stride(from: 0, to: out.data.count, by: 4) {
                    XCTAssertEqual(out.data[i], 0.4, accuracy: 1e-4, "\(method) \(scale)")
                }
            }
        }
    }
}

final class ColorTests: XCTestCase {
    func testHSLRoundTrip() {
        for c in [RGBA(r: 0.2, g: 0.7, b: 0.4), RGBA(r: 1, g: 0, b: 0), RGBA(r: 0.1, g: 0.1, b: 0.9), RGBA(r: 0.5, g: 0.5, b: 0.5)] {
            let hsl = ColorConversion.hsl(from: c)
            let back = ColorConversion.rgb(h: hsl.h, s: hsl.s, l: hsl.l)
            XCTAssertEqual(back.r, c.r, accuracy: 1e-5)
            XCTAssertEqual(back.g, c.g, accuracy: 1e-5)
            XCTAssertEqual(back.b, c.b, accuracy: 1e-5)
        }
        XCTAssertEqual(ColorConversion.hsl(from: RGBA(r: 0, g: 0, b: 1)).h, 240, accuracy: 1e-4)
    }

    func testCMYKRoundTrip() {
        for c in [RGBA(r: 0.2, g: 0.7, b: 0.4), RGBA(r: 0, g: 0, b: 0), RGBA(r: 1, g: 1, b: 1)] {
            let v = ColorConversion.cmyk(from: c)
            let back = ColorConversion.rgb(c: v.c, m: v.m, y: v.y, k: v.k)
            XCTAssertEqual(back.r, c.r, accuracy: 1e-5)
            XCTAssertEqual(back.g, c.g, accuracy: 1e-5)
            XCTAssertEqual(back.b, c.b, accuracy: 1e-5)
        }
    }
}

final class LineSamplerTests: XCTestCase {
    func testSampleCountScalesWithResample() {
        let src = gradient(width: 200, height: 100)
        let half = Resampler.resample(src, scale: 0.5, method: .tent)
        let a = LineSampler.sample(src, along: .initial)
        let b = LineSampler.sample(half, along: .initial)
        XCTAssertEqual(a.colors.count, 201)
        XCTAssertEqual(b.colors.count, 101)
        XCTAssertEqual(a.spacing, 1, accuracy: 1e-9)
    }

    func testSamplesFollowContent() {
        let src = gradient(width: 100, height: 100)
        let line = LineSegment(start: .init(x: 0.505, y: 0.1), end: .init(x: 0.505, y: 0.9))
        let s = LineSampler.sample(src, along: line)
        // Vertical line through pixel column 50: red is constant, green increases.
        XCTAssertEqual(s.colors.first!.r, 0.5, accuracy: 1e-4)
        XCTAssertEqual(s.colors.last!.r, 0.5, accuracy: 1e-4)
        XCTAssertLessThan(s.colors.first!.g, s.colors.last!.g)
    }
}

final class CGBridgeTests: XCTestCase {
    func testCGImageRoundTrip() throws {
        let src = gradient(width: 32, height: 16)
        let cg = try XCTUnwrap(src.makeCGImage())
        let back = try XCTUnwrap(PixelBuffer(cgImage: cg))
        XCTAssertEqual(back.width, 32)
        XCTAssertEqual(back.height, 16)
        for (a, b) in zip(back.data, src.data) {
            XCTAssertEqual(a, b, accuracy: 1.0 / 255 + 1e-4)
        }
    }

    func testFiltersKeepSize() {
        let src = gradient(width: 24, height: 12)
        let filters: [FilterKind] = [.gaussianBlur(radius: 2), .boxBlur(radius: 2), .median, .unsharpMask(radius: 2, intensity: 0.5),
                                     .noiseReduction(level: 0.02, sharpness: 0.4), .sobel, .laplacian, .emboss, .grayscale, .invert]
        for f in filters {
            let out = Filters.apply(f, to: src)
            XCTAssertEqual(out.width, 24, f.displayName)
            XCTAssertEqual(out.height, 12, f.displayName)
        }
        let blurred = Filters.apply(.gaussianBlur(radius: 1), to: src)
        // Horizontal gradient stays roughly monotone and in range after blur.
        XCTAssertLessThan(blurred[2, 6].r, blurred[20, 6].r)
    }
}

final class TestPatternTests: XCTestCase {
    func testAllPatternsGenerateRequestedSize() {
        for kind in TestPatternKind.allCases {
            let buf = TestPatternGenerator.make(TestPatternSpec(kind: kind, width: 64, height: 48))
            XCTAssertEqual(buf.width, 64, kind.displayName)
            XCTAssertEqual(buf.height, 48, kind.displayName)
            XCTAssertTrue(buf.data.allSatisfy { $0 >= 0 && $0 <= 1 }, kind.displayName)
        }
    }

    func testSineGratingPeaksAtItsFrequency() {
        let spec = TestPatternSpec(kind: .sineGrating, width: 256, height: 8, parameter: 0.125)
        let samples = LineSampler.sample(TestPatternGenerator.make(spec), along: .initial)
        let values = samples.colors.map(\.r)
        let s = SpectrumAnalyzer.compute(values, spacing: samples.spacing, window: .hann, removeMean: true)
        let peak = try! XCTUnwrap(s.peakIndex)
        XCTAssertEqual(s.cyclesPerPixel[peak], 0.125, accuracy: 1.0 / Double(s.fftSize) + 1e-9)
    }

    func testCheckerboardAndNoiseDeterminism() {
        let board = TestPatternGenerator.make(TestPatternSpec(kind: .checkerboard, width: 8, height: 8, parameter: 2))
        XCTAssertEqual(board[0, 0].r, 1)
        XCTAssertEqual(board[2, 0].r, 0)
        XCTAssertEqual(board[2, 2].r, 1)
        let a = TestPatternGenerator.make(TestPatternSpec(kind: .whiteNoise, width: 16, height: 16, parameter: 7))
        let b = TestPatternGenerator.make(TestPatternSpec(kind: .whiteNoise, width: 16, height: 16, parameter: 7))
        XCTAssertEqual(a, b)
    }
}
