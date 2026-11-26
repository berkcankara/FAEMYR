import SwiftUI
import AppKit
import FaemyrCore

/// Development aid: `Faemyr --snapshot out.png [--view albums|album|artists|artist|tracks|queue|fullscreen|settings] [--play] [--size 1440x900]`
/// renders the real window offscreen and writes a PNG, then exits. No screen-recording permission needed.
@MainActor enum Snapshot {
    static func runIfRequested() -> Bool {
        let args = CommandLine.arguments
        guard let i = args.firstIndex(of: "--snapshot"), i + 1 < args.count else { return false }
        let out = args[i + 1]
        func value(_ k: String) -> String? { args.firstIndex(of: k).flatMap { $0 + 1 < args.count ? args[$0 + 1] : nil } }
        let view = value("--view") ?? "albums"
        let dims = (value("--size") ?? "1440x900").split(separator: "x").compactMap { Double($0) }
        let size = NSSize(width: dims.first ?? 1440, height: dims.last ?? 900)
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)
        let model = AppModel()
        Task { @MainActor in
            for _ in 0..<300 where !model.libraryReady || model.scanning { try? await Task.sleep(nanoseconds: 100_000_000) }
            if args.contains("--play"), let first = AlbumDiscs.order(model.library.filter { $0.albumKey == (model.library.first?.albumKey ?? "") }).first {
                model.queue = AlbumDiscs.order(model.library.filter { $0.albumKey == first.albumKey }); model.queueIndex = 0; model.order.reset(model.queue.count, 0)
                try? await model.play(first, start: 30)
                try? await Task.sleep(nanoseconds: 1_500_000_000)
                model.tick()
                FileHandle.standardError.write("scope frames: \(model.audio.scope.written) rate: \(model.audio.scope.sampleRate)\n".data(using: .utf8)!)
            }
            switch view {
            case "album": if let k = model.library.first?.albumKey { model.openAlbum(k) }
            case "artist": if let a = model.library.first?.artist { model.openArtist(a) }
            case "fullscreen": model.fullscreen = true
            case "settings": break
            default: model.navigate(view)
            }
            try? await Task.sleep(nanoseconds: 2_500_000_000)
            let root: AnyView = view == "settings"
                ? AnyView(SettingsView(close: { _ in }).environment(model))
                : AnyView(MainWindowView().environment(model))
            let host = NSHostingView(rootView: root.frame(width: size.width, height: size.height))
            host.frame = NSRect(origin: .zero, size: size)
            let window = NSWindow(contentRect: host.frame, styleMask: [.borderless], backing: .buffered, defer: false)
            window.contentView = host
            window.setFrameOrigin(NSPoint(x: -20000, y: -20000))
            window.orderFrontRegardless()
            try? await Task.sleep(nanoseconds: 2_000_000_000)
            host.layoutSubtreeIfNeeded()
            if let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) {
                host.cacheDisplay(in: host.bounds, to: rep)
                try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: out))
            }
            model.audio.shutdown()
            exit(0)
        }
        app.run()
        return true
    }
}
