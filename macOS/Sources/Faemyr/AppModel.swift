import SwiftUI
import AppKit
import Observation
import FaemyrCore

/// panel-settings.json (PanelCustomization.cs).
struct PanelPreferences: Codable, Equatable {
    var width: Double = 364
    var showCover = true, showTitle = true, showArtist = true, showLyrics = true, showTechnicalButton = true, showAudioOutput = true
    var hiddenFields: [String] = []
    enum CodingKeys: String, CodingKey { case width = "Width", showCover = "ShowCover", showTitle = "ShowTitle", showArtist = "ShowArtist", showLyrics = "ShowLyrics", showTechnicalButton = "ShowTechnicalButton", showAudioOutput = "ShowAudioOutput", hiddenFields = "HiddenFields" }
    init() {}
    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        width = ((try? c.decode(Double.self, forKey: .width)) ?? 364).clamped(280, 640)
        showCover = (try? c.decode(Bool.self, forKey: .showCover)) ?? true; showTitle = (try? c.decode(Bool.self, forKey: .showTitle)) ?? true
        showArtist = (try? c.decode(Bool.self, forKey: .showArtist)) ?? true; showLyrics = (try? c.decode(Bool.self, forKey: .showLyrics)) ?? true
        showTechnicalButton = (try? c.decode(Bool.self, forKey: .showTechnicalButton)) ?? true; showAudioOutput = (try? c.decode(Bool.self, forKey: .showAudioOutput)) ?? true
        hiddenFields = (try? c.decode([String].self, forKey: .hiddenFields)) ?? []
    }
    func isVisible(_ key: String) -> Bool { !hiddenFields.contains { $0.caseInsensitiveCompare(key) == .orderedSame } }
    static var path: String { Paths.file("panel-settings.json") }
    static func load() -> PanelPreferences { PlayerJson.read(PanelPreferences.self, path) ?? PanelPreferences() }
    func save() { try? PlayerJson.write(Self.path, self, pretty: true) }
}

/// signal-display.json (SignalPresentation.cs).
struct SignalDisplayPreferences: Codable, Equatable {
    var showHeading = true
    var hidden: [String] = ["gain", "decoder", "volume", "wavpack", "notes"]
    enum CodingKeys: String, CodingKey { case showHeading = "ShowHeading", hidden = "Hidden" }
    static let choices: [(String, String)] = [("source", "Kaynak ses biçimi"), ("device", "Çıkış aygıtı"), ("output", "Çıkış modu ve Windows biçimi"), ("gain", "ReplayGain"), ("decoder", "Decoder ve kesintisiz oynatma"), ("volume", "Ses seviyesi"), ("wavpack", "WavPack bilgisi"), ("notes", "Ek teknik notlar")]
    func shows(_ k: String) -> Bool { !hidden.contains(k) }
    static var path: String { Paths.file("signal-display.json") }
    func format(_ raw: String) -> String {
        let lines = raw.components(separatedBy: "\n")
        guard lines.contains(where: { $0.hasPrefix("Kaynak:") || $0.hasPrefix("DSD:") || $0.hasPrefix("Core Audio") }) else { return L.signal(raw) }
        var visible: [String] = []
        for line in lines {
            if line.hasPrefix("Kaynak:") || line.hasPrefix("DSD:") {
                if let arrow = line.firstIndex(of: "→") {
                    let src = shows("source"), dev = shows("device")
                    if src && dev { visible.append(line) } else if src { visible.append(String(line[..<arrow]).trimmingCharacters(in: .whitespaces)) }
                    else if dev { visible.append("Çıkış: " + line[line.index(after: arrow)...].trimmingCharacters(in: .whitespaces)) }
                } else if shows("source") { visible.append(line) }
                continue
            }
            let cat = line.hasPrefix("Core Audio") ? "output" : line.hasPrefix("ReplayGain") ? "gain" : line.hasPrefix("Akış decoder") ? "decoder" : line.hasPrefix("Ses %") || line.hasPrefix("Sessiz ·") ? "volume" : line.hasPrefix("WavPack:") ? "wavpack" : "notes"
            if shows(cat) { visible.append(line) }
        }
        return visible.isEmpty ? tr("Bilgileri seçmek için sağ tıkla.") : L.signal(visible.joined(separator: "\n"))
    }
}

struct NavigationPlace: Equatable {
    var view: String, album: String, artist: String, playlist: String, tab: String, query: String
    var key: String { view + "\n" + (view == "album" ? album : view == "artist" ? artist : view == "playlist" ? playlist : view == "favorites" ? tab : "") }
}

@MainActor @Observable
final class AppModel {
    // Library
    var library: [Track] = []
    var libraryReady = false
    var roots: [String] = []
    @ObservationIgnored var store: LibraryStore?
    @ObservationIgnored var favorites = FavoriteStore(directory: Paths.data)
    @ObservationIgnored var listening = ListeningStore(directory: Paths.data)
    var playlists: [LocalPlaylist] = []
    @ObservationIgnored var playlistStore: PlaylistStore?

    // Queue & playback
    var queue: [Track] = []
    var queueIndex = -1
    var now: Track?
    @ObservationIgnored let order = PlaybackOrder()
    var shuffle = false
    var repeatMode: RepeatMode = .off
    @ObservationIgnored let audio = AudioEngine()
    var isPlaying = false
    var isPaused = false
    var position: Double = 0
    var duration: Double = 0
    var resumePrepared = false
    var resumeSeconds: Double = 0
    var signalRaw = ""
    var nextTrack: Track?
    var queueUndo: [([Track], Int)] = []

    // Navigation
    var view = "albums"
    var albumKey = ""
    var artistName = ""
    var playlistId = ""
    var favoritesTab = "tracks"
    var search = ""
    var smartViewIndex = 0
    var ratingFilterIndex = 0
    var navigationHistory: [NavigationPlace] = []
    @ObservationIgnored var navigationPlace: NavigationPlace?
    var selectedPaths: Set<String> = []
    var scrollTarget: String?

    // Computed page content (RefreshView)
    var albumRows: [GalleryRow<AlbumCard>] = []
    var artistRows: [GalleryRow<ArtistCard>] = []
    var playlistCards: [LocalPlaylist] = []
    var trackRows: [Track] = []
    var discGrouped = false
    var pageTitle = ""
    var sectionTitle = ""
    var libraryStats = ""
    var emptyTitle = ""
    var emptyHint = ""
    var showCollectionEmpty = false
    var showEmptyPlaylistCreate = false
    var smartFilterError = ""
    var revision = 0

    // Preferences
    var themeId = Themes.loadThemeId()
    var appearance = AppearancePreferences.load()
    var ui = InterfacePreferences.load()
    var panel = PanelPreferences.load()
    var audioPrefs = AudioPreferences.load()
    var playerPrefs = PlayerPreferences.load()
    var lyricsPrefs = LyricsPreferences.load()
    var albumSort = AlbumSortSettings.load()
    var visualizerMode: VisualizerMode = .oscilloscope
    var signalDisplay = PlayerJson.read(SignalDisplayPreferences.self, SignalDisplayPreferences.path) ?? SignalDisplayPreferences()
    var photoPrefs = ArtistPhotoPreferences.load()
    var palette: Palette
    var theme: Theme { Theme(palette: palette, ui: ui) }

    // Status
    var status = ""
    var scanning = false
    @ObservationIgnored var scanTask: Task<Void, Never>?

    // Lyrics
    var lyricsDocument: LyricsDocument?
    var lyricLines: [LyricLine] = []
    var lyricActive = -1
    var lyricsStatus = ""
    var lyricsBusy = false
    @ObservationIgnored var lyricsTask: Task<Void, Never>?
    @ObservationIgnored let lyricsService = LyricsService()

    // Waveforms & bitrate
    var waveforms: [String: WaveformData] = [:]
    var waveformStatus: [String: String] = [:]
    @ObservationIgnored var waveformQueue: WaveformQueue!

    // Windows
    var fullscreen = false
    var fullscreenLyrics = true
    var showMiniPlayer = false
    var showSettings = false
    var settingsSection = "colors"
    var devices: [AudioDevice] = []

    // Artist profile
    var artistBiography: ArtistBiography?
    var artistBiographyText = ""
    @ObservationIgnored var biographyTask: Task<Void, Never>?
    var artistPhotoPath: String?
    var artistPhotoCredit: ArtistPhoto?

    @ObservationIgnored var timer: Timer?
    @ObservationIgnored var listeningTrack: Track?
    @ObservationIgnored var listened = 0.0
    @ObservationIgnored var lastListenPosition = 0.0
    @ObservationIgnored var listenCounted = false
    @ObservationIgnored var lastHistorySave = Date()
    @ObservationIgnored var searchWork: DispatchWorkItem?
    @ObservationIgnored var watcher: FolderWatcher?

    init() {
        L.load()
        palette = Themes.resolve(theme: Themes.loadThemeId(), appearance: AppearancePreferences.load())
        visualizerMode = ui.visualizerMode.flatMap(VisualizerMode.init(rawValue:)) ?? VisualizerPreferences.load().mode
        if let w = ui.rightPanelWidth { panel.width = w }
        waveformQueue = WaveformQueue { [weak self] t, data, error in
            guard let self else { return }
            if let data { self.waveforms[t.path] = data } else { self.waveformStatus[t.path] = error }
        }
        audio.volume = Float(playerPrefs.volume); audio.muted = playerPrefs.muted
        audio.onEnded = { [weak self] in Task { @MainActor in await self?.advance(natural: true) } }
        audio.onAdvanced = { [weak self] t in Task { @MainActor in self?.gaplessAdvanced(t) } }
        audio.onFailed = { [weak self] message in Task { @MainActor in self?.status = message; self?.signalRaw = message; self?.resumePrepared = self?.now != nil } }
        do { playlistStore = try PlaylistStore(directory: Paths.data); playlists = playlistStore?.items ?? [] }
        catch { status = tr("Çalma listeleri okunamadı. Mevcut dosya korundu: ") + error.localizedDescription }
        devices = AudioDevices.outputs()
        AudioDevices.observe { [weak self] in Task { @MainActor in self?.devices = AudioDevices.outputs() } }
        libraryStats = tr("Arşivin hazırlanıyor…")
        timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in Task { @MainActor in self?.tick() } }
        Task { await loadLibrary() }
    }

    // MARK: Appearance

    func applyAppearance() {
        palette = Themes.resolve(theme: themeId, appearance: appearance)
    }
    func saveInterface() { ui.save() }

    // MARK: Startup

    func loadLibrary() async {
        do {
            let s = try LibraryStore()
            store = s
            let loaded = await Task.detached { s.load() }.value
            roots = s.roots()
            for t in loaded { favorites.apply(t) }
            library = loaded
            let changed = AlbumGrouping.apply(library)
            if !changed.isEmpty { s.saveMany(changed); favorites.migrateAlbumEditions(library) }
        } catch { status = tr("Arşiv açılamadı: ") + error.localizedDescription }
        libraryReady = true
        restoreSession()
        refresh()
        startWatching()
        if !roots.isEmpty { Task { await scan(roots, quiet: true) } }
        if playerPrefs.autoPlayOnStartup, let now { try? await play(now, start: resumeSeconds) }
    }

    func restoreSession() {
        guard let s = PlayerSession.load() else { return }
        let r = s.resolve(library)
        queue = r.queue; queueIndex = r.index; now = r.current
        if let o = s.order { order.restore(o) }
        shuffle = order.shuffle; repeatMode = order.repeat
        if let t = r.current { resumePrepared = true; resumeSeconds = r.seconds; position = r.seconds; duration = t.seconds; showDetails(t) }
        view = ["albums", "artists", "tracks", "favorites", "playlists", "queue", "album", "artist", "playlist"].contains(s.view) ? s.view : "albums"
        albumKey = s.albumKey; artistName = s.artistName; playlistId = s.playlistId; favoritesTab = s.favoritesTab; search = s.search
        smartViewIndex = s.smartViewIndex.clamped(0, smartPresets.count - 1)
        if view == "album" && !library.contains(where: { $0.albumKey == albumKey }) { view = "albums" }
        if view == "artist" && ArtistCatalog.resolveProfile(library, artistName) == nil { view = "artists" }
        if view == "playlist" && currentPlaylist == nil { view = "playlists" }
        updateNextTrack()
    }

    func saveSession() {
        var s = PlayerSession()
        s.queue = queue.map(\.path); s.queueIndex = queueIndex; s.trackPath = now?.path
        s.seconds = isPlaying || isPaused ? audio.position : resumeSeconds
        s.view = view; s.albumKey = albumKey; s.artistName = artistName; s.playlistId = playlistId; s.search = search; s.favoritesTab = favoritesTab
        s.order = order.capture(); s.smartViewIndex = smartViewIndex
        s.save()
    }

    func shutdown() {
        saveSession(); listening.save(); playerPrefs.save(); panel.save()
        audio.shutdown()
        try? PlayerJson.write(Paths.file("queue.json"), queue.map(\.path))
        AudioEngine.trimCache()
    }

    // MARK: Timer (MainWindow timer.Tick)

    func tick() {
        guard libraryReady else { return }
        // @Observable notifies on every assignment, even an equal one; assigning unchanged values ten times a
        // second invalidated every view that reads them and kept the whole window re-laying out.
        func set<V: Equatable>(_ key: ReferenceWritableKeyPath<AppModel, V>, _ value: V) { if self[keyPath: key] != value { self[keyPath: key] = value } }
        set(\.isPlaying, audio.isPlaying); set(\.isPaused, audio.isPaused)
        if isPlaying || isPaused { set(\.position, audio.position); set(\.duration, audio.duration) }
        else if resumePrepared { set(\.position, resumeSeconds); set(\.duration, now?.seconds ?? 0) }
        updateListening()
        updateLyricsPlayback()
        if let t = now, isPlaying, !audio.hasPreloaded, preloadAttempt != t.path, audio.duration - audio.position < 12 { preloadAttempt = t.path; preloadNext() }
    }
    @ObservationIgnored var preloadAttempt: String?

    // MARK: Navigation & RefreshView

    func navigate(_ v: String) { view = v; refresh() }
    func openAlbum(_ key: String) { albumKey = key; view = "album"; refresh() }
    func openPlaylist(_ id: String) { playlistId = id; view = "playlist"; search = ""; refresh() }
    func openArtist(_ name: String) {
        guard let profile = ArtistCatalog.resolveProfile(library, name.trimmingCharacters(in: .whitespaces)) else { return }
        if fullscreen { fullscreen = false }
        artistName = profile; view = "artist"; search = ""; refresh()
    }
    func openTrackAlbum(_ t: Track) {
        guard let current = library.first(where: { $0.path.caseInsensitiveCompare(t.path) == .orderedSame }) else { return }
        albumKey = current.albumKey; view = "album"; search = ""; refresh()
    }
    var currentPlaylist: LocalPlaylist? { playlists.first { $0.id == playlistId } }

    func searchChanged() {
        searchWork?.cancel()
        let w = DispatchWorkItem { [weak self] in Task { @MainActor in self?.refresh() } }
        searchWork = w; DispatchQueue.main.asyncAfter(deadline: .now() + 0.18, execute: w)
    }

    func recordNavigation() {
        let next = NavigationPlace(view: view, album: albumKey, artist: artistName, playlist: playlistId, tab: favoritesTab, query: search)
        if let p = navigationPlace, p.key != next.key, !navigatingBack {
            navigationHistory.append(p); if navigationHistory.count > 100 { navigationHistory.removeFirst() }
        }
        navigationPlace = next
    }
    @ObservationIgnored var navigatingBack = false
    func navigateBack() {
        guard libraryReady, let prev = navigationHistory.popLast() else { return }
        navigatingBack = true; defer { navigatingBack = false }
        view = prev.view; albumKey = prev.album; artistName = prev.artist; playlistId = prev.playlist; favoritesTab = prev.tab; search = prev.query
        if view == "album" && !library.contains(where: { $0.albumKey == albumKey }) { view = "albums" }
        if view == "artist" && ArtistCatalog.resolveProfile(library, artistName) == nil { view = "artists" }
        if view == "playlist" && currentPlaylist == nil { view = "playlists" }
        refresh()
    }

    var isHero: Bool { view == "album" || view == "playlist" }
    var isCollection: Bool { ["albums", "artists", "artist", "playlists"].contains(view) || (view == "favorites" && favoritesTab == "albums") }

    func albumCards(_ tracks: [Track]) -> [AlbumCard] {
        var groups: [String: [Track]] = [:]; var order: [String] = []
        for t in tracks { if groups[t.albumKey] == nil { order.append(t.albumKey) }; groups[t.albumKey, default: []].append(t) }
        return order.map { key in
            let g = groups[key]!, f = g[0]
            return AlbumCard(key: key, title: f.album + (f.editionLabel.isEmpty ? "" : " · " + f.editionLabel), artist: f.albumArtist,
                             cover: g.lazy.map(\.cover).first { $0.fileExists }, count: g.count, year: AlbumSorting.year(g), isFavorite: favorites.isAlbumFavorite(key))
        }
    }

    func refresh() {
        guard libraryReady else { return }
        searchWork?.cancel()
        recordNavigation()
        let query = search.trimmingCharacters(in: .whitespaces)
        var err = ""
        let match = TrackQuery.compile(query, listening, &err)
        let list = query.isEmpty ? library : library.filter(match)
        smartFilterError = err
        let favoriteAlbums = view == "favorites" && favoritesTab == "albums", favoriteTracks = view == "favorites" && !favoriteAlbums
        let gallery = view == "albums" || view == "artist" || favoriteAlbums
        let profile = view == "artist"
        let albumCount = Set(library.map(\.albumKey)).count
        let hours = library.reduce(0) { $0 + $1.seconds } / 3600
        libraryStats = numberText(library.count) + tr(" parça · ") + numberText(albumCount) + tr(" albüm · ") + trimmedNumber(hours, 1) + tr(" saat müzik")
        showCollectionEmpty = false; showEmptyPlaylistCreate = false
        trackRows = []

        if gallery {
            var source = list
            if profile { let keys = ArtistCatalog.albumKeys(library, artistName); source = list.filter { keys.contains($0.albumKey) } }
            if favoriteAlbums { source = source.filter { favorites.isAlbumFavorite($0.albumKey) } }
            let cards = AlbumSorting.sort(albumCards(source), albumSort.mode)
            var key: ((AlbumCard) -> String)?
            if view == "albums" && albumSort.groupByLetter { key = albumSort.mode == .artist ? { $0.artist } : albumSort.mode == .title ? { $0.title } : nil }
            albumRows = GallerySections.rows(cards, columns: ui.albumColumns, key: key)
            pageTitle = favoriteAlbums ? tr("Favoriler") : tr("Albümlerin")
            if favoriteAlbums && cards.isEmpty { showEmpty(query.isEmpty ? tr("Henüz favori albüm yok.") : tr("Albüm bulunamadı."), tr("Albüme sağ tıklayıp Favorile seçeneğini kullanarak veya albümü açıp kalp düğmesiyle buraya ekleyebilirsin.")) }
            sectionTitle = profile ? tr("Albümler") : query.isEmpty ? tr("Albümler") : tr("Arama sonuçları")
            if profile { loadArtistProfile() }
        } else if view == "artists" {
            pageTitle = tr("Sanatçılar"); sectionTitle = tr("Koleksiyonundaki sanatçılar")
            let artists = ArtistCatalog.cards(list), descending = albumSort.artistMode == .nameDescending
            artistRows = GallerySections.rows(descending ? artists.reversed() : artists, columns: ui.artistColumns, key: albumSort.groupByLetter ? { $0.name } : nil, descending: descending)
        } else if view == "playlists" {
            for p in playlists where p.smartQuery != nil { p.smartCount = PlaylistStore.resolve(p, library, listening).count }
            pageTitle = tr("Çalma listelerin"); sectionTitle = tr("Senin seçkin")
            playlistCards = playlists.filter { query.isEmpty || ($0.name + " " + $0.description).ciContains(query) }.sorted { $0.name.ciCompare($1.name) == .orderedAscending }
            if playlistCards.isEmpty { showCollectionEmpty = true; showEmptyPlaylistCreate = true; emptyTitle = query.isEmpty ? tr("Kendi seçkini oluştur.") : tr("Liste bulunamadı."); emptyHint = tr("Sevdiğin parçaları bir araya getir, listene bir kapak seç.") }
        } else {
            let albumSongs = view == "album" ? library.filter { $0.albumKey == albumKey } : nil
            var result: [Track]
            if view == "queue" { result = queue }
            else if view == "playlist", let p = currentPlaylist { result = PlaylistStore.resolve(p, library, listening) }
            else if let albumSongs { result = AlbumDiscs.order(albumSongs) }
            else { result = library.sorted { a, b in
                if a.artist != b.artist { return a.artist < b.artist }; if a.album != b.album { return a.album < b.album }
                if a.disc != b.disc { return a.disc < b.disc }; return a.number < b.number } }
            if !query.isEmpty { result = result.filter(match) }
            if view == "tracks" {
                let preset = smartPresets[smartViewIndex.clamped(0, smartPresets.count - 1)]
                var e = ""; let m = TrackQuery.compile(preset.query, listening, &e)
                result = result.filter(m)
                if preset.sort != "library" || smartViewIndex > 0 { result = TrackQuery.sort(result, preset.sort, listening) }
                if let stars = ratingFilterChoices[ratingFilterIndex].stars { result = result.filter { $0.rating == stars } }
            }
            if favoriteTracks { result = result.filter { favorites.isTrackFavorite($0.path) } }
            trackRows = result
            discGrouped = albumSongs != nil && AlbumDiscs.multiple(albumSongs!)
            if isHero { for t in result { if ui.showWaveforms && waveforms[t.path] == nil { waveformQueue.enqueue(t, urgent: true) } } }
            pageTitle = favoriteTracks ? tr("Favoriler") : view == "queue" ? tr("Çalma kuyruğu") : view == "album" ? (albumSongs?.first?.album ?? tr("Albüm")) : view == "playlist" ? (currentPlaylist?.name ?? tr("Çalma listesi")) : tr("Tüm parçalar")
            sectionTitle = view == "queue" ? "\(queue.count)" + tr(" parça sırada") : ""
            if favoriteTracks && result.isEmpty { showEmpty(query.isEmpty ? tr("Henüz favori parça yok.") : tr("Parça bulunamadı."), tr("Bir parçanın yanındaki kalbe tıklayarak veya sağ tık menüsünden favorilerine ekleyebilirsin.")) }
            if view == "tracks" && result.isEmpty && !library.isEmpty { showEmpty(tr("Eşleşen parça yok."), tr("Aramanı veya yıldız filtresini değiştirebilirsin.")) }
            if view == "playlist" && result.isEmpty { showCollectionEmpty = true; emptyTitle = tr("Bu liste müziğini bekliyor."); emptyHint = tr("Albümlerde veya tüm parçalarda bir parçaya sağ tıkla; “Çalma listesine ekle” menüsünden bu listeyi seç.") }
        }
        if view == "favorites" {
            libraryStats = "\(library.filter { favorites.isTrackFavorite($0.path) }.count)" + tr(" favori parça · ") + "\(Set(library.filter { favorites.isAlbumFavorite($0.albumKey) }.map(\.albumKey)).count)" + tr(" favori albüm")
        }
        updateNextTrack()
        revision &+= 1
    }
    func showEmpty(_ title: String, _ hint: String) { showCollectionEmpty = true; showEmptyPlaylistCreate = false; emptyTitle = title; emptyHint = hint }

    // MARK: Favorites / ratings

    func toggleTrackFavorite(_ t: Track) {
        do { try favorites.setTrackFavorite(t.path, !favorites.isTrackFavorite(t.path)) } catch { alert(tr("Favori veya yıldız kaydedilemedi.") + "\n" + error.localizedDescription); return }
        sync(t.path); if view == "favorites" { refresh() } else { revision &+= 1 }
    }
    func rate(_ t: Track, _ r: Int) {
        do { try favorites.setRating(t.path, r) } catch { alert(tr("Favori veya yıldız kaydedilemedi.") + "\n" + error.localizedDescription); return }
        sync(t.path); if view == "tracks" && ratingFilterChoices[ratingFilterIndex].stars != nil { refresh() } else { revision &+= 1 }
    }
    func toggleAlbumFavorite(_ key: String) {
        do { try favorites.setAlbumFavorite(key, !favorites.isAlbumFavorite(key)) } catch { alert(error.localizedDescription); return }
        refresh()
    }
    func sync(_ path: String) {
        for t in library where t.path.caseInsensitiveCompare(path) == .orderedSame { favorites.apply(t) }
        for t in queue where t.path.caseInsensitiveCompare(path) == .orderedSame { favorites.apply(t) }
        if let now, now.path.caseInsensitiveCompare(path) == .orderedSame { favorites.apply(now) }
    }

    func alert(_ text: String, title: String = "Faemyr") {
        let a = NSAlert(); a.messageText = title; a.informativeText = text; a.addButton(withTitle: tr("Tamam")); a.runModal()
    }

    // MARK: Playback

    func startFromView(_ t: Track) async {
        if view != "queue" { rememberQueue(); queue = trackRows }
        queueIndex = queue.firstIndex { $0 === t } ?? queue.firstIndex { $0.path == t.path } ?? 0
        order.reset(queue.count, queueIndex)
        try? await play(t)
    }

    func play(_ t: Track, start: Double = 0, paused: Bool = false) async throws {
        status = tr("Ses çözümleniyor · ") + t.title
        signalRaw = tr("Decoder çalışıyor…")
        do {
            try audio.play(t, start: start, paused: paused, preferences: audioPrefs, deviceUID: audioPrefs.automaticOutput ? nil : audioPrefs.deviceId)
            now = t; resumePrepared = false; resumeSeconds = 0
            beginListening(t); showDetails(t)
            signalRaw = audio.signal; status = tr("Çalıyor · ") + t.quality
            isPlaying = audio.isPlaying; isPaused = audio.isPaused; duration = t.seconds; position = start
            updateNextTrack(); saveSession(); updateNowPlayingInfo(); notifyTrackChange(t)
        } catch {
            signalRaw = tr("Oynatma başlatılamadı"); status = error.localizedDescription
            alert(error.localizedDescription, title: "Faemyr — " + tr("oynatma"))
            throw error
        }
    }

    func togglePlayback() async {
        if audio.isPlaying || audio.isPaused { audio.toggle(); isPlaying = audio.isPlaying; isPaused = audio.isPaused; updateNowPlayingInfo(); return }
        if resumePrepared, let now { try? await play(now, start: resumeSeconds); return }
        if !selectedPaths.isEmpty, let t = trackRows.first(where: { selectedPaths.contains($0.path) }) { await startFromView(t); return }
        if let now { try? await play(now); return }
        if !queue.isEmpty { queueIndex = 0; order.reset(queue.count, 0); try? await play(queue[0]); return }
        if let first = trackRows.first { await startFromView(first) }
    }

    func advance(natural: Bool) async {
        let next = order.next(queue.count, queueIndex, natural)
        if next >= 0 { queueIndex = next; try? await play(queue[next]) }
        else { audio.stop(); isPlaying = false; isPaused = false; status = tr("Çalma kuyruğu tamamlandı."); updateNowPlayingInfo() }
    }

    func previous() async {
        if position > 3 {
            if resumePrepared && !audio.isPlaying && !audio.isPaused { resumeSeconds = 0 } else { audio.seek(0) }
            return
        }
        let p = order.previous(queueIndex)
        if p >= 0 { queueIndex = p; try? await play(queue[p]) }
    }

    func seek(_ seconds: Double) {
        if audio.isPlaying || audio.isPaused { audio.seek(seconds.clamped(0, duration)); position = seconds; if audio.hasPreloaded { audio.cancelPreload() } }
        else if resumePrepared { resumeSeconds = seconds.clamped(0, duration); position = resumeSeconds }
        updateNowPlayingInfo()
    }

    func toggleShuffle() { preloadAttempt = nil; order.setShuffle(!order.shuffle, queue.count, queueIndex); shuffle = order.shuffle; audio.cancelPreload(); updateNextTrack(); saveSession() }
    func cycleRepeat() { preloadAttempt = nil; order.repeat = RepeatMode(rawValue: (order.repeat.rawValue + 1) % 3)!; repeatMode = order.repeat; audio.cancelPreload(); updateNextTrack(); saveSession() }

    func setVolume(_ v: Double) { playerPrefs.volume = v.clamped(0, 1); audio.volume = Float(playerPrefs.volume); if v > 0 && playerPrefs.muted { playerPrefs.muted = false; audio.muted = false } }
    func toggleMute() { playerPrefs.muted.toggle(); audio.muted = playerPrefs.muted; playerPrefs.save() }

    func preloadNext() {
        let i = order.peek(queue.count, queueIndex, true)
        guard i >= 0, i < queue.count else { return }
        _ = audio.preloadNext(queue[i])
    }
    func gaplessAdvanced(_ t: Track) {
        let n = order.next(queue.count, queueIndex, true)
        if n >= 0 { queueIndex = n }
        now = t; beginListening(t); showDetails(t); signalRaw = audio.signal
        duration = t.seconds; updateNextTrack(); saveSession(); updateNowPlayingInfo(); notifyTrackChange(t)
    }

    func updateNextTrack() {
        guard let now, queueIndex >= 0, queueIndex < queue.count, queue[queueIndex].path.caseInsensitiveCompare(now.path) == .orderedSame else { nextTrack = nil; return }
        let i = order.peek(queue.count, queueIndex, true)
        nextTrack = i >= 0 && i < queue.count ? queue[i] : nil
    }

    // MARK: Queue editing (QueueManagement.cs)

    func rememberQueue() { queueUndo.append((queue, queueIndex)); if queueUndo.count > 20 { queueUndo.removeFirst() } }
    func enqueue(_ tracks: [Track], next playNext: Bool) {
        guard !tracks.isEmpty else { return }
        rememberQueue()
        let copies = tracks.map { $0 }
        if playNext && queueIndex >= 0 {
            queue.insert(contentsOf: copies, at: min(queue.count, queueIndex + 1))
            order.reset(queue.count, queueIndex)
            order.prioritize(Array(queueIndex + 1 ..< queueIndex + 1 + copies.count))
        } else {
            queue.append(contentsOf: copies)
            order.reset(queue.count, queueIndex)
        }
        audio.cancelPreload()
        status = playNext ? tr("Hemen sonra çalacak: ") + "\(tracks.count)" + tr(" parça") : "\(tracks.count)" + tr(" parça sıraya eklendi.")
        if view == "queue" { refresh() }; updateNextTrack(); saveSession()
    }
    func removeFromQueue(_ paths: Set<String>) {
        rememberQueue()
        let current = queueIndex >= 0 && queueIndex < queue.count ? queue[queueIndex] : nil
        queue.removeAll { paths.contains($0.path) && $0 !== current }
        queueIndex = current.flatMap { c in queue.firstIndex { $0 === c } } ?? -1
        order.reset(queue.count, queueIndex); audio.cancelPreload(); refresh(); saveSession()
    }
    func clearQueue() { rememberQueue(); queue.removeAll(); queueIndex = -1; order.reset(0, -1); audio.cancelPreload(); status = tr("Kuyruk temizlendi."); refresh(); saveSession() }
    func undoQueue() {
        guard let (q, i) = queueUndo.popLast() else { return }
        queue = q; queueIndex = i
        if let now, let j = queue.firstIndex(where: { $0.path == now.path }) { queueIndex = j }
        order.reset(queue.count, queueIndex); audio.cancelPreload(); refresh(); saveSession()
    }

    // MARK: Listening history

    func beginListening(_ t: Track) { listeningTrack = t; listened = 0; listenCounted = false; lastListenPosition = audio.position; _ = listening.get(t) }
    func updateListening() {
        if let t = listeningTrack, audio.isPlaying {
            let pos = audio.position, delta = pos - lastListenPosition; lastListenPosition = pos
            if delta > 0 && delta < 1.5 {
                listened += delta
                let counted = !listenCounted && listened >= min(30, max(0.05, t.seconds * 0.5))
                listening.add(t, delta, counted: counted); listenCounted = listenCounted || counted
            }
        }
        if Date().timeIntervalSince(lastHistorySave) >= 30 { listening.save(); lastHistorySave = Date(); saveSession() }
    }

    // MARK: Details / lyrics

    var inspected: Track?
    func showDetails(_ t: Track) {
        inspected = t
        selectLyrics(t)
        if view == "artist" { }
    }
    func detailsFor(_ t: Track) -> [MetadataField] {
        var f = [MetadataField("Dosya", t.audioPath), MetadataField("Sanatçı", t.artist), MetadataField("Albüm", t.album), MetadataField("Biçim", t.quality),
                 MetadataField("Ortalama bitrate", t.bitRateLabel + " · " + tr(t.bitRateSource)), MetadataField("Süre", t.duration)]
        if !t.wavPackInfo.isEmpty { f.append(MetadataField(tr("WavPack — decoder raporu"), t.wavPackInfo)) }
        f += t.fields
        return f.filter { panel.isVisible($0.key) }
    }
    func coverPath(_ t: Track?) -> String? {
        guard let t else { return nil }
        if t.cover.fileExists { return t.cover }
        return library.first { $0.albumKey == t.albumKey && $0.cover.fileExists }?.cover
    }

    func selectLyrics(_ t: Track) {
        lyricsTask?.cancel()
        lyricsDocument = nil; lyricLines = []; lyricActive = -1; lyricsStatus = tr("Yerel sözler kontrol ediliyor…"); lyricsBusy = true
        let service = lyricsService
        lyricsTask = Task {
            let doc = await service.local(t)
            guard !Task.isCancelled, now === t || inspected === t else { return }
            lyricsDocument = doc; lyricLines = doc?.lines ?? []; lyricActive = -2
            lyricsStatus = doc == nil ? tr("Dosyada söz yok. İnternette arayabilirsin.") : lyricsDescription(doc!)
            lyricsBusy = false
        }
    }
    /// Online provider names (LRCLIB, lyrics.ovh) are not shown; local sources keep their label.
    func lyricsDescription(_ d: LyricsDocument) -> String {
        let kind = d.instrumental ? tr(" · Enstrümantal") : !d.synced.isEmpty ? tr(" · Zamanlı sözler") : tr(" · Düz sözler")
        return LyricsService.isOnline(d) ? String(kind.dropFirst(3)) : tr(d.source) + kind
    }
    func fetchLyrics() {
        guard let t = now ?? inspected else { return }
        lyricsBusy = true; lyricsStatus = tr("Şarkı sözleri aranıyor…")
        let service = lyricsService
        lyricsTask?.cancel()
        lyricsTask = Task {
            do {
                let doc = try await service.fetch(t)
                guard !Task.isCancelled else { return }
                if let doc {
                    lyricsDocument = doc; lyricLines = doc.lines; lyricActive = -2; lyricsStatus = lyricsDescription(doc)
                    do { try await service.save(t, doc) } catch { lyricsStatus = lyricsDescription(doc) + tr(" · Çevrimdışı kaydedilemedi.") }
                } else { lyricsStatus = tr("Bu kayıtla eşleşen söz bulunamadı.") }
            } catch is URLError { lyricsStatus = tr("İnternet bağlantısı veya söz servisine erişim yok. Yeniden deneyebilirsin.") }
            catch { lyricsStatus = error.localizedDescription }
            lyricsBusy = false
        }
    }
    func updateLyricsPlayback() {
        guard !lyricLines.isEmpty else { return }
        let active = isPlaying || isPaused ? Lrc.active(lyricLines, position) : -1
        if active != lyricActive { lyricActive = active }
    }

    // MARK: Library scanning

    func addFolder() {
        let p = NSOpenPanel(); p.canChooseDirectories = true; p.canChooseFiles = false; p.allowsMultipleSelection = true
        p.message = tr("Müzik arşivinin klasörünü seç"); p.prompt = tr("Seç")
        guard p.runModal() == .OK else { return }
        let paths = p.urls.map(\.path)
        for path in paths { store?.addRoot(path) }
        roots = store?.roots() ?? roots
        startWatching()
        Task { await scan(paths) }
    }
    func removeRoot(_ path: String) {
        store?.removeRoot(path); roots = store?.roots() ?? []
        let prefix = path.hasSuffix("/") ? path : path + "/"
        let gone = library.filter { $0.audioPath.hasPrefix(prefix) }
        store?.deleteMany(gone.map(\.path))
        let goneSet = Set(gone.map(\.path)); library.removeAll { goneSet.contains($0.path) }
        startWatching(); refresh()
    }
    func rescan(force: Bool = false) { guard !scanning else { return }; if roots.isEmpty { addFolder(); return }; Task { await scan(roots, force: force) } }
    func cancelScan() { scanTask?.cancel() }

    nonisolated static func walk(_ roots: [String]) -> [(String, Int64)] {
        var result: [(String, Int64)] = []; var seen = Set<String>()
        for root in roots {
            guard let e = FileManager.default.enumerator(at: URL(fileURLWithPath: root), includingPropertiesForKeys: [.contentModificationDateKey, .isRegularFileKey, .isSymbolicLinkKey], options: [.skipsHiddenFiles, .skipsPackageDescendants]) else { continue }
            for case let url as URL in e {
                let ext = url.pathExtension.lowercased()
                guard MetadataReader.extensions.contains(ext) else { continue }
                let path = url.path; guard seen.insert(path.lowercased()).inserted else { continue }
                let date = (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
                result.append((path, Ticks.from(date)))
            }
        }
        return result.sorted { ($0.0.pathExtLower == "cue" ? 0 : 1) < ($1.0.pathExtLower == "cue" ? 0 : 1) }
    }

    func scan(_ scanRoots: [String], force: Bool = false, quiet: Bool = false) async {
        guard !scanning else { return }
        scanning = true
        let task = Task { await performScan(scanRoots, force: force, quiet: quiet) }
        scanTask = task
        await task.value
        scanning = false; scanTask = nil
        refresh()
    }

    func performScan(_ scanRoots: [String], force: Bool, quiet: Bool) async {
        var errors: [String] = []; var added = 0
        let files = await Task.detached { Self.walk(scanRoots) }.value
        var lookup: [String: Track] = [:]; for t in library { lookup[t.path.lowercased()] = t }
        var covered = Set<String>()
        let covers = Paths.file("covers")
        var pending: [String] = []
        for (path, modified) in files {
            if covered.contains(path.lowercased()) { continue }
            if !force, path.pathExtLower != "cue", let old = lookup[path.lowercased()], old.modified == modified { continue }
            pending.append(path)
        }
        var done = 0; var lastRefresh = Date()
        var index = 0
        while index < pending.count && !Task.isCancelled {
            let batch = Array(pending[index..<min(index + 4, pending.count)]); index += batch.count
            var results: [(String, Result<[Track], Error>)] = []
            let tasks = batch.map { p in
                Task.detached(priority: .utility) { () -> (String, Result<[Track], Error>) in
                    do { return (p, .success(try await MetadataReader.readLibraryFile(p, covers: covers))) } catch { return (p, .failure(error)) }
                }
            }
            for t in tasks { results.append(await t.value) }
            for (path, result) in results {
                done += 1
                status = trf("Taranıyor {}/{} · {}", numberText(done), numberText(pending.count), (path as NSString).lastPathComponent)
                switch result {
                case .success(let tracks):
                    if covered.contains(path.lowercased()) { break }
                    applyImported(path, tracks)
                    for t in tracks { lookup[t.path.lowercased()] = t; if t.isCue { covered.insert(t.audioPath.lowercased()) } }
                    added += tracks.count
                case .failure(let e): errors.append(path + ": " + e.localizedDescription)
                }
            }
            if Date().timeIntervalSince(lastRefresh) > 0.9 { refresh(); lastRefresh = Date() }
        }
        if !Task.isCancelled { reconcile(scanRoots, present: Set(files.map { $0.0.lowercased() })) }
        if !errors.isEmpty { try? errors.joined(separator: "\n").write(toFile: Paths.file("scan-errors.txt"), atomically: true, encoding: .utf8) }
        status = Task.isCancelled ? trf("Tarama durduruldu · {} parça kaydedildi.", added) : trf("Tarama tamamlandı · {} yeni/güncel parça · {} uyarı", added, errors.count)
        if quiet && added == 0 && errors.isEmpty { status = "" }
    }

    func applyImported(_ input: String, _ tracks: [Track]) {
        guard !tracks.isEmpty, let store else { return }
        var affected = Set(tracks.map { $0.albumArtist + "\u{1f}" + $0.album })
        var removed: [String] = []
        if tracks[0].isCue {
            let ids = Set(tracks.map { $0.path.lowercased() })
            for old in library where old.cueSheetPath.caseInsensitiveCompare(input) == .orderedSame && !ids.contains(old.path.lowercased()) { removed.append(old.path) }
            for source in Set(tracks.map { $0.audioPath.lowercased() }) {
                let referenced = favorites.isTrackFavorite(source) || playlists.contains { $0.tracks.contains { $0.lowercased() == source } } || queue.contains { $0.path.lowercased() == source }
                if !referenced { for old in library where !old.isCue && old.path.lowercased() == source { removed.append(old.path) } }
            }
        }
        let byPath = Set(tracks.map { $0.path.lowercased() })
        for old in library where byPath.contains(old.path.lowercased()) {
            if let n = tracks.first(where: { $0.path.lowercased() == old.path.lowercased() }), old.addedAt > Date(timeIntervalSince1970: 0) { n.addedAt = old.addedAt }
            affected.insert(old.albumArtist + "\u{1f}" + old.album)
        }
        let removedSet = Set(removed.map { $0.lowercased() })
        library.removeAll { byPath.contains($0.path.lowercased()) || removedSet.contains($0.path.lowercased()) }
        if !removed.isEmpty { store.deleteMany(removed) }
        for t in tracks { favorites.apply(t); _ = listening.get(t) }
        library.append(contentsOf: tracks); store.saveMany(tracks)
        let changed = AlbumGrouping.apply(library.filter { affected.contains($0.albumArtist + "\u{1f}" + $0.album) })
        if !changed.isEmpty { store.saveMany(changed); favorites.migrateAlbumEditions(library) }
    }

    /// LibrarySync: drop tracks whose files were deleted under a scanned, still-mounted root.
    func reconcile(_ scanRoots: [String], present: Set<String>) {
        var gone: [Track] = []
        for t in library {
            let path = t.audioPath
            guard scanRoots.contains(where: { path.hasPrefix($0.hasSuffix("/") ? $0 : $0 + "/") }) else { continue }
            if present.contains(path.lowercased()) || path.fileExists { continue }
            if path.hasPrefix("/Volumes/") { let vol = "/Volumes/" + (path.dropFirst(9).split(separator: "/").first ?? ""); if !vol.fileExists { continue } }
            gone.append(t)
        }
        guard !gone.isEmpty else { return }
        let set = Set(gone.map { $0.path })
        store?.deleteMany(Array(set)); library.removeAll { set.contains($0.path) }
        queue.removeAll { set.contains($0.path) }
        if let now, set.contains(now.path) { audio.stop(); self.now = nil }
        queueIndex = min(queueIndex, queue.count - 1); order.reset(queue.count, queueIndex)
    }

    func startWatching() {
        watcher?.stop()
        guard !roots.isEmpty else { watcher = nil; return }
        watcher = FolderWatcher(paths: roots) { [weak self] in
            Task { @MainActor in guard let self, !self.scanning else { return }; await self.scan(self.roots, quiet: true) }
        }
    }

    // MARK: Playlists

    func savePlaylists() {
        playlistStore?.items = playlists
        do { try playlistStore?.save() } catch { alert(tr("Çalma listesi kaydedilemedi: ") + error.localizedDescription) }
    }
    func addToPlaylist(_ p: LocalPlaylist, _ tracks: [Track]) {
        var known = Set(p.tracks.map { $0.lowercased() })
        for t in tracks where known.insert(t.path.lowercased()).inserted { p.tracks.append(t.path) }
        savePlaylists(); refresh()
    }
    func deletePlaylist(_ p: LocalPlaylist) {
        let a = NSAlert(); a.messageText = tr("Çalma listesini sil"); a.informativeText = trf("“{}” çalma listesi silinsin mi? Müzik dosyaların korunur.", p.name)
        a.addButton(withTitle: tr("Sil")); a.addButton(withTitle: tr("Vazgeç"))
        guard a.runModal() == .alertFirstButtonReturn else { return }
        playlists.removeAll { $0 === p }; savePlaylists(); view = "playlists"; refresh()
    }

    // MARK: Artist profile

    func loadArtistProfile() {
        let name = artistName
        artistPhotoPath = ArtistPhotoStore.shared.cachedImagePath(name); artistBiography = nil; artistPhotoCredit = nil
        Task {
            if photoPrefs.enabled && !photoPrefs.usesAlbumCover(name), let p = await ArtistPhotoStore.shared.get(name) {
                if artistName == name { artistPhotoPath = ArtistPhotoStore.shared.imagePath(p); artistPhotoCredit = p.file.isEmpty ? nil : p }
            }
        }
        let language = L.current
        artistBiographyText = tr("Wikipedia bilgileri yükleniyor…")
        biographyTask?.cancel()
        biographyTask = Task {
            do {
                let bio = try await ArtistBiographyService.shared.get(name, language)
                guard !Task.isCancelled, artistName == name, view == "artist", L.current == language else { return }
                artistBiography = bio
                artistBiographyText = (bio?.text.isEmpty == false) ? bio!.text : tr("Bu sanatçı için seçili dilde Wikipedia özeti bulunamadı.")
            } catch {
                guard !Task.isCancelled, artistName == name else { return }
                artistBiographyText = tr("Wikipedia'ya şu anda ulaşılamıyor. Profili yeniden açarak tekrar deneyebilirsin.")
            }
        }
    }

    // Hooks implemented in MediaControls.swift
    func updateNowPlayingInfo() { MediaControls.shared.update(self) }
    func notifyTrackChange(_ t: Track) { }
}
