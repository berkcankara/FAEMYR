import Foundation
import AVFoundation
import AudioToolbox
import CoreAudio
import os

/// Lock-protected ring of the most recent output frames (stereo, Float32) for the
/// oscilloscope / spectrum / VU / spectrogram views. The render thread never blocks:
/// it skips a block when the UI holds the lock.
/// Ring buffer filled on the real-time render thread and read by the visualizer on the main thread.
/// Storage and lock live in fixed heap memory: Swift arrays (copy-on-write, uniqueness checks) and an
/// `os_unfair_lock` stored property (whose address is not guaranteed stable) are unsafe to share between threads.
public final class ScopeBuffer: @unchecked Sendable {
    public let capacity = 1 << 15
    private let left: UnsafeMutablePointer<Float>, right: UnsafeMutablePointer<Float>
    private let lock: UnsafeMutablePointer<os_unfair_lock>
    private var write = 0
    private var rate: Double = 48000
    private var total = 0
    public init() {
        left = .allocate(capacity: 1 << 15); left.initialize(repeating: 0, count: 1 << 15)
        right = .allocate(capacity: 1 << 15); right.initialize(repeating: 0, count: 1 << 15)
        lock = .allocate(capacity: 1); lock.initialize(to: os_unfair_lock())
    }
    deinit { left.deallocate(); right.deallocate(); lock.deinitialize(count: 1); lock.deallocate() }

    public var sampleRate: Double { os_unfair_lock_lock(lock); defer { os_unfair_lock_unlock(lock) }; return rate }
    public var written: Int { os_unfair_lock_lock(lock); defer { os_unfair_lock_unlock(lock) }; return total }

    /// Real-time safe: never blocks (drops the block if the reader holds the lock) and never allocates.
    func push(_ l: UnsafePointer<Float>, _ r: UnsafePointer<Float>, _ count: Int, rate newRate: Double) {
        guard os_unfair_lock_trylock(lock) else { return }
        rate = newRate
        for i in 0..<count { left[write] = l[i]; right[write] = r[i]; write = (write + 1) & (capacity - 1) }
        total &+= count
        os_unfair_lock_unlock(lock)
    }
    public func reset() {
        os_unfair_lock_lock(lock); left.update(repeating: 0, count: capacity); right.update(repeating: 0, count: capacity); total = 0; os_unfair_lock_unlock(lock)
    }
    /// Copies the newest `count` frames (oldest first).
    public func latest(_ count: Int) -> (left: [Float], right: [Float], rate: Double, total: Int) {
        let n = min(count, capacity)
        var l = [Float](repeating: 0, count: n), r = l
        os_unfair_lock_lock(lock)
        var p = (write - n + capacity) & (capacity - 1)
        for i in 0..<n { l[i] = left[p]; r[i] = right[p]; p = (p + 1) & (capacity - 1) }
        let sr = rate, t = total
        os_unfair_lock_unlock(lock)
        return (l, r, sr, t)
    }
}

public final class AudioEngine: @unchecked Sendable {
    struct Segment {
        let track: Track
        let file: AVAudioFile
        let startFrame: AVAudioFramePosition
        let frameCount: AVAudioFrameCount
        let timelineStart: AVAudioFramePosition
        let offsetSeconds: Double      // track position at the start of the segment
        let gain: GainDecision
    }

    public let scope = ScopeBuffer()
    public var onEnded: (() -> Void)?
    public var onAdvanced: ((Track) -> Void)?
    public var onFailed: ((String) -> Void)?

    public private(set) var isPlaying = false
    public private(set) var isPaused = false
    public private(set) var signal = ""
    public private(set) var currentTrack: Track?
    public var volume: Float = 1 { didSet { applyVolume() } }
    public var muted = false { didSet { applyVolume() } }

    private let engine = AVAudioEngine()
    private let player = AVAudioPlayerNode()
    private let gainUnit = AVAudioUnitEQ(numberOfBands: 0)
    private var connectedFormat: AVAudioFormat?
    private var segments: [Segment] = []
    private var generation = 0
    private var nextTimeline: AVAudioFramePosition = 0
    private var currentDevice: AudioDeviceID = 0
    private var restoreRate: (AudioDeviceID, Double)?
    private var hogged: AudioDeviceID?
    private var notifyInstalled = false
    private var pausedPosition: Double = 0
    private var transport = ""
    private var preferences = AudioPreferences()
    private var deviceUID: String?
    public private(set) var lastFailurePosition: Double = 0

    public init() {
        engine.attach(player); engine.attach(gainUnit)
        NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange, object: engine, queue: .main) { [weak self] _ in self?.configurationChanged() }
    }

    // MARK: Position

    public var duration: Double { currentTrack?.seconds ?? 0 }
    public var position: Double {
        get {
            guard let seg = segments.first else { return pausedPosition }
            if !isPlaying { return pausedPosition }
            guard let node = player.lastRenderTime, let pt = player.playerTime(forNodeTime: node) else { return seg.offsetSeconds }
            let rate = seg.file.processingFormat.sampleRate
            let elapsed = Double(pt.sampleTime - seg.timelineStart) / rate
            return (seg.offsetSeconds + max(0, elapsed)).clamped(0, max(0, seg.track.seconds))
        }
        set { seek(newValue) }
    }

    // MARK: Playback

    public func play(_ track: Track, start: Double = 0, paused: Bool = false, preferences: AudioPreferences, deviceUID: String?) throws {
        stopPlayer(keepTrack: false)
        self.preferences = preferences; self.deviceUID = deviceUID
        let url = try playableURL(track)
        let file = try AVAudioFile(forReading: url)
        let gain = ReplayGain.decide(track, preferences)
        try configure(for: file, track: track)
        currentTrack = track
        nextTimeline = 0
        try schedule(track, file: file, from: start, gain: gain)
        applyVolume()
        if !engine.isRunning { try engine.start() }
        refreshOutputRate()
        if paused { isPlaying = false; isPaused = true; pausedPosition = start }
        else { player.play(); isPlaying = true; isPaused = false }
        updateSignal(track, file: file, gain: gain)
    }

    /// Queues the following track for a sample-accurate gapless transition when its
    /// processing format and gain match the current one (GaplessPlayback.cs rules).
    @discardableResult public func preloadNext(_ track: Track?) -> Bool {
        guard let track, let current = segments.last, segments.count == 1, !track.isDsd else { return false }
        guard let url = try? playableURL(track, decodeIfNeeded: false), let file = try? AVAudioFile(forReading: url) else { return false }
        let a = file.processingFormat, b = current.file.processingFormat
        guard a.sampleRate == b.sampleRate && a.channelCount == b.channelCount else { return false }
        let gain = ReplayGain.decide(track, preferences)
        guard abs(gain.multiplier - current.gain.multiplier) < 1e-9 else { return false }
        return (try? schedule(track, file: file, from: 0, gain: gain)) != nil
    }
    public var hasPreloaded: Bool { segments.count > 1 }
    public func cancelPreload() {
        guard segments.count > 1 else { return }
        let pos = position, playing = isPlaying, track = currentTrack
        if let track { try? play(track, start: pos, paused: !playing, preferences: preferences, deviceUID: deviceUID) }
    }

    public func toggle() { isPlaying ? pause() : resume() }
    public func pause() {
        guard isPlaying else { return }
        pausedPosition = position; player.pause(); isPlaying = false; isPaused = true
    }
    public func resume() {
        guard isPaused, let track = currentTrack else { return }
        if !engine.isRunning { try? engine.start() }
        refreshOutputRate()
        if segments.isEmpty { try? play(track, start: pausedPosition, preferences: preferences, deviceUID: deviceUID); return }
        player.play(); isPlaying = true; isPaused = false
    }
    public func stop() { stopPlayer(keepTrack: false); releaseDevice() }

    public func seek(_ seconds: Double) {
        guard let track = currentTrack, let seg = segments.first else { pausedPosition = seconds; return }
        let wasPlaying = isPlaying
        let target = seconds.clamped(0, max(0, track.seconds - 0.01))
        let file = seg.file, gain = seg.gain
        generation += 1; player.stop(); segments.removeAll(); nextTimeline = 0
        try? schedule(track, file: file, from: target, gain: gain)
        pausedPosition = target
        if wasPlaying { player.play() }
    }

    private func stopPlayer(keepTrack: Bool) {
        generation += 1
        player.stop(); segments.removeAll(); isPlaying = false; isPaused = false; nextTimeline = 0
        if !keepTrack { currentTrack = nil }
        scope.reset()
    }

    private func schedule(_ track: Track, file: AVAudioFile, from seconds: Double, gain: GainDecision) throws {
        let rate = file.processingFormat.sampleRate
        var first: AVAudioFramePosition = 0
        var last = file.length
        if track.isCue {
            // CUE frames are stored at the source sample rate.
            let scale = rate / Double(max(1, track.sampleRate))
            first = AVAudioFramePosition(Double(track.cueStartFrame) * scale)
            if let e = track.cueEndFrame { last = min(file.length, AVAudioFramePosition(Double(e) * scale)) }
        }
        let start = min(last, first + AVAudioFramePosition(max(0, seconds) * rate))
        let count = AVAudioFrameCount(max(0, last - start))
        guard count > 0 else { throw MetadataError.message("Parça aralığı boş.") }
        let seg = Segment(track: track, file: file, startFrame: start, frameCount: count, timelineStart: nextTimeline, offsetSeconds: seconds, gain: gain)
        segments.append(seg); nextTimeline += AVAudioFramePosition(count)
        let gen = generation
        player.scheduleSegment(file, startingFrame: start, frameCount: count, at: nil, completionCallbackType: .dataPlayedBack) { [weak self] _ in
            DispatchQueue.main.async { self?.segmentFinished(gen) }
        }
    }

    private func segmentFinished(_ gen: Int) {
        guard gen == generation, !segments.isEmpty else { return }
        segments.removeFirst()
        if let next = segments.first {
            currentTrack = next.track
            updateSignal(next.track, file: next.file, gain: next.gain)
            onAdvanced?(next.track)
        } else {
            isPlaying = false; isPaused = false; pausedPosition = 0
            onEnded?()
        }
    }

    private func applyVolume() {
        player.volume = muted ? 0 : volume
        let m = segments.first?.gain.multiplier ?? 1
        gainUnit.globalGain = Float(20 * log10(max(1e-6, m)))
        gainUnit.bypass = abs(m - 1) < 1e-12
    }

    // MARK: Output configuration

    private func configure(for file: AVAudioFile, track: Track) throws {
        let device = AudioDevices.device(uid: deviceUID)
        let format = file.processingFormat
        let needsReconnect = connectedFormat == nil || connectedFormat!.sampleRate != format.sampleRate || connectedFormat!.channelCount != format.channelCount
        let sourceRate = format.sampleRate
        var wantRate: Double?
        if preferences.automaticOutput || preferences.exclusive {
            let uid = deviceUID ?? ""
            let limited = Double(preferences.pcmRate(uid, Int(sourceRate)))
            let rates = AudioDevices.rates(device)
            if rates.contains(limited) { wantRate = limited }
            else if let best = rates.filter({ $0 >= limited }).first ?? rates.last { wantRate = best }
        }
        let deviceChanged = device != currentDevice
        let rateChange = wantRate.map { abs(AudioDevices.nominalRate(device) - $0) > 0.5 } ?? false
        if needsReconnect || deviceChanged || rateChange || !engine.isRunning {
            engine.stop()
            if deviceChanged { releaseDevice() }
            if let out = engine.outputNode.audioUnit {
                var id = device
                AudioUnitSetProperty(out, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &id, UInt32(MemoryLayout<AudioDeviceID>.size))
            }
            currentDevice = device
            if let wantRate, rateChange {
                if restoreRate == nil || restoreRate!.0 != device { restoreRate = (device, AudioDevices.nominalRate(device)) }
                AudioDevices.setNominalRate(device, wantRate)
            }
            if preferences.exclusive && preferences.hogMode && hogged != device { if AudioDevices.setHog(device, true) { hogged = device } }
            engine.disconnectNodeOutput(player); engine.disconnectNodeOutput(gainUnit)
            engine.connect(player, to: gainUnit, format: format)
            engine.connect(gainUnit, to: engine.mainMixerNode, format: format)
            let outFormat = engine.outputNode.outputFormat(forBus: 0)
            engine.connect(engine.mainMixerNode, to: engine.outputNode, format: outFormat.sampleRate > 0 ? outFormat : nil)
            connectedFormat = format
            installScopeNotify()
            engine.prepare()
        }
        let outRate = AudioDevices.nominalRate(device)
        let bits = AudioDevices.physicalBits(device)
        let resampled = abs(outRate - sourceRate) > 0.5
        let rateText = trimmedNumber(outRate / 1000, 3)
        transport = "Core Audio " + (preferences.exclusive ? (hogged == device ? "Özel (Hog Mode)" : "Uyumlu çıkış") : "Paylaşımlı") + " · \(bits > 0 ? "\(bits) bit" : "Float32") / macOS: \(rateText) kHz · "
            + (resampled ? "macOS biçimine dönüşüm · bit-perfect değil" : "yeniden örnekleme yok")
    }

    private func releaseDevice() {
        if let h = hogged { AudioDevices.setHog(h, false); hogged = nil }
        if let (dev, rate) = restoreRate { AudioDevices.setNominalRate(dev, rate); restoreRate = nil }
    }
    public func shutdown() { stop(); engine.stop(); releaseDevice() }

    private func installScopeNotify() {
        guard !notifyInstalled, let unit = engine.outputNode.audioUnit else { return }
        notifyInstalled = true
        let ctx = Unmanaged.passUnretained(self).toOpaque()
        AudioUnitAddRenderNotify(unit, { ref, flags, _, _, frames, data in
            guard flags.pointee.contains(.unitRenderAction_PostRender), let data else { return noErr }
            let me = Unmanaged<AudioEngine>.fromOpaque(ref).takeUnretainedValue()
            let abl = UnsafeMutableAudioBufferListPointer(data)
            guard abl.count > 0, let l = abl[0].mData?.assumingMemoryBound(to: Float.self) else { return noErr }
            let n = Int(frames)
            if abl.count >= 2, let r = abl[1].mData?.assumingMemoryBound(to: Float.self) {
                me.scope.push(l, r, n, rate: me.outputRate)
            } else if abl[0].mNumberChannels >= 2 {
                // Interleaved: deinterleave into a small stack buffer.
                let ch = Int(abl[0].mNumberChannels)
                withUnsafeTemporaryAllocation(of: Float.self, capacity: n * 2) { buf in
                    for i in 0..<n { buf[i] = l[i * ch]; buf[n + i] = l[i * ch + 1] }
                    me.scope.push(buf.baseAddress!, buf.baseAddress! + n, n, rate: me.outputRate)
                }
            } else { me.scope.push(l, l, n, rate: me.outputRate) }
            return noErr
        }, ctx)
    }
    /// Written on the main thread whenever the engine (re)starts; the render callback only reads this Double.
    private var outputRate: Double = 48000
    private func refreshOutputRate() { let r = engine.outputNode.outputFormat(forBus: 0).sampleRate; if r > 0 { outputRate = r } }

    private func configurationChanged() {
        // Device unplugged or format changed underneath us: rebuild at the same position.
        guard let track = currentTrack else { return }
        let pos = position, playing = isPlaying
        connectedFormat = nil; currentDevice = 0
        do { try play(track, start: pos, paused: !playing, preferences: preferences, deviceUID: deviceUID) }
        catch { lastFailurePosition = pos; signal = "Ses çıkışı durdu: " + error.localizedDescription; onFailed?(signal) }
    }

    private func updateSignal(_ t: Track, file: AVAudioFile, gain: GainDecision) {
        let devName = AudioDevices.name(currentDevice)
        let bits = t.bits > 0 ? "\(t.bits) bit" : tr("kodlanmış ses")
        var text = "Kaynak: \(bits) / \(trimmedNumber(Double(t.sampleRate) / 1000, 3)) kHz / \(t.channels) kanal → \(devName)\n" + transport + "\n" + gain.description
        if decodedWithFFmpeg.contains(t.audioPath) { text += "\nAkış decoder: FFmpeg → Float32 PCM" }
        if !t.wavPackMode.isEmpty { text += "\nWavPack: " + t.wavPackMode }
        signal = text
    }

    // MARK: Decoding fallbacks

    private var decodedWithFFmpeg = Set<String>()
    public static var cacheDirectory: String { Paths.file("cache") }

    func playableURL(_ t: Track, decodeIfNeeded: Bool = true) throws -> URL {
        let (url, ffmpeg) = try AudioDecoding.playableURL(t, decodeIfNeeded: decodeIfNeeded)
        if ffmpeg { decodedWithFFmpeg.insert(t.audioPath) }
        return url
    }

    /// Keeps the decode cache bounded (AudioEngine.TrimCache).
    public static func trimCache(keep: String? = nil, limit: Int64 = 2 * 1024 * 1024 * 1024) {
        let dir = cacheDirectory
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: dir) else { return }
        var files = names.map { (dir as NSString).appendingPathComponent($0) }.compactMap { p -> (String, Date, Int64)? in
            guard let a = try? FileManager.default.attributesOfItem(atPath: p) else { return nil }
            return (p, a[.modificationDate] as? Date ?? .distantPast, a[.size] as? Int64 ?? 0)
        }.sorted { $0.1 > $1.1 }
        var total = files.reduce(0) { $0 + $1.2 }
        while total > limit, let last = files.popLast() { if last.0 != keep { try? FileManager.default.removeItem(atPath: last.0); total -= last.2 } }
    }
}

public enum AudioDecoding {
    /// Returns a URL AVAudioFile can read: the original file, or an FFmpeg-decoded
    /// Float32 CAF in the cache for formats macOS cannot decode (DSD, Vorbis, APE, WavPack…).
    public static func playableURL(_ t: Track, decodeIfNeeded: Bool = true) throws -> (URL, Bool) {
        let path = t.audioPath
        guard path.fileExists else { throw MetadataError.message(tr("Dosya bulunamadı: ") + path) }
        if !t.isDsd, (try? AVAudioFile(forReading: URL(fileURLWithPath: path))) != nil { return (URL(fileURLWithPath: path), false) }
        guard decodeIfNeeded else { throw MetadataError.message("decode") }
        guard let ffmpeg = FFmpegTools.ffmpeg else {
            throw MetadataError.message(t.isDsd ? tr("DSD oynatmak için FFmpeg gerekli (brew install ffmpeg).") : tr("Bu biçim için macOS çözücüsü yok. FFmpeg yüklendiğinde (örneğin Homebrew: brew install ffmpeg) bu dosyalar da okunur."))
        }
        try FileManager.default.createDirectory(atPath: AudioEngine.cacheDirectory, withIntermediateDirectories: true)
        let out = (AudioEngine.cacheDirectory as NSString).appendingPathComponent(Paths.hash(path + String(t.modified)) + ".caf")
        if !out.fileExists {
            let sem = DispatchSemaphore(value: 0)
            final class Box: @unchecked Sendable { var error: Error? }
            let box = Box()
            let rate = t.isDsd ? ["-ar", String(min(352800, max(88200, t.dsdRate / 16)))] : []
            let part = out + "." + UUID().uuidString + ".part"
            Task.detached {
                do { _ = try await ProcessTool.run(ffmpeg, ["-v", "error", "-y", "-i", path, "-vn"] + rate + ["-c:a", "pcm_f32le", "-f", "caf", part]) } catch { box.error = error }
                sem.signal()
            }
            sem.wait()
            if let e = box.error { try? FileManager.default.removeItem(atPath: part); throw e }
            try? FileManager.default.moveItem(atPath: part, toPath: out)
        }
        return (URL(fileURLWithPath: out), true)
    }
}
