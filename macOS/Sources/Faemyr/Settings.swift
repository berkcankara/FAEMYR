import SwiftUI
import AppKit
import FaemyrCore

@MainActor final class SettingsController: NSObject, NSWindowDelegate {
    static let shared = SettingsController()
    var window: NSWindow?
    weak var model: AppModel?
    var snapshot: (String, AppearancePreferences, InterfacePreferences, PanelPreferences, AudioPreferences, LyricsPreferences, ArtistPhotoPreferences, VisualizerMode, PlayerPreferences)?
    var committed = false

    func open(_ model: AppModel) {
        self.model = model
        if let w = window { w.makeKeyAndOrderFront(nil); return }
        snapshot = (model.themeId, model.appearance, model.ui, model.panel, model.audioPrefs, model.lyricsPrefs, model.photoPrefs, model.visualizerMode, model.playerPrefs)
        committed = false
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 960, height: 850), styleMask: [.titled, .closable, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        w.title = tr("Faemyr — Ayarlar"); w.titlebarAppearsTransparent = true; w.minSize = NSSize(width: 800, height: 650)
        w.contentView = NSHostingView(rootView: SettingsView(close: { [weak self] save in self?.finish(save) }).environment(model))
        w.delegate = self; w.isReleasedWhenClosed = false
        if let main = NSApp.mainWindow { let f = main.frame; w.setFrameOrigin(NSPoint(x: f.midX - 480, y: max(f.minY, f.midY - 425))) } else { w.center() }
        window = w; w.makeKeyAndOrderFront(nil)
    }
    func finish(_ save: Bool) {
        guard let model else { return }
        if save {
            model.ui.validate(); model.ui.save(); model.appearance.save(); Themes.saveThemeId(model.themeId); model.panel.save(); model.audioPrefs.save()
            model.lyricsPrefs.save(); model.photoPrefs.save(); model.playerPrefs.save()
            var v = VisualizerPreferences(); v.mode = model.visualizerMode; v.save()
            committed = true
        }
        window?.close()
    }
    func windowWillClose(_ notification: Notification) {
        if !committed, let s = snapshot, let model {
            model.themeId = s.0; model.appearance = s.1; model.ui = s.2; model.panel = s.3; model.audioPrefs = s.4; model.lyricsPrefs = s.5; model.photoPrefs = s.6; model.visualizerMode = s.7; model.playerPrefs = s.8
            model.applyAppearance()
        }
        model?.showSettings = false
        model?.refresh()
        window = nil
    }
}

struct SettingsView: View {
    @Environment(AppModel.self) var model
    let close: (Bool) -> Void
    let sections = [("colors", "Renkler"), ("layout", "Düzen"), ("typography", "Yazı ve kartlar"), ("player", "Oynatıcı"), ("desktop", "Masaüstü oynatıcı"), ("artistPhotos", "Sanatçı görselleri"), ("panel", "Sağ panel"), ("lyrics", "Şarkı sözleri"), ("audio", "Ses"), ("library", "Müzik arşivi")]
    var body: some View {
        @Bindable var model = model
        let theme = model.theme
        VStack(alignment: .leading, spacing: 0) {
            Text(tr("Ayarlar")).font(.system(size: 28, weight: .bold)).padding(.bottom, 24)
            HStack(alignment: .top, spacing: 24) {
                ScrollView {
                    VStack(spacing: 8) {
                        ForEach(sections, id: \.0) { key, title in
                            let active = model.settingsSection == key
                            Button { model.settingsSection = key } label: { Text(tr(title)).frame(maxWidth: .infinity, alignment: .leading).frame(height: 44 - 20) }
                                .buttonStyle(FButtonStyle(background: active ? theme.accent : theme.c("NavigationSurface"), foreground: active ? theme.onAccent : theme.ink))
                        }
                    }
                }
                .frame(width: 150).scrollIndicators(.never)
                ScrollView {
                    Group {
                        switch model.settingsSection {
                        case "layout": LayoutSettings()
                        case "typography": TypographySettings()
                        case "player": PlayerSettings()
                        case "desktop": DesktopSettings()
                        case "artistPhotos": ArtistPhotoSettings()
                        case "panel": PanelSettings()
                        case "lyrics": LyricsSettings()
                        case "audio": AudioSettings()
                        case "library": LibrarySettings()
                        default: ColorSettings()
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading).padding(.trailing, 14)
                }
            }
            HStack {
                Spacer()
                Button(tr("Vazgeç")) { close(false) }.buttonStyle(FButtonStyle()).keyboardShortcut(.cancelAction)
                Button(tr("Kaydet")) { close(true) }.buttonStyle(FButtonStyle(background: theme.accent, foreground: theme.onAccent)).keyboardShortcut(.defaultAction)
            }
            .padding(.top, 20)
        }
        .padding(26)
        .background(theme.canvas)
        .foregroundStyle(theme.ink)
        .environment(\.theme, theme)
        .ignoresSafeArea(edges: .top)
        .padding(.top, 0)
    }
}

// MARK: - Shared controls

struct SettingsHeading: View {
    @Environment(\.theme) var theme
    let title: String; var subtitle: String? = nil
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(.system(size: 23, weight: .semibold))
            if let subtitle { Text(subtitle).font(.system(size: 13)).foregroundStyle(theme.muted) }
        }.padding(.bottom, 16)
    }
}
struct SettingsGroup<Content: View>: View {
    @Environment(\.theme) var theme
    let title: String
    @ViewBuilder var content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title).font(.system(size: 17, weight: .semibold)).padding(.top, 18)
            content
        }
    }
}
struct SettingSlider: View {
    @Environment(\.theme) var theme
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    var step: Double = 1
    var unit = ""
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(title).font(.system(size: 13))
                Spacer()
                Text((step < 1 ? String(format: "%.1f", value) : "\(Int(value.rounded()))") + unit).font(.system(size: 12)).foregroundStyle(theme.muted)
            }
            SeekBar(value: value - range.lowerBound, maximum: range.upperBound - range.lowerBound, onChange: { set($0) }, onCommit: { set($0) }, thumbAlways: true)
        }
    }
    func set(_ raw: Double) { value = ((raw + range.lowerBound) / step).rounded() * step }
}
extension Binding where Value == Int {
    var double: Binding<Double> { Binding<Double>(get: { Double(wrappedValue) }, set: { wrappedValue = Int($0.rounded()) }) }
}

// MARK: - Sections

struct ColorSettings: View {
    @Environment(AppModel.self) var model
    @Local private var role = "Accent"
    @Local private var hex = ""
    var body: some View {
        @Bindable var model = model
        let theme = model.theme
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Renkler ve görünüm"), subtitle: tr("Değişiklikleri hemen önizle. Sonraki oturum için Kaydet’e bas."))
            Text(tr("Tema")).font(.system(size: 13))
            FPicker(items: Themes.choices.map(\.id), label: { id in Themes.choices.first { $0.id == id }!.name }, selection: Binding(get: { model.themeId }, set: { model.themeId = $0; model.applyAppearance() }))
            FSwitch(title: tr("Modül saydamlığı"), isOn: Binding(get: { model.appearance.moduleTransparency }, set: { model.appearance.moduleTransparency = $0; model.applyAppearance() })).font(.system(size: 14, weight: .semibold))
            Text(tr("Yan paneller ve oynatıcı kutuları, bulanık albüm arka planını hafifçe geçirir.")).font(.system(size: 12)).foregroundStyle(theme.muted)
            Text(tr("Dil")).font(.system(size: 13)).padding(.top, 6)
            FPicker(items: ["tr", "en"], label: { $0 == "en" ? "English" : "Türkçe" }, selection: Binding(get: { L.readLanguage() }, set: { L.save($0) }))
            Text(tr("Dil değişikliği Faemyr yeniden başlatıldığında uygulanır.")).font(.system(size: 13)).foregroundStyle(theme.muted)
            HStack(spacing: 16) {
                Icon(name: "play", size: 20, color: theme.onAccent).frame(width: 48, height: 48).background(Circle().fill(theme.accent))
                VStack(alignment: .leading, spacing: 4) {
                    Text(tr("Çalan parça")).font(.system(size: 18, weight: .semibold)).foregroundStyle(theme.playing)
                    Text(tr("Oynatma düğmesi ve çalan parça vurgusu")).font(.system(size: 12)).foregroundStyle(theme.muted)
                }
            }.padding(.vertical, 18).padding(.leading, 18)
            FPicker(items: colorRoles.map(\.key), label: { k in colorRoles.first { $0.key == k }!.name }, selection: $role)
            HStack(spacing: 10) {
                ForEach(["#C8F281", "#F4F4F4", "#5CB8FF", "#A99CFF", "#F28FC1", "#FF9B6A", "#E9C25F", "#4FD1B2", "#4A7BE0", "#D9475B"], id: \.self) { c in
                    Button { apply(c) } label: { RoundedRectangle(cornerRadius: 8).fill(Color(hex: c)).frame(width: 34, height: 34).overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(theme.line)) }.buttonStyle(.plain)
                }
            }
            HStack(spacing: 12) {
                RoundedRectangle(cornerRadius: 8).fill(Color(model.palette[role])).frame(width: 40, height: 40).overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(theme.line))
                FTextField(placeholder: "#RRGGBB", text: $hex).onSubmit { apply(hex) }
                ColorPicker("", selection: Binding(get: { Color(model.palette[role]) }, set: { c in
                    if let n = NSColor(c).usingColorSpace(.sRGB) { apply(String(format: "#%02X%02X%02X", Int(n.redComponent * 255), Int(n.greenComponent * 255), Int(n.blueComponent * 255))) }
                }), supportsOpacity: false).labelsHidden()
                Button(tr("Varsayılan renk")) { reset(role) }.buttonStyle(FButtonStyle(background: .clear))
            }
            SettingSlider(title: tr("Parça listesi zemini"), value: Binding(get: { model.appearance.listOpacity }, set: { model.appearance.listOpacity = $0; model.applyAppearance() }), range: 0...100, unit: "%")
            SettingSlider(title: tr("Liste bulanıklığı"), value: Binding(get: { model.appearance.blurRadius }, set: { model.appearance.blurRadius = $0; model.applyAppearance() }), range: 0...60)
            HStack { Spacer(); Button(tr("Tümünü sıfırla")) { model.appearance = AppearancePreferences(); model.applyAppearance() }.buttonStyle(FButtonStyle(background: .clear)); Spacer() }.padding(.top, 12)
        }
        .onAppear { hex = model.palette[role].hex }
        .onChange(of: role) { hex = model.palette[role].hex }
    }
    func apply(_ value: String) {
        guard let h = AppearancePreferences.normalizeHex(value) else { return }
        switch role { case "Accent": model.appearance.accent = h; case "PlayingAccent": model.appearance.playingColor = h; default: model.appearance.colors[role] = h }
        model.applyAppearance(); hex = h
    }
    func reset(_ key: String) {
        switch key { case "Accent": model.appearance.accent = nil; case "PlayingAccent": model.appearance.playingColor = nil; default: model.appearance.colors[key] = nil }
        model.applyAppearance(); hex = model.palette[key].hex
    }
}

struct LayoutSettings: View {
    @Environment(AppModel.self) var model
    var body: some View {
        @Bindable var model = model
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Arayüz düzeni"))
            SettingsGroup(title: tr("Sol menü")) {
                SettingSlider(title: tr("Sol menü genişliği"), value: $model.ui.sidebarWidth, range: 180...260)
                SettingSlider(title: tr("Menü düğmesi yüksekliği"), value: $model.ui.navigationHeight, range: 36...56)
                SettingSlider(title: tr("Menü düğmesi aralığı"), value: $model.ui.navigationGap, range: 4...14)
                ForEach([("albums", "Albümler"), ("artists", "Sanatçılar"), ("tracks", "Tüm parçalar"), ("favorites", "Favoriler"), ("playlists", "Çalma listeleri"), ("queue", "Çalma kuyruğu")], id: \.0) { key, title in
                    FSwitch(title: tr(title) + tr(" menüsünü göster"), isOn: Binding(get: { !model.ui.hiddenNavigation.contains(key) }, set: { on in model.ui.hiddenNavigation.removeAll { $0 == key }; if !on { model.ui.hiddenNavigation.append(key) } }))
                }
            }
            SettingsGroup(title: tr("Koleksiyon")) {
                SettingSlider(title: tr("İç kenar boşluğu"), value: $model.ui.contentPadding, range: 16...42)
                SettingSlider(title: tr("Bir sıradaki albüm sayısı"), value: $model.ui.albumColumns.double, range: 3...8)
                SettingSlider(title: tr("Bir sıradaki sanatçı sayısı"), value: $model.ui.artistColumns.double, range: 3...8)
                SettingSlider(title: tr("Bir sıradaki çalma listesi sayısı"), value: $model.ui.playlistColumns.double, range: 2...6)
                SettingSlider(title: tr("Kartlar arasındaki boşluk"), value: $model.ui.cardGap, range: 2...20)
                FSwitch(title: tr("Arşiv istatistiklerini göster"), isOn: $model.ui.showLibraryStats)
            }
            SettingsGroup(title: tr("Köşeler ve kapaklar")) {
                SettingSlider(title: tr("Kart ve kapak köşeleri"), value: $model.ui.cardRadius, range: 0...32)
                SettingSlider(title: tr("Düğme ve menü köşeleri"), value: $model.ui.buttonRadius, range: 0...24)
                SettingSlider(title: tr("Oynatıcı kutusu köşeleri"), value: $model.ui.panelRadius, range: 0...32)
                SettingSlider(title: tr("Albüm kapağı en büyük boyutu"), value: $model.ui.coverMaximum, range: 220...360)
                FSwitch(title: tr("Kapak renklerini ve görselini arka plana yay"), isOn: $model.ui.albumArtworkBackground)
                SettingSlider(title: tr("Arka plan kapak görünürlüğü"), value: $model.ui.artworkOpacity, range: 0...70, unit: "%")
                SettingSlider(title: tr("Arka plan kapak bulanıklığı"), value: $model.ui.artworkBlur, range: 0...120)
            }
            SettingsGroup(title: tr("Sağ panel ve kaydırma")) {
                FSwitch(title: tr("Sağ oynatıcı panelini göster"), isOn: $model.ui.showRightPanel)
                SettingSlider(title: tr("Sağ panel genişliği"), value: $model.panel.width, range: 280...640)
                SettingSlider(title: tr("Sağ panel yazı ölçeği"), value: $model.ui.panelTextScale, range: 85...135, unit: "%")
                FSwitch(title: tr("Sağ panelde sıradaki parçayı göster"), isOn: $model.ui.showNextTrack)
            }
            ResetButton { let p = InterfacePreferences(); model.ui.sidebarWidth = p.sidebarWidth; model.ui.navigationHeight = p.navigationHeight; model.ui.navigationGap = p.navigationGap; model.ui.contentPadding = p.contentPadding; model.ui.albumColumns = p.albumColumns; model.ui.artistColumns = p.artistColumns; model.ui.playlistColumns = p.playlistColumns; model.ui.cardGap = p.cardGap; model.ui.cardRadius = p.cardRadius; model.ui.buttonRadius = p.buttonRadius; model.ui.panelRadius = p.panelRadius; model.ui.coverMaximum = p.coverMaximum; model.ui.hiddenNavigation = [] }
        }
        .onChange(of: model.ui.albumColumns) { model.refresh() }
        .onChange(of: model.ui.artistColumns) { model.refresh() }
        .onChange(of: model.ui.playlistColumns) { model.refresh() }
    }
}

struct ResetButton: View {
    let action: () -> Void
    var body: some View { Button(tr("Bu bölümü sıfırla"), action: action).buttonStyle(FButtonStyle()).padding(.top, 20) }
}

struct TypographySettings: View {
    @Environment(AppModel.self) var model
    var body: some View {
        @Bindable var model = model
        let fonts = ["Segoe UI"] + NSFontManager.shared.availableFontFamilies
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Yazılar ve şekiller"))
            SettingsGroup(title: tr("Yazı tipi")) {
                Text(tr("Uygulama yazı tipi")).font(.system(size: 13))
                FPicker(items: fonts, label: { $0 == "Segoe UI" ? "System (SF Pro)" : $0 }, selection: $model.ui.fontFamily)
                SettingSlider(title: tr("Genel yazı ölçeği"), value: $model.ui.fontScale, range: 85...125, unit: "%")
                SettingSlider(title: tr("Sayfa başlığı boyutu"), value: $model.ui.pageTitleSize, range: 24...44)
                SettingSlider(title: tr("Albüm adı boyutu"), value: $model.ui.albumTitleSize, range: 30...72)
                SettingSlider(title: tr("Şarkı sözü boyutu"), value: $model.ui.lyricsFontSize, range: 13...28)
            }
            SettingsGroup(title: tr("Albümler")) {
                FSwitch(title: tr("Kartların sanatçı ve parça bilgisini göster"), isOn: $model.ui.showCardDetails)
                FSwitch(title: tr("Sanatçı kapaklarını daire şeklinde göster"), isOn: $model.ui.circularArtists)
                FSwitch(title: tr("Albüm ses bilgisi kutusunu göster"), isOn: $model.ui.showAlbumQuality)
                SettingSlider(title: tr("Albüm oynatma düğmesi yüksekliği"), value: $model.ui.albumButtonHeight, range: 44...60)
            }
            SettingsGroup(title: tr("Parça listeleri")) {
                SettingSlider(title: tr("Parça satırı yüksekliği"), value: $model.ui.rowHeight, range: 38...82)
                FSwitch(title: tr("Alternatif satır rengi kullan"), isOn: $model.ui.alternatingRows)
                FSwitch(title: tr("Parça kalplerini göster"), isOn: $model.ui.showTrackFavorites)
                FSwitch(title: tr("Parça numarası ve oynatma simgesini göster"), isOn: $model.ui.showTrackNumbers)
                FSwitch(title: tr("Yıldızları göster"), isOn: $model.ui.showRatings)
                FSwitch(title: tr("Sanatçı sütununu göster"), isOn: $model.ui.showArtists)
                FSwitch(title: tr("Albüm içinde de sanatçı sütununu göster"), isOn: $model.ui.artistsInAlbums)
                FSwitch(title: tr("Biçim sütununu göster"), isOn: $model.ui.showFormats)
                FSwitch(title: tr("Bitrate sütununu göster"), isOn: $model.ui.showBitrate)
                FSwitch(title: tr("Dalga formu sütununu göster"), isOn: $model.ui.showWaveforms)
                FSwitch(title: tr("Süre sütununu göster"), isOn: $model.ui.showDurations)
                FSwitch(title: tr("Teknik sütunları tüm parça listelerinde göster"), isOn: $model.ui.technicalColumnsEverywhere)
                SettingSlider(title: tr("Sanatçı sütunu genişliği"), value: $model.ui.artistColumnWidth, range: 100...260)
                SettingSlider(title: tr("Dalga formu en büyük genişliği"), value: $model.ui.waveformMaximum, range: 100...260)
            }
            ResetButton { let p = InterfacePreferences(); model.ui.fontFamily = p.fontFamily; model.ui.fontScale = p.fontScale; model.ui.pageTitleSize = p.pageTitleSize; model.ui.albumTitleSize = p.albumTitleSize; model.ui.lyricsFontSize = p.lyricsFontSize; model.ui.rowHeight = p.rowHeight }
        }
    }
}

struct PlayerSettings: View {
    @Environment(AppModel.self) var model
    var body: some View {
        @Bindable var model = model
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Oynatıcı ve visualizer"))
            SettingsGroup(title: tr("Oynatıcı")) {
                SettingSlider(title: tr("Alt oynatıcı yüksekliği"), value: $model.ui.playerHeight, range: 110...200)
                SettingSlider(title: tr("Oynatıcı yan kutu genişliği"), value: $model.ui.playerSideWidth, range: 260...340)
                SettingSlider(title: tr("Ana oynatma düğmesi boyutu"), value: $model.ui.playSize, range: 40...64)
                SettingSlider(title: tr("Ana oynatma düğmesi yuvarlaklığı"), value: $model.ui.playRoundness, range: 0...100, unit: "%")
                FSwitch(title: tr("Oynatıcı kutularının zemin ve çerçevesini göster"), isOn: $model.ui.playerFrames)
                FSwitch(title: tr("Sinyal yolu kutusunu göster"), isOn: $model.ui.showSignal)
                FSwitch(title: tr("Akış çubuğunu göster"), isOn: $model.ui.showSeek)
                FSwitch(title: tr("Süre göstergelerini göster"), isOn: $model.ui.showTimes)
                FSwitch(title: tr("Ses seviyesi kontrolünü göster"), isOn: $model.ui.showVolume)
                FSwitch(title: tr("Karışık çalma düğmesini göster"), isOn: $model.ui.showShuffle)
                FSwitch(title: tr("Tekrar düğmesini göster"), isOn: $model.ui.showRepeat)
            }
            SettingsGroup(title: tr("Görselleştirme")) {
                FSwitch(title: tr("Visualizer'ı göster"), isOn: $model.ui.showVisualizer)
                Text(tr("Visualizer türü")).font(.system(size: 13))
                FPicker(items: VisualizerMode.allCases, label: { $0.title }, selection: Binding(get: { model.visualizerMode }, set: { model.visualizerMode = $0; model.ui.visualizerMode = $0.rawValue }))
                SettingSlider(title: tr("Visualizer kare hızı hedefi"), value: $model.ui.visualizerFps, range: 30...120, unit: " fps")
                SettingSlider(title: tr("Çizgi kalınlığı"), value: $model.ui.traceWidth, range: 0.8...3.5, step: 0.1)
                SettingSlider(title: tr("Çizgi parıltısı"), value: $model.ui.traceGlow, range: 0...45, unit: "%")
                SettingSlider(title: tr("Visualizer görünürlüğü"), value: $model.ui.traceOpacity, range: 20...100, unit: "%")
                FSwitch(title: tr("Şarkı sözü kartının iç parıltısını göster"), isOn: $model.ui.lyricsGlow)
            }
        }
    }
}

struct DesktopSettings: View {
    @Environment(AppModel.self) var model
    var body: some View {
        @Bindable var model = model
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Masaüstü oynatıcı"), subtitle: tr("Mini oynatıcı, medya tuşları ve Denetim Merkezi."))
            FSwitch(title: tr("Açılışta son parçayı otomatik çal"), isOn: $model.playerPrefs.autoPlayOnStartup)
            Button(tr("Mini oynatıcıyı aç")) { MiniPlayerController.shared.toggle(model) }.buttonStyle(FButtonStyle())
            Text(tr("Klavyedeki medya tuşları, AirPods kontrolleri ve Denetim Merkezi’ndeki “Şimdi Çalıyor” kartı Faemyr’i yönetir.")).font(.system(size: 12)).foregroundStyle(model.theme.muted)
        }
    }
}

struct ArtistPhotoSettings: View {
    @Environment(AppModel.self) var model
    var body: some View {
        @Bindable var model = model
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Sanatçı görselleri"), subtitle: tr("Sanatçı fotoğrafları Wikipedia / Wikimedia Commons’tan, lisans ve kaynak bilgisiyle indirilir ve yerel olarak saklanır."))
            FSwitch(title: tr("Çevrimiçi sanatçı fotoğraflarını kullan"), isOn: $model.photoPrefs.enabled)
            FSwitch(title: tr("Sanatçı kartlarında fotoğraf göster"), isOn: $model.photoPrefs.onCards)
            Text(tr("Bir sanatçıya sağ tıklayarak yerel fotoğraf seçebilir, fotoğrafı yenileyebilir veya albüm kapağına dönebilirsin.")).font(.system(size: 12)).foregroundStyle(model.theme.muted)
        }
    }
}

struct PanelSettings: View {
    @Environment(AppModel.self) var model
    @Local private var query = ""
    var body: some View {
        @Bindable var model = model
        let core = ["Dosya", "Sanatçı", "Albüm", "Biçim", "Ortalama bitrate", "Süre"]
        var seen = Set<String>()
        let keys = (core + model.library.prefix(3000).flatMap { $0.fields.map(\.key) } + model.panel.hiddenFields).filter { seen.insert($0.lowercased()).inserted }
            .sorted { a, b in let ca = core.contains(a), cb = core.contains(b); return ca != cb ? ca : a.ciCompare(b) == .orderedAscending }
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Sağ panelinde neler görünsün?"), subtitle: tr("Seçimlerin tüm parçalar için saklanır. Dosyaların etiketleri değişmez."))
            FSwitch(title: tr("Albüm kapağı"), isOn: $model.panel.showCover)
            FSwitch(title: tr("Parça adı"), isOn: $model.panel.showTitle)
            FSwitch(title: tr("Sanatçı adı"), isOn: $model.panel.showArtist)
            FSwitch(title: tr("Şarkı sözleri"), isOn: $model.panel.showLyrics)
            FSwitch(title: tr("Teknik veriler düğmesi"), isOn: $model.panel.showTechnicalButton)
            FSwitch(title: tr("Ses çıkışı kontrolleri"), isOn: $model.panel.showAudioOutput)
            Text(tr("BİLGİ ALANLARI")).font(.system(size: 11)).foregroundStyle(model.theme.accent).padding(.top, 10)
            FTextField(placeholder: tr("Bilgi alanı ara"), text: $query)
            HStack {
                Button(tr("Tümünü göster")) { model.panel.hiddenFields = [] }.buttonStyle(FButtonStyle(padding: EdgeInsets(top: 7, leading: 12, bottom: 7, trailing: 12)))
                Button(tr("Tümünü gizle")) { model.panel.hiddenFields = keys }.buttonStyle(FButtonStyle(padding: EdgeInsets(top: 7, leading: 12, bottom: 7, trailing: 12)))
            }
            ForEach(keys.filter { query.isEmpty || tr($0).ciContains(query) }, id: \.self) { key in
                Toggle(tr(key), isOn: Binding(get: { model.panel.isVisible(key) }, set: { on in model.panel.hiddenFields.removeAll { $0.caseInsensitiveCompare(key) == .orderedSame }; if !on { model.panel.hiddenFields.append(key) } }))
                    .toggleStyle(.checkbox).font(.system(size: 13))
            }
            Button(tr("Varsayılanlar")) { model.panel = PanelPreferences() }.buttonStyle(FButtonStyle()).padding(.top, 12)
        }
    }
}

struct LyricsSettings: View {
    @Environment(AppModel.self) var model
    var body: some View {
        @Bindable var model = model
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Şarkı sözleri"))
            FSwitch(title: tr("Çalan satırı takip et"), isOn: $model.lyricsPrefs.followCurrentLine)
            Text(tr("Zamanlı sözlerde çalan satıra yumuşakça kaydırır. Kapalıyken çalan satır vurgulanır; sözleri kendin kaydırabilirsin. Bu tercih sonraki açılışta da hatırlanır.")).font(.system(size: 12)).foregroundStyle(model.theme.muted)
            Text(tr("Geçerli parçanın söz bilgisi")).font(.system(size: 19, weight: .semibold)).padding(.top, 20)
            Text(model.now.map { $0.title + " · " + $0.artist } ?? tr("Henüz çalan parça yok.")).font(.system(size: 14))
            Text(model.lyricsStatus).foregroundStyle(model.theme.muted)
            Text(tr("Söz servisleri")).font(.system(size: 19, weight: .semibold)).padding(.top, 20)
            Text(tr("İnternette bul / Yeniden ara düğmesi parça bilgilerini LRCLIB’e, gerekirse sanatçı ve parça adını lyrics.ovh’ye gönderir. Müzik dosyası gönderilmez. Bulunan sözler çevrimdışı kullanım için yerel olarak saklanır."))
                .font(.system(size: 12)).foregroundStyle(model.theme.muted).lineSpacing(5)
        }
    }
}

struct AudioSettings: View {
    @Environment(AppModel.self) var model
    var body: some View {
        @Bindable var model = model
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Ses"), subtitle: tr("Core Audio çıkışı, örnekleme hızı eşleme ve ReplayGain."))
            FSwitch(title: tr("Otomatik uyumlu çıkış"), isOn: $model.audioPrefs.automaticOutput)
            Text(tr("Açıkken macOS varsayılan çıkışı kullanılır ve aygıtın örnekleme hızı parçaya göre ayarlanır; parça bitince eski hıza dönülür.")).font(.system(size: 12)).foregroundStyle(model.theme.muted)
            Text(tr("Ses çıkış aygıtı")).font(.system(size: 13)).padding(.top, 8)
            AudioOutputPanel()
            FSwitch(title: tr("Özel mod: aygıtın örnekleme hızını kaynağa eşle"), isOn: $model.audioPrefs.exclusive)
            FSwitch(title: tr("Hog Mode: aygıtı yalnızca Faemyr kullansın"), isOn: $model.audioPrefs.hogMode)
            Text(tr("Hog Mode açıkken diğer uygulamalar bu aygıttan ses çalamaz. Faemyr durduğunda aygıt serbest bırakılır.")).font(.system(size: 12)).foregroundStyle(model.theme.muted)
            SettingsGroup(title: "ReplayGain") {
                FPicker(items: [GainMode.off, .track, .album], label: { [tr("Kapalı"), tr("Parça"), tr("Albüm")][$0.rawValue] }, selection: $model.audioPrefs.replayGain)
                SettingSlider(title: tr("Ön kazanç"), value: $model.audioPrefs.preampDb, range: -12...12, step: 0.5, unit: " dB")
                FSwitch(title: tr("Tepeye göre kırpılmayı önle"), isOn: $model.audioPrefs.preventClipping)
            }
            Text(tr("Değişiklikler bir sonraki parçada uygulanır.")).font(.system(size: 12)).foregroundStyle(model.theme.muted).padding(.top, 8)
        }
    }
}

struct LibrarySettings: View {
    @Environment(AppModel.self) var model
    var body: some View {
        let theme = model.theme
        VStack(alignment: .leading, spacing: 12) {
            SettingsHeading(title: tr("Müzik arşivi"), subtitle: tr("Faemyr müzik dosyalarını yalnızca okur; etiketlerini veya klasörlerini değiştirmez."))
            ForEach(model.roots, id: \.self) { root in
                HStack {
                    Image(systemName: "folder").foregroundStyle(theme.muted)
                    Text(root).lineLimit(1).truncationMode(.middle)
                    Spacer()
                    Button(tr("Kaldır")) { model.removeRoot(root) }.buttonStyle(FButtonStyle(padding: EdgeInsets(top: 6, leading: 10, bottom: 6, trailing: 10)))
                }
                .padding(10).background(RoundedRectangle(cornerRadius: theme.buttonRadius).fill(theme.surface))
            }
            if model.roots.isEmpty { Text(tr("Henüz klasör eklenmedi.")).foregroundStyle(theme.muted) }
            HStack {
                Button(tr("Klasör ekle")) { model.addFolder() }.buttonStyle(FButtonStyle(background: theme.accent, foreground: theme.onAccent))
                Button(tr("Yeniden tara")) { model.rescan() }.buttonStyle(FButtonStyle()).disabled(model.scanning)
                Button(tr("Tüm etiketleri yeniden oku")) { model.rescan(force: true) }.buttonStyle(FButtonStyle()).disabled(model.scanning)
                if model.scanning { Button(tr("Taramayı durdur")) { model.cancelScan() }.buttonStyle(FButtonStyle()) }
            }
            Text(model.status.isEmpty ? numberText(model.library.count) + tr(" parça") : model.status).font(.system(size: 12)).foregroundStyle(theme.muted)
            Text(tr("Klasör izleme açık: eklenen, değişen veya silinen dosyalar arşive otomatik yansır.")).font(.system(size: 12)).foregroundStyle(theme.muted).padding(.top, 8)
            if FFmpegTools.ffmpeg == nil {
                Text(tr("Ek biçimler (Ogg Vorbis, Opus, APE, WavPack, DSD) için FFmpeg gerekir: Terminal’de “brew install ffmpeg”. FLAC, ALAC, WAV, AIFF, MP3 ve AAC macOS ile yerel çalışır."))
                    .font(.system(size: 12)).foregroundStyle(theme.muted).padding(.top, 8)
            }
            Button(tr("Veri klasörünü Finder’da göster")) { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: Paths.data)]) }.buttonStyle(FButtonStyle()).padding(.top, 8)
        }
    }
}
