import Foundation

/// Port of `L` in Localization.cs. The source strings are Turkish; en.json is the
/// same translation table the Windows build embeds.
public enum L {
    public private(set) static var current = "tr"
    /// en.json is copied from the Windows build on every macOS build; en-macos.json holds strings that
    /// only the macOS port uses and is merged on top.
    private static let english: [String: String] = {
        func load(_ name: String) -> [String: String] {
            guard let url = Paths.resource(name, "json"), let data = try? Data(contentsOf: url),
                  let table = try? JSONDecoder().decode([String: String].self, from: data) else { return [:] }
            return table
        }
        return load("en").merging(load("en-macos")) { _, mac in mac }
    }()
    private static let signalPairs: [(String, String)] = english.filter { $0.key.count > 3 }.sorted { $0.key.count > $1.key.count }.map { ($0.key, $0.value) }
    public static var locale: Locale { Locale(identifier: current == "en" ? "en_US" : "tr_TR") }

    public static func t(_ text: String) -> String { current == "en" ? (english[text] ?? text) : text }

    /// Translates a composed sentence by replacing every known literal fragment,
    /// which mirrors the interpolated-string handler used on Windows.
    public static func f(_ parts: Any...) -> String {
        parts.map { part in
            if let s = part as? LiteralPart { return t(s.text) }
            return "\(part)"
        }.joined()
    }
    public struct LiteralPart { public let text: String; public init(_ t: String) { text = t } }

    public static var languagePath: String { Paths.file("language.json") }
    public static func readLanguage() -> String { PlayerJson.read(String.self, languagePath) == "en" ? "en" : "tr" }
    public static func save(_ id: String) { try? PlayerJson.write(languagePath, id == "en" ? "en" : "tr") }
    public static func load() { apply(readLanguage()) }
    public static func apply(_ id: String) { current = id == "en" ? "en" : "tr" }

    public static func signal(_ text: String) -> String {
        guard current == "en" else { return text }
        var result = text
        for (k, v) in signalPairs where result.contains(k) { result = result.replacingOccurrences(of: k, with: v) }
        return result
    }
}

/// `L.T($"...{x}...")` on Windows translates the literal pieces. In Swift we write
/// `tr("{0} parça", n)` with numbered placeholders, matching en.json keys that
/// contain `{0}` style holes when present; otherwise literal segments are translated.
public func tr(_ text: String) -> String { L.t(text) }

/// Translates `template` where `{}` marks values. The literal segments are looked up
/// individually, the way LocalizedStringHandler.AppendLiteral does.
public func trf(_ template: String, _ values: CustomStringConvertible...) -> String {
    let pieces = template.components(separatedBy: "{}")
    var out = ""
    for (i, piece) in pieces.enumerated() {
        out += piece.isEmpty ? "" : L.t(piece)
        if i < values.count && i < pieces.count - 1 { out += values[i].description }
    }
    return out
}
