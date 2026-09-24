import LineScopeCore
import SwiftUI

/// One horizontal zone: a header with its controls, and one or more side-by-side panes.
struct ZoneView: View {
    let zone: Zone
    @Bindable var session: Session

    var body: some View {
        VStack(spacing: 0) {
            header
                .padding(.horizontal, 10)
                .padding(.vertical, 5)
                .background(.bar)
            Divider()
            HSplitView {
                ForEach(Array(session.panes[zone.rawValue].enumerated()), id: \.element.id) { index, _ in
                    PaneView(zone: zone, paneIndex: index, session: session)
                        .frame(minWidth: 200)
                }
            }
        }
    }

    private var header: some View {
        HStack(spacing: 12) {
            Text(zone.title).font(.headline)
            Spacer(minLength: 8)
            controls
            Divider().frame(height: 16)
            splitButtons
        }
        .controlSize(.small)
    }

    @ViewBuilder
    private var controls: some View {
        switch zone {
        case .images:
            Text(lineDescription)
                .font(.caption.monospacedDigit())
                .foregroundStyle(.secondary)
            Button("Reset Line") { session.line = .initial }
        case .profile:
            Picker("Color space", selection: $session.colorModel) {
                ForEach(ColorModel.allCases) { Text($0.displayName).tag($0) }
            }
            .pickerStyle(.segmented)
            .fixedSize()
            Picker("Channel", selection: $session.channelIndex) {
                ForEach(Array(session.colorModel.channels.enumerated()), id: \.offset) { i, name in
                    Text(name).tag(i)
                }
            }
            .pickerStyle(.segmented)
            .fixedSize()
        case .spectrum:
            Picker("Window", selection: $session.window) {
                ForEach(WindowFunction.allCases) { Text($0.displayName).tag($0) }
            }
            .fixedSize()
            Picker("Axis", selection: $session.frequencyAxis) {
                ForEach(FrequencyAxis.allCases) { Text($0.displayName).tag($0) }
            }
            .fixedSize()
            Toggle("Remove mean", isOn: $session.removeMean)
            Toggle("dB", isOn: $session.logMagnitude)
        }
    }

    private var splitButtons: some View {
        HStack(spacing: 4) {
            Button {
                session.split(zone)
            } label: {
                Image(systemName: "rectangle.split.2x1")
            }
            .help("Split: add a pane to \(zone.title)")
            Button {
                session.unsplit(zone)
            } label: {
                Image(systemName: "rectangle")
            }
            .help("Unsplit: remove the last pane")
            .disabled(session.panes[zone.rawValue].count < 2)
        }
        .labelsHidden()
    }

    private var lineDescription: String {
        let l = session.line
        func p(_ v: Double) -> String { v.formatted(.number.precision(.fractionLength(3))) }
        return "(\(p(l.start.x)), \(p(l.start.y))) → (\(p(l.end.x)), \(p(l.end.y)))"
    }
}
