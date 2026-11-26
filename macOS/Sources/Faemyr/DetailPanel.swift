import SwiftUI
import AppKit
import FaemyrCore

struct DetailPanel: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var dragStart: Double?
    @Local private var edges = ScrollEdges()

    var body: some View {
        let t = model.inspected ?? model.now
        GeometryReader { geo in
            let width = geo.size.width
            let scale = ((width - 40) / 300).clamped(0.8, 1.8)
            let fontScale = theme.ui.panelTextScale / 100
            VStack(spacing: 0) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 0) {
                        if model.panel.showCover {
                            ZStack {
                                RoundedRectangle(cornerRadius: theme.cardRadius).fill(Color(.sRGB, red: 32 / 255, green: 32 / 255, blue: 32 / 255))
                                Text("♫").font(theme.font(64)).foregroundStyle(theme.muted)
                                Thumb(path: model.coverPath(t), pixels: 720, radius: theme.cardRadius)
                            }
                            .aspectRatio(1, contentMode: .fit)
                            .padding(.bottom, 18)
                        }
                        if model.panel.showTitle {
                            HoverWrap { h in
                                Text(t?.title ?? tr("Henüz çalan parça yok"))
                                    .font(.system(size: ((27 * scale).clamped(23, 40)) * fontScale * theme.ui.fontScale / 100, weight: .bold))
                                    .foregroundStyle(h && t != nil ? theme.accent : theme.ink).underline(h && t != nil)
                                    .fixedSize(horizontal: false, vertical: true).padding(.bottom, 7)
                            }
                            .onTapGesture { if let t { model.openTrackAlbum(t) } }
                            .help(t != nil ? tr("Albümü aç") : "")
                        }
                        if model.panel.showArtist, let t {
                            ArtistLinks(names: t.artist, size: (14 * sqrt(scale)).clamped(13, 19) * fontScale, color: theme.muted).padding(.bottom, 24)
                        }
                        if model.panel.showLyrics && model.now != nil { LyricsCard(compact: true).padding(.bottom, 24) }
                        if theme.ui.showNextTrack, let next = model.nextTrack { UpNextCard(track: next).padding(.bottom, 24) }
                        if model.panel.showTechnicalButton, t != nil {
                            Button(tr("Tüm teknik veriler / JSON")) { if let t { RawMetadataWindow.open(t, model) } }
                                .buttonStyle(FButtonStyle(padding: EdgeInsets(top: 9, leading: 10, bottom: 9, trailing: 10))).font(theme.font(11)).padding(.bottom, 22)
                        }
                        if let t {
                            let label = (10.5 * sqrt(scale)).clamped(10, 14) * fontScale, value = (12 * sqrt(scale)).clamped(11.5, 16) * fontScale
                            ForEach(Array(model.detailsFor(t).enumerated()), id: \.offset) { _, f in
                                VStack(alignment: .leading, spacing: 5) {
                                    Text(f.label).font(theme.font(label)).foregroundStyle(theme.muted)
                                    if f.isArtist { ArtistLinks(names: f.value, size: value) }
                                    else { Text(f.value).font(theme.font(value)).textSelection(.enabled).fixedSize(horizontal: false, vertical: true).lineLimit(20) }
                                }
                                .padding(.bottom, 17)
                            }
                        }
                    }
                    .padding(.trailing, 8 + 8)
                    .background(ScrollEdgeProbe(edges: $edges))
                }
                .scrollIndicators(.automatic)
                .frame(minHeight: 0, maxHeight: .infinity)
                .mask(EdgeFade(edges: edges))
                if model.panel.showAudioOutput { AudioOutputPanel().padding(.top, 18).padding(.trailing, 20) }
            }
            .padding(EdgeInsets(top: 16, leading: 16, bottom: 16, trailing: 0))
            .accentPanel(theme.c("ModuleDetailSurface"), rim: theme.c("DetailFrameAccent"), radius: theme.panelRadius)
            .padding(8)
            .overlay(alignment: .leading) {
                // GridSplitter: drag to resize, highlight on hover.
                HoverWrap { h in Rectangle().fill(dragStart != nil ? theme.accent : h ? Color(hex: "#70888888") : Color.clear).frame(width: 7) }
                    .contentShape(Rectangle())
                    .onHover { inside in if inside { NSCursor.resizeLeftRight.push() } else { NSCursor.pop() } }
                    .gesture(DragGesture(minimumDistance: 1, coordinateSpace: .global).onChanged { v in
                        if dragStart == nil { dragStart = model.panel.width }
                        let maxW = ((NSApp.keyWindow?.frame.width ?? 1440) - theme.ui.sidebarWidth - 500).clamped(280, 640)
                        model.panel.width = (dragStart! - v.translation.width).clamped(280, maxW)
                    }.onEnded { _ in dragStart = nil; model.panel.save(); model.ui.rightPanelWidth = model.panel.width; model.saveInterface() })
                    .help(tr("Panel genişliğini ayarla"))
            }
            .contextMenu { Button(tr("Paneli özelleştir…")) { model.settingsSection = "panel"; model.showSettings = true } }
        }
    }
}

struct LyricsCard: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var compact: Bool
    var body: some View {
        let doc = model.lyricsDocument
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(tr("Şarkı sözleri")).font(theme.font(18, .semibold))
                if !model.lyricLines.isEmpty { Text(tr("• Senkron")).font(theme.font(12)).foregroundStyle(theme.muted).opacity(0.72) }
            }
            Text(model.lyricsStatus).font(theme.font(11)).foregroundStyle(theme.muted).fixedSize(horizontal: false, vertical: true).padding(.top, 8).padding(.bottom, 10)
            if !model.lyricLines.isEmpty {
                LyricsLines(fontSize: theme.ui.lyricsFontSize, fullscreen: false).frame(maxHeight: 300)
            } else if let doc {
                ScrollView { Text(doc.instrumental ? tr("Bu kayıt enstrümantal olarak işaretlenmiş.") : doc.plain).font(theme.font(16)).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading) }
                    .frame(maxHeight: 300).scrollIndicators(.never).padding(.top, 10)
            }
            if doc == nil || LyricsService.isOnline(doc) {
                Button(LyricsService.isOnline(doc) ? tr("Yeniden ara") : tr("İnternette bul")) { model.fetchLyrics() }
                    .buttonStyle(FButtonStyle(padding: EdgeInsets(top: 9, leading: 10, bottom: 9, trailing: 10))).font(theme.font(12))
                    .frame(maxWidth: .infinity).disabled(model.lyricsBusy)
                    .padding(.top, 12).padding(.bottom, 7)
                    .help(tr("LRCLIB, gerekirse lyrics.ovh üzerinden arar. Müzik dosyası gönderilmez."))
            }
        }
        .padding(16)
        .accentPanel(theme.c("ModuleLyricsSurface"), rim: theme.c("LyricsAccent"), glow: theme.ui.lyricsGlow ? theme.c("LyricsAccent") : .clear, radius: theme.cardRadius)
    }
}

/// Timed lyric lines: click to seek, active line highlighted, smooth follow.
struct LyricsLines: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var fontSize: Double
    var fullscreen: Bool
    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: fullscreen ? 24 : 14) {
                    ForEach(Array(model.lyricLines.enumerated()), id: \.offset) { i, line in
                        let active = i == model.lyricActive
                        Text(line.text.isEmpty ? "♪" : line.text)
                            .font(theme.font(fontSize, active ? .semibold : .regular))
                            .foregroundStyle(active ? theme.c("LyricsAccent") : theme.muted)
                            .scaleEffect(fullscreen ? (active ? 1.12 : 1) : 1, anchor: .leading)
                            .animation(.easeInOut(duration: 0.28), value: active)
                            .fixedSize(horizontal: false, vertical: true)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .contentShape(Rectangle())
                            .onTapGesture { if model.isPlaying || model.isPaused || model.resumePrepared { model.seek(line.seconds) } }
                            .help(formatTime(line.seconds))
                            .id(i)
                    }
                }
                .padding(.vertical, fullscreen ? 200 : 0)
            }
            .scrollIndicators(.never)
            .mask(fullscreen ? AnyView(LinearGradient(stops: [.init(color: .clear, location: 0), .init(color: .black, location: 0.15), .init(color: .black, location: 0.85), .init(color: .clear, location: 1)], startPoint: .top, endPoint: .bottom)) : AnyView(Color.black))
            .onChange(of: model.lyricActive) {
                guard model.lyricsPrefs.followCurrentLine, model.lyricActive >= 0 else { return }
                withAnimation(.easeInOut(duration: 0.45)) { proxy.scrollTo(model.lyricActive, anchor: UnitPoint(x: 0, y: fullscreen ? 0.5 : 0.35)) }
            }
        }
    }
}

struct UpNextCard: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    let track: Track
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(model.repeatMode == .one ? tr("TEKRAR ÇALACAK") : tr("SIRADAKİ")).font(theme.font(10, .semibold)).foregroundStyle(theme.muted).padding(.bottom, 12)
            HStack(spacing: 12) {
                ZStack { RoundedRectangle(cornerRadius: 6).fill(theme.control); Thumb(path: model.coverPath(track), pixels: 160, radius: 6) }.frame(width: 48, height: 48)
                VStack(alignment: .leading, spacing: 4) {
                    Text(track.title).font(theme.font(14, .semibold)).lineLimit(1)
                    ArtistLinks(names: track.artist, size: 12, color: theme.muted).lineLimit(1)
                }
            }
        }
        .padding(13).frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: theme.cardRadius).fill(theme.c("ModuleUpNextSurface")))
    }
}

struct AudioOutputPanel: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    struct DeviceChoice: Hashable { let uid: String?; let name: String }
    var body: some View {
        let choices = [DeviceChoice(uid: nil, name: tr("macOS varsayılan aygıtı"))] + model.devices.map { DeviceChoice(uid: $0.uid, name: $0.name) }
        let modes = [0, 1, 2]
        VStack(alignment: .leading, spacing: 0) {
            Text(tr("SES ÇIKIŞI")).font(theme.font(10)).foregroundStyle(theme.accent)
            FPicker(items: choices, label: { $0.name }, selection: Binding(get: { choices.first { $0.uid == (model.audioPrefs.automaticOutput ? nil : model.audioPrefs.deviceId) } ?? choices[0] },
                                                                           set: { model.audioPrefs.deviceId = $0.uid; if $0.uid != nil { model.audioPrefs.automaticOutput = false }; model.audioPrefs.save() }))
                .padding(.top, 7).padding(.bottom, 12).help(tr("Ses çıkış aygıtı"))
            FPicker(items: modes, label: { [tr("Core Audio · Paylaşımlı"), tr("Core Audio · Özel (örnekleme hızı eşleme)"), tr("Otomatik · En iyi uyum")][$0] },
                    selection: Binding(get: { model.audioPrefs.automaticOutput ? 2 : model.audioPrefs.exclusive ? 1 : 0 },
                                       set: { m in model.audioPrefs.automaticOutput = m == 2; model.audioPrefs.exclusive = m == 1; model.audioPrefs.save() }))
                .padding(.bottom, 12)
            Text(tr("Seçim bir sonraki oynatmada uygulanır.")).font(theme.font(10)).foregroundStyle(theme.muted)
            Button(tr("Aygıtları yenile")) { model.devices = AudioDevices.outputs() }
                .buttonStyle(FButtonStyle(padding: EdgeInsets(top: 6, leading: 8, bottom: 6, trailing: 8))).font(theme.font(11)).padding(.top, 8)
        }
    }
}

// MARK: - Player bar (PlayerPanel)

struct PlayerBar: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var fullscreen = false
    var body: some View {
        let p = theme.ui
        let left = fullscreen ? max(p.playerSideWidth, p.playSize + 212) : p.sidebarWidth - 16
        let right = fullscreen ? left : (model.ui.showRightPanel ? model.panel.width - 16 : 0)
        let height = max(p.playerHeight, p.playSize + 44 + (p.showVolume ? 24 : 0)) - 16
        HStack(spacing: 0) {
            TransportBox(width: left).frame(width: left)
            ScopeFrame().padding(.horizontal, 8)
            if p.showSignal && (right > 0 || fullscreen) { SignalBox().frame(width: max(right, fullscreen ? left : 0)) }
            else if p.showSignal { SignalBox().frame(width: max(240, model.panel.width - 16)) }
        }
        .frame(height: height)
        .padding(fullscreen ? EdgeInsets(top: 12, leading: 16, bottom: 12, trailing: 16) : EdgeInsets(top: 8, leading: 8, bottom: 8, trailing: 8))
    }
}

struct ModuleBox: ViewModifier {
    @Environment(\.theme) var theme
    func body(content: Content) -> some View {
        let frames = theme.ui.playerFrames
        content.accentPanel(frames ? theme.c("ModulePlayerSurface") : theme.c("PlayerSurface"), rim: theme.c("PlayerFrameAccent"), radius: theme.panelRadius, plain: !frames)
    }
}

struct TransportBox: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var width: Double = 0
    var body: some View {
        let p = theme.ui
        let natural = (p.showShuffle ? 40.0 : 0) + 40 + p.playSize + 16 + 40 + (p.showRepeat ? 40 : 0)
        let fit = width > 0 ? min(1, max(0.4, (width - 12) / natural)) : 1
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                if p.showShuffle {
                    Button { model.toggleShuffle() } label: { ToggleGlyph(icon: "shuffle", active: model.shuffle) }
                        .buttonStyle(TransportStyle(size: 36)).padding(.horizontal, 2).help(tr("Karışık çalma: ") + tr(model.shuffle ? "açık" : "kapalı"))
                }
                Button { Task { await model.previous() } } label: { Icon(name: "previous", size: 22, color: theme.ink) }.buttonStyle(TransportStyle(size: 36)).padding(.horizontal, 2).help(tr("Önceki"))
                Button { Task { await model.togglePlayback() } } label: { Icon(name: model.isPlaying ? "pause" : "play", size: 25, color: theme.onAccent) }
                    .buttonStyle(TransportStyle(size: p.playSize, radius: p.playSize / 2 * p.playRoundness / 100, fill: theme.accent)).padding(.horizontal, 8).help(tr("Oynat / duraklat"))
                Button { Task { await model.advance(natural: false) } } label: { Icon(name: "next", size: 22, color: theme.ink) }.buttonStyle(TransportStyle(size: 36)).padding(.horizontal, 2).help(tr("Sonraki"))
                if p.showRepeat {
                    Button { model.cycleRepeat() } label: {
                        ZStack {
                            ToggleGlyph(icon: "repeat", active: model.repeatMode != .off)
                            if model.repeatMode == .one {
                                Text("1").font(theme.font(9, .bold)).foregroundStyle(theme.accent).frame(width: 11, height: 12).background(RoundedRectangle(cornerRadius: 4).fill(theme.canvas)).offset(y: -2)
                            }
                        }
                    }
                    .buttonStyle(TransportStyle(size: 36)).padding(.horizontal, 2)
                    .help("Tekrar: " + tr(model.repeatMode == .off ? "kapalı" : model.repeatMode == .all ? "tüm kuyruk" : "tek parça"))
                }
            }
            .fixedSize()
            // Windows wraps the buttons in a DownOnly Viewbox: shrink them to fit, never enlarge.
            .scaleEffect(fit, anchor: .center)
            .frame(width: natural * fit)   // scaleEffect alone keeps the unscaled layout width
            .frame(maxWidth: .infinity)
            .frame(height: max(52, p.playSize + 4))
            if p.showVolume { VolumeRow().frame(maxWidth: .infinity).frame(height: 24).padding(.top, 1) }
        }
        .padding(6)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .frame(width: width > 0 ? width : nil)
        .clipped()
        .modifier(ModuleBox())
    }
}

struct ToggleGlyph: View {
    @Environment(\.theme) var theme
    let icon: String
    let active: Bool
    var body: some View {
        ZStack(alignment: .bottom) {
            Icon(name: icon, size: 22, color: active ? theme.accent : theme.ink).frame(width: 28, height: 34)
            Circle().fill(theme.accent).frame(width: 5, height: 5).padding(.bottom, 1).opacity(active ? 1 : 0)
        }.frame(width: 28, height: 34)
    }
}

struct VolumeRow: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        let v = model.playerPrefs.muted ? 0 : model.playerPrefs.volume
        HStack(spacing: 0) {
            Button { model.toggleMute() } label: { Icon(name: model.playerPrefs.muted || v == 0 ? "muted" : "volume", size: 18, color: theme.ink) }
                .buttonStyle(TransportStyle(size: 24)).frame(width: 26).help(tr("Sessize al"))
            SeekBar(value: v * 100, maximum: 100, onChange: { model.setVolume($0 / 100) }, onCommit: { model.setVolume($0 / 100); model.playerPrefs.save() })
                .help(tr("Ses seviyesi"))
            Text("\(Int((v * 100).rounded()))%").font(theme.font(10)).foregroundStyle(theme.muted).frame(width: 36, alignment: .trailing)
        }
        .onScrollWheel { dy in model.setVolume(model.playerPrefs.volume + dy * 0.02); model.playerPrefs.save() }
    }
}

/// Reads the playback position on its own so the ten-per-second updates re-render only this row.
struct SeekRow: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var dragValue: Double?
    var body: some View {
        let p = theme.ui
        HStack(spacing: 0) {
            if p.showTimes { Text(formatTime(dragValue ?? model.position)).font(theme.font(10)).foregroundStyle(theme.muted).frame(width: 36, alignment: .leading) }
            SeekBar(value: dragValue ?? model.position, maximum: max(1, model.duration), onChange: { dragValue = $0 }, onCommit: { model.seek($0); dragValue = nil })
                .disabled(model.now == nil)
            if p.showTimes { Text(formatTime(model.duration)).font(theme.font(10)).foregroundStyle(theme.muted).frame(minWidth: 36) }
        }
    }
}

struct ScopeFrame: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        let p = theme.ui
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                Color.clear.frame(width: p.showTimes ? 36 : 0)
                ZStack {
                    if p.showVisualizer { IsolatedVisualizer().padding(.bottom, 6).opacity(p.traceOpacity / 100) }
                }.frame(maxWidth: .infinity, maxHeight: .infinity)
                VStack(spacing: 4) {
                    Spacer(minLength: 0)
                    Button { withAnimation { model.fullscreen.toggle() } } label: { Icon(name: model.fullscreen ? "fullscreen-exit" : "fullscreen", size: 18, color: theme.ink) }
                        .buttonStyle(TransportStyle(size: 28)).help(model.fullscreen ? tr("Tam ekrandan çık (Esc / F11)") : tr("Tam ekran oynatıcı (F11)"))
                    Button { MiniPlayerController.shared.toggle(model) } label: { Icon(name: "miniplayer", size: 18, color: theme.ink) }
                        .buttonStyle(TransportStyle(size: 28)).help(tr("Mini oynatıcı · Ctrl+Shift+M"))
                }
                .frame(minWidth: 36).padding(.bottom, 4)
            }
            .frame(minHeight: 64)
            if p.showSeek { SeekRow().frame(height: 22) }
        }
        .padding(EdgeInsets(top: 6, leading: 10, bottom: 6, trailing: 10))
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .modifier(ModuleBox())
    }
}

struct SignalBox: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        let raw = model.signalRaw.isEmpty ? tr("Henüz ses çıkışı yok") : model.signalRaw
        VStack(alignment: .leading, spacing: 4) {
            if model.signalDisplay.showHeading { Text(tr("SİNYAL YOLU")).font(theme.font(9)).foregroundStyle(theme.accent) }
            Text(model.signalDisplay.format(raw)).font(.system(size: 9.5 * theme.ui.fontScale / 100)).foregroundStyle(theme.muted).lineSpacing(2)
                .fixedSize(horizontal: false, vertical: true).help(model.signalDisplay.format(raw))
        }
        .padding(12)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .modifier(ModuleBox())
        .contextMenu {
            Toggle(tr("Başlığı göster"), isOn: Binding(get: { model.signalDisplay.showHeading }, set: { model.signalDisplay.showHeading = $0; saveSignal() }))
            Divider()
            ForEach(SignalDisplayPreferences.choices, id: \.0) { key, label in
                Toggle(tr(label == "Çıkış modu ve Windows biçimi" ? "Çıkış modu ve macOS biçimi" : label), isOn: Binding(get: { model.signalDisplay.shows(key) }, set: { on in
                    model.signalDisplay.hidden.removeAll { $0 == key }; if !on { model.signalDisplay.hidden.append(key) }; saveSignal()
                }))
            }
            Divider()
            Button(tr("Varsayılanlara dön")) { model.signalDisplay = SignalDisplayPreferences(); saveSignal() }
        }
    }
    func saveSignal() { try? PlayerJson.write(SignalDisplayPreferences.path, model.signalDisplay) }
}

extension View {
    func onScrollWheel(_ action: @escaping (Double) -> Void) -> some View { overlay(ScrollWheelCatcher(action: action)) }
}
struct ScrollWheelCatcher: NSViewRepresentable {
    let action: (Double) -> Void
    func makeNSView(context: Context) -> NSView { let v = WheelView(); v.action = action; return v }
    func updateNSView(_ v: NSView, context: Context) { (v as? WheelView)?.action = action }
    final class WheelView: NSView {
        var action: ((Double) -> Void)?
        override func scrollWheel(with e: NSEvent) { action?(Double(e.scrollingDeltaY) * (e.hasPreciseScrollingDeltas ? 0.1 : 1)) }
        override func hitTest(_ point: NSPoint) -> NSView? {
            // Only claim wheel events; clicks fall through to the SwiftUI controls below.
            guard let e = NSApp.currentEvent, e.type == .scrollWheel else { return nil }
            return super.hitTest(point)
        }
    }
}
