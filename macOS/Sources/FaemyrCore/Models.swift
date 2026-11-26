import Foundation

public struct MetadataField: Codable, Hashable, Sendable {
    public var key: String
    public var value: String
    public init(_ key: String, _ value: String) { self.key = key; self.value = value }
    enum CodingKeys: String, CodingKey { case key = "Key", value = "Value" }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        key = (try? c.decodeIfPresent(String.self, forKey: .key)) ?? ""
        value = (try? c.decodeIfPresent(String.self, forKey: .value)) ?? ""
    }
    public var isArtist: Bool { ArtistCatalog.isArtistField(key) }
    public var label: String { tr(key) }
}

/// Port of `Track` (Library.cs). Coding keys keep the Windows PascalCase JSON so a
/// library.db row written by either platform can be read by the other.
public final class Track: Codable, Identifiable, Hashable, @unchecked Sendable {
    public var id: String { path }
    public var path = ""
    public var title = ""
    public var artist = ""
    public var sourcePath = ""
    public var cueSheetPath = ""
    public var cueStartFrame: Int64 = 0
    public var cueEndFrame: Int64?
    public var fileSize: Int64 = 0
    public var addedAt = Date()
    public var albumEditionKey = ""
    public var editionLabel = ""
    public var fileSignature = ""
    public var sourceModified: Int64 = 0
    public var album = ""
    public var albumArtist = ""
    public var cover = ""
    public var codec = ""
    public var sampleFormat = ""
    public var wavPackInfo = ""
    public var sampleRate = 0
    public var bits = 0
    public var channels = 0
    public var dsdRate = 0
    public var number: UInt32 = 0
    public var disc: UInt32 = 0
    public var seconds: Double = 0
    public var modified: Int64 = 0
    public var fields: [MetadataField] = []
    public var probeJson = ""
    public var bitRate: Int64 = 0
    public var bitRateSource = ""

    // Runtime state (JsonIgnore on Windows).
    public var isFavorite = false
    public var rating = 0

    public init() {}

    enum CodingKeys: String, CodingKey {
        case path = "Path", title = "Title", artist = "Artist", sourcePath = "SourcePath", cueSheetPath = "CueSheetPath"
        case cueStartFrame = "CueStartFrame", cueEndFrame = "CueEndFrame", fileSize = "FileSize", addedAt = "AddedAt"
        case albumEditionKey = "AlbumEditionKey", editionLabel = "EditionLabel", fileSignature = "FileSignature"
        case sourceModified = "SourceModified", album = "Album", albumArtist = "AlbumArtist", cover = "Cover"
        case codec = "Codec", sampleFormat = "SampleFormat", wavPackInfo = "WavPackInfo", sampleRate = "SampleRate"
        case bits = "Bits", channels = "Channels", dsdRate = "DsdRate", number = "Number", disc = "Disc"
        case seconds = "Seconds", modified = "Modified", fields = "Fields", probeJson = "ProbeJson"
        case bitRate = "BitRate", bitRateSource = "BitRateSource"
    }

    public required init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func s(_ k: CodingKeys) -> String { (try? c.decodeIfPresent(String.self, forKey: k)) ?? "" }
        func i64(_ k: CodingKeys) -> Int64 { (try? c.decodeIfPresent(Int64.self, forKey: k)) ?? 0 }
        func i(_ k: CodingKeys) -> Int { (try? c.decodeIfPresent(Int.self, forKey: k)) ?? 0 }
        path = s(.path); title = s(.title); artist = s(.artist); sourcePath = s(.sourcePath); cueSheetPath = s(.cueSheetPath)
        cueStartFrame = i64(.cueStartFrame); cueEndFrame = try? c.decodeIfPresent(Int64.self, forKey: .cueEndFrame)
        fileSize = i64(.fileSize); addedAt = (try? c.decodeIfPresent(Date.self, forKey: .addedAt)) ?? Date()
        albumEditionKey = s(.albumEditionKey); editionLabel = s(.editionLabel); fileSignature = s(.fileSignature)
        sourceModified = i64(.sourceModified); album = s(.album); albumArtist = s(.albumArtist); cover = s(.cover)
        codec = s(.codec); sampleFormat = s(.sampleFormat); wavPackInfo = s(.wavPackInfo)
        sampleRate = i(.sampleRate); bits = i(.bits); channels = i(.channels); dsdRate = i(.dsdRate)
        number = (try? c.decodeIfPresent(UInt32.self, forKey: .number)) ?? 0
        disc = (try? c.decodeIfPresent(UInt32.self, forKey: .disc)) ?? 0
        seconds = (try? c.decodeIfPresent(Double.self, forKey: .seconds)) ?? 0
        modified = i64(.modified)
        fields = (try? c.decodeIfPresent([MetadataField].self, forKey: .fields)) ?? []
        probeJson = s(.probeJson); bitRate = i64(.bitRate); bitRateSource = s(.bitRateSource)
        if albumArtist.isEmpty { albumArtist = artist }
        if !seconds.isFinite || seconds < 0 || seconds > 1e9 { seconds = 0 }
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(path, forKey: .path); try c.encode(title, forKey: .title); try c.encode(artist, forKey: .artist)
        try c.encode(sourcePath, forKey: .sourcePath); try c.encode(cueSheetPath, forKey: .cueSheetPath)
        try c.encode(cueStartFrame, forKey: .cueStartFrame); try c.encode(cueEndFrame, forKey: .cueEndFrame)
        try c.encode(fileSize, forKey: .fileSize); try c.encode(addedAt, forKey: .addedAt)
        try c.encode(albumEditionKey, forKey: .albumEditionKey); try c.encode(editionLabel, forKey: .editionLabel)
        try c.encode(fileSignature, forKey: .fileSignature); try c.encode(sourceModified, forKey: .sourceModified)
        try c.encode(album, forKey: .album); try c.encode(albumArtist, forKey: .albumArtist); try c.encode(cover, forKey: .cover)
        try c.encode(codec, forKey: .codec); try c.encode(sampleFormat, forKey: .sampleFormat); try c.encode(wavPackInfo, forKey: .wavPackInfo)
        try c.encode(sampleRate, forKey: .sampleRate); try c.encode(bits, forKey: .bits); try c.encode(channels, forKey: .channels)
        try c.encode(dsdRate, forKey: .dsdRate); try c.encode(number, forKey: .number); try c.encode(disc, forKey: .disc)
        try c.encode(seconds, forKey: .seconds); try c.encode(modified, forKey: .modified); try c.encode(fields, forKey: .fields)
        try c.encode(probeJson, forKey: .probeJson); try c.encode(bitRate, forKey: .bitRate); try c.encode(bitRateSource, forKey: .bitRateSource)
    }

    public static func == (a: Track, b: Track) -> Bool { a === b }
    public func hash(into h: inout Hasher) { h.combine(ObjectIdentifier(self)) }

    public func copy() -> Track {
        let data = (try? PlayerJson.encoder.encode(self)) ?? Data()
        let t = (try? PlayerJson.decoder.decode(Track.self, from: data)) ?? Track()
        t.isFavorite = isFavorite; t.rating = rating
        return t
    }

    public var audioPath: String { sourcePath.isEmpty ? path : sourcePath }
    public var isCue: Bool { !cueSheetPath.isEmpty }
    public var isDsd: Bool {
        let ext = path.pathExtLower
        return ext == "dsf" || ext == "dff" || codec.lowercased().hasPrefix("dsd") || codec.lowercased() == "dst"
    }
    public var albumDisc: UInt32 { AlbumDiscs.number(self) }
    public var duration: String { formatTime(seconds) }
    public var albumKey: String { albumArtist + "\u{1f}" + album + (albumEditionKey.isEmpty ? "" : "\u{1f}" + albumEditionKey) }
    public var bitRateLabel: String { bitRate > 0 ? "\(numberText(Int((Double(bitRate) / 1000).rounded()))) kbps" : "—" }
    public var bitRateHint: String {
        tr("Ortalama bitrate · ") + tr(bitRateSource) + (isCue ? tr(" · CUE kaynak dosyasının ortalaması") : "") + (audioPath.pathExtLower == "wv" ? tr(" · .wvc düzeltme dosyası hariç") : "")
    }
    public var wavPackMode: String {
        guard !wavPackInfo.isEmpty else { return "" }
        for line in wavPackInfo.split(separator: "\n") {
            let l = line.trimmingCharacters(in: .whitespaces)
            if l.lowercased().hasPrefix("modalities:") { return String(l.dropFirst("modalities:".count)).trimmingCharacters(in: .whitespaces) }
        }
        return ""
    }
    public var quality: String {
        if isDsd {
            let mhz = String(format: "%.4g", Double(dsdRate) / 1_000_000)
            return "DSD\(dsdRate > 0 ? dsdRate / 44100 : 0) · \(mhz) MHz · \(channels)" + tr(" kanal")
        }
        let khz = trimmedNumber(Double(sampleRate) / 1000, 3)
        return "\(codec.uppercased()) · \(bits > 0 ? "\(bits) bit" : tr("kodlanmış ses")) / \(khz) kHz · \(channels)" + tr(" kanal")
    }
}

/// Formats like .NET "0.###" (no trailing zeros), using the UI culture's decimal separator.
public func trimmedNumber(_ v: Double, _ digits: Int) -> String {
    let f = NumberFormatter(); f.minimumFractionDigits = 0; f.maximumFractionDigits = digits; f.locale = L.locale; f.usesGroupingSeparator = false
    return f.string(from: NSNumber(value: v)) ?? "\(v)"
}

public struct AlbumCard: Identifiable, Hashable {
    public var id: String { key }
    public var key = ""
    public var title = ""
    public var artist = ""
    public var cover: String?
    public var count = 0
    public var year = 0
    public var isFavorite = false
    public var detail: String { "\(artist) · \(count)" + tr(" parça") }
    public var countLabel: String { " · \(count)" + tr(" parça") }
    public init(key: String, title: String, artist: String, cover: String?, count: Int, year: Int, isFavorite: Bool) {
        self.key = key; self.title = title; self.artist = artist; self.cover = cover; self.count = count; self.year = year; self.isFavorite = isFavorite
    }
    public var tooltip: String { "\(title)\n\(artist) · " + (year > 0 ? "\(year) · " : "") + "\(count)" + tr(" parça") }
}

public struct ArtistCard: Identifiable, Hashable {
    public var id: String { name }
    public var name = ""
    public var cover: String?
    public var albums = 0
    public var tracks = 0
    public init(name: String, cover: String?, albums: Int, tracks: Int) { self.name = name; self.cover = cover; self.albums = albums; self.tracks = tracks }
    public var detail: String { "\(albums)" + tr(" albüm · ") + "\(tracks)" + tr(" parça") }
}

public struct GalleryRow<T: Hashable>: Hashable, Identifiable {
    public let id: Int
    public let heading: String
    public let cards: [T]
}
