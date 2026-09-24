import ImageIO
import LineScopeCore
import SwiftUI
import UniformTypeIdentifiers

/// Facts about the file as decoded, before conversion to the working float buffer.
struct SourceInfo {
    var fileType: String
    var pixelWidth: Int
    var pixelHeight: Int
    var bitsPerComponent: Int
    var bitsPerPixel: Int
    var colorSpaceName: String
    var hasAlpha: Bool
    var dpi: Double?
}

/// Read-only image document. Pixels are decoded once into a `PixelBuffer`; EXIF orientation is ignored
/// on purpose so analysis runs on the stored pixel grid.
struct ImageDocument: FileDocument {
    static var readableContentTypes: [UTType] { [.image] }

    let buffer: PixelBuffer
    let displayImage: CGImage
    let info: SourceInfo

    init(configuration: ReadConfiguration) throws {
        guard
            let data = configuration.file.regularFileContents,
            let source = CGImageSourceCreateWithData(data as CFData, nil),
            let cg = CGImageSourceCreateImageAtIndex(source, 0, nil),
            let buffer = PixelBuffer(cgImage: cg),
            let display = buffer.makeCGImage()
        else { throw CocoaError(.fileReadCorruptFile) }

        let props = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any]
        let type = (CGImageSourceGetType(source) as String?).flatMap { UTType($0)?.localizedDescription }
        let alpha = cg.alphaInfo
        self.buffer = buffer
        self.displayImage = display
        self.info = SourceInfo(
            fileType: type ?? "Image",
            pixelWidth: cg.width,
            pixelHeight: cg.height,
            bitsPerComponent: cg.bitsPerComponent,
            bitsPerPixel: cg.bitsPerPixel,
            colorSpaceName: (cg.colorSpace?.name as String?)?.replacingOccurrences(of: "kCGColorSpace", with: "") ?? "Unknown",
            hasAlpha: !(alpha == .none || alpha == .noneSkipFirst || alpha == .noneSkipLast),
            dpi: props?[kCGImagePropertyDPIWidth] as? Double
        )
    }

    /// A generated test pattern, shown in the same analysis window as a file.
    init?(pattern spec: TestPatternSpec) {
        let buffer = TestPatternGenerator.make(spec)
        guard let display = buffer.makeCGImage() else { return nil }
        self.buffer = buffer
        self.displayImage = display
        self.info = SourceInfo(
            fileType: "Generated test pattern",
            pixelWidth: buffer.width,
            pixelHeight: buffer.height,
            bitsPerComponent: 32,
            bitsPerPixel: 128,
            colorSpaceName: "sRGB (float)",
            hasAlpha: false,
            dpi: nil
        )
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        throw CocoaError(.featureUnsupported)
    }
}
