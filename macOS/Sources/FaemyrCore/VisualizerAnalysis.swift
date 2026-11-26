import Foundation
import Accelerate

public enum VisualizerMode: Int, Codable, CaseIterable { case oscilloscope, spectrum, vuMeter, spectrogram
    public var title: String {
        switch self { case .oscilloscope: return tr("Osiloskop"); case .spectrum: return "Spectrum"; case .vuMeter: return tr("VU metre"); case .spectrogram: return tr("Spektrogram") }
    }
}

/// visualizer.json uses the enum name, as JsonStringEnumConverter writes it on Windows.
public struct VisualizerPreferences: Codable {
    public var mode: VisualizerMode = .oscilloscope
    enum CodingKeys: String, CodingKey { case mode = "Mode" }
    static let names = ["Oscilloscope", "Spectrum", "VuMeter", "Spectrogram"]
    public init() {}
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        if let s = try? c.decode(String.self, forKey: .mode), let i = Self.names.firstIndex(of: s) { mode = VisualizerMode(rawValue: i)! }
        else if let i = try? c.decode(Int.self, forKey: .mode) { mode = VisualizerMode(rawValue: i) ?? .oscilloscope }
    }
    public func encode(to e: Encoder) throws { var c = e.container(keyedBy: CodingKeys.self); try c.encode(Self.names[mode.rawValue], forKey: .mode) }
    public static var path: String { Paths.file("visualizer.json") }
    public static func load() -> VisualizerPreferences { PlayerJson.read(VisualizerPreferences.self, path) ?? VisualizerPreferences() }
    public func save() { try? PlayerJson.write(Self.path, self, pretty: true) }
}

public final class SpectrumAnalysis {
    public static let bandCount = 64
    public static let floorDb = -72.0
    public let sampleRate: Int
    public let size: Int
    let log2n: vDSP_Length
    let fft: FFTSetup
    var window: [Float]
    var power: [Double]
    public var levels: [Double]
    let normalization: Double
    public var maximumFrequency: Double { min(20000, Double(sampleRate) / 2) }
    public static func sizeForRate(_ rate: Int) -> Int { var s = 512; while Double(s) < Double(rate) * 0.085 && s < 32768 { s *= 2 }; return s }

    public init(sampleRate: Int, bands: Int = bandCount) {
        self.sampleRate = sampleRate; size = Self.sizeForRate(sampleRate); log2n = vDSP_Length(log2(Double(size)))
        fft = vDSP_create_fftsetup(log2n, FFTRadix(kFFTRadix2))!
        let n = Self.sizeForRate(sampleRate)
        window = (0..<n).map { Float(0.5 - 0.5 * cos(2 * Double.pi * Double($0) / Double(n - 1))) }
        let sum = window.reduce(0.0) { $0 + Double($1) }
        normalization = 4 / (sum * sum)
        power = [Double](repeating: 0, count: size / 2 + 1); levels = [Double](repeating: Self.floorDb, count: max(1, min(256, bands)))
    }
    deinit { vDSP_destroy_fftsetup(fft) }

    public func bandFrequency(_ i: Int) -> Double { 20 * pow(maximumFrequency / 20, (Double(i) + 0.5) / Double(levels.count)) }

    @discardableResult public func analyze(_ left: [Float], _ right: [Float], channels: Int) -> [Double] {
        for i in 0..<power.count { power[i] = 0 }
        transform(left); if channels > 1 { transform(right) }
        let divisor = channels > 1 ? 2.0 : 1.0
        let n = Double(levels.count)
        for band in 0..<levels.count {
            let lower = 20 * pow(maximumFrequency / 20, Double(band) / n) * Double(size) / Double(sampleRate)
            let upper = 20 * pow(maximumFrequency / 20, Double(band + 1) / n) * Double(size) / Double(sampleRate)
            let first = max(1, Int(floor(lower))), last = min(size / 2, Int(ceil(upper)))
            var peak = 0.0
            if first <= last { for bin in first...last {
                let overlap = max(0, min(upper, Double(bin) + 0.5) - max(lower, Double(bin) - 0.5))
                peak = max(peak, power[bin] * min(1, overlap) / divisor)
            } }
            levels[band] = peak > 0 ? (10 * log10(peak)).clamped(Self.floorDb, 0) : Self.floorDb
        }
        return levels
    }

    func transform(_ samples: [Float]) {
        var input = [Float](repeating: 0, count: size)
        let count = min(size, samples.count), offset = size - count
        for i in 0..<count { let s = samples[samples.count - count + i]; input[offset + i] = (s.isFinite ? s : 0) * window[offset + i] }
        var real = [Float](repeating: 0, count: size / 2), imag = real
        real.withUnsafeMutableBufferPointer { rp in imag.withUnsafeMutableBufferPointer { ip in
            var split = DSPSplitComplex(realp: rp.baseAddress!, imagp: ip.baseAddress!)
            input.withUnsafeBufferPointer { inp in
                inp.baseAddress!.withMemoryRebound(to: DSPComplex.self, capacity: size / 2) { vDSP_ctoz($0, 2, &split, 1, vDSP_Length(size / 2)) }
            }
            vDSP_fft_zrip(fft, &split, 1, log2n, FFTDirection(FFT_FORWARD))
        } }
        // vDSP's packed real FFT is scaled by 2 relative to the textbook DFT.
        power[0] += Double(real[0] / 2 * real[0] / 2) * normalization
        power[size / 2] += Double(imag[0] / 2 * imag[0] / 2) * normalization
        for i in 1..<size / 2 { let r = Double(real[i]) / 2, im = Double(imag[i]) / 2; power[i] += (r * r + im * im) * normalization }
    }
}

public struct VuLevel { public var rmsDb: Double; public var peakDb: Double; public init(rmsDb: Double, peakDb: Double) { self.rmsDb = rmsDb; self.peakDb = peakDb } }
public enum VuAnalysis {
    public static let floorDb = -60.0
    public static func measure(_ s: ArraySlice<Float>) -> VuLevel {
        guard !s.isEmpty else { return VuLevel(rmsDb: floorDb, peakDb: floorDb) }
        var energy = 0.0, peak = 0.0
        for x in s { let v = x.isFinite ? Double(abs(x)) : 0; energy += v * v; peak = max(peak, v) }
        func db(_ a: Double) -> Double { a > 0 ? (20 * log10(a)).clamped(floorDb, 0) : floorDb }
        return VuLevel(rmsDb: db(sqrt(energy / Double(s.count))), peakDb: db(peak))
    }
    public static func unit(_ db: Double, _ floor: Double) -> Double { ((db - floor) / -floor).clamped(0, 1) }
    public static func smooth(_ current: Double, _ target: Double, _ seconds: Double) -> Double {
        current + (target - current) * (1 - exp(-seconds.clamped(0, 0.1) / (target > current ? 0.045 : 0.24)))
    }
}

public final class SpectrogramHistory {
    public static let bands = 128, columns = 600, columnsPerSecond = 60
    public private(set) var data = [UInt8](repeating: 0, count: 600 * 128)
    var lastColumn: Int64 = -1
    public private(set) var lastFrame: Int64 = -1
    public private(set) var sampleRate = 0
    public init() {}
    public func clear() { for i in 0..<data.count { data[i] = 0 }; lastFrame = -1; lastColumn = -1; sampleRate = 0 }
    @discardableResult public func update(_ levels: [Double], sampleRate rate: Int, endFrame: Int64) -> Bool {
        guard rate >= 1, endFrame >= 0, levels.count == Self.bands else { return false }
        if rate != sampleRate || (lastFrame >= 0 && endFrame < lastFrame) { clear() }
        sampleRate = rate
        let columnIndex = endFrame * Int64(Self.columnsPerSecond) / Int64(rate)
        let elapsed = lastColumn < 0 ? 1 : columnIndex - lastColumn
        if elapsed < 1 { return false }
        let steps = Int(min(Int64(Self.columns), elapsed)), keep = (Self.columns - steps) * Self.bands
        if keep > 0 { data.withUnsafeMutableBufferPointer { p in memmove(p.baseAddress!, p.baseAddress! + steps * Self.bands, keep) } }
        for i in keep..<data.count { data[i] = 0 }
        let first = elapsed > Int64(Self.columnsPerSecond / 4) ? Self.columns - 1 : Self.columns - steps
        for column in first..<Self.columns { for band in 0..<Self.bands {
            let db = levels[band].isFinite ? levels[band] : SpectrumAnalysis.floorDb
            data[column * Self.bands + band] = UInt8((255 * VuAnalysis.unit(db, SpectrumAnalysis.floorDb)).rounded())
        } }
        lastFrame = endFrame; lastColumn = columnIndex; return true
    }
}

public enum ScopeTrigger {
    /// Rising zero crossing on the louder channel, searched backwards (ScopeCapture.TriggerStart).
    public static func start(_ left: [Float], _ right: [Float], window: Int) -> Int {
        let search = left.count - window; if search <= 0 { return 0 }
        var l = 0.0, r = 0.0
        for i in 0..<left.count { l += Double(left[i] * left[i]); r += Double(right[i] * right[i]) }
        let ch = r > l ? right : left
        var i = search
        while i > 0 { if ch[i - 1] <= 0 && ch[i] > 0 { return i }; i -= 1 }
        return search
    }
}
