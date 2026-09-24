import Charts
import LineScopeCore
import SwiftUI

/// Zone 2: the selected channel sampled along the line in this variant's own pixel grid.
struct ProfileChart: View {
    let variant: ImageVariant
    let session: Session

    var body: some View {
        let profile = session.profile(for: variant)
        let channel = session.channelName
        Chart {
            LinePlot(profile.points, x: .value("Position", \.x), y: .value(channel, \.y))
                .foregroundStyle(channelColor)
                .lineStyle(StrokeStyle(lineWidth: 1.2))
        }
        .chartXScale(domain: 0...1)
        .chartYScale(domain: session.colorModel.range(ofChannel: session.channelIndex))
        .chartXAxisLabel("Position along line (\(profile.samples.colors.count) samples)")
        .chartYAxisLabel(channel)
        .padding(10)
    }

    private var channelColor: Color {
        switch (session.colorModel, session.channelIndex) {
        case (.rgba, 0): .red
        case (.rgba, 1): .green
        case (.rgba, 2): .blue
        case (.cmyk, 0): .cyan
        case (.cmyk, 1): .pink
        case (.cmyk, 2): .yellow
        case (.hsl, 0): .purple
        case (.hsl, 1): .orange
        default: .primary
        }
    }
}

/// Zone 3: amplitude spectrum of the zone-2 profile.
struct SpectrumChart: View {
    let variant: ImageVariant
    let session: Session

    var body: some View {
        let profile = session.profile(for: variant)
        let spectrum = session.spectrum(for: profile)
        let points = session.spectrumPoints(spectrum)
        let nyquist = nyquist(spectrum)
        Chart {
            LinePlot(points, x: .value("Frequency", \.frequency), y: .value("Amplitude", \.magnitude))
                .foregroundStyle(Color.accentColor)
                .lineStyle(StrokeStyle(lineWidth: 1.2))
            if let nyquist {
                RuleMark(x: .value("Nyquist", nyquist))
                    .foregroundStyle(.secondary)
                    .lineStyle(StrokeStyle(lineWidth: 1, dash: [4, 3]))
                    .annotation(position: .leading, alignment: .top) {
                        Text("Nyquist").font(.caption2).foregroundStyle(.secondary)
                    }
            }
        }
        .chartXScale(domain: 0...max(nyquist ?? 0.5, 1e-9))
        .chartYScale(domain: yDomain(points))
        .chartXAxisLabel("Frequency (\(session.frequencyAxis.displayName)), FFT size \(spectrum.fftSize)")
        .chartYAxisLabel(session.logMagnitude ? "Amplitude (dB)" : "Amplitude")
        .padding(10)
    }

    private func nyquist(_ s: SpectrumResult) -> Double? {
        guard s.fftSize > 0 else { return nil }
        switch session.frequencyAxis {
        case .cyclesPerPixel: return s.cyclesPerPixel.last
        case .cyclesPerLine: return s.cyclesPerLine.last
        }
    }

    private func yDomain(_ points: [SpectrumPoint]) -> ClosedRange<Double> {
        let top = points.map(\.magnitude).max() ?? 1
        if session.logMagnitude {
            return -100...max(0, (top / 10).rounded(.up) * 10)
        }
        return 0...max(top * 1.05, 1e-6)
    }
}
