import SwiftUI
import AppKit
import FaemyrCore

final class AppDelegate: NSObject, NSApplicationDelegate {
    var model: AppModel?
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationWillTerminate(_ notification: Notification) { MainActor.assumeIsolated { model?.shutdown() } }
    func application(_ application: NSApplication, open urls: [URL]) {
        // Finder → Open With: add the files' folder to the library and play the first file.
        MainActor.assumeIsolated {
            guard let model else { return }
            Task {
                for url in urls { if let tracks = try? await MetadataReader.readLibraryFile(url.path, covers: Paths.file("covers")) { model.applyImported(url.path, tracks) } }
                model.refresh()
                if let first = urls.first, let t = model.library.first(where: { $0.path == first.path || $0.cueSheetPath == first.path }) {
                    model.queue = [t]; model.queueIndex = 0; model.order.reset(1, 0); try? await model.play(t)
                }
            }
        }
    }
}

struct FaemyrApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @Local private var model = AppModel()
    var body: some Scene {
        WindowGroup("Faemyr") {
            MainWindowView()
                .environment(model)
                .onAppear { delegate.model = model; MediaControls.shared.configure(model) }
                .onChange(of: model.showSettings) { if model.showSettings { SettingsController.shared.open(model) } }
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1440, height: 900)
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button(tr("Faemyr Hakkında")) {
                    NSApp.orderFrontStandardAboutPanel(options: [
                        .applicationVersion: AppBrand.version + " · " + tr("macOS önizleme"),
                        .credits: NSAttributedString(string: tr("Faemyr'ın macOS sürümü hâlâ geliştirme aşamasında. Bazı özellikler eksik olabilir veya beklenmedik davranabilir."),
                                                     attributes: [.font: NSFont.systemFont(ofSize: 11), .foregroundColor: NSColor.secondaryLabelColor])
                    ])
                }
            }
            CommandGroup(replacing: .appSettings) { Button(tr("Ayarlar") + "…") { model.showSettings = true }.keyboardShortcut(",") }
            CommandGroup(replacing: .newItem) {
                Button(tr("Müzik klasörünü seç") + "…") { model.addFolder() }.keyboardShortcut("o")
                Button(tr("Yeniden tara")) { model.rescan() }.keyboardShortcut("r")
            }
            CommandGroup(replacing: .undoRedo) { Button(tr("Kuyruk değişikliğini geri al")) { model.undoQueue() }.keyboardShortcut("z").disabled(model.queueUndo.isEmpty) }
            CommandMenu(tr("Oynatıcı")) {
                Button(tr("Oynat / duraklat")) { Task { await model.togglePlayback() } }.keyboardShortcut(.space, modifiers: [])
                Button(tr("Sonraki parça")) { Task { await model.advance(natural: false) } }.keyboardShortcut(.rightArrow, modifiers: .command)
                Button(tr("Önceki parça")) { Task { await model.previous() } }.keyboardShortcut(.leftArrow, modifiers: .command)
                Divider()
                Button(tr("Karışık çalma")) { model.toggleShuffle() }.keyboardShortcut("s", modifiers: [.command, .option])
                Button(tr("Tekrar")) { model.cycleRepeat() }.keyboardShortcut("r", modifiers: [.command, .option])
                Divider()
                Button(tr("Tam ekran oynatıcı")) { model.fullscreen.toggle() }.keyboardShortcut("f", modifiers: [.command, .control])
                Button(tr("Mini oynatıcı")) { MiniPlayerController.shared.toggle(model) }.keyboardShortcut("m", modifiers: [.command, .shift])
            }
            CommandMenu(tr("Görünüm")) {
                Button(tr("Ara")) { NotificationCenter.default.post(name: .faemyrFocusSearch, object: nil) }.keyboardShortcut("f")
                Button(tr("Geri")) { model.navigateBack() }.keyboardShortcut("[")
                Button(tr(model.ui.showRightPanel ? "Sağ paneli gizle" : "Sağ paneli göster")) { model.ui.showRightPanel.toggle(); model.saveInterface() }.keyboardShortcut("\\")
                Divider()
                Button(tr("Albümler")) { model.navigate("albums") }.keyboardShortcut("1")
                Button(tr("Sanatçılar")) { model.navigate("artists") }.keyboardShortcut("2")
                Button(tr("Tüm parçalar")) { model.navigate("tracks") }.keyboardShortcut("3")
                Button(tr("Favoriler")) { model.navigate("favorites") }.keyboardShortcut("4")
                Button(tr("Çalma listeleri")) { model.navigate("playlists") }.keyboardShortcut("5")
                Button(tr("Çalma kuyruğu")) { model.navigate("queue") }.keyboardShortcut("6")
            }
        }
    }
}

// Space must not toggle playback while typing in a text field.
NSEvent.addLocalMonitorForEvents(matching: .keyDown) { e in
    if e.keyCode == 53, let w = NSApp.keyWindow, w.styleMask.contains(.fullScreen) { w.toggleFullScreen(nil) }
    return e
}
if !MainActor.assumeIsolated({ Snapshot.runIfRequested() }) { FaemyrApp.main() }
