import Foundation

public struct RGBA: Hashable, Codable, Sendable {
    public var r: UInt8, g: UInt8, b: UInt8, a: UInt8
    public init(_ r: UInt8, _ g: UInt8, _ b: UInt8, _ a: UInt8 = 255) { self.r = r; self.g = g; self.b = b; self.a = a }
    /// Accepts #RRGGBB and WPF #AARRGGBB.
    public init?(hex: String) {
        var s = hex.trimmingCharacters(in: .whitespaces); if s.hasPrefix("#") { s.removeFirst() }
        guard let v = UInt32(s, radix: 16) else { return nil }
        switch s.count {
        case 6: self.init(UInt8(v >> 16 & 255), UInt8(v >> 8 & 255), UInt8(v & 255))
        case 8: self.init(UInt8(v >> 16 & 255), UInt8(v >> 8 & 255), UInt8(v & 255), UInt8(v >> 24 & 255))
        default: return nil
        }
    }
    public var hex: String { String(format: "#%02X%02X%02X", r, g, b) }
    public func with(alpha: UInt8) -> RGBA { RGBA(r, g, b, alpha) }
    public static let black = RGBA(0, 0, 0), white = RGBA(255, 255, 255), clear = RGBA(0, 0, 0, 0)
    public var brightness: Double { (0.2126 * Double(r) + 0.7152 * Double(g) + 0.0722 * Double(b)) / 255 }
    public static func foregroundOn(_ c: RGBA) -> RGBA {
        func lin(_ v: UInt8) -> Double { let s = Double(v) / 255; return s <= 0.04045 ? s / 12.92 : pow((s + 0.055) / 1.055, 2.4) }
        let l = 0.2126 * lin(c.r) + 0.7152 * lin(c.g) + 0.0722 * lin(c.b)
        return (l + 0.05) / 0.05 >= 1.05 / (l + 0.05) ? .black : .white
    }
    public static func mix(_ a: RGBA, _ b: RGBA, _ wa: Double) -> RGBA {
        func ch(_ x: UInt8, _ y: UInt8) -> UInt8 { UInt8((Double(x) * wa + Double(y) * (1 - wa)).rounded().clamped(0, 255)) }
        return RGBA(ch(a.r, b.r), ch(a.g, b.g), ch(a.b, b.b))
    }
}

public struct ThemeChoice: Hashable { public let id: String; public let text: String; public var name: String { tr(text) } }

public struct ColorRole: Hashable { public let key: String; public let text: String; public var name: String { tr(text) } }
public let colorRoles: [ColorRole] = [("Accent", "Vurgu rengi"), ("PlayingAccent", "Çalan parça rengi"), ("Canvas", "Ana arka plan"), ("SidebarSurface", "Sol menü"), ("PanelSurface", "Sağ panel"),
    ("PlayerSurface", "Alt oynatıcı"), ("CaptionSurface", "Pencere başlığı"), ("Surface", "Kart ve kutular"), ("ControlSurface", "Düğmeler ve girişler"), ("HoverSurface", "Üzerine gelinen alan"),
    ("SelectedSurface", "Seçili alan"), ("Line", "İnce kenarlıklar"), ("Ink", "Ana yazı"), ("Muted", "İkincil yazı"), ("OnAccent", "Vurgulu düğme simgesi"), ("GlassSurface", "Parça listesi zemini"),
    ("HeaderSurface", "Liste sütun başlıkları"), ("AlternateSurface", "Alternatif liste satırı"), ("AlbumTint", "Albüm renk geçişi"), ("VisualizerAccent", "Visualizer"),
    ("LyricsSurface", "Şarkı sözü kartı"), ("LyricsAccent", "Aktif şarkı sözü ve parıltı"), ("UpNextSurface", "Sıradaki parça kartı"), ("RatingAccent", "Yıldızlar")].map { ColorRole(key: $0.0, text: $0.1) }

public struct AppearancePreferences: Codable, Equatable {
    public var accent: String?
    public var playingColor: String?
    public var listOpacity: Double = 70
    public var blurRadius: Double = 24
    public var moduleTransparency = false
    public var colors: [String: String] = [:]
    enum CodingKeys: String, CodingKey { case accent = "Accent", playingColor = "PlayingColor", listOpacity = "ListOpacity", blurRadius = "BlurRadius", moduleTransparency = "ModuleTransparency", colors = "Colors" }
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        accent = try? c.decodeIfPresent(String.self, forKey: .accent); playingColor = try? c.decodeIfPresent(String.self, forKey: .playingColor)
        listOpacity = (try? c.decode(Double.self, forKey: .listOpacity)) ?? 70; blurRadius = (try? c.decode(Double.self, forKey: .blurRadius)) ?? 24
        moduleTransparency = (try? c.decode(Bool.self, forKey: .moduleTransparency)) ?? false
        colors = (try? c.decode([String: String].self, forKey: .colors)) ?? [:]
    }
    public static func normalizeHex(_ v: String?) -> String? {
        var t = (v ?? "").trimmingCharacters(in: .whitespaces); if !t.hasPrefix("#") { t = "#" + t }
        return t.range(of: "^#[0-9A-Fa-f]{6}$", options: .regularExpression) != nil ? t.uppercased() : nil
    }
    public mutating func validate() {
        accent = Self.normalizeHex(accent); playingColor = Self.normalizeHex(playingColor)
        listOpacity = listOpacity.isFinite ? listOpacity.clamped(0, 100) : 70; blurRadius = blurRadius.isFinite ? blurRadius.clamped(0, 60) : 24
        colors = colors.reduce(into: [:]) { r, p in
            if colorRoles.contains(where: { $0.key == p.key }), p.key != "Accent", p.key != "PlayingAccent", let h = Self.normalizeHex(p.value) { r[p.key] = h }
        }
    }
    public static var path: String { Paths.file("appearance.json") }
    public static func load() -> AppearancePreferences { var p = PlayerJson.read(AppearancePreferences.self, path) ?? AppearancePreferences(); p.validate(); return p }
    public func save() { var c = self; c.validate(); try? PlayerJson.write(Self.path, c, pretty: true) }
}

/// The resolved palette (Themes.Apply + ApplyAppearance on Windows).
public struct Palette: Equatable {
    public var colors: [String: RGBA] = [:]
    public subscript(_ key: String) -> RGBA { colors[key] ?? RGBA(128, 128, 128) }
    public var isLight: Bool { self["Canvas"].brightness > 0.5 }
}

public enum Themes {
    public static let choices = [ThemeChoice(id: "dark", text: "Koyu"), ThemeChoice(id: "light", text: "Açık"), ThemeChoice(id: "warm", text: "Sıcak"), ThemeChoice(id: "oled", text: "OLED · Tam siyah")]
    static let names = ["Canvas", "Surface", "PanelSurface", "PlayerSurface", "CaptionSurface", "ControlSurface", "HoverSurface", "SelectedSurface", "Line", "Ink", "Muted", "Accent", "OnAccent", "GlassSurface", "HeaderSurface", "AlternateSurface"]
    static func values(_ id: String) -> [String] {
        switch id {
        case "oled": return ["#000000", "#000000", "#FF000000", "#FF000000", "#000000", "#000000", "#111111", "#181818", "#242424", "#F0F0F0", "#A0A0A0", "#C8F281", "#142015", "#FF000000", "#FF000000", "#00000000"]
        case "light": return ["#F4F3F1", "#FFFFFF", "#E6FFFFFF", "#F2FFFFFF", "#F4F3F1", "#E7E6E3", "#DCDAD7", "#DAD6CF", "#D1CECA", "#191919", "#65615D", "#486129", "#FFFFFF", "#B3F4F3F1", "#D9E9E6E2", "#09101010"]
        case "warm": return ["#171310", "#241E19", "#D91D1814", "#ED211B16", "#14110E", "#332B23", "#43382E", "#4A3E31", "#46392F", "#F5EBDE", "#B8AA98", "#E7B776", "#21170C", "#B3171310", "#80312620", "#09FFFFFF"]
        default: return ["#0B0B0B", "#181818", "#D9111111", "#ED181818", "#080808", "#242424", "#303030", "#353535", "#303030", "#F0F0F0", "#A0A0A0", "#C8F281", "#142015", "#B30B0B0B", "#80202020", "#09FFFFFF"]
        }
    }
    public static var themePath: String { Paths.file("theme.json") }
    public static func loadThemeId() -> String { let id = PlayerJson.read(String.self, themePath) ?? "dark"; return choices.contains { $0.id == id } ? id : "dark" }
    public static func saveThemeId(_ id: String) { try? PlayerJson.write(themePath, id) }

    public static func base(_ id: String) -> [String: RGBA] {
        var b: [String: RGBA] = [:]
        for (n, v) in zip(names, values(id)) { b[n] = RGBA(hex: v) }
        b["SidebarSurface"] = b["PanelSurface"]; b["LyricsSurface"] = b["Surface"]; b["UpNextSurface"] = b["Surface"]; b["AlbumTint"] = RGBA(48, 48, 48)
        return b
    }
    public static func defaultAccent(_ id: String) -> String { values(id)[11] }

    public static func resolve(theme id: String, appearance raw: AppearancePreferences) -> Palette {
        var appearance = raw; appearance.validate()
        let baseColors = base(id)
        let accent = RGBA(hex: appearance.accent ?? defaultAccent(id))!
        let playing = RGBA(hex: appearance.playingColor ?? appearance.accent ?? defaultAccent(id))!
        let oledKeys: Set<String> = ["Canvas", "SidebarSurface", "PanelSurface", "PlayerSurface", "CaptionSurface", "Surface", "ControlSurface", "GlassSurface", "HeaderSurface", "LyricsSurface", "UpNextSurface", "AlbumTint", "AlternateSurface"]
        func custom(_ key: String, _ fallback: RGBA) -> RGBA {
            if id == "oled" && oledKeys.contains(key) { return key == "AlternateSurface" ? .clear : .black }
            guard let text = appearance.colors[key], let c = RGBA(hex: text) else { return fallback }
            return c.with(alpha: fallback.a)
        }
        var p = Palette()
        for (k, v) in baseColors { p.colors[k] = custom(k, v) }
        let player = p["PlayerSurface"], canvas = p["Canvas"]
        let alpha = Double(player.a) / 255
        p.colors["WorkspaceSurface"] = RGBA.mix(player, canvas, alpha)
        p.colors["CaptionInk"] = RGBA.mix(p["Ink"], p["WorkspaceSurface"], 0.75)
        p.colors["NavigationSurface"] = id == "oled" ? RGBA(38, 38, 38) : RGBA.mix(p["ControlSurface"], p["Ink"], 0.94)
        p.colors["Accent"] = accent; p.colors["PlayingAccent"] = playing
        p.colors["OnAccent"] = custom("OnAccent", RGBA.foregroundOn(accent))
        func frame(_ surfaceKey: String) -> RGBA {
            let s = p[surfaceKey]
            return abs(accent.brightness - s.brightness) >= 0.25 ? accent : (s.brightness > 0.5 ? RGBA(72, 72, 72) : RGBA(220, 220, 220))
        }
        p.colors["SidebarFrameAccent"] = frame("SidebarSurface"); p.colors["DetailFrameAccent"] = frame("PanelSurface")
        p.colors["PlayerFrameAccent"] = frame("Surface"); p.colors["ListFrameAccent"] = frame("GlassSurface")
        for (key, source, a) in [("ModuleSidebarSurface", "SidebarSurface", 0.75), ("ModuleDetailSurface", "PanelSurface", 0.75), ("ModulePlayerSurface", "Surface", 0.75), ("ModuleLyricsSurface", "LyricsSurface", 0.28), ("ModuleUpNextSurface", "UpNextSurface", 0.28)] {
            p.colors[key] = appearance.moduleTransparency ? p[source].with(alpha: UInt8((255 * a).rounded())) : p[source]
        }
        for key in ["VisualizerAccent", "LyricsAccent", "RatingAccent"] { p.colors[key] = custom(key, accent) }
        var glass = custom("GlassSurface", p["Canvas"]); glass.a = UInt8((appearance.listOpacity * 255 / 100).rounded())
        p.colors["GlassSurface"] = glass
        return p
    }
}

/// InterfacePreferences.cs — same JSON keys (interface-settings.json).
public struct InterfacePreferences: Codable, Equatable {
    public var fontFamily = "Segoe UI"
    public var fontScale: Double = 100
    public var pageTitleSize: Double = 32
    public var albumTitleSize: Double = 52
    public var lyricsFontSize: Double = 17
    public var panelTextScale: Double = 100
    public var sidebarWidth: Double = 224
    public var navigationHeight: Double = 42
    public var navigationGap: Double = 8
    public var contentPadding: Double = 30
    public var albumColumns = 6
    public var artistColumns = 6
    public var playlistColumns = 4
    public var cardGap: Double = 8
    public var cardRadius: Double = 12
    public var circularArtists = true
    public var buttonRadius: Double = 8
    public var panelRadius: Double = 14
    public var coverMaximum: Double = 286
    public var rowHeight: Double = 54
    public var columnOrder: [String] = []
    public var hiddenNavigation: [String] = []
    public var waveformMaximum: Double = 176
    public var artistColumnWidth: Double = 150
    public var alternatingRows = true
    public var showRightPanel = true
    public var rightPanelWidth: Double?
    public var showLibraryStats = true
    public var showCardDetails = true
    public var showAlbumQuality = true
    public var showNextTrack = true
    public var albumArtworkBackground = true
    public var artworkOpacity: Double = 30
    public var artworkBlur: Double = 90
    public var showTrackFavorites = true
    public var showTrackNumbers = true
    public var showRatings = true
    public var showArtists = true
    public var artistsInAlbums = false
    public var showFormats = true
    public var showBitrate = true
    public var showWaveforms = true
    public var showDurations = true
    public var technicalColumnsEverywhere = false
    public var playerHeight: Double = 120
    public var playerSideWidth: Double = 260
    public var playSize: Double = 48
    public var playRoundness: Double = 100
    public var albumButtonHeight: Double = 52
    public var playerFrames = true
    public var showSignal = true
    public var showVisualizer = true
    public var showSeek = true
    public var showTimes = true
    public var showVolume = true
    public var showShuffle = true
    public var showRepeat = true
    public var visualizerFps: Double = 60
    public var traceWidth: Double = 1.4
    public var traceGlow: Double = 12
    public var traceOpacity: Double = 100
    public var visualizerMode: Int?
    public var lyricsGlow = true
    public var autoHideScrollbars = true
    public var scrollbarWidth: Double = 12
    public var scrollbarDelay: Double = 1.5

    public init() {}

    enum CodingKeys: String, CodingKey {
        case fontFamily = "FontFamily", fontScale = "FontScale", pageTitleSize = "PageTitleSize", albumTitleSize = "AlbumTitleSize", lyricsFontSize = "LyricsFontSize", panelTextScale = "PanelTextScale"
        case sidebarWidth = "SidebarWidth", navigationHeight = "NavigationHeight", navigationGap = "NavigationGap", contentPadding = "ContentPadding", albumColumns = "AlbumColumns", artistColumns = "ArtistColumns"
        case playlistColumns = "PlaylistColumns", cardGap = "CardGap", cardRadius = "CardRadius", circularArtists = "CircularArtists", buttonRadius = "ButtonRadius", panelRadius = "PanelRadius"
        case coverMaximum = "CoverMaximum", rowHeight = "RowHeight", columnOrder = "ColumnOrder", hiddenNavigation = "HiddenNavigation", waveformMaximum = "WaveformMaximum", artistColumnWidth = "ArtistColumnWidth"
        case alternatingRows = "AlternatingRows", showRightPanel = "ShowRightPanel", rightPanelWidth = "RightPanelWidth", showLibraryStats = "ShowLibraryStats", showCardDetails = "ShowCardDetails"
        case showAlbumQuality = "ShowAlbumQuality", showNextTrack = "ShowNextTrack", albumArtworkBackground = "AlbumArtworkBackground", artworkOpacity = "ArtworkOpacity", artworkBlur = "ArtworkBlur"
        case showTrackFavorites = "ShowTrackFavorites", showTrackNumbers = "ShowTrackNumbers", showRatings = "ShowRatings", showArtists = "ShowArtists", artistsInAlbums = "ArtistsInAlbums"
        case showFormats = "ShowFormats", showBitrate = "ShowBitrate", showWaveforms = "ShowWaveforms", showDurations = "ShowDurations", technicalColumnsEverywhere = "TechnicalColumnsEverywhere"
        case playerHeight = "PlayerHeight", playerSideWidth = "PlayerSideWidth", playSize = "PlaySize", playRoundness = "PlayRoundness", albumButtonHeight = "AlbumButtonHeight", playerFrames = "PlayerFrames"
        case showSignal = "ShowSignal", showVisualizer = "ShowVisualizer", showSeek = "ShowSeek", showTimes = "ShowTimes", showVolume = "ShowVolume", showShuffle = "ShowShuffle", showRepeat = "ShowRepeat"
        case visualizerFps = "VisualizerFps", traceWidth = "TraceWidth", traceGlow = "TraceGlow", traceOpacity = "TraceOpacity", visualizerMode = "VisualizerMode", lyricsGlow = "LyricsGlow"
        case autoHideScrollbars = "AutoHideScrollbars", scrollbarWidth = "ScrollbarWidth", scrollbarDelay = "ScrollbarDelay"
    }

    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        var p = InterfacePreferences()
        func dbl(_ k: CodingKeys, _ v: inout Double) { if let x = try? c.decode(Double.self, forKey: k) { v = x } }
        func int(_ k: CodingKeys, _ v: inout Int) { if let x = try? c.decode(Int.self, forKey: k) { v = x } }
        func bool(_ k: CodingKeys, _ v: inout Bool) { if let x = try? c.decode(Bool.self, forKey: k) { v = x } }
        if let f = try? c.decode(String.self, forKey: .fontFamily) { p.fontFamily = f }
        dbl(.fontScale, &p.fontScale); dbl(.pageTitleSize, &p.pageTitleSize); dbl(.albumTitleSize, &p.albumTitleSize); dbl(.lyricsFontSize, &p.lyricsFontSize); dbl(.panelTextScale, &p.panelTextScale)
        dbl(.sidebarWidth, &p.sidebarWidth); dbl(.navigationHeight, &p.navigationHeight); dbl(.navigationGap, &p.navigationGap); dbl(.contentPadding, &p.contentPadding)
        int(.albumColumns, &p.albumColumns); int(.artistColumns, &p.artistColumns); int(.playlistColumns, &p.playlistColumns)
        dbl(.cardGap, &p.cardGap); dbl(.cardRadius, &p.cardRadius); bool(.circularArtists, &p.circularArtists); dbl(.buttonRadius, &p.buttonRadius); dbl(.panelRadius, &p.panelRadius)
        dbl(.coverMaximum, &p.coverMaximum); dbl(.rowHeight, &p.rowHeight)
        p.columnOrder = (try? c.decode([String].self, forKey: .columnOrder)) ?? []; p.hiddenNavigation = (try? c.decode([String].self, forKey: .hiddenNavigation)) ?? []
        dbl(.waveformMaximum, &p.waveformMaximum); dbl(.artistColumnWidth, &p.artistColumnWidth); bool(.alternatingRows, &p.alternatingRows); bool(.showRightPanel, &p.showRightPanel)
        p.rightPanelWidth = try? c.decodeIfPresent(Double.self, forKey: .rightPanelWidth)
        bool(.showLibraryStats, &p.showLibraryStats); bool(.showCardDetails, &p.showCardDetails); bool(.showAlbumQuality, &p.showAlbumQuality); bool(.showNextTrack, &p.showNextTrack)
        bool(.albumArtworkBackground, &p.albumArtworkBackground); dbl(.artworkOpacity, &p.artworkOpacity); dbl(.artworkBlur, &p.artworkBlur)
        bool(.showTrackFavorites, &p.showTrackFavorites); bool(.showTrackNumbers, &p.showTrackNumbers); bool(.showRatings, &p.showRatings); bool(.showArtists, &p.showArtists); bool(.artistsInAlbums, &p.artistsInAlbums)
        bool(.showFormats, &p.showFormats); bool(.showBitrate, &p.showBitrate); bool(.showWaveforms, &p.showWaveforms); bool(.showDurations, &p.showDurations); bool(.technicalColumnsEverywhere, &p.technicalColumnsEverywhere)
        dbl(.playerHeight, &p.playerHeight); dbl(.playerSideWidth, &p.playerSideWidth); dbl(.playSize, &p.playSize); dbl(.playRoundness, &p.playRoundness); dbl(.albumButtonHeight, &p.albumButtonHeight)
        bool(.playerFrames, &p.playerFrames); bool(.showSignal, &p.showSignal); bool(.showVisualizer, &p.showVisualizer); bool(.showSeek, &p.showSeek); bool(.showTimes, &p.showTimes)
        bool(.showVolume, &p.showVolume); bool(.showShuffle, &p.showShuffle); bool(.showRepeat, &p.showRepeat)
        dbl(.visualizerFps, &p.visualizerFps); dbl(.traceWidth, &p.traceWidth); dbl(.traceGlow, &p.traceGlow); dbl(.traceOpacity, &p.traceOpacity)
        p.visualizerMode = try? c.decodeIfPresent(Int.self, forKey: .visualizerMode)
        bool(.lyricsGlow, &p.lyricsGlow); bool(.autoHideScrollbars, &p.autoHideScrollbars); dbl(.scrollbarWidth, &p.scrollbarWidth); dbl(.scrollbarDelay, &p.scrollbarDelay)
        p.validate(); self = p
    }

    static func bound(_ v: Double, _ lo: Double, _ hi: Double, _ f: Double) -> Double { v.isFinite ? v.clamped(lo, hi) : f }
    public mutating func validate() {
        if fontFamily.trimmingCharacters(in: .whitespaces).isEmpty || fontFamily.count > 160 || fontFamily.rangeOfCharacter(from: CharacterSet(charactersIn: "/\\:#")) != nil { fontFamily = "Segoe UI" }
        fontScale = Self.bound(fontScale, 85, 125, 100); pageTitleSize = Self.bound(pageTitleSize, 24, 44, 32); albumTitleSize = Self.bound(albumTitleSize, 30, 72, 52)
        lyricsFontSize = Self.bound(lyricsFontSize, 13, 28, 17); panelTextScale = Self.bound(panelTextScale, 85, 135, 100)
        sidebarWidth = Self.bound(sidebarWidth, 180, 260, 224); navigationHeight = Self.bound(navigationHeight, 36, 56, 42); navigationGap = Self.bound(navigationGap, 4, 14, 8)
        contentPadding = Self.bound(contentPadding, 16, 42, 30); albumColumns = albumColumns.clamped(3, 8); artistColumns = artistColumns.clamped(3, 8); playlistColumns = playlistColumns.clamped(2, 6)
        cardGap = Self.bound(cardGap, 2, 20, 8); cardRadius = Self.bound(cardRadius, 0, 32, 12); buttonRadius = Self.bound(buttonRadius, 0, 24, 8); panelRadius = Self.bound(panelRadius, 0, 32, 14)
        coverMaximum = Self.bound(coverMaximum, 220, 360, 286); rowHeight = Self.bound(rowHeight, 38, 82, 54)
        if let w = rightPanelWidth { rightPanelWidth = w.isFinite ? w.clamped(280, 640) : nil }
        let cols = ["favorite", "number", "title", "artist", "format", "bitrate", "waveform", "duration"]
        var seen = Set<String>(); columnOrder = columnOrder.filter { cols.contains($0) && seen.insert($0).inserted }
        hiddenNavigation = Array(Set(hiddenNavigation.filter { ["albums", "artists", "tracks", "favorites", "playlists", "queue"].contains($0) })).sorted()
        waveformMaximum = Self.bound(waveformMaximum, 100, 260, 176); artistColumnWidth = Self.bound(artistColumnWidth, 100, 260, 150)
        artworkOpacity = Self.bound(artworkOpacity, 0, 70, 30); artworkBlur = Self.bound(artworkBlur, 0, 120, 90); playerHeight = Self.bound(playerHeight, 110, 200, 120)
        playerSideWidth = Self.bound(playerSideWidth, 260, 340, 260); playSize = Self.bound(playSize, 40, 64, 48); playRoundness = Self.bound(playRoundness, 0, 100, 100)
        albumButtonHeight = Self.bound(albumButtonHeight, 44, 60, 52); visualizerFps = Self.bound(visualizerFps, 30, 120, 60); traceWidth = Self.bound(traceWidth, 0.8, 3.5, 1.4)
        traceGlow = Self.bound(traceGlow, 0, 45, 12); traceOpacity = Self.bound(traceOpacity, 20, 100, 100); scrollbarWidth = Self.bound(scrollbarWidth, 6, 16, 12); scrollbarDelay = Self.bound(scrollbarDelay, 0.5, 4, 1.5)
        if let m = visualizerMode, !(0...3).contains(m) { visualizerMode = nil }
    }
    public static var path: String { Paths.file("interface-settings.json") }
    public static func load() -> InterfacePreferences { PlayerJson.read(InterfacePreferences.self, path) ?? InterfacePreferences() }
    public func save() { var c = self; c.validate(); try? PlayerJson.write(Self.path, c) }
    public func font(_ size: Double) -> Double { size * fontScale / 100 }
}
