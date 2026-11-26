import Foundation
import AVFoundation

/// waveforms/*.json — identical layout to WaveformData on Windows (192 bins, 3 bands).
public struct WaveformData: Codable, Equatable {
    public var version = 1
    public var low: [Float] = []
    public var mid: [Float] = []
    public var high: [Float] = []
    enum CodingKeys: String, CodingKey { case version = "Version", low = "Low", mid = "Mid", high = "High" }
    public var valid: Bool { version == 1 && [low, mid, high].allSatisfy { $0.count == 192 && $0.allSatisfy { $0 >= 0 && $0 <= 1 } } }
}

public enum WaveformAnalyzer {
    static let bins = 192
    public static var root: String { Paths.file("waveforms") }

    public static func cachePath(_ t: Track) -> String {
        let fm = FileManager.default
        let a = (try? fm.attributesOfItem(atPath: t.audioPath)) ?? [:]
        let len = a[.size] as? Int64 ?? 0, ticks = Ticks.from(a[.modificationDate] as? Date ?? .distantPast)
        let corr = (t.audioPath as NSString).deletingPathExtension + ".wvc"
        let extra = (try? fm.attributesOfItem(atPath: corr)).map { "\($0[.size] as? Int64 ?? 0)|\(Ticks.from($0[.modificationDate] as? Date ?? .distantPast))" } ?? "none"
        let cue = t.isCue ? "\(t.cueStartFrame)|\(t.cueEndFrame.map(String.init) ?? "")|" : ""
        return (root as NSString).appendingPathComponent(Paths.hash("wave-v1|" + t.path + "|" + cue + "\(len)|\(ticks)|" + extra) + ".json")
    }

    public static func get(_ t: Track) throws -> WaveformData {
        let path = cachePath(t)
        if let saved = PlayerJson.read(WaveformData.self, path), saved.valid { return saved }
        let data = try analyze(t)
        try? PlayerJson.write(path, data)
        return data
    }

    /// Same one-pole band split as Windows (180 Hz / 2.6 kHz), RMS per ~42.7 ms window.
    public static func analyze(_ t: Track) throws -> WaveformData {
        let (url, _) = try AudioDecoding.playableURL(t)
        let file = try AVAudioFile(forReading: url)
        let format = file.processingFormat
        let rate = format.sampleRate, channels = Int(format.channelCount)
        var startFrame: AVAudioFramePosition = 0, endFrame = file.length
        if t.isCue {
            let scale = rate / Double(max(1, t.sampleRate))
            startFrame = AVAudioFramePosition(Double(t.cueStartFrame) * scale)
            if let e = t.cueEndFrame { endFrame = min(file.length, AVAudioFramePosition(Double(e) * scale)) }
        }
        file.framePosition = startFrame
        let window = max(1, Int((512.0 * rate / 12000).rounded()))
        let lowAlpha = 1 - exp(-2 * Double.pi * 180 / rate), upperAlpha = 1 - exp(-2 * Double.pi * 2600 / rate)
        var lowState = [Double](repeating: 0, count: channels), upperState = lowState
        var lowE = 0.0, midE = 0.0, highE = 0.0, count = 0
        var windows: [(Float, Float, Float)] = []
        func flush() {
            guard count > 0 else { return }
            let n = Double(count * channels)
            windows.append((Float(sqrt(lowE / n)), Float(sqrt(midE / n)), Float(sqrt(highE / n)))); count = 0; lowE = 0; midE = 0; highE = 0
        }
        let chunk: AVAudioFrameCount = 65536
        guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: chunk) else { throw MetadataError.message("Tampon ayrılamadı.") }
        var remaining = endFrame - startFrame
        while remaining > 0 {
            try Task.checkCancellation()
            try file.read(into: buffer, frameCount: AVAudioFrameCount(min(Int64(chunk), remaining)))
            let n = Int(buffer.frameLength); if n == 0 { break }
            remaining -= Int64(n)
            guard let data = buffer.floatChannelData else { break }
            for i in 0..<n {
                for c in 0..<channels {
                    var s = Double(data[c][i]); if !s.isFinite { s = 0 }
                    lowState[c] += lowAlpha * (s - lowState[c]); upperState[c] += upperAlpha * (s - upperState[c])
                    let m = upperState[c] - lowState[c], h = s - upperState[c]
                    lowE += lowState[c] * lowState[c]; midE += m * m; highE += h * h
                }
                count += 1; if count == window { flush() }
            }
        }
        flush()
        if windows.isEmpty { throw MetadataError.message("Ses örneği bulunamadı.") }
        var r = WaveformData(); r.low = [Float](repeating: 0, count: bins); r.mid = r.low; r.high = r.low
        for i in 0..<bins {
            let s = i * windows.count / bins, e = max(s + 1, (i + 1) * windows.count / bins)
            for j in s..<min(e, windows.count) { r.low[i] = max(r.low[i], windows[j].0); r.mid[i] = max(r.mid[i], windows[j].1); r.high[i] = max(r.high[i], windows[j].2) }
        }
        let peak = (0..<bins).map { r.low[$0] + r.mid[$0] + r.high[$0] }.max() ?? 0
        if peak > 0.000001 { for i in 0..<bins { r.low[i] /= peak; r.mid[i] /= peak; r.high[i] /= peak } }
        return r
    }
}

/// Serial background queue with urgent items first (WaveformQueue).
public final class WaveformQueue: @unchecked Sendable {
    private let lock = NSLock()
    private var urgent: [Track] = [], normal: [Track] = []
    private var queued = Set<String>()
    private var running = false
    private let completed: (Track, WaveformData?, String) -> Void
    public init(completed: @escaping (Track, WaveformData?, String) -> Void) { self.completed = completed }
    public func enqueue(_ t: Track, urgent u: Bool = false) {
        lock.lock(); defer { lock.unlock() }
        if queued.contains(t.path) {
            if u, let i = normal.firstIndex(where: { $0 === t }) { normal.remove(at: i); urgent.append(t) }
            return
        }
        queued.insert(t.path); if u { urgent.append(t) } else { normal.append(t) }
        if !running { running = true; DispatchQueue.global(qos: .utility).async { self.work() } }
    }
    private func work() {
        while true {
            lock.lock()
            let next = urgent.isEmpty ? (normal.isEmpty ? nil : normal.removeFirst()) : urgent.removeFirst()
            if next == nil { running = false; lock.unlock(); return }
            lock.unlock()
            let t = next!
            var data: WaveformData?; var message = ""
            do { data = try WaveformAnalyzer.get(t) } catch { message = "Oluşturulamadı: " + error.localizedDescription }
            lock.lock(); queued.remove(t.path); lock.unlock()
            DispatchQueue.main.async { self.completed(t, data, message) }
        }
    }
}
