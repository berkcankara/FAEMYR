import Foundation

// MARK: - AlbumSorting.cs

public enum AlbumSortMode: Int, Codable, CaseIterable { case artist, title, yearNewest, trackCount }
public enum ArtistSortMode: Int, Codable, CaseIterable { case nameAscending, nameDescending }

public struct AlbumSortChoice: Hashable { public let mode: AlbumSortMode; public let text: String; public var label: String { tr(text) } }
public struct ArtistSortChoice: Hashable { public let mode: ArtistSortMode; public let text: String; public var label: String { tr(text) } }

public let albumSortChoices = [AlbumSortChoice(mode: .artist, text: "Sanatçı · A–Z"), AlbumSortChoice(mode: .title, text: "Albüm adı · A–Z"),
                               AlbumSortChoice(mode: .yearNewest, text: "Yıl · yeni → eski"), AlbumSortChoice(mode: .trackCount, text: "Parça sayısı · çoktan aza")]
public let artistSortChoices = [ArtistSortChoice(mode: .nameAscending, text: "Sanatçı adı · A–Z"), ArtistSortChoice(mode: .nameDescending, text: "Sanatçı adı · Z–A")]

public struct AlbumSortSettings: Codable {
    public var mode: AlbumSortMode = .artist
    public var artistMode: ArtistSortMode = .nameAscending
    public var groupByLetter = true
    enum CodingKeys: String, CodingKey { case mode = "Mode", artistMode = "ArtistMode", groupByLetter = "GroupByLetter" }
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        mode = AlbumSortMode(rawValue: (try? c.decode(Int.self, forKey: .mode)) ?? 0) ?? .artist
        artistMode = ArtistSortMode(rawValue: (try? c.decode(Int.self, forKey: .artistMode)) ?? 0) ?? .nameAscending
        groupByLetter = (try? c.decode(Bool.self, forKey: .groupByLetter)) ?? true
    }
    public static var path: String { Paths.file("album-sort.json") }
    public static func load() -> AlbumSortSettings { PlayerJson.read(AlbumSortSettings.self, path) ?? AlbumSortSettings() }
    public func save() { try? PlayerJson.write(Self.path, self) }
}

public enum AlbumSorting {
    public static func year(_ tracks: [Track]) -> Int {
        for t in tracks {
            let dates = t.fields.compactMap { f -> (Int, String)? in
                let key = String(f.key[f.key.index(after: f.key.lastIndex(of: "/") ?? f.key.index(before: f.key.startIndex))...]).trimmingCharacters(in: .whitespaces).uppercased()
                switch key { case "DATE": return (0, f.value); case "YEAR": return (1, f.value); case "ORIGINALDATE", "ORIGINALYEAR": return (2, f.value); default: return nil }
            }.sorted { $0.0 < $1.0 }
            for (_, value) in dates {
                let v = value.trimmingCharacters(in: .whitespaces)
                if v.count >= 4, let y = Int(v.prefix(4)), y >= 1000, y <= 9999 { return y }
            }
        }
        return 0
    }
    public static func sort(_ albums: [AlbumCard], _ mode: AlbumSortMode) -> [AlbumCard] {
        albums.sorted { a, b in
            func cmp(_ x: String, _ y: String) -> ComparisonResult { x.ciCompare(y) }
            let order: [ComparisonResult]
            switch mode {
            case .title: order = [cmp(a.title, b.title), cmp(a.artist, b.artist)]
            case .yearNewest: order = [a.year == b.year ? .orderedSame : (a.year > b.year ? .orderedAscending : .orderedDescending), cmp(a.artist, b.artist), cmp(a.title, b.title)]
            case .trackCount: order = [a.count == b.count ? .orderedSame : (a.count > b.count ? .orderedAscending : .orderedDescending), cmp(a.artist, b.artist), cmp(a.title, b.title)]
            case .artist: order = [cmp(a.artist, b.artist), cmp(a.title, b.title)]
            }
            for o in order where o != .orderedSame { return o == .orderedAscending }
            return a.key < b.key
        }
    }
}

// MARK: - AlbumDiscs.cs / AlbumGrouping.cs

public enum AlbumDiscs {
    static let discFolder = try! NSRegularExpression(pattern: #"^(?:cd|disc|disk)[\s._-]*(\d+)(?:\s+of\s+\d+)?$"#, options: [.caseInsensitive])
    public static func number(_ t: Track) -> UInt32 {
        if t.disc > 0 { return t.disc }
        let folder = ((t.audioPath as NSString).deletingLastPathComponent as NSString).lastPathComponent
        let range = NSRange(folder.startIndex..., in: folder)
        if let m = discFolder.firstMatch(in: folder, range: range), let r = Range(m.range(at: 1), in: folder), let n = UInt32(folder[r]), n > 0 { return n }
        return 1
    }
    public static func order(_ tracks: [Track]) -> [Track] {
        tracks.map { ($0, $0.albumDisc) }.sorted { a, b in
            if a.1 != b.1 { return a.1 < b.1 }
            if a.0.number != b.0.number { return a.0.number < b.0.number }
            return a.0.title.ciCompare(b.0.title) == .orderedAscending
        }.map(\.0)
    }
    public static func multiple(_ tracks: [Track]) -> Bool { Set(tracks.map(\.albumDisc)).count > 1 }
}

public enum AlbumGrouping {
    static func tag(_ t: Track, _ names: [String]) -> String {
        t.fields.first { f in names.contains { $0.caseInsensitiveCompare(ReplayGainTag.name(f.key)) == .orderedSame } }?.value.trimmingCharacters(in: .whitespaces) ?? ""
    }
    public static func folder(_ t: Track) -> String {
        var folder = (t.audioPath as NSString).deletingLastPathComponent
        let leaf = (folder as NSString).lastPathComponent
        if AlbumDiscs.discFolder.firstMatch(in: leaf, range: NSRange(leaf.startIndex..., in: leaf)) != nil { folder = (folder as NSString).deletingLastPathComponent }
        return folder
    }
    /// Separates different releases with the same artist/title (Windows AlbumGrouping.Apply).
    public static func apply(_ tracks: [Track]) -> [Track] {
        var changed: [Track] = []
        for (_, album) in Dictionary(grouping: tracks, by: { $0.albumArtist + "\u{1f}" + $0.album }) {
            let byFolder = Dictionary(grouping: album, by: { folder($0).lowercased() })
            guard byFolder.count >= 2 else { continue }
            let releases = Dictionary(grouping: byFolder.map { (key: $0.key, tracks: $0.value) }, by: { g -> String in
                (g.tracks.lazy.map { tag($0, ["MUSICBRAINZALBUMID", "MUSICBRAINZRELEASEID"]) }.first { !$0.isEmpty } ?? g.key).lowercased()
            })
            guard releases.count > 1 else { continue }
            for (releaseKey, groups) in releases {
                let songs = groups.flatMap(\.tracks)
                let edition = songs.lazy.map(\.albumEditionKey).first { !$0.isEmpty } ?? String(Paths.hash(releaseKey).prefix(12))
                var label = tag(songs[0], ["EDITION", "VERSION", "CATALOGNUMBER"])
                if label.isEmpty { label = (folder(groups[0].tracks[0]) as NSString).lastPathComponent }
                let y = AlbumSorting.year(songs)
                if y > 0 && !label.contains(String(y)) { label = "\(y) · " + label }
                for t in songs where t.albumEditionKey != edition || t.editionLabel != label { t.albumEditionKey = edition; t.editionLabel = label; changed.append(t) }
            }
        }
        return changed
    }
}

public enum ReplayGainTag {
    /// `ReplayGain.Tag`: the part after the last "/" of a field key, trimmed and normalized.
    public static func name(_ key: String) -> String {
        let tail = key.split(separator: "/", omittingEmptySubsequences: false).last.map(String.init) ?? key
        return tail.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "_", with: "").replacingOccurrences(of: " ", with: "").uppercased()
    }
}

// MARK: - GallerySections.cs

public enum GallerySections {
    public static func initial(_ name: String?) -> String {
        guard let name, !name.trimmingCharacters(in: .whitespaces).isEmpty else { return "#" }
        for ch in name.precomposedStringWithCanonicalMapping {
            if ch.isWhitespace || ch.isPunctuation { continue }
            return ch.isLetter ? String(ch).uppercased(with: L.locale) : "#"
        }
        return "#"
    }
    public static func rows<T: Hashable>(_ sorted: [T], columns: Int, key: ((T) -> String)? = nil, descending: Bool = false) -> [GalleryRow<T>] {
        let cols = max(1, columns)
        var rows: [GalleryRow<T>] = []
        func chunk(_ items: [T], heading: String) {
            var i = 0
            while i < items.count {
                rows.append(GalleryRow(id: rows.count, heading: i == 0 ? heading : "", cards: Array(items[i..<min(i + cols, items.count)])))
                i += cols
            }
        }
        guard let key else { chunk(sorted, heading: ""); return rows }
        var order: [String] = []; var groups: [String: [T]] = [:]
        for card in sorted { let k = initial(key(card)); if groups[k] == nil { order.append(k) }; groups[k, default: []].append(card) }
        order.sort { a, b in
            if (a == "#") != (b == "#") { return descending ? b == "#" : a == "#" }
            let r = a.compare(b, locale: L.locale)
            return descending ? r == .orderedDescending : r == .orderedAscending
        }
        for k in order { chunk(groups[k]!, heading: k) }
        return rows
    }
}

// MARK: - ArtistLinks.cs (ArtistCatalog)

public enum ArtistCatalog {
    public struct Credit: Hashable { public let label: String; public let profile: String }
    static let featureMarker = try! NSRegularExpression(pattern: #"(?:\s+|[\(\[]\s*)(?:feat(?:uring)?|ft)\.?\s+"#, options: [.caseInsensitive])
    static let ampersandMarker = try! NSRegularExpression(pattern: #"(?<=\S)\s*&\s*(?=\S)"#)
    static let slashMarker = try! NSRegularExpression(pattern: #"(?<=\S)(?:\s+/\s*|/\s+)(?=\S)"#)
    static let compoundArtists: [(String, NSRegularExpression)] = ["Above & Beyond", "Simon & Garfunkel", "Belle & Sebastian", "Earth, Wind & Fire",
        "Of Mice & Men", "Nick Cave & The Bad Seeds", "Hall & Oates", "Kool & The Gang"].sorted { $0.count > $1.count }.map { name in
            let parts = name.split(separator: "&").map { NSRegularExpression.escapedPattern(for: $0.trimmingCharacters(in: .whitespaces)).replacingOccurrences(of: " ", with: #"\s+"#) }
            return (name, try! NSRegularExpression(pattern: "^" + parts.joined(separator: #"\s*&\s*"#) + #"(?=$|[\s(/;&\[])"#, options: [.caseInsensitive]))
        }
    static func compoundPrefix(_ text: String) -> (String?, Int) {
        let ns = text as NSString
        for (name, re) in compoundArtists { if let m = re.firstMatch(in: text, range: NSRange(location: 0, length: ns.length)) { return (name, m.range.length) } }
        return (nil, 0)
    }
    static func profileIdentity(_ name: String) -> String {
        let g = compoundPrefix(name); return g.0 != nil && g.1 == (name as NSString).length ? g.0! : name
    }
    public static func split(_ value: String?) -> [String] {
        var seen = Set<String>(); var out: [String] = []
        var parts = [value ?? ""]
        for sep in [" / ", ";", "\r\n", "\n"] { parts = parts.flatMap { $0.components(separatedBy: sep) } }
        for p in parts.map({ $0.trimmingCharacters(in: .whitespacesAndNewlines) }) where !p.isEmpty {
            if seen.insert(p.lowercased()).inserted { out.append(p) }
        }
        return out
    }
    static var creditCache: [String: [Credit]] = [:]
    static let cacheLock = NSLock()
    public static func credits(_ value: String?) -> [Credit] {
        let text = (value ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        cacheLock.lock(); if let c = creditCache[text] { cacheLock.unlock(); return c }; cacheLock.unlock()
        let ns = text as NSString; let full = NSRange(location: 0, length: ns.length)
        var feature = featureMarker.firstMatch(in: text, range: full)?.range
        let compound = compoundPrefix(text)
        if compound.1 <= ns.length, let amp = ampersandMarker.firstMatch(in: text, range: NSRange(location: compound.1, length: ns.length - compound.1))?.range,
           feature == nil || amp.location < feature!.location { feature = amp }
        if let slash = slashMarker.firstMatch(in: text, range: full)?.range, feature == nil || slash.location < feature!.location { feature = slash }
        var head = feature != nil ? ns.substring(to: feature!.location) : text
        while let last = head.last, " \t([,".contains(last) { head.removeLast() }
        let names = split(head)
        var result: [Credit]
        if names.isEmpty { result = split(text).map { Credit(label: $0, profile: $0) } }
        else {
            var tail = feature != nil ? ns.substring(from: feature!.location) : ""
            if let first = tail.first, !first.isWhitespace, first != "&", first != "/" { tail = " " + tail }
            result = names.enumerated().map { i, n in Credit(label: n + (i == names.count - 1 ? tail : ""), profile: profileIdentity(n)) }
        }
        cacheLock.lock(); if creditCache.count > 20000 { creditCache.removeAll() }; creditCache[text] = result; cacheLock.unlock()
        return result
    }
    public static func isArtistField(_ key: String) -> Bool {
        if key.caseInsensitiveCompare("Sanatçı") == .orderedSame { return true }
        let tag = (key.split(separator: "/").last.map(String.init) ?? key).trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "_", with: "").replacingOccurrences(of: " ", with: "")
        return tag.caseInsensitiveCompare("artist") == .orderedSame
    }
    public static func names(_ t: Track) -> [String] {
        let creds = t.artist.trimmingCharacters(in: .whitespaces).isEmpty ? t.fields.filter { isArtistField($0.key) }.flatMap { credits($0.value) } : credits(t.artist)
        var seen = Set<String>(); return creds.map(\.profile).filter { seen.insert($0.lowercased()).inserted }
    }
    public static func profileNames(_ tracks: [Track]) -> Set<String> {
        var s = Set<String>(); for t in tracks { for n in names(t) { s.insert(n.lowercased()) } }; return s
    }
    public static func matches(_ t: Track, _ name: String) -> Bool { names(t).contains { $0.caseInsensitiveCompare(name) == .orderedSame } }
    public static func albumKeys(_ tracks: [Track], _ name: String) -> Set<String> { Set(tracks.filter { matches($0, name) }.map(\.albumKey)) }
    public static func resolveProfile(_ tracks: [Track], _ label: String) -> String? {
        var canonical: [String: String] = [:]
        for t in tracks { for n in names(t) where canonical[n.lowercased()] == nil { canonical[n.lowercased()] = n } }
        for c in credits(label) { if let p = canonical[c.profile.lowercased()] { return p } }
        return nil
    }
    public static func cards(_ tracks: [Track]) -> [ArtistCard] {
        var groups: [String: (name: String, tracks: [Track])] = [:]
        for t in tracks { for n in names(t) { groups[n.lowercased(), default: (n, [])].tracks.append(t) } }
        return groups.values.sorted { $0.name.ciCompare($1.name) == .orderedAscending }.map { g in
            ArtistCard(name: g.name, cover: g.tracks.lazy.map(\.cover).first { $0.fileExists }, albums: Set(g.tracks.map(\.albumKey)).count, tracks: g.tracks.count)
        }
    }
}

// MARK: - AlbumAudioInfo.cs

public struct AlbumAudioInfo {
    public let text: String
    public let details: String
    static func channels(_ n: Int) -> String { n == 1 ? "Mono" : n == 2 ? "Stereo" : n > 2 ? "\(n)" + tr(" kanal") : tr("Kanal bilgisi yok") }
    static func rate(_ hz: Int) -> String { hz > 0 ? trTurkishNumber(Double(hz) / 1000) + " kHz" : tr("Örnekleme bilgisi yok") }
    static func trTurkishNumber(_ v: Double) -> String {
        let f = NumberFormatter(); f.maximumFractionDigits = 3; f.minimumFractionDigits = 0; f.locale = Locale(identifier: "tr_TR"); f.usesGroupingSeparator = false
        return f.string(from: NSNumber(value: v)) ?? "\(v)"
    }
    static func format(_ t: Track) -> String {
        let resolution = t.isDsd ? (t.dsdRate > 0 ? "DSD\(t.dsdRate / 44100) · \(trTurkishNumber(Double(t.dsdRate) / 1_000_000)) MHz" : "DSD") : (t.bits > 0 ? "\(t.bits)-bit/" : "") + rate(t.sampleRate)
        return resolution + ", " + channels(t.channels)
    }
    public static func from(_ tracks: [Track]) -> AlbumAudioInfo {
        var order: [String] = []; var counts: [String: Int] = [:]
        for t in tracks { let k = format(t); if counts[k] == nil { order.append(k) }; counts[k, default: 0] += 1 }
        if order.isEmpty { return AlbumAudioInfo(text: tr("Ses bilgisi yok"), details: "") }
        let details = order.map { "\($0) · \(counts[$0]!)" + tr(" parça") }.joined(separator: "\n")
        if order.count <= 2 { return AlbumAudioInfo(text: order.joined(separator: "\n"), details: details) }
        if tracks.contains(where: { $0.isDsd || $0.bits <= 0 || $0.sampleRate <= 0 }) { return AlbumAudioInfo(text: tr("Karma ses biçimleri"), details: details) }
        let bits = Array(Set(tracks.map(\.bits))).sorted()
        let span = bits.count == 1 ? "\(bits[0])" : "\(bits[0])–\(bits.last!)"
        let lo = tracks.map(\.sampleRate).min()!, hi = tracks.map(\.sampleRate).max()!
        let rates = lo == hi ? rate(lo) : trTurkishNumber(Double(lo) / 1000) + "–" + rate(hi)
        let ch = Set(tracks.map(\.channels))
        return AlbumAudioInfo(text: span + "-bit/" + rates + "\n" + (ch.count == 1 ? channels(ch.first!) : tr("Farklı kanallar")), details: details)
    }
}
