import Foundation

public struct LyricLine: Hashable { public let seconds: Double; public let text: String }

public struct LyricsDocument: Codable {
    public var version = 1
    public var key = ""
    public var source = ""
    public var remoteId: Int64 = 0
    public var plain = ""
    public var synced = ""
    public var instrumental = false
    enum CodingKeys: String, CodingKey { case version = "Version", key = "Key", source = "Source", remoteId = "RemoteId", plain = "Plain", synced = "Synced", instrumental = "Instrumental" }
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        version = (try? c.decode(Int.self, forKey: .version)) ?? 1; key = (try? c.decode(String.self, forKey: .key)) ?? ""
        source = (try? c.decode(String.self, forKey: .source)) ?? ""; remoteId = (try? c.decode(Int64.self, forKey: .remoteId)) ?? 0
        plain = (try? c.decode(String.self, forKey: .plain)) ?? ""; synced = (try? c.decode(String.self, forKey: .synced)) ?? ""
        instrumental = (try? c.decode(Bool.self, forKey: .instrumental)) ?? false
    }
    public var hasLyrics: Bool { !plain.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || !Lrc.parse(synced).isEmpty }
    public var lines: [LyricLine] { Lrc.parse(synced) }
}

public enum Lrc {
    static let stamp = try! NSRegularExpression(pattern: #"\[([0-9]{1,4}):([0-9]{2})(?:[\.:]([0-9]{1,3}))?\]"#)
    static let offset = try! NSRegularExpression(pattern: #"\[offset:\s*([+-]?\d+)\]"#, options: [.caseInsensitive])
    public static func parse(_ text: String?) -> [LyricLine] {
        guard let text, !text.isEmpty, text.count <= 262144 else { return [] }
        let ns = text as NSString
        var off = 0.0
        if let m = offset.matches(in: text, range: NSRange(location: 0, length: ns.length)).last, let ms = Int(ns.substring(with: m.range(at: 1))) { off = Double(ms) / 1000 }
        var result: [LyricLine] = []
        for raw in text.replacingOccurrences(of: "\r", with: "").components(separatedBy: "\n") {
            let row = raw.trimmingCharacters(in: CharacterSet(charactersIn: "\u{feff}"))
            let r = row as NSString
            let stamps = stamp.matches(in: row, range: NSRange(location: 0, length: r.length))
            guard let first = stamps.first, first.range.location == 0 else { continue }
            var end = 0
            for s in stamps { if s.range.location != end { break }; end = s.range.location + s.range.length }
            let lyric = r.substring(from: end).trimmingCharacters(in: .whitespaces)
            for s in stamps where s.range.location < end {
                let minutes = Int(r.substring(with: s.range(at: 1))) ?? 0, seconds = Int(r.substring(with: s.range(at: 2))) ?? 0
                if seconds >= 60 { continue }
                var fraction = 0.0
                if s.range(at: 3).location != NSNotFound { let f = r.substring(with: s.range(at: 3)); fraction = Double(Int(f) ?? 0) / pow(10, Double(f.count)) }
                result.append(LyricLine(seconds: max(0, Double(minutes * 60 + seconds) + fraction + off), text: lyric))
            }
        }
        var seen = Set<LyricLine>()
        return result.sorted { $0.seconds < $1.seconds }.filter { seen.insert($0).inserted }
    }
    public static func active(_ lines: [LyricLine], _ seconds: Double) -> Int {
        var lo = 0, hi = lines.count - 1, result = -1
        while lo <= hi { let mid = (lo + hi) / 2; if lines[mid].seconds <= seconds { result = mid; lo = mid + 1 } else { hi = mid - 1 } }
        return result
    }
}

public struct LyricsPreferences: Codable { public var followCurrentLine = true
    enum CodingKeys: String, CodingKey { case followCurrentLine = "FollowCurrentLine" }
    public init() {}
    public static var path: String { Paths.file("lyrics-settings.json") }
    public static func load() -> LyricsPreferences { PlayerJson.read(LyricsPreferences.self, path) ?? LyricsPreferences() }
    public func save() { try? PlayerJson.write(Self.path, self) }
}

struct LyricsRecord: Decodable {
    var id: Int64 = 0; var trackName = ""; var artistName = ""; var albumName = ""; var duration: Double = 0; var instrumental = false
    var plainLyrics: String?; var syncedLyrics: String?
}

public actor LyricsService {
    let directory: String
    var blockedUntil: [String: Date] = [:]
    var lastRequest = Date.distantPast
    let session: URLSession

    public init(directory: String = Paths.file("lyrics")) {
        self.directory = directory
        let config = URLSessionConfiguration.default; config.timeoutIntervalForRequest = 20
        config.httpAdditionalHeaders = ["User-Agent": "Faemyr/\(AppBrand.version) (macOS local music player)", "Accept": "application/json"]
        session = URLSession(configuration: config)
    }

    public static func key(_ t: Track) -> String {
        Paths.hash([t.path, String(t.modified), t.title, t.artist, t.album, roundTrip(t.seconds)].joined(separator: "\u{1f}"))
    }
    static func roundTrip(_ d: Double) -> String {
        // .NET "R" format: shortest round-trip; integers print without ".0".
        if d == d.rounded() && abs(d) < 1e15 { return String(Int64(d)) }
        return "\(d)"
    }
    static func tag(_ key: String) -> String {
        let tail = key.split(separator: "/", omittingEmptySubsequences: false).last.map(String.init) ?? key
        return tail.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "_", with: "").replacingOccurrences(of: "-", with: "").replacingOccurrences(of: " ", with: "").uppercased()
    }
    public static func fromText(_ text: String?, _ source: String) -> LyricsDocument? {
        guard var t = text, !t.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, t.count <= 262144 else { return nil }
        t = t.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n").trimmingCharacters(in: CharacterSet(charactersIn: "\u{feff} \n\t"))
        var d = LyricsDocument(); d.source = source
        let lines = Lrc.parse(t)
        if lines.isEmpty { d.plain = t } else { d.synced = t; d.plain = lines.map(\.text).joined(separator: "\n") }
        return d
    }
    public static func embedded(_ t: Track) -> LyricsDocument? {
        let keys: Set<String> = ["LYRICS", "UNSYNCEDLYRICS", "UNSYNCHRONISEDLYRICS", "SYNCHRONIZEDLYRICS", "SYNCHRONISEDLYRICS", "SYNCEDLYRICS", "USLT"]
        let docs = t.fields.filter { f in let k = tag(f.key); return keys.contains(k) || k.range(of: "^LYRICS[A-Z]{3}$", options: .regularExpression) != nil }.compactMap { fromText($0.value, "Dosyadan") }
        return docs.first { !$0.synced.isEmpty } ?? docs.first
    }
    public static func cueLyrics(_ t: Track, _ doc: LyricsDocument) -> LyricsDocument? {
        guard t.isCue else { return doc }
        let lines = Lrc.parse(doc.synced); if lines.isEmpty { return nil }
        let start = Double(t.cueStartFrame) / Double(max(1, t.sampleRate)), end = start + t.seconds
        var selected = lines.filter { $0.seconds >= start && $0.seconds < end }.map { LyricLine(seconds: $0.seconds - start, text: $0.text) }
        if let carried = lines.last(where: { $0.seconds < start }), !carried.text.isEmpty, selected.isEmpty || selected[0].seconds > 0 { selected.insert(LyricLine(seconds: 0, text: carried.text), at: 0) }
        if selected.isEmpty { return nil }
        var d = LyricsDocument(); d.source = doc.source; d.plain = selected.map(\.text).joined(separator: "\n")
        d.synced = selected.map { String(format: "[%02d:%06.3f]", Int($0.seconds / 60), $0.seconds.truncatingRemainder(dividingBy: 60)) + $0.text }.joined(separator: "\n")
        return d
    }

    public func local(_ t: Track) async -> LyricsDocument? {
        if let e = Self.embedded(t) { return Self.cueLyrics(t, e) }
        let sidecar = ((t.audioPath as NSString).deletingPathExtension) + ".lrc"
        if let attrs = try? FileManager.default.attributesOfItem(atPath: sidecar), (attrs[.size] as? Int ?? 0) <= 262144,
           let data = FileManager.default.contents(atPath: sidecar), let doc = Self.fromText(String(decoding: data, as: UTF8.self), "Yerel LRC") { return Self.cueLyrics(t, doc) }
        return readCache(t)
    }
    func cachePath(_ t: Track) -> String { (directory as NSString).appendingPathComponent(Self.key(t) + ".json") }
    public func readCache(_ t: Track) -> LyricsDocument? {
        guard let doc = PlayerJson.read(LyricsDocument.self, cachePath(t)), doc.version == 1, doc.key == Self.key(t), Self.remote(doc), Self.valid(doc) else { return nil }
        return doc
    }
    public func save(_ t: Track, _ doc: LyricsDocument) throws {
        var d = doc; d.key = Self.key(t)
        try PlayerJson.write(cachePath(t), d, pretty: true)
    }
    static func valid(_ d: LyricsDocument) -> Bool { d.plain.count <= 262144 && d.synced.count <= 262144 && (d.instrumental || d.hasLyrics) }
    public static func isOnline(_ d: LyricsDocument?) -> Bool { d?.source == "LRCLIB" || d?.source == "lyrics.ovh" }
    static func remote(_ d: LyricsDocument) -> Bool { d.source == "LRCLIB" && d.remoteId > 0 || d.source == "lyrics.ovh" && d.remoteId == 0 && !d.instrumental && d.synced.isEmpty }
    static func normalize(_ v: String?) -> String {
        // Diacritics are folded so a "Tiesto" tag still matches LRCLIB's "Tiësto" record.
        (v ?? "").precomposedStringWithCompatibilityMapping.folding(options: [.diacriticInsensitive, .caseInsensitive], locale: Locale(identifier: "en_US_POSIX")).uppercased().unicodeScalars.filter { CharacterSet.letters.contains($0) || CharacterSet.decimalDigits.contains($0) }.map(String.init).joined()
    }
    static func primaryArtist(_ a: String) -> String { ArtistCatalog.credits(a).first?.profile ?? a }

    static func match(_ t: Track, _ records: [LyricsRecord]) -> LyricsRecord? {
        let artist = normalize(primaryArtist(t.artist)), title = normalize(t.title)
        return records.filter { r in r.id > 0 && normalize(r.trackName) == title && normalize(primaryArtist(r.artistName)) == artist }
            .filter { r in r.duration.isFinite && (t.seconds <= 0 || abs(r.duration - t.seconds) <= 2) }
            .filter { r in r.instrumental || !(r.plainLyrics ?? "").trimmingCharacters(in: .whitespaces).isEmpty || !Lrc.parse(r.syncedLyrics).isEmpty }
            .sorted { a, b in
                let ax = normalize(a.albumName) == normalize(t.album) ? 0 : 1, bx = normalize(b.albumName) == normalize(t.album) ? 0 : 1
                if ax != bx { return ax < bx }
                let ad = abs(a.duration - t.seconds), bd = abs(b.duration - t.seconds)
                if ad != bd { return ad < bd }
                return !(a.syncedLyrics ?? "").isEmpty && (b.syncedLyrics ?? "").isEmpty
            }.first
    }

    func send(_ url: URL) async throws -> Data? {
        let wait = lastRequest.addingTimeInterval(0.35).timeIntervalSinceNow
        if wait > 0 { try await Task.sleep(nanoseconds: UInt64(wait * 1e9)) }
        defer { lastRequest = Date() }
        let host = url.host ?? ""
        if let until = blockedUntil[host], Date() < until { throw MetadataError.message(tr("Bu söz kaynağı için bekleme süresi devam ediyor.")) }
        do {
            let (data, response) = try await session.data(from: url)
            guard let http = response as? HTTPURLResponse else { return nil }
            if http.statusCode == 429 || http.statusCode == 503 {
                blockedUntil[host] = Date().addingTimeInterval(http.statusCode == 429 ? 60 : 5)
                throw MetadataError.message(tr("Söz servisi şu anda yoğun. Biraz sonra yeniden dene."))
            }
            if http.statusCode == 404 { return nil }
            if !(200..<300).contains(http.statusCode) {
                if http.statusCode >= 500 { blockedUntil[host] = Date().addingTimeInterval(60) }
                throw MetadataError.message(tr("Söz servisine şu anda ulaşılamıyor. Biraz sonra yeniden dene."))
            }
            if data.count > 1_048_576 { throw MetadataError.message(tr("Şarkı sözü yanıtı çok büyük.")) }
            return data
        } catch let e as URLError { blockedUntil[host] = Date().addingTimeInterval(30); throw e }
    }

    func request(_ t: Track, search: Bool) -> URL {
        var c = URLComponents(string: "https://lrclib.net/api/" + (search ? "search" : "get"))!
        var items = [URLQueryItem(name: "track_name", value: t.title), URLQueryItem(name: "artist_name", value: Self.primaryArtist(t.artist))]
        if !search {
            if !t.album.trimmingCharacters(in: .whitespaces).isEmpty { items.append(URLQueryItem(name: "album_name", value: t.album)) }
            if t.seconds >= 1 && t.seconds <= 3600 { items.append(URLQueryItem(name: "duration", value: String(format: "%.3f", t.seconds).replacingOccurrences(of: #"\.?0+$"#, with: "", options: .regularExpression))) }
        }
        c.queryItems = items; return c.url!
    }

    public func fetch(_ t: Track) async throws -> LyricsDocument? {
        if t.title.trimmingCharacters(in: .whitespaces).isEmpty || t.artist.trimmingCharacters(in: .whitespaces).isEmpty { throw MetadataError.message(tr("Aramak için parça adı ve sanatçı bilgisi gerekli.")) }
        if AppBrand.offline("LYRICS") { return nil }
        var failure: Error?
        do {
            var record: LyricsRecord?
            let dec = JSONDecoder(); dec.keyDecodingStrategy = .convertFromSnakeCase
            if let data = try await send(request(t, search: false)), let one = try? dec.decode(LyricsRecord.self, from: data) { record = Self.match(t, [one]) }
            if record == nil, let data = try await send(request(t, search: true)) { record = Self.match(t, (try? dec.decode([LyricsRecord].self, from: data)) ?? []) }
            if let r = record {
                var d = LyricsDocument(); d.source = "LRCLIB"; d.remoteId = r.id; d.plain = r.plainLyrics ?? ""; d.synced = r.syncedLyrics ?? ""; d.instrumental = r.instrumental
                if Self.valid(d) { return d }
            }
        } catch { failure = error }
        try Task.checkCancellation()
        do {
            let artist = Self.primaryArtist(t.artist).addingPercentEncoding(withAllowedCharacters: .urlPathAllowed.subtracting(CharacterSet(charactersIn: "/"))) ?? ""
            let title = t.title.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed.subtracting(CharacterSet(charactersIn: "/"))) ?? ""
            if let data = try await send(URL(string: "https://api.lyrics.ovh/v1/\(artist)/\(title)")!),
               let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any], let text = (obj["lyrics"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty, text.count <= 262144 {
                var d = LyricsDocument(); d.source = "lyrics.ovh"; d.plain = text; return d
            }
        } catch { failure = error }
        if failure != nil { throw MetadataError.message(tr("Söz kaynaklarına şu anda ulaşılamıyor veya alternatif kaynakta eşleşme bulunamadı. Daha sonra yeniden dene.")) }
        return nil
    }
}
