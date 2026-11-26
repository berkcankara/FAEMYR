import SwiftUI
import AppKit
import FaemyrCore

/// Fullscreen player (FullscreenPlayer.cs): artwork + title on the left, large
/// synchronized lyrics on the right, auto-hiding player controls below.
struct FullscreenPlayer: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var controlsVisible = true
    @Local private var lastMove = Date()
    @Local private var tint: RGBA?
    let idle = Timer.publish(every: 0.5, on: .main, in: .common).autoconnect()

    var body: some View {
        let t = model.now ?? model.inspected
        GeometryReader { geo in
            let width = geo.size.width, height = geo.size.height
            let showLyrics = model.fullscreenLyrics && t != nil
            let playerHeight = (theme.ui.playerHeight + 40).clamped(160, 240)
            let bodyHeight = max(180, height - 74 - playerHeight - 32)
            let size = (min(bodyHeight - 200 * theme.ui.fontScale / 100, (showLyrics ? width * 0.39 : width * 0.60) - 80)).clamped(140, 580)
            let lyricScale = (width / 1500).clamped(1.1, 1.45) * 1.35
            ZStack {
                theme.canvas
                LinearGradient(colors: [Color(tint ?? theme.palette["Canvas"]), theme.canvas], startPoint: .topLeading, endPoint: .bottomTrailing)
                VStack(spacing: 0) {
                    HStack {
                        Spacer()
                        Button { withAnimation(.timingCurve(0.645, 0.045, 0.355, 1, duration: 0.36)) { model.fullscreenLyrics.toggle() } } label: { Icon(name: "lyrics", size: 24, color: showLyrics ? theme.accent : theme.muted) }
                            .buttonStyle(TransportStyle(size: 44)).help(tr("Şarkı sözlerini göster / gizle"))
                        Button { model.fullscreen = false } label: { Icon(name: "fullscreen-exit", size: 24, color: theme.ink) }
                            .buttonStyle(TransportStyle(size: 44)).padding(.leading, 12).help(tr("Tam ekrandan çık (Esc / F11)"))
                    }
                    .padding(.horizontal, 32).padding(.vertical, 12).frame(height: 74)
                    .opacity(controlsVisible ? 1 : 0)
                    HStack(spacing: 0) {
                        VStack(spacing: 0) {
                            ZStack {
                                RoundedRectangle(cornerRadius: theme.cardRadius).fill(theme.surface)
                                Text("♫").font(theme.font(64)).foregroundStyle(theme.muted)
                                Thumb(path: model.coverPath(t), pixels: 1200, radius: theme.cardRadius)
                            }
                            .frame(width: size, height: size)
                            .shadow(color: .black.opacity(0.35), radius: 30, y: 12)
                            VStack(spacing: 0) {
                                Text(t?.title ?? tr("Henüz çalan parça yok")).font(theme.font(38, .bold)).multilineTextAlignment(.center).lineLimit(2).minimumScaleFactor(0.5)
                                if let t { ArtistLinks(names: t.artist, size: 20, color: theme.muted).multilineTextAlignment(.center).lineLimit(2).padding(.top, 8) }
                                if let t { Text(t.quality).font(theme.font(12)).foregroundStyle(theme.muted).padding(.top, 10) }
                            }
                            .frame(width: min(showLyrics ? width * 0.43 : width - 100, 760)).padding(.top, 24)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(EdgeInsets(top: 0, leading: 16, bottom: 0, trailing: 24))
                        if showLyrics {
                            Group {
                                if !model.lyricLines.isEmpty {
                                    LyricsLines(fontSize: theme.ui.lyricsFontSize * lyricScale, fullscreen: true)
                                } else if let doc = model.lyricsDocument {
                                    ScrollView { Text(doc.instrumental ? tr("Bu kayıt enstrümantal olarak işaretlenmiş.") : doc.plain).font(theme.font((theme.ui.lyricsFontSize - 1) * lyricScale)).foregroundStyle(theme.muted).frame(maxWidth: .infinity, alignment: .leading).padding(.vertical, 120) }
                                        .scrollIndicators(.never)
                                } else {
                                    VStack(alignment: .leading, spacing: 16) {
                                        Text(model.lyricsStatus).font(theme.font(16)).foregroundStyle(theme.muted)
                                        Button(tr("Şarkı sözlerini bul")) { model.fetchLyrics() }.buttonStyle(FButtonStyle()).disabled(model.lyricsBusy)
                                    }.frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
                                }
                            }
                            .frame(maxWidth: .infinity, maxHeight: bodyHeight)
                            .padding(EdgeInsets(top: 0, leading: 24, bottom: 0, trailing: 16))
                            .transition(.opacity.combined(with: .offset(x: 40)))
                        }
                    }
                    .padding(EdgeInsets(top: 8, leading: 36, bottom: 24, trailing: 36))
                    .frame(maxWidth: 1800, maxHeight: .infinity)
                    PlayerBar(fullscreen: true)
                        .frame(height: playerHeight)
                        .offset(y: controlsVisible ? 0 : playerHeight + 20)
                        .opacity(controlsVisible ? 1 : 0)
                }
            }
        }
        .ignoresSafeArea()
        .onContinuousHover { _ in lastMove = Date(); if !controlsVisible { withAnimation(.easeOut(duration: 0.25)) { controlsVisible = true } } }
        .onReceive(idle) { _ in
            // FullscreenControlsAutoHide.cs: hide after 3 s without pointer movement while playing.
            if controlsVisible && model.isPlaying && Date().timeIntervalSince(lastMove) > 3 { withAnimation(.easeInOut(duration: 0.4)) { controlsVisible = false } }
            if !controlsVisible { NSCursor.setHiddenUntilMouseMoves(true) }
        }
        .task(id: model.coverPath(t)) {
            guard let p = model.coverPath(t), let img = await ImageCache.shared.load(p, 256), let (r, g, b) = img.averageRGB else { tint = nil; return }
            let light = model.themeId == "light"
            tint = light ? RGBA(UInt8(200 + r * 0.2), UInt8(200 + g * 0.2), UInt8(200 + b * 0.2)) : RGBA(UInt8((r * 0.45).clamped(10, 90)), UInt8((g * 0.45).clamped(10, 90)), UInt8((b * 0.45).clamped(10, 100)))
        }
        .onAppear { if let w = NSApp.keyWindow, !w.styleMask.contains(.fullScreen) { w.toggleFullScreen(nil) } }
        .onDisappear { if let w = NSApp.windows.first(where: { $0.styleMask.contains(.fullScreen) && $0.identifier?.rawValue != "mini" }) { w.toggleFullScreen(nil) } }
    }
}

// MARK: - Mini player (MiniPlayerWindow.cs)

@MainActor final class MiniPlayerController: NSObject, NSWindowDelegate {
    static let shared = MiniPlayerController()
    var window: NSPanel?
    func toggle(_ model: AppModel) {
        if let w = window, w.isVisible { w.orderOut(nil); model.showMiniPlayer = false; return }
        if window == nil {
            let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 430, height: 196), styleMask: [.titled, .closable, .resizable, .fullSizeContentView, .nonactivatingPanel], backing: .buffered, defer: false)
            panel.identifier = NSUserInterfaceItemIdentifier("mini")
            panel.titlebarAppearsTransparent = true; panel.titleVisibility = .hidden; panel.isMovableByWindowBackground = true
            panel.standardWindowButton(.miniaturizeButton)?.isHidden = true; panel.standardWindowButton(.zoomButton)?.isHidden = true
            panel.minSize = NSSize(width: 350, height: 190); panel.title = "Faemyr — " + tr("Mini oynatıcı")
            panel.isFloatingPanel = false; panel.hidesOnDeactivate = false; panel.delegate = self
            panel.contentView = NSHostingView(rootView: MiniPlayerView().environment(model).environment(\.theme, model.theme))
            panel.setFrameAutosaveName("FaemyrMini")
            if !panel.setFrameUsingName("FaemyrMini") { panel.center() }
            window = panel
        }
        window?.makeKeyAndOrderFront(nil); model.showMiniPlayer = true
    }
    func windowWillClose(_ notification: Notification) {}
}

struct MiniPlayerView: View {
    @Environment(AppModel.self) var model
    @Local private var pinned = false
    @Local private var expanded = false
    var body: some View {
        let theme = model.theme
        let t = model.now
        GeometryReader { geo in
            ZStack(alignment: .bottom) {
                theme.surface
                if expanded {
                    Thumb(path: model.coverPath(t), pixels: 1024, radius: 0)
                    LinearGradient(colors: [.clear, .black.opacity(0.75)], startPoint: .center, endPoint: .bottom)
                }
                VStack(spacing: 0) {
                    HStack(spacing: 12) {
                        if !expanded {
                            let size = (min(geo.size.width * 0.2, geo.size.height - 110)).clamped(76, 128)
                            ZStack { RoundedRectangle(cornerRadius: 10).fill(theme.control); Icon(name: "music", size: 32, color: theme.muted); Thumb(path: model.coverPath(t), pixels: 300, radius: 10) }
                                .frame(width: size, height: size).onTapGesture { expanded = true }
                        }
                        VStack(alignment: .leading, spacing: 5) {
                            Text(t?.title ?? tr("Henüz çalan parça yok")).font(.system(size: (geo.size.width / 28).clamped(15, 22), weight: .semibold)).lineLimit(1)
                            if let t { ArtistLinks(names: t.artist, size: (geo.size.width / 35).clamped(12, 16), color: expanded ? Color(red: 0.93, green: 0.93, blue: 0.96) : theme.muted).lineLimit(1) }
                        }
                        Spacer(minLength: 0)
                    }
                    .padding(.top, expanded ? 0 : 12)
                    Spacer(minLength: 8)
                    HStack(spacing: 4) {
                        Button { model.toggleShuffle() } label: { ToggleGlyph(icon: "shuffle", active: model.shuffle).scaleEffect(0.75) }.buttonStyle(TransportStyle(size: 28))
                        Button { Task { await model.previous() } } label: { Icon(name: "previous", size: 14, color: theme.ink) }.buttonStyle(TransportStyle(size: 28))
                        Button { Task { await model.togglePlayback() } } label: { Icon(name: model.isPlaying ? "pause" : "play", size: 21, color: theme.onAccent) }
                            .buttonStyle(TransportStyle(size: 42, fill: theme.accent)).padding(.horizontal, 5)
                        Button { Task { await model.advance(natural: false) } } label: { Icon(name: "next", size: 14, color: theme.ink) }.buttonStyle(TransportStyle(size: 28))
                        Button { model.cycleRepeat() } label: { ToggleGlyph(icon: "repeat", active: model.repeatMode != .off).scaleEffect(0.75) }.buttonStyle(TransportStyle(size: 28))
                        Spacer(minLength: 8)
                        if geo.size.width >= 350 {
                            Button { model.toggleMute() } label: { Icon(name: model.playerPrefs.muted ? "muted" : "volume", size: 16, color: theme.muted) }.buttonStyle(TransportStyle(size: 28))
                            SeekBar(value: model.playerPrefs.muted ? 0 : model.playerPrefs.volume * 100, maximum: 100, onChange: { model.setVolume($0 / 100) }, onCommit: { model.setVolume($0 / 100); model.playerPrefs.save() }).frame(width: 78)
                        }
                    }
                    MiniSeek().padding(.top, 6)
                }
                .padding(14)
                .frame(height: expanded ? 180 : nil)
            }
            .overlay(alignment: .topTrailing) {
                HStack(spacing: 4) {
                    Button { pinned.toggle(); MiniPlayerController.shared.window?.level = pinned ? .floating : .normal } label: { Icon(name: "pin", size: 14, color: pinned ? theme.accent : theme.ink) }
                        .buttonStyle(TransportStyle(size: 30)).help(tr("Her zaman üstte"))
                    Button { MiniPlayerController.shared.window?.orderOut(nil); model.showMiniPlayer = false; NSApp.activate(ignoringOtherApps: true) } label: { Icon(name: "fullscreen", size: 14, color: theme.ink) }
                        .buttonStyle(TransportStyle(size: 30)).help(tr("Ana pencereye dön"))
                }.padding(12)
            }
            .overlay(alignment: .topLeading) {
                if expanded { Button { expanded = false } label: { Icon(name: "fullscreen-exit", size: 14, color: .white) }.buttonStyle(TransportStyle(size: 30)).padding(12) }
            }
        }
        .foregroundStyle(expanded ? .white : theme.ink)
        .environment(\.theme, theme)
        .ignoresSafeArea()
    }
}

/// Position-only subview so the mini player's other controls are not rebuilt on every tick.
struct MiniSeek: View {
    @Environment(AppModel.self) var model
    var body: some View { SeekBar(value: model.position, maximum: max(1, model.duration), onChange: { _ in }, onCommit: { model.seek($0) }) }
}
