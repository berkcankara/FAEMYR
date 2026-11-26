import Foundation
import FaemyrCore

// Command-line checks for the portable core. Usage: FaemyrSelfTest <audio-or-cue files...>
let covers = NSTemporaryDirectory() + "faemyr-selftest-covers"
var failures = 0
func check(_ ok: Bool, _ what: String) { if !ok { failures += 1; print("FAIL:", what) } else { print("ok:", what) } }

// Playback order mirrors PlaybackOrderTests on Windows.
let order = PlaybackOrder(seed: 1)
order.reset(4, 0)
check(order.next(4, 0, true) == 1, "sequential next")
order.repeat = .all
check(order.peek(4, 3, true) == 0, "repeat all wraps")
order.repeat = .one
check(order.peek(4, 2, true) == 2, "repeat one")

var err = ""
let match = TrackQuery.compile("artist:\"Tiesto\" year:2000..2010 -format:mp3", nil, &err)
check(err.isEmpty, "query compiles")
let probe = Track(); probe.artist = "Tiesto"; probe.codec = "flac"; probe.path = "/x.flac"; probe.fields = [MetadataField("Vorbis / DATE", "2004")]
check(match(probe), "query matches")
check(ArtistCatalog.credits("AC/DC").map(\.profile) == ["AC/DC"], "AC/DC kept")
check(ArtistCatalog.credits("Tiësto feat. Kirsty Hawkshaw").map(\.profile) == ["Tiësto"], "feature split")
check(ArtistCatalog.credits("Above & Beyond & Zoë Johnston").map(\.profile) == ["Above & Beyond"], "compound artist")

if CommandLine.arguments.count > 2 && CommandLine.arguments[1] == "--bio" {
    for lang in ["tr", "en"] {
        let bio = try await ArtistBiographyService.shared.get(CommandLine.arguments[2], lang)
        print(lang, "→", bio?.title ?? "nil", "| chars:", bio?.text.count ?? 0, "|", bio?.url ?? "")
        print(String((bio?.text ?? "").prefix(160)))
    }
    exit(0)
}
if CommandLine.arguments.count > 2 && CommandLine.arguments[1] == "--lyrics" {
    let tracks = try await MetadataReader.readLibraryFile(CommandLine.arguments[2], covers: covers)
    let service = LyricsService(directory: NSTemporaryDirectory() + "faemyr-selftest-lyrics")
    let doc = try await service.fetch(tracks[0])
    print(tracks[0].title, "|", tracks[0].artist, "→", doc?.source ?? "nil", "synced:", !(doc?.synced.isEmpty ?? true), "remoteId:", doc?.remoteId ?? 0)
    exit(doc?.synced.isEmpty == false ? 0 : 1)
}
if CommandLine.arguments.count > 2 && CommandLine.arguments[1] == "--play" {
    let tracks = try await MetadataReader.readLibraryFile(CommandLine.arguments[2], covers: covers)
    let engine = AudioEngine(); engine.volume = 0.05
    try engine.play(tracks[0], start: 20, preferences: AudioPreferences(), deviceUID: nil)
    for i in 0..<6 { try await Task.sleep(nanoseconds: 500_000_000); print("t=\(Double(i+1)/2) pos=\(engine.position) playing=\(engine.isPlaying) scope=\(engine.scope.written)") }
    print(engine.signal); engine.shutdown(); exit(0)
}
for path in CommandLine.arguments.dropFirst() {
    do {
        let tracks = try await MetadataReader.readLibraryFile(path, covers: covers)
        for t in tracks { print("•", t.title, "|", t.artist, "|", t.album, "|", t.quality, "|", t.duration, "|", t.bitRateLabel, "| cover:", t.cover.fileExists, "| fields:", t.fields.count) }
    } catch { failures += 1; print("FAIL:", path, error.localizedDescription) }
}
print(failures == 0 ? "ALL PASSED" : "\(failures) FAILURE(S)")
exit(failures == 0 ? 0 : 1)
