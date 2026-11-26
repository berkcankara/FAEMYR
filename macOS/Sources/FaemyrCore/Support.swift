import Foundation
import CryptoKit

/// Mirrors `Paths` in the Windows source. Data lives in Application Support on macOS;
/// FAEMYR_DATA_DIRECTORY / LYRA_DATA_DIRECTORY isolate it, exactly as on Windows.
public enum Paths {
    public static let appName = "Faemyr"
    public static var data: String {
        let env = ProcessInfo.processInfo.environment
        if let isolated = env["FAEMYR_DATA_DIRECTORY"] ?? env["LYRA_DATA_DIRECTORY"], !isolated.isEmpty {
            return (isolated as NSString).standardizingPath
        }
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        return base.appendingPathComponent(appName).path
    }
    public static func file(_ name: String) -> String { (data as NSString).appendingPathComponent(name) }
    public static func hash(_ value: String) -> String {
        SHA256.hash(data: Data(value.utf8)).map { String(format: "%02X", $0) }.joined()
    }
    public static var resources: Bundle { Bundle.module }
    public static func resource(_ name: String, _ ext: String, subdirectory: String? = nil) -> URL? {
        let sub = subdirectory.map { "Resources/" + $0 } ?? "Resources"
        return Bundle.module.url(forResource: name, withExtension: ext, subdirectory: sub)
    }
}

/// Offline switches used by the Windows tests (FAEMYR_LYRICS_OFFLINE=1 etc.).
public enum AppBrand {
    public static let name = "Faemyr"
    public static var version: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.10.53" }
    public static func offline(_ feature: String) -> Bool {
        let env = ProcessInfo.processInfo.environment
        return (env["FAEMYR_\(feature)_OFFLINE"] ?? env["LYRA_\(feature)_OFFLINE"]) == "1"
    }
}

/// .NET DateTime ticks so library.db rows stay interchangeable with the Windows build.
public enum Ticks {
    static let unixOffset: Int64 = 621_355_968_000_000_000
    public static func from(_ date: Date) -> Int64 { Int64(date.timeIntervalSince1970 * 10_000_000) + unixOffset }
    public static func date(_ ticks: Int64) -> Date { Date(timeIntervalSince1970: Double(ticks - unixOffset) / 10_000_000) }
    public static func modified(_ path: String) -> Int64 {
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: path), let date = attrs[.modificationDate] as? Date else { return 0 }
        return from(date)
    }
}

public enum PlayerJson {
    public static let encoder: JSONEncoder = { let e = JSONEncoder(); e.dateEncodingStrategy = .custom(encodeDate); return e }()
    public static let pretty: JSONEncoder = { let e = JSONEncoder(); e.outputFormatting = [.prettyPrinted]; e.dateEncodingStrategy = .custom(encodeDate); return e }()
    public static let decoder: JSONDecoder = { let d = JSONDecoder(); d.dateDecodingStrategy = .custom(decodeDate); return d }()

    static let isoFractional: ISO8601DateFormatter = { let f = ISO8601DateFormatter(); f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]; return f }()
    static let iso: ISO8601DateFormatter = { let f = ISO8601DateFormatter(); f.formatOptions = [.withInternetDateTime]; return f }()
    static func encodeDate(_ date: Date, _ encoder: Encoder) throws {
        var c = encoder.singleValueContainer(); try c.encode(isoFractional.string(from: date))
    }
    static func decodeDate(_ decoder: Decoder) throws -> Date {
        let c = try decoder.singleValueContainer()
        if let number = try? c.decode(Double.self) { return Date(timeIntervalSince1970: number) }
        var text = try c.decode(String.self)
        // .NET writes up to 7 fractional digits; ISO8601DateFormatter accepts at most 3.
        if let dot = text.firstIndex(of: "."), let end = text[dot...].firstIndex(where: { $0 == "Z" || $0 == "+" || $0 == "-" }) {
            let digits = text[text.index(after: dot)..<end]
            if digits.count > 3 { text.replaceSubrange(text.index(after: dot)..<end, with: digits.prefix(3)) }
        }
        if let d = isoFractional.date(from: text) ?? iso.date(from: text) { return d }
        if !text.contains("Z") && !text.contains("+"), let d = iso.date(from: text + "Z") { return d }
        throw DecodingError.dataCorruptedError(in: c, debugDescription: "Tarih okunamadı: \(text)")
    }

    public static func read<T: Decodable>(_ type: T.Type, _ path: String) -> T? {
        guard let data = FileManager.default.contents(atPath: path) else { return nil }
        return try? decoder.decode(T.self, from: data)
    }
    public static func write<T: Encodable>(_ path: String, _ value: T, pretty: Bool = false) throws {
        let data = try (pretty ? self.pretty : encoder).encode(value)
        try atomicWrite(path, data)
    }
    public static func atomicWrite(_ path: String, _ data: Data) throws {
        let dir = (path as NSString).deletingLastPathComponent
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        let temp = path + "." + UUID().uuidString.replacingOccurrences(of: "-", with: "") + ".tmp"
        defer { try? FileManager.default.removeItem(atPath: temp) }
        try data.write(to: URL(fileURLWithPath: temp))
        if FileManager.default.fileExists(atPath: path) {
            _ = try FileManager.default.replaceItemAt(URL(fileURLWithPath: path), withItemAt: URL(fileURLWithPath: temp))
        } else {
            try FileManager.default.moveItem(atPath: temp, toPath: path)
        }
    }
}

public extension String {
    /// Case-insensitive comparison matching StringComparer.CurrentCultureIgnoreCase.
    func ciCompare(_ other: String) -> ComparisonResult {
        compare(other, options: [.caseInsensitive], range: nil, locale: L.locale)
    }
    func ciContains(_ other: String) -> Bool {
        range(of: other, options: [.caseInsensitive, .diacriticInsensitive], range: nil, locale: L.locale) != nil
    }
    var fileExists: Bool { !isEmpty && FileManager.default.fileExists(atPath: self) }
    var lastPathComponentNoExt: String { ((self as NSString).lastPathComponent as NSString).deletingPathExtension }
    var pathExtLower: String { (self as NSString).pathExtension.lowercased() }
}

public extension Comparable { func clamped(_ lo: Self, _ hi: Self) -> Self { min(max(self, lo), hi) } }

public func formatTime(_ seconds: Double) -> String {
    let s = max(0, seconds.isFinite ? seconds : 0)
    let total = Int(s)
    let h = total / 3600, m = (total % 3600) / 60, sec = total % 60
    return h > 0 ? String(format: "%d:%02d:%02d", h, m, sec) : String(format: "%d:%02d", m, sec)
}

public func numberText(_ value: Int) -> String {
    let f = NumberFormatter(); f.numberStyle = .decimal; f.locale = L.locale
    return f.string(from: NSNumber(value: value)) ?? "\(value)"
}
