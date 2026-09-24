import LineScopeCore
import SwiftUI

struct MainView: View {
    @State private var session: Session

    init(document: ImageDocument, fileName: String) {
        _session = State(initialValue: Session(document: document, fileName: fileName))
    }

    var body: some View {
        VSplitView {
            ZoneView(zone: .images, session: session)
                .frame(minHeight: 180, idealHeight: 420)
            ZoneView(zone: .profile, session: session)
                .frame(minHeight: 140, idealHeight: 220)
            ZoneView(zone: .spectrum, session: session)
                .frame(minHeight: 140, idealHeight: 220)
        }
        .frame(minWidth: 720, minHeight: 560)
        .focusedSceneValue(\.session, session)
        .background(WindowKeyObserver(
            onBecomeKey: { ActiveSession.shared.session = session },
            onClose: {
                if ActiveSession.shared.session === session { ActiveSession.shared.session = nil }
            }
        ))
        .sheet(item: $session.pendingRequest) { request in
            ParameterSheet(request: request, session: session)
        }
        .alert(
            "Operation failed",
            isPresented: Binding(get: { session.errorMessage != nil }, set: { if !$0 { session.errorMessage = nil } }),
            presenting: session.errorMessage
        ) { _ in
            Button("OK") {}
        } message: { Text($0) }
        .toolbar {
            ToolbarItem(placement: .status) {
                if session.runningOperations > 0 {
                    ProgressView().controlSize(.small)
                }
            }
        }
    }
}

/// Reports when the hosting window becomes key or closes.
struct WindowKeyObserver: NSViewRepresentable {
    var onBecomeKey: () -> Void
    var onClose: () -> Void

    func makeNSView(context: Context) -> ObserverView { ObserverView() }

    func updateNSView(_ view: ObserverView, context: Context) {
        view.onBecomeKey = onBecomeKey
        view.onClose = onClose
    }

    final class ObserverView: NSView {
        var onBecomeKey: () -> Void = {}
        var onClose: () -> Void = {}
        private var tokens: [NSObjectProtocol] = []

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            tokens.forEach(NotificationCenter.default.removeObserver)
            tokens = []
            guard let window else { return }
            let center = NotificationCenter.default
            tokens.append(center.addObserver(forName: NSWindow.didBecomeKeyNotification, object: window, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.onBecomeKey() }
            })
            tokens.append(center.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.onClose() }
            })
            if window.isKeyWindow {
                DispatchQueue.main.async { [weak self] in self?.onBecomeKey() }
            }
        }

        deinit {
            tokens.forEach(NotificationCenter.default.removeObserver)
        }
    }
}
