import Foundation
import ImageIO
import UniformTypeIdentifiers

public struct ArtistPhotoPreferences: Codable {
    public var enabled = true
    public var onCards = true
    public var albumCoverArtists: [String] = []
    enum CodingKeys: String, CodingKey { case enabled = "Enabled", onCards = "OnCards", albumCoverArtists = "AlbumCoverArtists" }
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        enabled = (try? c.decode(Bool.self, forKey: .enabled)) ?? true; onCards = (try? c.decode(Bool.self, forKey: .onCards)) ?? true
        albumCoverArtists = (try? c.decode([String].self, forKey: .albumCoverArtists)) ?? []
    }
    public static var path: String { Paths.file("artist-photo-settings.json") }
    public static func load() -> ArtistPhotoPreferences { PlayerJson.read(ArtistPhotoPreferences.self, path) ?? ArtistPhotoPreferences() }
    public func save() { try? PlayerJson.write(Self.path, self) }
    public func usesAlbumCover(_ artist: String) -> Bool { albumCoverArtists.contains { $0.caseInsensitiveCompare(artist) == .orderedSame } }
}

public struct ArtistPhoto: Codable {
    public var artist = "", file = "", sourceUrl = "", articleUrl = "", author = "", license = ""
    public var checkedAt = Date(), retryAfter = Date()
    public var local = false
    enum CodingKeys: String, CodingKey { case artist = "Artist", file = "File", sourceUrl = "SourceUrl", articleUrl = "ArticleUrl", author = "Author", license = "License", checkedAt = "CheckedAt", retryAfter = "RetryAfter", local = "Local" }
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        func s(_ k: CodingKeys) -> String { (try? c.decode(String.self, forKey: k)) ?? "" }
        artist = s(.artist); file = windowsLastComponent(s(.file)); sourceUrl = s(.sourceUrl); articleUrl = s(.articleUrl); author = s(.author); license = s(.license)
        checkedAt = (try? c.decode(Date.self, forKey: .checkedAt)) ?? Date(); retryAfter = (try? c.decode(Date.self, forKey: .retryAfter)) ?? Date.distantFuture
        local = (try? c.decode(Bool.self, forKey: .local)) ?? false
    }
    public var credit: String { local ? tr("Yerel fotoğraf") : [author, license, "Wikimedia Commons"].filter { !$0.isEmpty }.joined(separator: " · ") }
}

enum Wiki {
    static let artistCategory = try! NSRegularExpression(pattern: "musician|singer|composer|musical group|music group|musical duo|rock group|rock band|pop group|rapper|disc jockey|record producer|electronic music duo", options: [.caseInsensitive])
    static func normalize(_ s: String) -> String {
        // Diacritics folded so a "Tiesto" tag matches the "Tiësto" article.
        s.precomposedStringWithCompatibilityMapping.folding(options: .diacriticInsensitive, locale: Locale(identifier: "en_US_POSIX")).unicodeScalars.filter { CharacterSet.letters.contains($0) || CharacterSet.decimalDigits.contains($0) }.map(String.init).joined().uppercased()
    }
    static func baseTitle(_ t: String) -> String { t.replacingOccurrences(of: #"\s+\((?:band|musician|singer|composer|rapper|DJ)\)$"#, with: "", options: [.regularExpression, .caseInsensitive]) }
    static func titles(_ name: String) -> String { [name, name + " (band)", name + " (musician)", name + " (singer)", name + " (composer)", name + " (rapper)", name + " (DJ)"].joined(separator: "|") }
    static func escape(_ s: String) -> String { s.addingPercentEncoding(withAllowedCharacters: .alphanumerics.union(CharacterSet(charactersIn: "-._~"))) ?? s }
    static func selectPage(_ root: Any, _ name: String, requireThumb: Bool) -> [String: Any]? {
        guard let q = (root as? [String: Any])?["query"] as? [String: Any], let pages = q["pages"] as? [[String: Any]] else { return nil }
        let candidates = pages.filter { p in
            if p["missing"] != nil { return false }
            if let props = p["pageprops"] as? [String: Any], props["disambiguation"] != nil { return false }
            let title = p["title"] as? String ?? ""
            guard normalize(baseTitle(title)) == normalize(name) else { return false }
            if requireThumb { guard let th = p["thumbnail"] as? [String: Any], !(th["source"] as? String ?? "").isEmpty, !(p["pageimage"] as? String ?? "").isEmpty else { return false } }
            let cats = (p["categories"] as? [[String: Any]] ?? []).compactMap { $0["title"] as? String }
            return cats.contains { artistCategory.firstMatch(in: $0, range: NSRange(location: 0, length: ($0 as NSString).length)) != nil }
        }
        let exact = candidates.filter { ($0["title"] as? String ?? "").caseInsensitiveCompare(name) == .orderedSame }
        return exact.count == 1 ? exact[0] : candidates.count == 1 ? candidates[0] : nil
    }
    static func session(timeout: Double) -> URLSession {
        let c = URLSessionConfiguration.default; c.timeoutIntervalForRequest = timeout
        c.httpAdditionalHeaders = ["User-Agent": "Faemyr/\(AppBrand.version) (macOS desktop music library; https://github.com/berkcankara/FAEMYR)"]
        return URLSession(configuration: c)
    }
}

public actor ArtistPhotoStore {
    public static let shared = ArtistPhotoStore()
    let directory = Paths.file("artist-photos")
    var records: [String: ArtistPhoto] = [:]
    var pending: [String: Task<ArtistPhoto?, Never>] = [:]
    var nextRequest = Date.distantPast, backoff = Date.distantPast
    let session = Wiki.session(timeout: 18)

    public static func identity(_ name: String) -> String { ArtistCatalog.credits(name).first?.profile.trimmingCharacters(in: .whitespaces) ?? "" }
    nonisolated func key(_ name: String) -> String { Paths.hash(Self.identity(name).uppercased()) }
    nonisolated func recordPath(_ name: String) -> String { (directory as NSString).appendingPathComponent(key(name) + ".json") }

    public func cached(_ raw: String) -> ArtistPhoto? {
        let name = Self.identity(raw); guard !name.isEmpty else { return nil }
        if let r = records[name.lowercased()] { return r }
        guard let r = PlayerJson.read(ArtistPhoto.self, recordPath(name)), r.artist.caseInsensitiveCompare(name) == .orderedSame else { return nil }
        records[name.lowercased()] = r; return r
    }
    public nonisolated func imagePath(_ r: ArtistPhoto?) -> String? {
        guard let r, !r.file.isEmpty else { return nil }
        let p = (directory as NSString).appendingPathComponent(r.file); return p.fileExists ? p : nil
    }
    /// Cached image path without network access, for synchronous view rendering.
    public nonisolated func cachedImagePath(_ raw: String) -> String? {
        let name = Self.identity(raw); guard !name.isEmpty, let r = PlayerJson.read(ArtistPhoto.self, recordPath(name)) else { return nil }
        return imagePath(r)
    }

    public func get(_ raw: String, force: Bool = false) async -> ArtistPhoto? {
        let name = Self.identity(raw); guard !name.isEmpty, name.count <= 180 else { return nil }
        let c = cached(name)
        if !force, let c, c.retryAfter > Date(), c.file.isEmpty || imagePath(c) != nil { return c }
        if AppBrand.offline("ARTIST_PHOTOS") { return c }
        if let p = pending[name.lowercased()] { return await p.value }
        let task = Task<ArtistPhoto?, Never> {
            do { return try await fetch(name) } catch {
                var r = cached(name) ?? { var a = ArtistPhoto(); a.artist = name; return a }()
                r.retryAfter = Date().addingTimeInterval(15 * 60); save(r); return r
            }
        }
        pending[name.lowercased()] = task
        let r = await task.value
        pending[name.lowercased()] = nil
        return r
    }

    func read(_ url: URL, maximum: Int) async throws -> Data {
        guard url.scheme == "https", ["en.wikipedia.org", "commons.wikimedia.org", "upload.wikimedia.org", "thumb.wikimedia.org"].contains(url.host ?? "") else { throw MetadataError.message("Desteklenmeyen görsel kaynağı.") }
        if backoff > Date() { throw MetadataError.message("Görsel servisi beklemede.") }
        let wait = nextRequest.timeIntervalSinceNow; if wait > 0 { try await Task.sleep(nanoseconds: UInt64(wait * 1e9)) }
        nextRequest = Date().addingTimeInterval(0.6)
        let (data, response) = try await session.data(from: url)
        let code = (response as? HTTPURLResponse)?.statusCode ?? 0
        if code == 429 || code == 503 { backoff = Date().addingTimeInterval(300); throw MetadataError.message("Görsel servisi yoğun.") }
        if code == 403 { backoff = Date().addingTimeInterval(60) }
        guard (200..<300).contains(code), data.count <= maximum else { throw MetadataError.message("Görsel yanıtı alınamadı.") }
        return data
    }

    func fetch(_ name: String) async throws -> ArtistPhoto {
        let q = URL(string: "https://en.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=pageimages%7Cpageprops%7Ccategories&piprop=name%7Cthumbnail&pithumbsize=1024&pilicense=free&cllimit=100&titles=" + Wiki.escape(Wiki.titles(name)))!
        let root = try JSONSerialization.jsonObject(with: try await read(q, maximum: 1_048_576))
        guard let page = Wiki.selectPage(root, name, requireThumb: true) else {
            var missing = ArtistPhoto(); missing.artist = name; missing.retryAfter = Date().addingTimeInterval(7 * 86400); save(missing); return missing
        }
        let filename = page["pageimage"] as? String ?? ""
        let info = URL(string: "https://commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2&prop=imageinfo&iiprop=url%7Cextmetadata&iiurlwidth=1024&titles=" + Wiki.escape("File:" + filename))!
        let infoRoot = try JSONSerialization.jsonObject(with: try await read(info, maximum: 1_048_576)) as? [String: Any]
        guard let pages = (infoRoot?["query"] as? [String: Any])?["pages"] as? [[String: Any]], let item = (pages.first?["imageinfo"] as? [[String: Any]])?.first else { throw MetadataError.message("Görsel kaynak bilgisi bulunamadı.") }
        let meta = item["extmetadata"] as? [String: Any] ?? [:]
        func m(_ k: String) -> String {
            let raw = (meta[k] as? [String: Any])?["value"] as? String ?? ""
            let plain = raw.replacingOccurrences(of: "<[^>]+>", with: " ", options: .regularExpression).replacingOccurrences(of: "&amp;", with: "&").replacingOccurrences(of: "&quot;", with: "\"").trimmingCharacters(in: .whitespacesAndNewlines)
            return String(plain.prefix(500))
        }
        let license = m("LicenseShortName"), author = m("Artist")
        guard !license.isEmpty else { throw MetadataError.message("Fotoğraf lisans bilgisi bulunamadı.") }
        let source = item["descriptionurl"] as? String ?? ""
        guard let su = URL(string: source), su.scheme == "https", su.host == "commons.wikimedia.org" else { throw MetadataError.message("Görsel kaynağı doğrulanamadı.") }
        let imageUrl = (item["thumburl"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? item["url"] as? String ?? ""
        guard let iu = URL(string: imageUrl) else { throw MetadataError.message("Görsel adresi geçersiz.") }
        let bytes = try await read(iu, maximum: 8_388_608)
        let local = key(name) + "-" + UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased() + ".png"
        try Self.writeImage(bytes, (directory as NSString).appendingPathComponent(local))
        var photo = ArtistPhoto(); photo.artist = name; photo.file = local; photo.sourceUrl = source
        photo.articleUrl = "https://en.wikipedia.org/wiki/" + Wiki.escape((page["title"] as? String ?? name).replacingOccurrences(of: " ", with: "_"))
        photo.author = author; photo.license = license; photo.retryAfter = Date().addingTimeInterval(90 * 86400)
        save(photo); return photo
    }

    public static func writeImage(_ bytes: Data, _ path: String) throws {
        guard let src = CGImageSourceCreateWithData(bytes as CFData, nil), let img = CGImageSourceCreateImageAtIndex(src, 0, nil),
              img.width >= 32, img.height >= 32, img.width * img.height <= 16_777_216 else { throw MetadataError.message("Fotoğraf boyutu uygun değil.") }
        try FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        guard let dest = CGImageDestinationCreateWithURL(URL(fileURLWithPath: path) as CFURL, UTType.png.identifier as CFString, 1, nil) else { throw MetadataError.message("PNG yazılamadı.") }
        CGImageDestinationAddImage(dest, img, nil)
        guard CGImageDestinationFinalize(dest) else { throw MetadataError.message("PNG yazılamadı.") }
    }
    public func importLocal(_ raw: String, _ bytes: Data) throws -> ArtistPhoto {
        let name = Self.identity(raw)
        let file = key(name) + "-" + UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased() + ".png"
        try Self.writeImage(bytes, (directory as NSString).appendingPathComponent(file))
        var p = ArtistPhoto(); p.artist = name; p.file = file; p.local = true; p.retryAfter = .distantFuture
        save(p); return p
    }
    func save(_ r: ArtistPhoto) { try? PlayerJson.write(recordPath(r.artist), r); records[r.artist.lowercased()] = r }
}

public struct ArtistBiography: Codable {
    public var artist = "", language = "tr", title = "", text = "", url = ""
    public var checkedAt = Date()
    public var contentVersion = 0
    enum CodingKeys: String, CodingKey { case artist = "Artist", language = "Language", title = "Title", text = "Text", url = "Url", checkedAt = "CheckedAt", contentVersion = "ContentVersion" }
    public init() {}
}

public actor ArtistBiographyService {
    public static let shared = ArtistBiographyService()
    let directory = Paths.file("artist-biographies")
    var nextRequest = Date.distantPast, blockedUntil = Date.distantPast
    let session = Wiki.session(timeout: 12)

    static func language(_ l: String) -> String { l == "en" ? "en" : "tr" }
    func path(_ artist: String, _ lang: String) -> String {
        (directory as NSString).appendingPathComponent(Paths.hash(ArtistPhotoStore.identity(artist).uppercased() + "|" + Self.language(lang)) + ".json")
    }
    public static func articleUrl(_ s: String, _ lang: String) -> Bool {
        guard let u = URL(string: s) else { return false }
        return u.scheme == "https" && u.host == language(lang) + ".wikipedia.org" && u.path.hasPrefix("/wiki/")
    }
    public func readCache(_ artist: String, _ lang: String) -> ArtistBiography? {
        guard let d = PlayerJson.read(ArtistBiography.self, path(artist, lang)), d.artist.caseInsensitiveCompare(ArtistPhotoStore.identity(artist)) == .orderedSame,
              d.language == Self.language(lang), d.text.count <= 12000, d.text.isEmpty || Self.articleUrl(d.url, lang) else { return nil }
        return d
    }
    func send(_ url: URL) async throws -> Any? {
        guard url.scheme == "https", url.host == "en.wikipedia.org" || url.host == "tr.wikipedia.org" else { throw MetadataError.message("Unexpected Wikipedia endpoint.") }
        if blockedUntil > Date() { throw MetadataError.message("Wikipedia cooldown.") }
        let wait = nextRequest.timeIntervalSinceNow; if wait > 0 { try await Task.sleep(nanoseconds: UInt64(wait * 1e9)) }
        nextRequest = Date().addingTimeInterval(0.35)
        let (data, response) = try await session.data(from: url)
        let code = (response as? HTTPURLResponse)?.statusCode ?? 0
        if code == 404 { return nil }
        if code == 429 || code == 503 {
            let retry = ((response as? HTTPURLResponse)?.value(forHTTPHeaderField: "Retry-After")).flatMap(Double.init) ?? 60
            blockedUntil = Date().addingTimeInterval(retry); throw MetadataError.message("Wikipedia cooldown.")
        }
        guard (200..<300).contains(code), data.count <= 1_048_576 else { throw MetadataError.message("Wikipedia yanıtı alınamadı.") }
        return try JSONSerialization.jsonObject(with: data)
    }
    static func biographyText(_ raw: String) -> String {
        var text = raw.replacingOccurrences(of: "\r", with: "").trimmingCharacters(in: .whitespacesAndNewlines)
        let endPattern = #"(?mi)^={2,}\s*(?:Discography|Selected discography|Videography|Filmography|References|Notes|External links|Bibliography|Diskografi|Seçili diskografi|Videografi|Filmografi|Kaynakça|Notlar|Dış bağlantılar|Bibliyografya)\s*={2,}\s*$"#
        if let r = text.range(of: endPattern, options: .regularExpression), text.distance(from: text.startIndex, to: r.lowerBound) > 100 { text = String(text[..<r.lowerBound]).trimmingCharacters(in: .whitespacesAndNewlines) }
        text = text.replacingOccurrences(of: #"(?m)^={2,}\s*(.*?)\s*={2,}\s*$"#, with: "$1", options: .regularExpression)
        text = text.replacingOccurrences(of: #"\n{3,}"#, with: "\n\n", options: .regularExpression)
        if text.count <= 5000 { return text }
        let head = String(text.prefix(5000))
        var cut = head.range(of: "\n\n", options: .backwards).map { head.distance(from: head.startIndex, to: $0.lowerBound) } ?? 0
        if cut < 2500 { cut = (head.lastIndex(where: { ".!?".contains($0) }).map { head.distance(from: head.startIndex, to: $0) + 1 }) ?? 0 }
        if cut < 2500 { cut = head.lastIndex(of: " ").map { head.distance(from: head.startIndex, to: $0) } ?? 5000 }
        if cut < 2500 { cut = 5000 }
        return String(text.prefix(cut)).trimmingCharacters(in: .whitespacesAndNewlines) + "…"
    }
    /// Returns nil only for an unusable name; an empty `text` means Wikipedia has no matching article.
    /// Throws when Wikipedia is unreachable and no cached text exists (ArtistBiographyService.Get).
    public func get(_ raw: String, _ lang: String) async throws -> ArtistBiography? {
        let artist = ArtistPhotoStore.identity(raw), language = Self.language(lang)
        guard !artist.isEmpty, artist.count <= 180 else { return nil }
        let cached = readCache(artist, language)
        if let c = cached, c.contentVersion >= 2, c.checkedAt > Date().addingTimeInterval(-(c.text.isEmpty ? 1 : 30) * 86400) { return c }
        if AppBrand.offline("ARTIST_BIOGRAPHY") { return cached }
        do {
            var result = ArtistBiography(); result.artist = artist; result.language = language; result.contentVersion = 2
            let q = URL(string: "https://en.wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=pageprops%7Ccategories%7Clanglinks&cllimit=100&lllimit=1&llprop=url&lllang=\(language)&titles=" + Wiki.escape(Wiki.titles(artist)))!
            var title = ""
            if let root = try await send(q), let page = Wiki.selectPage(root, artist, requireThumb: false) {
                if language == "en" { title = page["title"] as? String ?? "" }
                else { title = (page["langlinks"] as? [[String: Any]] ?? []).first { $0["lang"] as? String == language }?["title"] as? String ?? "" }
            }
            if !title.isEmpty, let summary = try await send(URL(string: "https://\(language).wikipedia.org/api/rest_v1/page/summary/" + Wiki.escape(title.replacingOccurrences(of: " ", with: "_")))!) as? [String: Any] {
                let text = summary["extract"] as? String ?? ""
                let page = ((summary["content_urls"] as? [String: Any])?["desktop"] as? [String: Any])?["page"] as? String ?? ""
                if summary["type"] as? String == "standard", summary["lang"] as? String == language, !text.isEmpty, text.count <= 12000, Self.articleUrl(page, language) {
                    result.title = summary["title"] as? String ?? title; result.text = text.trimmingCharacters(in: .whitespacesAndNewlines); result.url = page
                    let detail = URL(string: "https://\(language).wikipedia.org/w/api.php?action=query&format=json&formatversion=2&redirects=1&prop=extracts&explaintext=1&exsectionformat=wiki&titles=" + Wiki.escape(result.title))!
                    if let full = try? await send(detail) as? [String: Any], let pages = (full["query"] as? [String: Any])?["pages"] as? [[String: Any]] {
                        for a in pages where (a["title"] as? String ?? "").caseInsensitiveCompare(result.title) == .orderedSame {
                            let expanded = Self.biographyText(a["extract"] as? String ?? ""); if expanded.count > result.text.count { result.text = expanded }; break
                        }
                    }
                }
            }
            try Task.checkCancellation()
            try? PlayerJson.write(path(artist, language), result)
            return result
        } catch {
            if let c = cached, !c.text.isEmpty { return c }
            throw error
        }
    }
}
