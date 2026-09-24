// swift-tools-version: 5.10
import PackageDescription

let package = Package(
    name: "LineScopeCore",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "LineScopeCore", targets: ["LineScopeCore"]),
    ],
    targets: [
        .target(
            name: "LineScopeCore",
            // Pixel loops are unusably slow at -Onone; optimize even in Debug.
            swiftSettings: [.unsafeFlags(["-O"], .when(configuration: .debug))]
        ),
        .testTarget(
            name: "LineScopeCoreTests",
            dependencies: ["LineScopeCore"]
        ),
    ]
)
