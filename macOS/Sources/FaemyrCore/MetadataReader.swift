import Foundation
import AVFoundation
import AudioToolbox

public enum MetadataError: LocalizedError {
    case message(String)
    public var errorDescription: String? { if case .message(let m) = self { return m }; return nil }
}

/// Byte reader used by the native tag parsers.
struct ByteReader {
    let data: Data
    var pos: Int = 0
    init(_ d: Data) { data = d }
    var remaining: Int { data.count - pos }
    mutating func bytes(_ n: Int) throws -> Data {
        guard n >= 0, pos + n <= data.count else { throw MetadataError.message("Eksik veri.") }
        defer { pos += n }; return data.subdata(in: data.startIndex + pos ..< data.startIndex + pos + n)
    }
    mutating func u8() throws -> UInt8 { try bytes(1).first! }
    mutating func be(_ n: Int) throws -> UInt64 { try bytes(n).reduce(0) { $0 << 8 | UInt64($1) } }
    mutating func le(_ n: Int) throws -> UInt64 { try bytes(n).reversed().reduce(0) { $0 << 8 | UInt64($1) } }
    mutating func skip(_ n: Int) throws { _ = try bytes(n) }
}

public enum LibraryFingerprint {
    /// SHA-256 over length + first/last 64 KiB, identical to the Windows fingerprint.
    public static func read(_ path: String) -> String {
        guard let h = FileHandle(forReadingAtPath: path) else { return "" }
        defer { try? h.close() }
        let length = (try? h.seekToEnd()) ?? 0
        var hasher = SHA256Stream()
        var len = Int64(length); hasher.update(Data(bytes: &len, count: 8))
        try? h.seek(toOffset: 0)
        hasher.update(h.readData(ofLength: 65536))
        if length > 65536 { try? h.seek(toOffset: max(65536, length - 65536)); hasher.update(h.readData(ofLength: 65536)) }
        return hasher.hex()
    }
}

import CryptoKit
struct SHA256Stream {
    var h = SHA256()
    mutating func update(_ d: Data) { h.update(data: d) }
    func hex() -> String { h.finalize().map { String(format: "%02X", $0) }.joined() }
}

public enum MetadataReader {
    public static let extensions: Set<String> = ["cue", "flac", "wv", "wav", "wave", "aiff", "aif", "aifc", "mp3", "m4a", "alac", "dsf", "dff", "ogg", "opus", "ape", "caf", "aac"]

    public static func readLibraryFile(_ path: String, covers: String) async throws -> [Track] {
        if path.pathExtLower == "cue" { return try await CueSheet.read(path, covers: covers) }
        return [try await read(path, covers: covers)]
    }

    public static func read(_ path: String, covers: String) async throws -> Track {
        let t = Track()
        t.path = path; t.title = path.lastPathComponentNoExt; t.artist = "Bilinmeyen sanatçı"; t.album = "Adsız albüm"
        t.modified = Ticks.modified(path); t.sourceModified = t.modified; t.addedAt = Date()
        t.fileSize = (try? FileManager.default.attributesOfItem(atPath: path)[.size] as? Int64) ?? 0
        var tags: [(String, String)] = []   // normalized lowercase key -> value, in file order
        var picture: Data?
        let ext = path.pathExtLower

        if ext == "flac" {
            let info = try FLAC.read(path)
            t.codec = "flac"; t.sampleRate = info.sampleRate; t.channels = info.channels; t.bits = info.bits
            t.sampleFormat = info.bits > 16 ? "s32" : "s16"
            t.seconds = info.sampleRate > 0 ? Double(info.totalSamples) / Double(info.sampleRate) : 0
            t.fields.append(contentsOf: info.fields)
            for f in info.fields where f.key.hasPrefix("Vorbis / ") && f.key != "Vorbis / vendor" { tags.append((String(f.key.dropFirst(9)).lowercased(), f.value)) }
            picture = info.picture
            if info.audioOffset > 0 && t.seconds > 0 {
                t.bitRate = Int64((Double(t.fileSize - info.audioOffset) * 8 / t.seconds).rounded())
                t.bitRateSource = "FLAC ses verisi ortalaması; kapak ve etiketler hariç"
            }
        } else if ext == "dsf" || ext == "dff" {
            let info = try DSD.read(path)
            t.codec = ext == "dsf" ? "dsd_lsbf_planar" : (info.compressed ? "dst" : "dsd_msbf")
            t.dsdRate = info.rate; t.channels = info.channels; t.bits = 1; t.seconds = info.seconds; t.sampleRate = info.rate / 8
            t.fields.append(MetadataField("DSD / Biçim", "DSD\(info.rate / 44100) · \(info.channels) kanal"))
            if let id3 = info.id3 { let r = ID3.parse(id3); t.fields.append(contentsOf: r.fields); tags += r.tags; picture = r.picture }
            if info.seconds > 0 { t.bitRate = Int64((Double(info.rate) * Double(info.channels)).rounded()); t.bitRateSource = "Ses akışı" }
        } else if let info = CoreAudioInfo.read(path) {
            t.codec = info.codec; t.sampleRate = info.sampleRate; t.channels = info.channels; t.bits = info.bits; t.seconds = info.seconds
            t.sampleFormat = info.sampleFormat
            if info.bitRate > 0 { t.bitRate = info.bitRate; t.bitRateSource = "Ses akışı" }
            let asset = AVURLAsset(url: URL(fileURLWithPath: path))
            let items = (try? await asset.load(.metadata)) ?? []
            for item in items {
                let raw = item.identifier?.rawValue ?? (item.key as? String) ?? "?"
                let (source, key) = keyspace(raw)
                var value = (try? await item.load(.stringValue)) ?? nil
                if value == nil, let n = try? await item.load(.numberValue) { value = n.stringValue }
                if let dataValue = try? await item.load(.dataValue), let common = item.commonKey, common == .commonKeyArtwork { picture = picture ?? dataValue; t.fields.append(MetadataField(source + " / " + key, "Gömülü resim · \(numberText(dataValue.count)) bayt")); continue }
                guard let v = value, !v.isEmpty else { continue }
                t.fields.append(MetadataField(source + " / " + key, v))
                if let common = item.commonKey?.rawValue { tags.append((common.lowercased(), v)) }
                tags.append((key.lowercased(), v))
            }
            // ID3 in WAV/AIFF chunks is not surfaced by AVFoundation on every macOS release.
            if ["wav", "wave", "aif", "aiff", "aifc"].contains(ext), let id3 = RIFF.embeddedID3(path) {
                let r = ID3.parse(id3); t.fields.append(contentsOf: r.fields); tags += r.tags; picture = picture ?? r.picture
            }
        } else if let probe = FFmpegTools.ffprobe {
            try await readWithFFprobe(t, probe: probe, tags: &tags)
        } else {
            throw MetadataError.message(tr("Bu biçim için macOS çözücüsü yok. FFmpeg yüklendiğinde (örneğin Homebrew: brew install ffmpeg) bu dosyalar da okunur."))
        }

        if let ape = try? APE.read(path) { t.fields.append(contentsOf: ape); for f in ape { tags.append((String(f.key.dropFirst(6)).lowercased(), f.value)) } }

        func tag(_ names: String...) -> String? {
            for n in names { if let v = tags.first(where: { $0.0 == n })?.1.trimmingCharacters(in: .whitespacesAndNewlines.union(CharacterSet(charactersIn: "\0\u{feff}"))), !v.isEmpty { return v } }
            return nil
        }
        func all(_ names: String...) -> [String] {
            var out: [String] = []
            for (k, v) in tags where names.contains(k) { let s = v.trimmingCharacters(in: .whitespacesAndNewlines); if !s.isEmpty && !out.contains(s) { out.append(s) } }
            return out
        }
        if let v = tag("title", "tit2", "©nam") { t.title = v }
        let performers = all("artist", "tpe1", "©art")
        if !performers.isEmpty { t.artist = Array(NSOrderedSet(array: performers.flatMap { $0.components(separatedBy: "\0") }.filter { !$0.isEmpty })).map { "\($0)" }.joined(separator: " / ") }
        if let v = tag("album", "albumname", "talb", "©alb") { t.album = v }
        let albumArtists = all("albumartist", "album artist", "album_artist", "tpe2", "aart")
        t.albumArtist = albumArtists.isEmpty ? t.artist : albumArtists.joined(separator: " / ")
        if let v = tag("tracknumber", "track", "trck", "trkn"), let n = UInt32(v.split(separator: "/").first.map { $0.trimmingCharacters(in: .whitespaces) } ?? "") { t.number = n }
        if let v = tag("discnumber", "disc", "tpos", "disk"), let n = UInt32(v.split(separator: "/").first.map { $0.trimmingCharacters(in: .whitespaces) } ?? "") { t.disc = n }
        if t.fields.allSatisfy({ !["DATE", "YEAR"].contains(ReplayGainTag.name($0.key)) }), let y = tag("date", "year", "tdrc", "tyer", "©day", "creationdate") {
            t.fields.append(MetadataField("Kapsayıcı / DATE", y))
        }
        if t.albumArtist.trimmingCharacters(in: .whitespaces).isEmpty { t.albumArtist = t.artist }

        if let picture, picture.count < 32 * 1024 * 1024 {
            try? FileManager.default.createDirectory(atPath: covers, withIntermediateDirectories: true)
            let cover = (covers as NSString).appendingPathComponent(Paths.hash(path + String(t.modified)) + ".img")
            if (try? picture.write(to: URL(fileURLWithPath: cover))) != nil { t.cover = cover }
        }
        if t.cover.isEmpty { t.cover = folderCover(path) ?? "" }
        t.fileSignature = LibraryFingerprint.read(path)
        return t
    }

    static func folderCover(_ path: String) -> String? {
        let dir = (path as NSString).deletingLastPathComponent
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: dir) else { return nil }
        let preferred = ["cover", "folder", "front", "albumart", "album"]
        let images = names.filter { ["jpg", "jpeg", "png", "webp"].contains($0.pathExtLower) }
        for p in preferred { if let n = images.first(where: { $0.lastPathComponentNoExt.lowercased() == p }) { return (dir as NSString).appendingPathComponent(n) } }
        return nil
    }

    static func keyspace(_ raw: String) -> (String, String) {
        let parts = raw.split(separator: "/", maxSplits: 1).map(String.init)
        guard parts.count == 2 else { return ("Kapsayıcı", raw) }
        let space: String
        switch parts[0] {
        case "id3": space = "ID3"
        case "itsk", "itlk": space = "iTunes"
        case "common": space = "Kapsayıcı"
        case "qt", "mdta", "udta": space = "QuickTime"
        default: space = parts[0]
        }
        return (space, parts[1].removingPercentEncoding ?? parts[1])
    }

    static func readWithFFprobe(_ t: Track, probe: String, tags: inout [(String, String)]) async throws {
        let json = try await ProcessTool.run(probe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", t.path])
        t.probeJson = json
        guard let root = try JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any],
              let streams = root["streams"] as? [[String: Any]],
              let audio = streams.first(where: { $0["codec_type"] as? String == "audio" }) else { throw MetadataError.message("Ses akışı bulunamadı.") }
        func int(_ d: [String: Any], _ k: String) -> Int { if let n = d[k] as? Int { return n }; return Int("\(d[k] ?? "")") ?? 0 }
        t.codec = audio["codec_name"] as? String ?? ""; t.sampleFormat = audio["sample_fmt"] as? String ?? ""
        t.sampleRate = int(audio, "sample_rate"); t.channels = int(audio, "channels")
        t.bits = int(audio, "bits_per_raw_sample"); if t.bits == 0 { t.bits = int(audio, "bits_per_sample") }
        let format = root["format"] as? [String: Any] ?? [:]
        t.seconds = Double("\(format["duration"] ?? "0")") ?? 0
        if let b = Int64("\(audio["bit_rate"] ?? "")"), b > 0 { t.bitRate = b; t.bitRateSource = "Ses akışı" }
        else if let b = Int64("\(format["bit_rate"] ?? "")"), b > 0 { t.bitRate = b; t.bitRateSource = "Dosya ortalaması; kapak ve etiketler dahil" }
        for (source, obj) in [("Kapsayıcı", format), ("Akış \(audio["index"] ?? 0)", audio)] {
            if let tg = obj["tags"] as? [String: Any] { for (k, v) in tg { t.fields.append(MetadataField(source + " / " + k, "\(v)")); tags.append((k.lowercased(), "\(v)")) } }
        }
    }
}

// MARK: - Core Audio file information

struct CoreAudioInfo {
    var codec = "", sampleFormat = ""
    var sampleRate = 0, channels = 0, bits = 0
    var seconds = 0.0
    var bitRate: Int64 = 0

    static func read(_ path: String) -> CoreAudioInfo? {
        var file: AudioFileID?
        guard AudioFileOpenURL(URL(fileURLWithPath: path) as CFURL, .readPermission, 0, &file) == noErr, let file else { return nil }
        defer { AudioFileClose(file) }
        var asbd = AudioStreamBasicDescription(); var size = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        guard AudioFileGetProperty(file, kAudioFilePropertyDataFormat, &size, &asbd) == noErr else { return nil }
        var info = CoreAudioInfo()
        info.sampleRate = Int(asbd.mSampleRate); info.channels = Int(asbd.mChannelsPerFrame)
        var depth: UInt32 = 0; size = 4
        if AudioFileGetProperty(file, kAudioFilePropertySourceBitDepth, &size, &depth) == noErr { info.bits = Int(Int32(bitPattern: depth)) }
        if info.bits <= 0 { info.bits = Int(asbd.mBitsPerChannel) }
        var duration: Float64 = 0; size = 8
        if AudioFileGetProperty(file, kAudioFilePropertyEstimatedDuration, &size, &duration) == noErr { info.seconds = duration }
        var rate: UInt32 = 0; size = 4
        if AudioFileGetProperty(file, kAudioFilePropertyBitRate, &size, &rate) == noErr { info.bitRate = Int64(rate) }
        let float = asbd.mFormatFlags & kAudioFormatFlagIsFloat != 0
        let big = asbd.mFormatFlags & kAudioFormatFlagIsBigEndian != 0
        switch asbd.mFormatID {
        case kAudioFormatLinearPCM:
            info.codec = float ? "pcm_f\(asbd.mBitsPerChannel)\(big ? "be" : "le")" : "pcm_s\(asbd.mBitsPerChannel)\(big ? "be" : "le")"
            info.sampleFormat = float ? "flt" : "s\(asbd.mBitsPerChannel)"
            if info.bitRate == 0 { info.bitRate = Int64(asbd.mSampleRate) * Int64(asbd.mChannelsPerFrame) * Int64(asbd.mBitsPerChannel) }
        case kAudioFormatAppleLossless: info.codec = "alac"
            if info.bits <= 0 { info.bits = [1: 16, 2: 20, 3: 24, 4: 32][Int(asbd.mFormatFlags)] ?? 0 }
        case kAudioFormatMPEGLayer3: info.codec = "mp3"; info.bits = 0
        case kAudioFormatMPEG4AAC, kAudioFormatMPEG4AAC_HE, kAudioFormatMPEG4AAC_HE_V2, kAudioFormatMPEG4AAC_LD: info.codec = "aac"; info.bits = 0
        case kAudioFormatFLAC: info.codec = "flac"
        case kAudioFormatOpus: info.codec = "opus"; info.bits = 0
        default:
            let id = asbd.mFormatID.bigEndian
            info.codec = withUnsafeBytes(of: id) { String(bytes: $0, encoding: .ascii) ?? "?" }.trimmingCharacters(in: .whitespaces).lowercased()
        }
        return info
    }
}

// MARK: - FLAC

enum FLAC {
    struct Info { var sampleRate = 0, channels = 0, bits = 0; var totalSamples: Int64 = 0; var fields: [MetadataField] = []; var picture: Data?; var audioOffset: Int64 = 0 }
    static func read(_ path: String) throws -> Info {
        guard let h = FileHandle(forReadingAtPath: path) else { throw MetadataError.message("Dosya açılamadı.") }
        defer { try? h.close() }
        var head = h.readData(ofLength: 4)
        // Skip a leading ID3v2 tag some taggers write before "fLaC".
        if head.starts(with: [0x49, 0x44, 0x33]) {
            let rest = h.readData(ofLength: 6); let size = rest.suffix(4).reduce(0) { $0 << 7 | Int($1 & 127) }
            try h.seek(toOffset: UInt64(10 + size)); head = h.readData(ofLength: 4)
        }
        guard head == Data("fLaC".utf8) else { throw MetadataError.message("Geçerli bir FLAC dosyası değil.") }
        var info = Info(); var last = false; var bestPicture: (Int, Data)?
        while !last {
            let header = h.readData(ofLength: 4); guard header.count == 4 else { throw MetadataError.message("Eksik FLAC metadata bloğu.") }
            last = header[header.startIndex] & 0x80 != 0
            let type = header[header.startIndex] & 0x7f
            let length = Int(header[header.startIndex + 1]) << 16 | Int(header[header.startIndex + 2]) << 8 | Int(header[header.startIndex + 3])
            if type == 0 || type == 4 || type == 6 {
                let data = h.readData(ofLength: length); guard data.count == length else { throw MetadataError.message("Eksik FLAC metadata bloğu.") }
                var r = ByteReader(data)
                if type == 0 {
                    try r.skip(10)
                    let packed = try r.be(8)
                    info.sampleRate = Int(packed >> 44); info.channels = Int((packed >> 41) & 7) + 1
                    info.bits = Int((packed >> 36) & 31) + 1; info.totalSamples = Int64(packed & 0xFFFFFFFFF)
                } else if type == 4 {
                    func string() throws -> String { let n = Int(try r.le(4)); return String(decoding: try r.bytes(n), as: UTF8.self) }
                    info.fields.append(MetadataField("Vorbis / vendor", try string()))
                    let count = Int(try r.le(4))
                    for _ in 0..<min(count, 100000) {
                        let s = try string()
                        if let eq = s.firstIndex(of: "=") { info.fields.append(MetadataField("Vorbis / " + s[..<eq], String(s[s.index(after: eq)...]))) }
                        else { info.fields.append(MetadataField("Vorbis / " + s, "")) }
                    }
                } else {
                    info.fields.append(MetadataField("FLAC / PICTURE", "Gömülü resim bloğu · \(numberText(length)) bayt"))
                    let pictureType = Int(try r.be(4)); let mime = Int(try r.be(4)); try r.skip(mime)
                    let desc = Int(try r.be(4)); try r.skip(desc); try r.skip(16)
                    let n = Int(try r.be(4)); let image = try r.bytes(n)
                    let rank = pictureType == 3 ? 0 : 1
                    if bestPicture == nil || rank < bestPicture!.0 { bestPicture = (rank, image) }
                }
            } else {
                try h.seek(toOffset: h.offsetInFile + UInt64(length))
            }
        }
        info.audioOffset = Int64(h.offsetInFile); info.picture = bestPicture?.1
        return info
    }
}

// MARK: - DSD (DSF / DSDIFF)

enum DSD {
    struct Info { var rate = 0, channels = 0; var seconds = 0.0; var id3: Data?; var compressed = false; var dataOffset: Int64 = 0; var dataSize: Int64 = 0; var blockSize = 4096; var lsbFirst = true }
    static func read(_ path: String) throws -> Info {
        guard let d = try? Data(contentsOf: URL(fileURLWithPath: path), options: .alwaysMapped) else { throw MetadataError.message("Dosya açılamadı.") }
        var r = ByteReader(d); var info = Info()
        let magic = String(decoding: try r.bytes(4), as: UTF8.self)
        if magic == "DSD " {
            try r.skip(8); _ = try r.le(8); let meta = Int64(try r.le(8))
            guard String(decoding: try r.bytes(4), as: UTF8.self) == "fmt " else { throw MetadataError.message("DSF fmt bloğu yok.") }
            try r.skip(8); try r.skip(8) // size, version+id
            _ = try r.le(4); info.channels = Int(try r.le(4)); info.rate = Int(try r.le(4)); _ = try r.le(4)
            let samples = Int64(try r.le(8)); info.blockSize = Int(try r.le(4))
            info.seconds = info.rate > 0 ? Double(samples) / Double(info.rate) : 0
            if meta > 0 && meta < d.count { info.id3 = d.subdata(in: Int(meta)..<d.count) }
            r.pos = 28 + 52
            if String(decoding: try r.bytes(4), as: UTF8.self) == "data" { info.dataSize = Int64(try r.le(8)) - 12; info.dataOffset = Int64(r.pos) }
            return info
        }
        guard magic == "FRM8" else { throw MetadataError.message("Desteklenmeyen DSD kapsayıcısı.") }
        try r.skip(8); guard String(decoding: try r.bytes(4), as: UTF8.self) == "DSD " else { throw MetadataError.message("DSDIFF türü geçersiz.") }
        info.lsbFirst = false
        func walk(_ end: Int) throws {
            while r.pos + 12 <= end {
                let id = String(decoding: try r.bytes(4), as: UTF8.self); let size = Int(try r.be(8)); let start = r.pos
                switch id {
                case "PROP": try r.skip(4); try walk(start + size)
                case "FS  ": info.rate = Int(try r.be(4))
                case "CHNL": info.channels = Int(try r.be(2))
                case "CMPR": info.compressed = String(decoding: try r.bytes(4), as: UTF8.self) != "DSD "
                case "DSD ": info.dataOffset = Int64(start); info.dataSize = Int64(size)
                case "DST ": info.compressed = true; info.dataOffset = Int64(start); info.dataSize = Int64(size)
                case "ID3 ": info.id3 = d.subdata(in: start..<min(d.count, start + size))
                default: break
                }
                r.pos = start + size + (size & 1)
            }
        }
        try walk(d.count)
        if info.rate > 0 && info.channels > 0 && !info.compressed { info.seconds = Double(info.dataSize * 8) / Double(info.rate * info.channels) }
        return info
    }
}

// MARK: - ID3v2

enum ID3 {
    static let names: [String: String] = ["TIT2": "title", "TPE1": "artist", "TALB": "album", "TPE2": "albumartist", "TRCK": "tracknumber", "TPOS": "discnumber", "TDRC": "date", "TYER": "year", "TCON": "genre", "TCOM": "composer"]
    static func parse(_ data: Data) -> (fields: [MetadataField], tags: [(String, String)], picture: Data?) {
        var fields: [MetadataField] = []; var tags: [(String, String)] = []; var picture: Data?
        let d = Data(data)
        guard d.count > 10, d.starts(with: [0x49, 0x44, 0x33]) else { return ([], [], nil) }
        let version = d[3]
        let size = d[6..<10].reduce(0) { $0 << 7 | Int($1 & 127) }
        var pos = 10; let end = min(d.count, 10 + size)
        while pos + 10 <= end {
            let id = String(decoding: d[pos..<pos + 4], as: UTF8.self)
            guard id.allSatisfy({ $0.isLetter || $0.isNumber }) else { break }
            let raw = d[pos + 4..<pos + 8]
            let len = version >= 4 ? raw.reduce(0) { $0 << 7 | Int($1 & 127) } : raw.reduce(0) { $0 << 8 | Int($1) }
            pos += 10; guard len > 0, pos + len <= end else { break }
            let body = d.subdata(in: pos..<pos + len); pos += len
            if id == "APIC", body.count > 4 {
                var i = 1; while i < body.count && body[i] != 0 { i += 1 }; i += 2
                let enc = body[0]
                if enc == 1 || enc == 2 { while i + 1 < body.count && !(body[i] == 0 && body[i + 1] == 0) { i += 2 }; i += 2 } else { while i < body.count && body[i] != 0 { i += 1 }; i += 1 }
                if i < body.count { picture = picture ?? body.subdata(in: i..<body.count) }
                fields.append(MetadataField("ID3 / APIC", "Gömülü resim · \(numberText(body.count)) bayt")); continue
            }
            if id.hasPrefix("T"), let text = decode(body) {
                let value = text.components(separatedBy: "\0").filter { !$0.isEmpty }.joined(separator: "\0")
                fields.append(MetadataField("ID3 / " + id, value.replacingOccurrences(of: "\0", with: " / ")))
                if let n = names[id] { tags.append((n, value)) }
                tags.append((id.lowercased(), value))
            } else if id == "USLT" || id == "COMM", body.count > 4 {
                let enc = body[0]; var rest = body.subdata(in: 4..<body.count)
                if let sep = (enc == 1 || enc == 2) ? stride(from: 0, to: rest.count - 1, by: 2).first(where: { rest[rest.startIndex + $0] == 0 && rest[rest.startIndex + $0 + 1] == 0 }).map({ $0 + 2 }) : rest.firstIndex(of: 0).map({ $0 - rest.startIndex + 1 }) {
                    rest = rest.subdata(in: rest.startIndex + sep..<rest.endIndex)
                }
                if let text = decode(Data([enc]) + rest) { fields.append(MetadataField("ID3 / " + id, text)); if id == "USLT" { tags.append(("lyrics", text)) } }
            }
        }
        return (fields, tags, picture)
    }
    static func decode(_ body: Data) -> String? {
        guard let enc = body.first else { return nil }
        let raw = body.dropFirst()
        switch enc {
        case 1: return String(data: Data(raw), encoding: .utf16)
        case 2: return String(data: Data(raw), encoding: .utf16BigEndian)
        case 3: return String(decoding: raw, as: UTF8.self)
        default: return String(data: Data(raw), encoding: .isoLatin1)
        }
    }
}

enum RIFF {
    /// Finds an "id3 " / "ID3 " chunk inside RIFF/WAVE or FORM/AIFF.
    static func embeddedID3(_ path: String) -> Data? {
        guard let d = try? Data(contentsOf: URL(fileURLWithPath: path), options: .alwaysMapped), d.count > 12 else { return nil }
        let little = d.starts(with: Data("RIFF".utf8)); guard little || d.starts(with: Data("FORM".utf8)) else { return nil }
        var pos = 12
        while pos + 8 <= d.count {
            let id = String(decoding: d[pos..<pos + 4], as: UTF8.self).lowercased()
            let sb = d[pos + 4..<pos + 8]
            let size = little ? sb.reversed().reduce(0) { $0 << 8 | Int($1) } : sb.reduce(0) { $0 << 8 | Int($1) }
            if id == "id3 " { return d.subdata(in: pos + 8..<min(d.count, pos + 8 + size)) }
            pos += 8 + size + (size & 1)
        }
        return nil
    }
}

enum APE {
    static func read(_ path: String) throws -> [MetadataField] {
        guard let h = FileHandle(forReadingAtPath: path) else { return [] }
        defer { try? h.close() }
        var end = try h.seekToEnd(); guard end >= 32 else { return [] }
        if end >= 128 { try h.seek(toOffset: end - 128); if h.readData(ofLength: 3) == Data("TAG".utf8) { end -= 128 } }
        try h.seek(toOffset: end - 32); let footer = h.readData(ofLength: 32)
        guard footer.count == 32, footer.prefix(8) == Data("APETAGEX".utf8) else { return [] }
        var r = ByteReader(footer); try r.skip(12); let size = try r.le(4); let items = try r.le(4)
        guard size >= 32, size <= 64 * 1024 * 1024, size <= end, items <= 100000 else { throw MetadataError.message("Geçersiz APEv2 etiketi.") }
        try h.seek(toOffset: end - size); var body = ByteReader(h.readData(ofLength: Int(size) - 32))
        var fields: [MetadataField] = []
        for _ in 0..<items {
            guard body.remaining > 8 else { break }
            let len = Int(try body.le(4)); let flags = try body.le(4)
            var key = Data(); while body.remaining > 0 { let c = try body.u8(); if c == 0 { break }; key.append(c) }
            let value = try body.bytes(len); let name = "APE / " + String(decoding: key, as: UTF8.self)
            if flags & 6 == 0 { for part in String(decoding: value, as: UTF8.self).components(separatedBy: "\0") { fields.append(MetadataField(name, part)) } }
            else { fields.append(MetadataField(name, "İkili veri · \(numberText(len)) bayt")) }
        }
        return fields
    }
}

// MARK: - Process helper / FFmpeg discovery

public enum ProcessTool {
    public static func run(_ exe: String, _ args: [String]) async throws -> String {
        try await withCheckedThrowingContinuation { cont in
            let p = Process(); p.executableURL = URL(fileURLWithPath: exe); p.arguments = args
            let out = Pipe(), err = Pipe(); p.standardOutput = out; p.standardError = err
            p.terminationHandler = { proc in
                let o = String(decoding: out.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
                let e = String(decoding: err.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
                if proc.terminationStatus != 0 { cont.resume(throwing: MetadataError.message((exe as NSString).lastPathComponent + ": " + e.trimmingCharacters(in: .whitespacesAndNewlines))) }
                else { cont.resume(returning: o) }
            }
            do { try p.run() } catch { cont.resume(throwing: error) }
        }
    }
}

public enum FFmpegTools {
    static func locate(_ name: String) -> String? {
        var dirs: [String] = []
        if let env = ProcessInfo.processInfo.environment["FAEMYR_AUDIO_HELPERS"] { dirs.append(env) }
        if let helpers = Bundle.main.executableURL?.deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Helpers").path { dirs.append(helpers) }
        dirs += ["/opt/homebrew/bin", "/usr/local/bin"]
        return dirs.map { ($0 as NSString).appendingPathComponent(name) }.first { FileManager.default.isExecutableFile(atPath: $0) }
    }
    public static let ffprobe = locate("ffprobe")
    public static let ffmpeg = locate("ffmpeg")
}

// MARK: - CUE sheets (CueSheet.cs)

public enum CueSheet {
    struct Part { let file: String; let number: Int; let title: String; let artist: String; let cdFrames: Int64 }
    struct Document { let title: String; let artist: String; let date: String; let parts: [Part] }
    static func value(_ s: String) -> String { let t = s.trimmingCharacters(in: .whitespaces); return t.count >= 2 && t.hasPrefix("\"") && t.hasSuffix("\"") ? String(t.dropFirst().dropLast()) : t }
    static func parse(_ text: String) throws -> Document {
        var album = "", artist = "", date = "", file = "", title = "", performer = ""; var number = 0; var index: Int64?
        var parts: [Part] = []
        func finish() { if number > 0 && !file.isEmpty, let i = index { parts.append(Part(file: file, number: number, title: title.isEmpty ? "Parça \(number)" : title, artist: performer.isEmpty ? artist : performer, cdFrames: i)) }; index = nil }
        for raw in text.components(separatedBy: "\n") {
            let line = raw.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "\u{feff}"))
            guard let sp = line.firstIndex(where: { $0 == " " || $0 == "\t" }) else { continue }
            let key = line[..<sp].uppercased(), val = line[line.index(after: sp)...].trimmingCharacters(in: .whitespaces)
            switch key {
            case "FILE":
                finish(); number = 0
                if val.hasPrefix("\""), let close = val.dropFirst().firstIndex(of: "\"") { file = String(val[val.index(after: val.startIndex)..<close]) }
                else { file = String(val.split(separator: " ").first ?? "") }
                if file.isEmpty { throw MetadataError.message("CUE FILE satırı geçersiz.") }
            case "TRACK": finish(); let p = val.split(separator: " "); number = p.count == 2 && p[1].uppercased() == "AUDIO" ? Int(p[0]) ?? 0 : 0; title = ""; performer = ""
            case "TITLE": if number > 0 { title = value(val) } else { album = value(val) }
            case "PERFORMER": if number > 0 { performer = value(val) } else { artist = value(val) }
            case "REM": if val.uppercased().hasPrefix("DATE ") { date = value(String(val.dropFirst(5))) }
            case "INDEX":
                let p = val.split(separator: " ")
                if p.count == 2 && p[0] == "01" {
                    let t = p[1].split(separator: ":").compactMap { Int64($0) }
                    if t.count == 3 { if t[1] >= 60 || t[2] >= 75 { throw MetadataError.message("CUE INDEX zamanı geçersiz.") }; index = (t[0] * 60 + t[1]) * 75 + t[2] }
                }
            default: break
            }
        }
        finish()
        if parts.isEmpty { throw MetadataError.message("CUE içinde AUDIO / INDEX 01 parçaları bulunamadı.") }
        return Document(title: album, artist: artist, date: date, parts: parts)
    }
    static func decode(_ bytes: Data) -> String {
        if bytes.starts(with: [0xff, 0xfe]) { return String(data: bytes.dropFirst(2), encoding: .utf16LittleEndian) ?? "" }
        if bytes.starts(with: [0xfe, 0xff]) { return String(data: bytes.dropFirst(2), encoding: .utf16BigEndian) ?? "" }
        if let s = String(data: bytes, encoding: .utf8) { return s }
        return String(data: bytes, encoding: .windowsCP1254) ?? String(decoding: bytes, as: UTF8.self)
    }
    public static func read(_ path: String, covers: String) async throws -> [Track] {
        let bytes = try Data(contentsOf: URL(fileURLWithPath: path))
        if bytes.count > 2 * 1024 * 1024 { throw MetadataError.message("CUE dosyası çok büyük.") }
        let cue = try parse(decode(bytes))
        var sources: [String: Track] = [:]; var result: [Track] = []
        let dir = (path as NSString).deletingLastPathComponent
        for (i, part) in cue.parts.enumerated() {
            let source = ((dir as NSString).appendingPathComponent(part.file.replacingOccurrences(of: "\\", with: "/")) as NSString).standardizingPath
            let original: Track
            if let o = sources[source] { original = o } else {
                original = try await MetadataReader.read(source, covers: covers)
                if original.isDsd { throw MetadataError.message("CUE sanal parçaları PCM kaynaklarını destekler.") }
                sources[source] = original
            }
            let rate = Double(original.sampleRate)
            let start = Int64((Double(part.cdFrames) * rate / 75).rounded())
            let next = cue.parts[(i + 1)...].first { $0.file.caseInsensitiveCompare(part.file) == .orderedSame }
            let total = Int64((original.seconds * rate).rounded())
            let end = next == nil ? total : Int64((Double(next!.cdFrames) * rate / 75).rounded())
            if end <= start || end > total || start < 0 { throw MetadataError.message("CUE aralığı kaynak süresi dışında.") }
            let t = original.copy()
            t.path = path + "#track=\(part.number)"; t.sourcePath = source; t.cueSheetPath = path; t.cueStartFrame = start; t.cueEndFrame = end
            t.title = part.title; t.artist = part.artist.isEmpty ? original.artist : part.artist
            t.album = cue.title.isEmpty ? original.album : cue.title; t.albumArtist = cue.artist.isEmpty ? original.albumArtist : cue.artist
            t.number = UInt32(part.number); t.seconds = Double(end - start) / rate; t.modified = max(t.modified, Ticks.modified(path))
            if !cue.date.isEmpty { t.fields.append(MetadataField("CUE / DATE", cue.date)) }
            t.fields.append(MetadataField("CUE / Aralık", "\(start)–\(end) örnek"))
            result.append(t)
        }
        return result
    }
}
