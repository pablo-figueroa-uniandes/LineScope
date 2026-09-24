import AppKit
import LineScopeCore
import SwiftUI

/// Shows a variant at its true scale (1 image pixel = 1 point, or 1 device pixel) with scrolling,
/// and lets the user drag the shared selection line.
struct ImageCanvas: View {
    let variant: ImageVariant
    @Bindable var session: Session
    @Environment(\.displayScale) private var displayScale
    @State private var drag: DragState?

    private struct DragState {
        enum Mode { case start, end, whole, draw }
        var mode: Mode
        var initial: LineSegment
    }

    var body: some View {
        let scale = session.useDevicePixels ? displayScale : 1
        let size = CGSize(width: CGFloat(variant.buffer.width) / scale, height: CGFloat(variant.buffer.height) / scale)
        GeometryReader { geo in
            ScrollView([.horizontal, .vertical]) {
                ZStack {
                    Image(decorative: variant.cgImage, scale: scale)
                        .interpolation(.none)
                    LineOverlay(line: session.line)
                        .allowsHitTesting(false)
                }
                .frame(width: size.width, height: size.height)
                .contentShape(Rectangle())
                .gesture(lineGesture(size: size))
                .simultaneousGesture(TapGesture().onEnded { session.focusedVariantID = variant.id })
                .frame(minWidth: geo.size.width, minHeight: geo.size.height)
            }
        }
        .background(Color(nsColor: .underPageBackgroundColor))
    }

    private func lineGesture(size: CGSize) -> some Gesture {
        DragGesture(minimumDistance: 2)
            .onChanged { g in
                if drag == nil {
                    drag = DragState(mode: hitTest(g.startLocation, size: size), initial: session.line)
                    session.focusedVariantID = variant.id
                }
                guard let drag else { return }
                session.line = updatedLine(drag, gesture: g, size: size)
            }
            .onEnded { _ in drag = nil }
    }

    private func hitTest(_ p: CGPoint, size: CGSize) -> DragState.Mode {
        let a = point(session.line.start, size)
        let b = point(session.line.end, size)
        if distance(p, a) <= 9 { return .start }
        if distance(p, b) <= 9 { return .end }
        if distanceToSegment(p, a, b) <= 6 { return .whole }
        return .draw
    }

    private func updatedLine(_ drag: DragState, gesture g: DragGesture.Value, size: CGSize) -> LineSegment {
        let shift = NSEvent.modifierFlags.contains(.shift)
        let current = normalized(g.location, size)
        var line = drag.initial
        switch drag.mode {
        case .start:
            line.start = snapped(current, anchor: line.end, size: size, enabled: shift)
        case .end:
            line.end = snapped(current, anchor: line.start, size: size, enabled: shift)
        case .draw:
            line.start = normalized(g.startLocation, size)
            line.end = snapped(current, anchor: line.start, size: size, enabled: shift)
        case .whole:
            // Move both endpoints, limiting the offset so neither leaves the image.
            var dx = g.translation.width / size.width
            var dy = g.translation.height / size.height
            let i = drag.initial
            dx = min(max(dx, -min(i.start.x, i.end.x)), 1 - max(i.start.x, i.end.x))
            dy = min(max(dy, -min(i.start.y, i.end.y)), 1 - max(i.start.y, i.end.y))
            line.start = NormalizedPoint(x: i.start.x + dx, y: i.start.y + dy)
            line.end = NormalizedPoint(x: i.end.x + dx, y: i.end.y + dy)
        }
        return line
    }

    /// With Shift held, constrains the segment to horizontal or vertical (in pixel space).
    private func snapped(_ p: NormalizedPoint, anchor: NormalizedPoint, size: CGSize, enabled: Bool) -> NormalizedPoint {
        guard enabled else { return p }
        let dx = abs(p.x - anchor.x) * size.width
        let dy = abs(p.y - anchor.y) * size.height
        return dx >= dy ? NormalizedPoint(x: p.x, y: anchor.y) : NormalizedPoint(x: anchor.x, y: p.y)
    }

    private func normalized(_ p: CGPoint, _ size: CGSize) -> NormalizedPoint {
        NormalizedPoint(x: p.x / size.width, y: p.y / size.height).clamped()
    }

    private func point(_ n: NormalizedPoint, _ size: CGSize) -> CGPoint {
        CGPoint(x: n.x * size.width, y: n.y * size.height)
    }

    private func distance(_ a: CGPoint, _ b: CGPoint) -> CGFloat {
        hypot(a.x - b.x, a.y - b.y)
    }

    private func distanceToSegment(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint) -> CGFloat {
        let ab = CGPoint(x: b.x - a.x, y: b.y - a.y)
        let len2 = ab.x * ab.x + ab.y * ab.y
        guard len2 > 0 else { return distance(p, a) }
        let t = min(max(((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / len2, 0), 1)
        return distance(p, CGPoint(x: a.x + ab.x * t, y: a.y + ab.y * t))
    }
}

/// Draws the selection line: a filled handle at the start (position 0) and a hollow one at the end.
private struct LineOverlay: View {
    let line: LineSegment

    var body: some View {
        Canvas { ctx, size in
            let a = CGPoint(x: line.start.x * size.width, y: line.start.y * size.height)
            let b = CGPoint(x: line.end.x * size.width, y: line.end.y * size.height)
            var path = Path()
            path.move(to: a)
            path.addLine(to: b)
            ctx.stroke(path, with: .color(.black.opacity(0.7)), lineWidth: 3)
            ctx.stroke(path, with: .color(.yellow), lineWidth: 1.25)

            let r: CGFloat = 5
            let startDot = Path(ellipseIn: CGRect(x: a.x - r, y: a.y - r, width: 2 * r, height: 2 * r))
            ctx.fill(startDot, with: .color(.yellow))
            ctx.stroke(startDot, with: .color(.black.opacity(0.7)), lineWidth: 1)
            let endDot = Path(ellipseIn: CGRect(x: b.x - r, y: b.y - r, width: 2 * r, height: 2 * r))
            ctx.fill(endDot, with: .color(.black.opacity(0.35)))
            ctx.stroke(endDot, with: .color(.yellow), lineWidth: 1.5)
        }
    }
}
