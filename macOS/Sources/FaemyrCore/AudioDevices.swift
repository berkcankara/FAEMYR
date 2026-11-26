import Foundation
import CoreAudio
import AudioToolbox

public struct AudioDevice: Hashable, Identifiable {
    public var id: String { uid }
    public let deviceID: AudioDeviceID
    public let uid: String
    public let name: String
    public let rates: [Double]
}

public enum AudioDevices {
    static func address(_ selector: AudioObjectPropertySelector, _ scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
    }
    static func string(_ id: AudioObjectID, _ selector: AudioObjectPropertySelector) -> String {
        var a = address(selector); var value: Unmanaged<CFString>?; var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        guard AudioObjectGetPropertyData(id, &a, 0, nil, &size, &value) == noErr, let v = value else { return "" }
        return v.takeRetainedValue() as String
    }
    public static func outputs() -> [AudioDevice] {
        var a = address(kAudioHardwarePropertyDevices); var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size) == noErr else { return [] }
        var ids = [AudioDeviceID](repeating: 0, count: Int(size) / MemoryLayout<AudioDeviceID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size, &ids) == noErr else { return [] }
        return ids.compactMap { id in
            var sa = address(kAudioDevicePropertyStreams, kAudioObjectPropertyScopeOutput); var ss: UInt32 = 0
            guard AudioObjectGetPropertyDataSize(id, &sa, 0, nil, &ss) == noErr, ss > 0 else { return nil }
            return AudioDevice(deviceID: id, uid: string(id, kAudioDevicePropertyDeviceUID), name: string(id, kAudioObjectPropertyName), rates: rates(id))
        }
    }
    public static func defaultOutput() -> AudioDeviceID {
        var a = address(kAudioHardwarePropertyDefaultOutputDevice); var id = AudioDeviceID(0); var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size, &id)
        return id
    }
    public static func device(uid: String?) -> AudioDeviceID {
        guard let uid, !uid.isEmpty, let d = outputs().first(where: { $0.uid == uid }) else { return defaultOutput() }
        return d.deviceID
    }
    public static func name(_ id: AudioDeviceID) -> String { string(id, kAudioObjectPropertyName) }
    public static func rates(_ id: AudioDeviceID) -> [Double] {
        var a = address(kAudioDevicePropertyAvailableNominalSampleRates); var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(id, &a, 0, nil, &size) == noErr else { return [] }
        var ranges = [AudioValueRange](repeating: AudioValueRange(), count: Int(size) / MemoryLayout<AudioValueRange>.size)
        guard AudioObjectGetPropertyData(id, &a, 0, nil, &size, &ranges) == noErr else { return [] }
        let common: [Double] = [44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000, 705600, 768000]
        return Array(Set(ranges.flatMap { r in common.filter { $0 >= r.mMinimum && $0 <= r.mMaximum } + (r.mMinimum == r.mMaximum ? [r.mMinimum] : []) })).sorted()
    }
    public static func nominalRate(_ id: AudioDeviceID) -> Double {
        var a = address(kAudioDevicePropertyNominalSampleRate); var rate: Float64 = 0; var size = UInt32(8)
        AudioObjectGetPropertyData(id, &a, 0, nil, &size, &rate); return rate
    }
    @discardableResult public static func setNominalRate(_ id: AudioDeviceID, _ rate: Double) -> Bool {
        var a = address(kAudioDevicePropertyNominalSampleRate); var r = Float64(rate)
        guard AudioObjectSetPropertyData(id, &a, 0, nil, 8, &r) == noErr else { return false }
        // Rate changes are asynchronous; wait briefly for the hardware to settle.
        for _ in 0..<40 { if abs(nominalRate(id) - rate) < 0.5 { return true }; usleep(10_000) }
        return abs(nominalRate(id) - rate) < 0.5
    }
    /// Physical format bit depth of the first output stream, used for the signal path text.
    public static func physicalBits(_ id: AudioDeviceID) -> Int {
        var a = address(kAudioDevicePropertyStreams, kAudioObjectPropertyScopeOutput); var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(id, &a, 0, nil, &size) == noErr, size > 0 else { return 0 }
        var streams = [AudioStreamID](repeating: 0, count: Int(size) / MemoryLayout<AudioStreamID>.size)
        guard AudioObjectGetPropertyData(id, &a, 0, nil, &size, &streams) == noErr, let s = streams.first else { return 0 }
        var fa = address(kAudioStreamPropertyPhysicalFormat); var asbd = AudioStreamBasicDescription(); var fs = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        guard AudioObjectGetPropertyData(s, &fa, 0, nil, &fs, &asbd) == noErr else { return 0 }
        return Int(asbd.mBitsPerChannel)
    }
    /// Hog mode gives Faemyr exclusive ownership; -1 releases it.
    @discardableResult public static func setHog(_ id: AudioDeviceID, _ take: Bool) -> Bool {
        var a = address(kAudioDevicePropertyHogMode); var pid: pid_t = take ? getpid() : -1
        guard AudioObjectSetPropertyData(id, &a, 0, nil, UInt32(MemoryLayout<pid_t>.size), &pid) == noErr else { return false }
        var owner: pid_t = 0; var size = UInt32(MemoryLayout<pid_t>.size)
        AudioObjectGetPropertyData(id, &a, 0, nil, &size, &owner)
        return take ? owner == getpid() : owner != getpid()
    }

    /// Notifies on device list / default device changes (DeviceMonitoring.cs).
    public static func observe(_ handler: @escaping () -> Void) {
        for sel in [kAudioHardwarePropertyDevices, kAudioHardwarePropertyDefaultOutputDevice] {
            var a = address(sel)
            AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &a, DispatchQueue.main) { _, _ in handler() }
        }
    }
}

/// audio-settings.json (ReplayGain.cs AudioPreferences).
public enum GainMode: Int, Codable { case off, track, album }

public struct AudioPreferences: Codable, Equatable {
    public var exclusive = true
    public var automaticOutput = true
    public var deviceId: String?
    public var replayGain: GainMode = .off
    public var preampDb: Double = 0
    public var preventClipping = true
    public var dopEnabled = false
    public var dopDevices: [String] = []
    public var pcmRateLimits: [String: Int] = [:]
    /// macOS only: take hog mode when exclusive output is on.
    public var hogMode = false
    enum CodingKeys: String, CodingKey { case exclusive = "Exclusive", automaticOutput = "AutomaticOutput", deviceId = "DeviceId", replayGain = "ReplayGain", preampDb = "PreampDb", preventClipping = "PreventClipping", dopEnabled = "DopEnabled", dopDevices = "DopDevices", pcmRateLimits = "PcmRateLimits", hogMode = "MacHogMode" }
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        exclusive = (try? c.decode(Bool.self, forKey: .exclusive)) ?? true
        automaticOutput = (try? c.decode(Bool.self, forKey: .automaticOutput)) ?? true
        deviceId = try? c.decodeIfPresent(String.self, forKey: .deviceId)
        replayGain = GainMode(rawValue: (try? c.decode(Int.self, forKey: .replayGain)) ?? 0) ?? .off
        preampDb = ((try? c.decode(Double.self, forKey: .preampDb)) ?? 0).clamped(-12, 12)
        preventClipping = (try? c.decode(Bool.self, forKey: .preventClipping)) ?? true
        dopEnabled = (try? c.decode(Bool.self, forKey: .dopEnabled)) ?? false
        dopDevices = (try? c.decode([String].self, forKey: .dopDevices)) ?? []
        pcmRateLimits = ((try? c.decode([String: Int].self, forKey: .pcmRateLimits)) ?? [:]).filter { [48000, 96000].contains($0.value) }
        hogMode = (try? c.decode(Bool.self, forKey: .hogMode)) ?? false
    }
    public func pcmRate(_ device: String, _ source: Int) -> Int { if let l = pcmRateLimits[device] { return min(source, l) }; return source }
    public static var path: String { Paths.file("audio-settings.json") }
    public static func load() -> AudioPreferences { PlayerJson.read(AudioPreferences.self, path) ?? AudioPreferences() }
    public func save() { try? PlayerJson.write(Self.path, self, pretty: true) }
}

public struct GainDecision {
    public let multiplier: Double
    public let description: String
    public var changesSamples: Bool { abs(multiplier - 1) > 1e-12 }
    public static let off = GainDecision(multiplier: 1, description: "ReplayGain kapalı · özgün örnekler")
}

public enum ReplayGain {
    static func number(_ v: String?, _ db: Bool) -> Double? {
        guard var t = v?.trimmingCharacters(in: .whitespaces) else { return nil }
        if db, let r = t.range(of: #"\s*dB\s*$"#, options: [.regularExpression, .caseInsensitive]) { t.removeSubrange(r) }
        guard let n = Double(t), n.isFinite else { return nil }; return n
    }
    public static func tags(_ t: Track, _ mode: GainMode) -> (gain: Double, peak: Double)? {
        let prefix = mode == .album ? "REPLAYGAINALBUM" : "REPLAYGAINTRACK"
        let gain = t.fields.filter { ReplayGainTag.name($0.key) == prefix + "GAIN" }.compactMap { number($0.value, true) }.first
        let peak = t.fields.filter { ReplayGainTag.name($0.key) == prefix + "PEAK" }.compactMap { number($0.value, false) }.first { $0 > 0 && $0 <= 128 }
        guard let g = gain, g >= -60, g <= 60 else { return nil }
        return (g, peak ?? 1)
    }
    public static func decide(_ t: Track, _ s: AudioPreferences) -> GainDecision {
        if s.replayGain == .off { return .off }
        guard let v = tags(t, s.replayGain) else { return GainDecision(multiplier: 1, description: "ReplayGain bilgisi yok · özgün örnekler (yerel analiz yapılabilir)") }
        var m = pow(10, (v.gain.clamped(-60, 60) + s.preampDb.clamped(-12, 12)) / 20); var limited = false
        if s.preventClipping { let cap = 1 / (v.peak > 0 ? v.peak : 1); if m > cap { m = cap; limited = true } }
        let mode = s.replayGain == .album ? "Albüm" : "Parça"
        let db = String(format: "%+.2f", 20 * log10(m))
        return GainDecision(multiplier: m, description: "ReplayGain \(mode) · \(db) dB · etiket" + (limited ? " · tepeye göre kazanç sınırlandı" : "") + (abs(m - 1) > 1e-12 ? " · sabit kazanç, bit-perfect değil" : " · özgün örnekler"))
    }
}
