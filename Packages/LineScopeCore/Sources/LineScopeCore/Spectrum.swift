import Accelerate
import Foundation

public enum WindowFunction: String, CaseIterable, Identifiable, Sendable {
    case none
    case hann

    public var id: String { rawValue }
    public var displayName: String { self == .none ? "Rectangular" : "Hann" }
}

public struct SpectrumResult: Sendable {
    /// Bin frequencies in cycles per pixel of the sampled image (Nyquist ≈ 0.5).
    public var cyclesPerPixel: [Double]
    /// Bin frequencies in cycles over the whole line; comparable across resampled copies.
    public var cyclesPerLine: [Double]
    /// Single-sided amplitude: a sine of amplitude A peaks at ≈ A.
    public var amplitudes: [Double]
    public var fftSize: Int
    public var sampleCount: Int

    public static let empty = SpectrumResult(cyclesPerPixel: [], cyclesPerLine: [], amplitudes: [], fftSize: 0, sampleCount: 0)

    /// Index of the largest non-DC bin.
    public var peakIndex: Int? {
        guard amplitudes.count > 1 else { return nil }
        return amplitudes.indices.dropFirst().max { amplitudes[$0] < amplitudes[$1] }
    }
}

public enum SpectrumAnalyzer {
    /// Real FFT of a profile, zero-padded to the next power of two.
    /// - Parameter spacing: distance between samples, in pixels.
    public static func compute(
        _ values: [Float], spacing: Double, window: WindowFunction, removeMean: Bool
    ) -> SpectrumResult {
        let n = values.count
        guard n >= 2 else { return .empty }
        let log2n = vDSP_Length(Int(ceil(log2(Double(n)))))
        let fftSize = 1 << Int(log2n)
        let half = fftSize / 2

        var signal = values
        if removeMean {
            let mean = vDSP.mean(signal)
            signal = vDSP.add(-mean, signal)
        }
        var windowSum = Float(n)
        if window == .hann {
            // Symmetric Hann over the n real samples.
            var w = [Float](repeating: 0, count: n)
            for i in 0..<n {
                w[i] = 0.5 - 0.5 * cos(2 * Float.pi * Float(i) / Float(n - 1))
            }
            signal = vDSP.multiply(signal, w)
            windowSum = vDSP.sum(w)
        }
        var padded = [Float](repeating: 0, count: fftSize)
        padded.replaceSubrange(0..<n, with: signal)

        var real = [Float](repeating: 0, count: half)
        var imag = [Float](repeating: 0, count: half)
        guard let setup = vDSP_create_fftsetup(log2n, FFTRadix(kFFTRadix2)) else { return .empty }
        defer { vDSP_destroy_fftsetup(setup) }

        real.withUnsafeMutableBufferPointer { rp in
            imag.withUnsafeMutableBufferPointer { ip in
                var split = DSPSplitComplex(realp: rp.baseAddress!, imagp: ip.baseAddress!)
                padded.withUnsafeBytes { raw in
                    vDSP_ctoz(raw.bindMemory(to: DSPComplex.self).baseAddress!, 2, &split, 1, vDSP_Length(half))
                }
                vDSP_fft_zrip(setup, &split, 1, log2n, FFTDirection(FFT_FORWARD))
            }
        }

        // vDSP's packed output is scaled by 2; DC is in real[0] and Nyquist in imag[0].
        var amplitudes = [Double](repeating: 0, count: half + 1)
        let norm = Double(max(windowSum, 1e-12))
        amplitudes[0] = abs(Double(real[0]) / 2) / norm
        amplitudes[half] = abs(Double(imag[0]) / 2) / norm
        for k in 1..<max(half, 1) {
            let re = Double(real[k]) / 2
            let im = Double(imag[k]) / 2
            amplitudes[k] = 2 * (re * re + im * im).squareRoot() / norm
        }

        let lineLength = spacing * Double(n - 1)
        var cpp = [Double](repeating: 0, count: half + 1)
        var cpl = [Double](repeating: 0, count: half + 1)
        for k in 0...half {
            cpp[k] = Double(k) / (Double(fftSize) * spacing)
            cpl[k] = cpp[k] * lineLength
        }
        return SpectrumResult(
            cyclesPerPixel: cpp, cyclesPerLine: cpl, amplitudes: amplitudes,
            fftSize: fftSize, sampleCount: n
        )
    }
}
