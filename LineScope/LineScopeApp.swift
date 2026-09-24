import LineScopeCore
import SwiftUI

@main
struct LineScopeApp: App {
    var body: some Scene {
        DocumentGroup(viewing: ImageDocument.self) { file in
            MainView(document: file.document, fileName: file.fileURL?.lastPathComponent ?? "Untitled")
        }
        .defaultSize(width: 1100, height: 900)
        .commands { LineScopeCommands() }

        WindowGroup("Test Pattern", for: TestPatternSpec.self) { $spec in
            if let spec {
                PatternWindow(spec: spec)
            }
        }
        .defaultSize(width: 1100, height: 900)

        Window("New Test Pattern", id: TestPatternGeneratorView.windowID) {
            TestPatternGeneratorView()
        }
        .windowResizability(.contentSize)

        Window("Inspector", id: InspectorView.windowID) {
            InspectorView()
        }
        .defaultSize(width: 340, height: 760)
        .defaultPosition(.topTrailing)
    }
}
