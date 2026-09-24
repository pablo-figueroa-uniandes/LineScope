import SwiftUI

/// A tab strip listing every variant, plus the zone's content for the selected one.
struct PaneView: View {
    let zone: Zone
    let paneIndex: Int
    let session: Session

    private var selectedID: UUID {
        let panes = session.panes[zone.rawValue]
        return paneIndex < panes.count ? panes[paneIndex].variantID : session.original.id
    }

    var body: some View {
        let variant = session.variant(selectedID) ?? session.original
        VStack(spacing: 0) {
            TabStrip(session: session, selectedID: variant.id) { id in
                session.select(id, zone: zone, pane: paneIndex)
            }
            Divider()
            Group {
                switch zone {
                case .images: ImageCanvas(variant: variant, session: session)
                case .profile: ProfileChart(variant: variant, session: session)
                case .spectrum: SpectrumChart(variant: variant, session: session)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

private struct TabStrip: View {
    let session: Session
    let selectedID: UUID
    let onSelect: (UUID) -> Void

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 2) {
                ForEach(session.variants) { v in
                    let selected = v.id == selectedID
                    Button {
                        onSelect(v.id)
                    } label: {
                        HStack(spacing: 4) {
                            if v.id == session.focusedVariantID {
                                Circle().fill(Color.accentColor).frame(width: 5, height: 5)
                            }
                            Text(v.name).lineLimit(1)
                            Text("\(v.buffer.width)×\(v.buffer.height)")
                                .foregroundStyle(.secondary)
                        }
                        .font(.caption)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3)
                        .background(
                            RoundedRectangle(cornerRadius: 5)
                                .fill(selected ? Color.accentColor.opacity(0.22) : Color.clear)
                        )
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help(v.name)
                    .contextMenu {
                        Button("Export Image…") {
                            onSelect(v.id)
                            session.exportFocused()
                        }
                        Button("Close Tab") { session.closeVariant(v.id) }
                            .disabled(v.isOriginal)
                    }
                }
            }
            .padding(.horizontal, 6)
            .padding(.vertical, 3)
        }
    }
}
