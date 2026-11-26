import Foundation

// MARK: - Favorites.cs

public struct FavoritesState: Codable {
    public var tracks: [String] = []
    public var albums: [String] = []
    public var ratings: [String: Int] = [:]
    enum CodingKeys: String, CodingKey { case tracks = "Tracks", albums = "Albums", ratings = "Ratings" }
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        tracks = (try? c.decodeIfPresent([String].self, forKey: .tracks)) ?? []
        albums = (try? c.decodeIfPresent([String].self, forKey: .albums)) ?? []
        ratings = (try? c.decodeIfPresent([String: Int].self, forKey: .ratings)) ?? [:]
    }
}

public final class FavoriteStore {
    let path: String
    private var trackSet = Set<String>()     // lowercased paths
    private var trackList: [String: String] = [:]
    private var albumSet = Set<String>()
    private var ratings: [String: (String, Int)] = [:]
    public private(set) var loadError = ""

    public init(directory: String) {
        path = (directory as NSString).appendingPathComponent("favorites.json")
        guard let data = FileManager.default.contents(atPath: path) else { return }
        do {
            let s = try PlayerJson.decoder.decode(FavoritesState.self, from: data)
            for p in s.tracks where !p.trimmingCharacters(in: .whitespaces).isEmpty { trackSet.insert(p.lowercased()); trackList[p.lowercased()] = p }
            albumSet = Set(s.albums.filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty })
            for (k, v) in s.ratings where (1...5).contains(v) && !k.isEmpty { ratings[k.lowercased()] = (k, v) }
        } catch { loadError = error.localizedDescription }
    }
    public func isTrackFavorite(_ p: String) -> Bool { trackSet.contains(p.lowercased()) }
    public func isAlbumFavorite(_ k: String) -> Bool { albumSet.contains(k) }
    public func rating(_ p: String) -> Int { ratings[p.lowercased()]?.1 ?? 0 }
    public func apply(_ t: Track) { t.isFavorite = isTrackFavorite(t.path); t.rating = rating(t.path) }
    func commit() throws {
        if !loadError.isEmpty { throw NSError(domain: "Faemyr", code: 1, userInfo: [NSLocalizedDescriptionKey: "Favoriler dosyası okunamadığı için üzerine yazılmadı. Dosya: " + path + "\n" + loadError]) }
        var s = FavoritesState()
        s.tracks = Array(trackList.values).sorted(); s.albums = albumSet.sorted()
        for (_, v) in ratings { s.ratings[v.0] = v.1 }
        try PlayerJson.write(path, s, pretty: true)
    }
    public func setTrackFavorite(_ p: String, _ value: Bool) throws {
        if value { trackSet.insert(p.lowercased()); trackList[p.lowercased()] = p } else { trackSet.remove(p.lowercased()); trackList[p.lowercased()] = nil }
        try commit()
    }
    public func setAlbumFavorite(_ k: String, _ value: Bool) throws { if value { albumSet.insert(k) } else { albumSet.remove(k) }; try commit() }
    public func setRating(_ p: String, _ r: Int) throws {
        if r == 0 { ratings[p.lowercased()] = nil } else { ratings[p.lowercased()] = (p, r.clamped(1, 5)) }
        try commit()
    }
    public func migrateAlbumEditions(_ tracks: [Track]) {
        var changed = false; var migrated = Set<String>()
        for t in tracks where !t.albumEditionKey.isEmpty {
            let old = t.albumArtist + "\u{1f}" + t.album
            if albumSet.contains(old) { changed = albumSet.insert(t.albumKey).inserted || changed; migrated.insert(old) }
        }
        for o in migrated { albumSet.remove(o); changed = true }
        if changed { try? commit() }
    }
}

public struct RatingFilterChoice: Hashable { public let stars: Int?; public let text: String; public var label: String { tr(text) } }
public let ratingFilterChoices: [RatingFilterChoice] = [RatingFilterChoice(stars: nil, text: "Tüm yıldızlar"), RatingFilterChoice(stars: 0, text: "Yıldızsız")]
    + (1...5).map { RatingFilterChoice(stars: $0, text: String(repeating: "★", count: $0) + " · \($0)/5") }

// MARK: - Playlists.cs

public final class LocalPlaylist: Codable, Identifiable, Hashable {
    public var id = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
    public var name = "Yeni çalma listesi"
    public var description = ""
    public var cover = ""
    public var color = "#53504B"
    public var tracks: [String] = []
    public var created = Date()
    public var smartQuery: String?
    public var smartSort = "library"
    public var smartCount = 0
    enum CodingKeys: String, CodingKey { case id = "Id", name = "Name", description = "Description", cover = "Cover", color = "Color", tracks = "Tracks", created = "Created", smartQuery = "SmartQuery", smartSort = "SmartSort" }
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        id = (try? c.decodeIfPresent(String.self, forKey: .id)) ?? id
        name = (try? c.decodeIfPresent(String.self, forKey: .name)) ?? ""
        description = (try? c.decodeIfPresent(String.self, forKey: .description)) ?? ""
        cover = (try? c.decodeIfPresent(String.self, forKey: .cover)) ?? ""
        color = (try? c.decodeIfPresent(String.self, forKey: .color)) ?? "#53504B"
        tracks = (try? c.decodeIfPresent([String].self, forKey: .tracks)) ?? []
        created = (try? c.decodeIfPresent(Date.self, forKey: .created)) ?? Date()
        smartQuery = try? c.decodeIfPresent(String.self, forKey: .smartQuery)
        smartSort = (try? c.decodeIfPresent(String.self, forKey: .smartSort)) ?? "library"
    }
    public func copy() -> LocalPlaylist {
        let p = LocalPlaylist(); p.id = id; p.name = name; p.description = description; p.cover = cover; p.color = color
        p.tracks = tracks; p.created = created; p.smartQuery = smartQuery; p.smartSort = smartSort; p.smartCount = smartCount; return p
    }
    public var detail: String { smartQuery != nil ? tr("Akıllı liste · ") + "\(smartCount)" + tr(" parça") : "\(tracks.count)" + tr(" parça") }
    public var initial: String { let n = name.trimmingCharacters(in: .whitespaces); return n.isEmpty ? "♪" : String(n.prefix(1)).uppercased() }
    public static func == (a: LocalPlaylist, b: LocalPlaylist) -> Bool { a === b }
    public func hash(into h: inout Hasher) { h.combine(ObjectIdentifier(self)) }
}

public final class PlaylistStore {
    let path: String
    let directory: String
    public var items: [LocalPlaylist] = []
    public init(directory: String) throws {
        self.directory = directory
        path = (directory as NSString).appendingPathComponent("playlists.json")
        guard let data = FileManager.default.contents(atPath: path) else { return }
        items = try PlayerJson.decoder.decode([LocalPlaylist?].self, from: data).compactMap { $0 }
        var ids = Set<String>()
        for p in items {
            if p.id.count < 32 || !ids.insert(p.id).inserted { p.id = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased(); ids.insert(p.id) }
            p.name = p.name.trimmingCharacters(in: .whitespaces).isEmpty ? "Adsız liste" : p.name.trimmingCharacters(in: .whitespaces)
            if !p.cover.isEmpty && !p.cover.fileExists {
                let relocated = ((directory as NSString).appendingPathComponent("playlist-covers") as NSString).appendingPathComponent(windowsLastComponent(p.cover))
                if relocated.fileExists { p.cover = relocated }
            }
            if p.color.range(of: "^#[0-9a-fA-F]{6}$", options: .regularExpression) == nil { p.color = "#53504B" }
            var seen = Set<String>(); p.tracks = p.tracks.filter { !$0.isEmpty && seen.insert($0.lowercased()).inserted }
        }
    }
    public func save() throws { try PlayerJson.write(path, items, pretty: true) }
    public func add(_ p: LocalPlaylist, _ tracks: [Track]) throws {
        var known = Set(p.tracks.map { $0.lowercased() })
        for t in tracks where known.insert(t.path.lowercased()).inserted { p.tracks.append(t.path) }
        try save()
    }
    public static func resolve(_ p: LocalPlaylist, _ library: [Track], _ history: ListeningStore?) -> [Track] {
        if let q = p.smartQuery {
            var err = ""; let match = TrackQuery.compile(q, history, &err)
            return TrackQuery.sort(library.filter(match), p.smartSort, history)
        }
        var lookup: [String: Track] = [:]; for t in library { lookup[t.path.lowercased()] = t }
        return p.tracks.compactMap { lookup[$0.lowercased()] }
    }
    public static func move(_ p: LocalPlaylist, _ path: String, _ delta: Int) {
        guard let i = p.tracks.firstIndex(where: { $0.caseInsensitiveCompare(path) == .orderedSame }) else { return }
        let j = i + delta; guard j >= 0 && j < p.tracks.count else { return }
        p.tracks.swapAt(i, j)
    }
}

// MARK: - ListeningHistory.cs

public final class ListeningStats: Codable {
    public var addedAt = Date()
    public var playCount = 0
    public var lastPlayed: Date?
    public var seconds: Double = 0
    enum CodingKeys: String, CodingKey { case addedAt = "AddedAt", playCount = "PlayCount", lastPlayed = "LastPlayed", seconds = "Seconds" }
    public init() {}
}

public final class ListeningStore {
    let path: String
    var entries: [String: ListeningStats] = [:]
    var dirty = false
    public init(directory: String) {
        path = (directory as NSString).appendingPathComponent("listening-history.json")
        guard let saved = PlayerJson.read([String: ListeningStats].self, path) else { return }
        for (k, v) in saved {
            let key = k.lowercased()
            if let other = entries[key], (other.lastPlayed ?? .distantPast) >= (v.lastPlayed ?? .distantPast) { continue }
            entries[key] = v
        }
    }
    public func get(_ t: Track) -> ListeningStats {
        if let v = entries[t.path.lowercased()] { return v }
        let v = ListeningStats(); v.addedAt = t.addedAt > Date(timeIntervalSince1970: 0) ? t.addedAt : Date()
        entries[t.path.lowercased()] = v; dirty = true; return v
    }
    public func add(_ t: Track, _ seconds: Double, counted: Bool) {
        let e = get(t); e.seconds += max(0, seconds)
        if counted { e.playCount += 1; e.lastPlayed = Date() }
        dirty = true
    }
    public func save() { guard dirty else { return }; try? PlayerJson.write(path, entries); dirty = false }
}

// MARK: - PlaybackOrder.cs

public enum RepeatMode: Int, Codable { case off, all, one }

public struct PlaybackOrderState: Codable {
    public var shuffle: Bool; public var `repeat`: RepeatMode; public var history: [Int]; public var remaining: [Int]
    public var priority: [Int] = []; public var randomState: UInt64 = 0
    enum CodingKeys: String, CodingKey { case shuffle = "Shuffle", `repeat` = "Repeat", history = "History", remaining = "Remaining", priority = "Priority", randomState = "RandomState" }
}

public final class PlaybackOrder {
    var randomState: UInt64
    var history: [Int] = []
    var remaining: [Int] = []
    var priority: [Int] = []
    public private(set) var shuffle = false
    public var `repeat`: RepeatMode = .off
    public init(seed: Int? = nil) { randomState = seed.map { UInt64(UInt32(truncatingIfNeeded: $0)) + 1 } ?? UInt64.random(in: 1..<UInt64(Int64.max)) }
    public func capture() -> PlaybackOrderState { PlaybackOrderState(shuffle: shuffle, repeat: `repeat`, history: history, remaining: remaining, priority: priority, randomState: randomState) }
    public func restore(_ s: PlaybackOrderState) { shuffle = s.shuffle; `repeat` = s.repeat; history = s.history; remaining = s.remaining; priority = s.priority; if s.randomState != 0 { randomState = s.randomState } }
    public func prioritize(_ indices: [Int]) { var seen = Set<Int>(); priority = indices.filter { seen.insert($0).inserted } }
    func randomIndex(_ maximum: Int) -> Int {
        randomState ^= randomState << 13; randomState ^= randomState >> 7; randomState ^= randomState << 17
        return Int(randomState % UInt64(maximum))
    }
    func fill(_ count: Int, _ current: Int) {
        remaining = (0..<count).filter { $0 != current }
        if shuffle && remaining.count > 1 { for i in stride(from: remaining.count - 1, to: 0, by: -1) { remaining.swapAt(i, randomIndex(i + 1)) } }
    }
    public func reset(_ count: Int, _ current: Int) { history.removeAll(); priority.removeAll(); fill(count, current) }
    public func setShuffle(_ on: Bool, _ count: Int, _ current: Int) { shuffle = on; reset(count, current) }
    public func peek(_ count: Int, _ current: Int, _ natural: Bool) -> Int {
        if count == 0 { return -1 }
        if natural && `repeat` == .one && current >= 0 { return current }
        priority.removeAll { $0 < 0 || $0 >= count || $0 == current }
        if let p = priority.first { return p }
        if !shuffle { return current + 1 < count ? current + 1 : (`repeat` == .all ? 0 : -1) }
        if remaining.isEmpty { if `repeat` != .all { return -1 }; if count == 1 { return 0 }; fill(count, current) }
        return remaining[0]
    }
    public func next(_ count: Int, _ current: Int, _ natural: Bool) -> Int {
        let n = peek(count, current, natural)
        if n >= 0 && !(natural && `repeat` == .one) {
            priority.removeAll { $0 == n }
            if shuffle { remaining.removeAll { $0 == n }; if current >= 0 { history.append(current) } }
        }
        return n
    }
    public func previous(_ current: Int) -> Int {
        if shuffle {
            guard let i = history.popLast() else { return -1 }
            remaining.removeAll { $0 == i }
            if current >= 0 && !remaining.contains(current) { remaining.append(current) }
            return i
        }
        return current > 0 ? current - 1 : -1
    }
}

// MARK: - PlayerState.cs

public struct PlayerPreferences: Codable {
    public var volume: Double = 1
    public var muted = false
    public var autoPlayOnStartup = false
    enum CodingKeys: String, CodingKey { case volume = "Volume", muted = "Muted", autoPlayOnStartup = "AutoPlayOnStartup" }
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        volume = ((try? c.decode(Double.self, forKey: .volume)) ?? 1).clamped(0, 1)
        muted = (try? c.decode(Bool.self, forKey: .muted)) ?? false
        autoPlayOnStartup = (try? c.decode(Bool.self, forKey: .autoPlayOnStartup)) ?? false
    }
    public static var path: String { Paths.file("player-settings.json") }
    public static func load() -> PlayerPreferences { PlayerJson.read(PlayerPreferences.self, path) ?? PlayerPreferences() }
    public func save() { try? PlayerJson.write(Self.path, self) }
}

public struct PlayerSession: Codable {
    public var version = 1
    public var queue: [String] = []
    public var queueIndex = -1
    public var trackPath: String?
    public var seconds: Double = 0
    public var view = "albums"
    public var albumKey = ""
    public var artistName = ""
    public var playlistId = ""
    public var search = ""
    public var favoritesTab = "tracks"
    public var selectedPath: String?
    public var order: PlaybackOrderState?
    public var smartViewIndex = 0
    enum CodingKeys: String, CodingKey { case version = "Version", queue = "Queue", queueIndex = "QueueIndex", trackPath = "TrackPath", seconds = "Seconds", view = "View", albumKey = "AlbumKey", artistName = "ArtistName", playlistId = "PlaylistId", search = "Search", favoritesTab = "FavoritesTab", selectedPath = "SelectedPath", order = "Order", smartViewIndex = "SmartViewIndex" }
    public init() {}
    public static var path: String { Paths.file("session.json") }
    public static func load() -> PlayerSession? { guard let s = PlayerJson.read(PlayerSession.self, path), s.version == 1 else { return nil }; return s }
    public func save() { try? PlayerJson.write(Self.path, self) }
    public func resolve(_ library: [Track]) -> (queue: [Track], index: Int, current: Track?, seconds: Double) {
        var lookup: [String: Track] = [:]; for t in library where lookup[t.path.lowercased()] == nil { lookup[t.path.lowercased()] = t }
        var present: [String: Bool] = [:]
        func find(_ p: String?) -> Track? {
            guard let p, let t = lookup[p.lowercased()] else { return nil }
            if present[t.audioPath] == nil { present[t.audioPath] = t.audioPath.fileExists }
            return present[t.audioPath]! ? t : nil
        }
        var q: [Track] = []; var index = -1
        for (i, p) in queue.enumerated() { if let t = find(p) { if i == queueIndex { index = q.count }; q.append(t) } }
        let current = find(trackPath)
        if current == nil || index < 0 || q[index].path.lowercased() != current!.path.lowercased() {
            index = current == nil ? -1 : (q.firstIndex { $0.path.lowercased() == current!.path.lowercased() } ?? -1)
        }
        let secs = current != nil && seconds.isFinite ? seconds.clamped(0, max(0, current!.seconds - 0.05)) : 0
        return (q, index, current, secs)
    }
}

// MARK: - SmartPlaylists.cs (TrackQuery)

public struct SmartPreset: Hashable { public let name: String; public let query: String; public let sort: String
    public init(_ n: String, _ q: String, _ s: String = "library") { name = n; query = q; sort = s } }

public var smartPresets: [SmartPreset] { [SmartPreset(tr("Tüm parçalar"), ""), SmartPreset(tr("Son eklenenler · 30 gün"), "added:30d", "added"), SmartPreset(tr("Hiç dinlemediklerim"), "plays:0"),
    SmartPreset(tr("Son dinlenenler"), "plays:>0", "played"), SmartPreset(tr("En çok dinlenenler"), "plays:>0", "plays"), SmartPreset("1990–2000", "year:1990..2000", "year"),
    SmartPreset(tr("Yüksek çözünürlük"), "bits:>=24 rate:>=96000"), SmartPreset(tr("Yüksek puanlılar"), "rating:>=4")] }

public enum TrackQuery {
    public static var help: String { tr("Sözcük veya tırnak içindeki ifadelerle ara. Koşullar birlikte uygulanır.\n\nartist:Metallica\nalbum:\"Bad Apple!!\"\nformat:flac\nyear:1990..2000\nbits:>=24 rate:>=96000\nrating:>=4\nplays:0\nadded:30d\nplayed:never\nfavorite:true\nmissing:true\nduration:<300\n\nSüre saniye, örnekleme oranı Hz olarak yazılır. added:30d son 30 günde eklenenlerdir. -artist:Deneme koşulu dışlar.") }
    struct QueryError: Error { let message: String }
    static let termPattern = try! NSRegularExpression(pattern: #"(?<not>-?)(?:(?<field>[a-zA-Z]+):(?<value>"[^"]*"|\S+)|(?<text>"[^"]*"|\S+))"#)
    static func numeric(_ value: String) throws -> (Double) -> Bool {
        let range = value.components(separatedBy: "..")
        if range.count == 2, let lo = Double(range[0]), let hi = Double(range[1]) { return { $0 >= lo && $0 <= hi } }
        guard let m = value.range(of: #"^(>=|<=|>|<|=)?([0-9]+(?:\.[0-9]+)?)$"#, options: .regularExpression) else { throw QueryError(message: tr("Sayısal filtre geçersiz: ") + value) }
        let text = String(value[m]); var op = ""
        for prefix in [">=", "<=", ">", "<", "="] where text.hasPrefix(prefix) { op = prefix; break }
        let n = Double(text.dropFirst(op.count))!
        switch op { case ">=": return { $0 >= n }; case "<=": return { $0 <= n }; case ">": return { $0 > n }; case "<": return { $0 < n }; default: return { $0 == n } }
    }
    public static func compile(_ query: String, _ history: ListeningStore?, _ error: inout String) -> (Track) -> Bool {
        error = ""; var terms: [(Track) -> Bool] = []
        let ns = query as NSString
        do {
            for m in termPattern.matches(in: query, range: NSRange(location: 0, length: ns.length)) {
                func g(_ n: String) -> String { let r = m.range(withName: n); return r.location == NSNotFound ? "" : ns.substring(with: r) }
                let field = g("field").lowercased()
                let value = (field.isEmpty ? g("text") : g("value")).trimmingCharacters(in: CharacterSet(charactersIn: "\""))
                let stats: (Track) -> ListeningStats = { t in history?.get(t) ?? { let s = ListeningStats(); s.addedAt = t.addedAt; return s }() }
                var test: (Track) -> Bool
                switch field {
                case "": test = { t in
                    let parts: [String] = [t.title, t.artist, t.album, t.albumArtist, t.audioPath]
                    return parts.joined(separator: " ").ciContains(value) }
                case "artist": test = { ($0.artist + " " + $0.albumArtist).ciContains(value) }
                case "album": test = { $0.album.ciContains(value) }
                case "format": test = { $0.codec.ciContains(value) || $0.audioPath.pathExtLower == value.lowercased() }
                case "year", "bits", "rate", "rating", "plays", "duration":
                    let compare = try numeric(value)
                    test = { t in
                        let v: Double
                        switch field { case "year": v = Double(AlbumSorting.year([t])); case "bits": v = Double(t.bits); case "rate": v = Double(t.sampleRate)
                        case "rating": v = Double(t.rating); case "plays": v = Double(stats(t).playCount); default: v = t.seconds }
                        return compare(v)
                    }
                case "favorite", "missing":
                    guard let flag = Bool(value.lowercased()) else { throw QueryError(message: "true veya false kullan: " + field) }
                    test = { (field == "favorite" ? $0.isFavorite : !$0.audioPath.fileExists) == flag }
                case "added":
                    guard let days = Int(value.trimmingCharacters(in: CharacterSet(charactersIn: "dD"))), days >= 0 else { throw QueryError(message: tr("Eklenme filtresi için gün sayısı kullan: added:30d")) }
                    let since = Date().addingTimeInterval(-Double(min(days, 365000)) * 86400)
                    test = { stats($0).addedAt >= since }
                case "played":
                    guard value.lowercased() == "never" else { throw QueryError(message: tr("Dinlenmemişler için played:never kullan.")) }
                    test = { stats($0).playCount == 0 }
                default: throw QueryError(message: tr("Bilinmeyen filtre: ") + field)
                }
                if g("not") == "-" { let positive = test; test = { !positive($0) } }
                terms.append(test)
            }
        } catch let e as QueryError { error = e.message; return { _ in false } } catch { return { _ in false } }
        if terms.isEmpty { return { _ in true } }
        return { t in terms.allSatisfy { $0(t) } }
    }
    public static func libraryOrder(_ a: Track, _ b: Track) -> Bool {
        if a.artist != b.artist { return a.artist < b.artist }
        if a.album != b.album { return a.album < b.album }
        if a.disc != b.disc { return a.disc < b.disc }
        if a.number != b.number { return a.number < b.number }
        return a.title < b.title
    }
    public static func sort(_ tracks: [Track], _ mode: String, _ history: ListeningStore?) -> [Track] {
        switch mode {
        case "added": return tracks.sorted { let x = history?.get($0).addedAt ?? $0.addedAt, y = history?.get($1).addedAt ?? $1.addedAt; return x != y ? x > y : $0.title < $1.title }
        case "played": return tracks.sorted { let x = history?.get($0).lastPlayed ?? .distantPast, y = history?.get($1).lastPlayed ?? .distantPast; return x != y ? x > y : $0.title < $1.title }
        case "plays": return tracks.sorted { let x = history?.get($0).playCount ?? 0, y = history?.get($1).playCount ?? 0; return x != y ? x > y : $0.title < $1.title }
        case "year":
            let pairs: [(Track, Int)] = tracks.map { ($0, AlbumSorting.year([$0])) }
            let sorted = pairs.sorted { (a: (Track, Int), b: (Track, Int)) -> Bool in
                if a.1 != b.1 { return a.1 > b.1 }
                if a.0.album != b.0.album { return a.0.album < b.0.album }
                return a.0.number < b.0.number
            }
            return sorted.map { $0.0 }
        default: return tracks.sorted(by: libraryOrder)
        }
    }
}
