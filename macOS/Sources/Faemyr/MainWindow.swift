import SwiftUI
import AppKit
import CoreImage
import FaemyrCore

struct MainWindowView: View {
    @Environment(AppModel.self) var model
    func hidden(_ k: String) -> Bool { (ProcessInfo.processInfo.environment["FAEMYR_SNAPSHOT_HIDE"] ?? "").contains(k) }
    var body: some View {
        let theme = model.theme
        ZStack {
            VStack(spacing: 0) {
                TitleBar().frame(height: 44)
                HStack(spacing: 0) {
                    if !hidden("sidebar") { Sidebar().frame(width: theme.ui.sidebarWidth) }
                    if !hidden("main") { MainContent().frame(minWidth: 500, maxWidth: .infinity, maxHeight: .infinity) } else { Spacer() }
                    if model.ui.showRightPanel && !hidden("detail") { DetailPanel().frame(width: model.panel.width).transition(.move(edge: .trailing)) }
                }
                .frame(minHeight: 0, maxHeight: .infinity)
                .clipped()
                if !hidden("player") { PlayerBar() }
            }
            if model.fullscreen { FullscreenPlayer().transition(.opacity) }
        }
        .background { ZStack { theme.c("WorkspaceSurface"); PageBackdrop() }.ignoresSafeArea() }
        .environment(\.theme, theme)
        .foregroundStyle(theme.ink)
        .font(theme.font(14))
        .frame(minWidth: 1120, minHeight: 720)
        .ignoresSafeArea()
        .background(WindowConfigurator())
        .animation(.easeInOut(duration: 0.3), value: model.ui.showRightPanel)
        .animation(.easeInOut(duration: 0.25), value: model.fullscreen)
    }
}

// MARK: - Title bar (TopBar.cs)

struct TitleBar: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var expanded = false
    @Local private var hovering = false
    /// AppKit hands the first text field focus when the window opens; only a click (while hovering) or ⌘F may focus search.
    @Local private var focusArmed = false
    @Local private var titleWidth: Double = 1440
    @FocusState private var searchFocused: Bool

    var prompt: String {
        guard expanded else { return tr("Bul") }
        switch model.view {
        case "albums": return tr("hangi albümü arıyorsun?")
        case "artists": return tr("hangi sanatçıyı arıyorsun?")
        case "playlists": return tr("hangi listeyi arıyorsun?")
        case "favorites": return tr("favorilerinde ne arıyorsun?")
        case "artist": return tr("sanatçının hangi albümünü arıyorsun?")
        default: return tr("hangi parçayı arıyorsun?")
        }
    }

    /// Collapsed search ends on the sidebar panel's right edge (traffic lights 76 + back 46 + gap 4, sidebar inset 8).
    var expandedSearch: Double { max(collapsedSearch, (titleWidth * 0.25).clamped(220, 300)) }
    var collapsedSearch: Double { max(72, theme.ui.sidebarWidth - 8 - 126) }

    var body: some View {
        @Bindable var model = model
        ZStack {
            WindowDragArea()
                .background(GeometryReader { g in Color.clear.onAppear { titleWidth = g.size.width }.onChange(of: g.size.width) { titleWidth = g.size.width } })
            HStack(spacing: 0) {
                Color.clear.frame(width: 76) // traffic lights
                Button { model.navigateBack() } label: { Icon(name: "back", size: 21, color: theme.c("CaptionInk")) }
                    .buttonStyle(CaptionButtonStyle()).disabled(model.navigationHistory.isEmpty).help(tr("Geri") + " · ⌘[")
                ZStack(alignment: .topTrailing) {
                    HStack(spacing: 0) {
                        Icon(name: "search", size: 18, color: theme.muted).padding(.leading, 10).padding(.trailing, 12)
                        TextField("", text: $model.search, prompt: Text(prompt).foregroundStyle(theme.muted.opacity(0.65)))
                            .textFieldStyle(.plain).font(theme.font(13)).foregroundStyle(theme.ink)
                            .focused($searchFocused)
                            .onChange(of: model.search) { model.searchChanged() }
                            .onSubmit { model.refresh() }
                            .onExitCommand { model.search = ""; searchFocused = false; model.refresh() }
                        if !model.search.isEmpty && expanded {
                            Button { model.search = ""; model.refresh() } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(theme.muted) }.buttonStyle(.plain).padding(.trailing, 8)
                        }
                    }
                    .frame(width: expanded ? expandedSearch : collapsedSearch, height: 34)
                    .animation(.easeOut(duration: 0.16), value: expanded)
                    .overlay(RoundedRectangle(cornerRadius: theme.buttonRadius).strokeBorder(theme.c("CaptionInk"), lineWidth: 1))
                    .contentShape(Rectangle())
                    .onTapGesture { searchFocused = true }
                    if !model.search.isEmpty && !expanded { Circle().fill(theme.accent).frame(width: 4, height: 4).padding(5) }
                }
                .padding(.leading, 4)
                .help(tr("Parça, albüm veya sanatçı ara"))
                .onHover { h in hovering = h; updateExpanded() }
                .onChange(of: searchFocused) { updateExpanded() }
                Spacer(minLength: 16)
                Button { togglePanel() } label: {
                    Image(systemName: "sidebar.right").font(.system(size: 15, weight: .regular)).foregroundStyle(theme.c("CaptionInk"))
                }
                .buttonStyle(ToolbarIconStyle()).help(tr(model.ui.showRightPanel ? "Sağ paneli gizle" : "Sağ paneli göster"))
                .padding(.trailing, 12)
            }
            // Centred on the window, not between the search box and the panel button. The reserve uses the
            // expanded search width so widening the search never moves or resizes the title (TopBar.cs).
            if !model.ui.showRightPanel, let now = model.now {
                let reserve = 76 + 46 + 4 + expandedSearch
                VStack(spacing: 1) {
                    Text(now.title).font(theme.font(13, .semibold)).lineLimit(1)
                    ArtistLinks(names: now.artist, size: 11, color: theme.muted).lineLimit(1)
                }
                .frame(width: max(0, min(520, titleWidth - 2 * reserve - 32)))
                .help(now.title + "\n" + now.artist)
            }
        }
        .onReceive(NotificationCenter.default.publisher(for: .faemyrFocusSearch)) { _ in focusArmed = true; searchFocused = true }
    }

    func updateExpanded() {
        if searchFocused && !focusArmed && !hovering { searchFocused = false; NSApp.keyWindow?.makeFirstResponder(nil); return }
        if searchFocused { focusArmed = true } else if !hovering { focusArmed = false }
        let target = hovering || searchFocused
        if target { withAnimation(.easeOut(duration: 0.21)) { expanded = true } }
        else { DispatchQueue.main.asyncAfter(deadline: .now() + 0.24) { if !hovering && !searchFocused { withAnimation(.easeOut(duration: 0.21)) { expanded = false } } } }
    }
    func togglePanel() { model.ui.showRightPanel.toggle(); model.saveInterface() }
}

struct CaptionButtonStyle: ButtonStyle {
    @Environment(\.theme) var theme
    @Environment(\.isEnabled) var enabled
    func makeBody(configuration: Configuration) -> some View {
        HoverWrap { h in
            configuration.label.frame(width: 46, height: 44)
                .background(configuration.isPressed ? theme.selected : h && enabled ? theme.hover : Color.clear)
                .opacity(enabled ? 1 : 0.45)
                .contentShape(Rectangle())
        }
    }
}

/// macOS toolbar-style icon button: soft rounded hover/press highlight instead of the Windows caption tile.
struct ToolbarIconStyle: ButtonStyle {
    @Environment(\.theme) var theme
    @Environment(\.isEnabled) var enabled
    func makeBody(configuration: Configuration) -> some View {
        HoverWrap { h in
            configuration.label.frame(width: 32, height: 28)
                .background(RoundedRectangle(cornerRadius: 7, style: .continuous)
                    .fill(configuration.isPressed ? theme.selected : h && enabled ? theme.hover : Color.clear))
                .opacity(enabled ? 1 : 0.45)
                .contentShape(RoundedRectangle(cornerRadius: 7, style: .continuous))
                .animation(.easeOut(duration: 0.12), value: h)
        }
    }
}

/// The macOS port is still in development: a small, always-visible note above Settings.
struct PreviewNotice: View {
    @Environment(\.theme) var theme
    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: "hammer").font(.system(size: 11, weight: .medium)).foregroundStyle(theme.accent).padding(.top, 1)
            VStack(alignment: .leading, spacing: 2) {
                Text(tr("macOS önizleme")).font(theme.font(11, .semibold)).foregroundStyle(theme.ink)
                Text(tr("Geliştirme aşamasında")).font(theme.font(10)).foregroundStyle(theme.muted)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 12).padding(.vertical, 9)
        .background(RoundedRectangle(cornerRadius: theme.buttonRadius, style: .continuous).strokeBorder(theme.line, lineWidth: 1))
        .help(tr("Faemyr'ın macOS sürümü hâlâ geliştirme aşamasında. Bazı özellikler eksik olabilir veya beklenmedik davranabilir."))
    }
}

extension Notification.Name { static let faemyrFocusSearch = Notification.Name("faemyr.focusSearch") }

// MARK: - Sidebar

struct Sidebar: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var activeKey: String {
        if model.showSettings { return "settings" }
        switch model.view { case "artists", "artist": return "artists"; case "tracks": return "tracks"; case "favorites": return "favorites"; case "playlists", "playlist": return "playlists"; case "queue": return "queue"; default: return "albums" }
    }
    var body: some View {
        let side = ((theme.ui.sidebarWidth - 156) / 2).clamped(12, 22)
        VStack(alignment: .leading, spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    Text(tr("KOLEKSİYON")).font(theme.font(10)).foregroundStyle(theme.muted).padding(.bottom, 16)
                    ForEach([("albums", "album", "Albümler"), ("artists", "artist", "Sanatçılar"), ("tracks", "music", "Tüm parçalar"), ("favorites", "heart", "Favoriler"), ("playlists", "playlist", "Çalma listeleri"), ("queue", "queue", "Çalma kuyruğu")], id: \.0) { key, icon, title in
                        if !theme.ui.hiddenNavigation.contains(key) {
                            NavButton(icon: icon, title: tr(title), active: activeKey == key) { model.navigate(key) }
                                .padding(.bottom, theme.ui.navigationGap)
                        }
                    }
                }
            }
            .scrollIndicators(.never)
            Spacer(minLength: 0)
            PreviewNotice().padding(.bottom, 10)
            NavButton(icon: "settings", title: tr("Ayarlar"), active: activeKey == "settings") { model.showSettings = true }
        }
        .padding(EdgeInsets(top: 22, leading: max(4, side - 8), bottom: 14, trailing: max(4, side - 8)))
        .accentPanel(theme.c("ModuleSidebarSurface"), rim: theme.c("SidebarFrameAccent"), radius: theme.panelRadius)
        .padding(8)
    }
}

struct NavButton: View {
    @Environment(\.theme) var theme
    let icon: String, title: String, active: Bool
    let action: () -> Void
    var body: some View {
        let fg = active ? theme.onAccent : theme.ink
        Button(action: action) {
            HStack(spacing: 10) { Icon(name: icon, size: 18, color: fg); Text(title).font(theme.font(14)).foregroundStyle(fg).lineLimit(1); Spacer(minLength: 0) }
                .padding(.horizontal, 12).frame(height: theme.ui.navigationHeight)
        }
        .buttonStyle(FButtonStyle(background: active ? theme.accent : theme.c("NavigationSurface"), foreground: fg, padding: EdgeInsets()))
    }
}

// MARK: - Backdrop (PageBackdrop.cs): blurred album art + tint gradient.

struct PageBackdrop: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var image: NSImage?
    @Local private var tint: RGBA = RGBA(35, 35, 35)
    var source: String? {
        guard model.ui.albumArtworkBackground else { return nil }
        if model.view == "album" { return model.library.first { $0.albumKey == model.albumKey && $0.cover.fileExists }?.cover }
        if model.view == "albums" || model.view == "artists" { return model.coverPath(model.now) }
        return nil
    }
    var body: some View {
        let src = source
        ZStack {
            if src != nil {
                LinearGradient(stops: gradientStops, startPoint: .topLeading, endPoint: .bottomTrailing)
                if let image {
                    GeometryReader { g in
                        Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fill)
                            .frame(width: g.size.width, height: g.size.height)
                            .opacity(theme.ui.artworkOpacity / 100)
                            .clipped()
                    }
                }
            }
        }
        .ignoresSafeArea()
        .allowsHitTesting(false)
        .animation(.easeInOut(duration: 0.46), value: src)
        .task(id: (src ?? "") + model.themeId) {
            guard let src else { image = nil; return }
            let img = await ImageCache.shared.load(src, 256)
            let blur = theme.ui.artworkBlur
            image = await Task.detached { img.flatMap { Self.blurred($0, radius: blur) } }.value
            var t = RGBA(35, 35, 35)
            if let (r, g, b) = img?.averageRGB { t = RGBA(UInt8((r * 0.6).clamped(20, 115)), UInt8((g * 0.6).clamped(20, 115)), UInt8((b * 0.6).clamped(20, 135))) }
            tint = t
        }
    }
    /// BlurEffect Radius=90 over the window ≈ radius/6 px on the 256 px source.
    nonisolated static func blurred(_ image: NSImage, radius: Double) -> NSImage? {
        guard radius > 0 else { return image }
        guard let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return image }
        let input = CIImage(cgImage: cg)
        let filter = CIFilter(name: "CIGaussianBlur", parameters: [kCIInputImageKey: input.clampedToExtent(), kCIInputRadiusKey: radius / 6])
        guard let out = filter?.outputImage?.cropped(to: input.extent), let result = CIContext().createCGImage(out, from: input.extent) else { return image }
        return NSImage(cgImage: result, size: image.size)
    }

    var gradientStops: [Gradient.Stop] {
        let light = model.themeId == "light"
        var t = tint
        if light { t = RGBA(UInt8(185 + Double(t.r) * 0.35), UInt8(185 + Double(t.g) * 0.35), UInt8(185 + Double(t.b) * 0.35)) }
        if let custom = model.appearance.colors["AlbumTint"], let c = RGBA(hex: custom) { t = c }
        let middle = light ? RGBA(UInt8(210 + Double(t.r) * 0.15), UInt8(210 + Double(t.g) * 0.15), UInt8(210 + Double(t.b) * 0.15)) : RGBA(UInt8(Double(t.r) * 0.55), UInt8(Double(t.g) * 0.55), UInt8(Double(t.b) * 0.65))
        return [.init(color: Color(t), location: 0), .init(color: Color(middle), location: 0.55), .init(color: theme.canvas, location: 1)]
    }
}

// MARK: - Window plumbing

/// Positions the traffic lights in the 44 pt title bar and keeps the window chrome transparent.
struct WindowConfigurator: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { let v = ConfigView(); return v }
    func updateNSView(_ nsView: NSView, context: Context) {}
    final class ConfigView: NSView {
        var observers: [NSObjectProtocol] = []
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            guard let w = window else { return }
            w.titlebarAppearsTransparent = true; w.titleVisibility = .hidden
            w.styleMask.insert(.fullSizeContentView)
            w.isMovableByWindowBackground = false
            w.setFrameAutosaveName("FaemyrMain")
            layoutButtons()
            for n in [NSWindow.didResizeNotification, NSWindow.didEndLiveResizeNotification, NSWindow.didExitFullScreenNotification, NSWindow.didBecomeKeyNotification] {
                observers.append(NotificationCenter.default.addObserver(forName: n, object: w, queue: .main) { [weak self] _ in self?.layoutButtons() })
            }
        }
        func layoutButtons() {
            guard let w = window, !w.styleMask.contains(.fullScreen) else { return }
            for (i, type) in [NSWindow.ButtonType.closeButton, .miniaturizeButton, .zoomButton].enumerated() {
                guard let b = w.standardWindowButton(type), let sup = b.superview else { continue }
                let y = sup.frame.height - 22 - b.frame.height / 2
                b.setFrameOrigin(NSPoint(x: 16 + CGFloat(i) * 20, y: y))
            }
        }
    }
}

/// Lets the empty parts of the custom title bar move and zoom the window.
struct WindowDragArea: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { DragView() }
    func updateNSView(_ nsView: NSView, context: Context) {}
    final class DragView: NSView {
        override func mouseDown(with event: NSEvent) {
            if event.clickCount == 2 { window?.performZoom(nil) } else { window?.performDrag(with: event) }
        }
    }
}
