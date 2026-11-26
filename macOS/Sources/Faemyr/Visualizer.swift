import AppKit
import SwiftUI
import FaemyrCore

/// Holds smoothing / history state between frames (OscilloscopeView fields).
final class VisualizerState {
    var spectrum: SpectrumAnalysis?
    var spectrogramAnalysis: SpectrumAnalysis?
    let history = SpectrogramHistory()
    var bars = [Double](repeating: 0, count: SpectrumAnalysis.bandCount), peaks = [Double](repeating: 0, count: SpectrumAnalysis.bandCount), holds = [Double](repeating: 0, count: SpectrumAnalysis.bandCount)
    var vuBars = [0.0, 0.0], vuPeaks = [0.0, 0.0], vuHolds = [0.0, 0.0]
    var vuLevels = [VuLevel(rmsDb: VuAnalysis.floorDb, peakDb: VuAnalysis.floorDb), VuLevel(rmsDb: VuAnalysis.floorDb, peakDb: VuAnalysis.floorDb)]
    var stamp: Date?
    var lastAnalysisFrame: Int64 = -1
    var spectrogramLevels = [Double](repeating: SpectrumAnalysis.floorDb, count: SpectrogramHistory.bands)
    var spectrogramImage: CGImage?
    var mode: VisualizerMode?
    func reset() {
        for i in bars.indices { bars[i] = 0; peaks[i] = 0; holds[i] = 0 }
        vuBars = [0, 0]; vuPeaks = [0, 0]; vuHolds = [0, 0]; stamp = nil; history.clear(); lastAnalysisFrame = -1; spectrogramImage = nil
    }
    static func holdPeak(_ peak: inout Double, _ hold: inout Double, _ value: Double, _ seconds: Double, _ immediate: Bool) {
        if immediate || value >= peak { peak = value; hold = 0.65 } else if hold > 0 { hold -= seconds } else { peak = max(value, peak - seconds * 0.35) }
    }
}

struct VisualizerView: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var state = VisualizerState()
    var body: some View {
        let running = model.isPlaying && model.ui.showVisualizer
        TimelineView(.animation(minimumInterval: 1 / model.ui.visualizerFps, paused: !running)) { tl in
            Canvas { ctx, size in draw(ctx, size, running: running, now: tl.date) }
        }
        .mask {
            if model.visualizerMode == .oscilloscope || model.visualizerMode == .spectrum {
                GeometryReader { g in LinearGradient(gradient: edgeGradient(g.size.width), startPoint: .leading, endPoint: .trailing) }
            } else { Color.black }
        }
        .contentShape(Rectangle())
        .contextMenu {
            ForEach(VisualizerMode.allCases, id: \.self) { m in
                Button { setMode(m) } label: { if m == model.visualizerMode { Label(m.title, systemImage: "checkmark") } else { Text(m.title) } }
            }
        }
        .help(tr("Sağ tıkla: Osiloskop, Spectrum, VU metre veya Spektrogram"))
        .onChange(of: model.visualizerMode) { state.reset() }
    }

    func setMode(_ m: VisualizerMode) {
        model.visualizerMode = m; model.ui.visualizerMode = m.rawValue; model.saveInterface()
        var p = VisualizerPreferences(); p.mode = m; p.save()
    }

    func draw(_ ctx: GraphicsContext, _ size: CGSize, running: Bool, now: Date) {
        guard size.width > 2, size.height > 2 else { return }
        let trace = theme.c("VisualizerAccent")
        let primary = trace, secondary = trace.opacity(0.44), axis = trace.opacity(0.13)
        let width = theme.ui.traceWidth
        let mode = model.visualizerMode
        if state.mode != mode { state.mode = mode; state.reset() }
        let seconds = state.stamp.map { now.timeIntervalSince($0).clamped(0, 0.1) } ?? 1.0 / 30
        state.stamp = now
        let scope = model.audio.scope
        let rate = Int(scope.sampleRate)

        switch mode {
        case .oscilloscope:
            let middle = size.height / 2, amplitude = max(1, middle - 4)
            var c = ctx // edge fade is a static view mask (see body); clipToLayer re-rendered an offscreen layer every frame
            c.stroke(Path { $0.move(to: CGPoint(x: 0, y: middle)); $0.addLine(to: CGPoint(x: size.width, y: middle)) }, with: .color(axis), lineWidth: 0.6)
            guard running else { return }
            let windowFrames = max(2, rate / 40), trigger = max(1, rate / 100)
            let s = scope.latest(windowFrames + trigger)
            let length = min(s.left.count, windowFrames)
            let start = ScopeTrigger.start(s.left, s.right, window: length)
            func path(_ samples: [Float]) -> Path {
                var p = Path()
                let columns = Int(size.width).clamped(32, 1024)
                func pt(_ i: Int) -> CGPoint { CGPoint(x: Double(i) * size.width / Double(max(1, length - 1)), y: middle - Double(samples[min(samples.count - 1, start + i)]).clamped(-1, 1) * amplitude) }
                p.move(to: pt(0))
                if length <= columns * 2 { for i in 1..<length { p.addLine(to: pt(i)) } }
                else {
                    for b in 0..<columns {
                        let first = b * length / columns, end = (b + 1) * length / columns
                        var mn = first, mx = first
                        for i in first..<end { if samples[start + i] < samples[start + mn] { mn = i }; if samples[start + i] > samples[start + mx] { mx = i } }
                        if mn < mx { p.addLine(to: pt(mn)); p.addLine(to: pt(mx)) } else { p.addLine(to: pt(mx)); p.addLine(to: pt(mn)) }
                    }
                }
                return p
            }
            guard length > 2, start + length <= s.left.count else { return }
            let first = path(s.left)
            let style = StrokeStyle(lineWidth: width, lineCap: .round, lineJoin: .round)
            c.stroke(first, with: .color(trace.opacity(theme.ui.traceGlow / 100)), style: StrokeStyle(lineWidth: width + 2.6, lineCap: .round, lineJoin: .round))
            c.stroke(path(s.right), with: .color(secondary), style: StrokeStyle(lineWidth: max(0.8, width * 0.7), lineCap: .round, lineJoin: .round))
            c.stroke(first, with: .color(primary), style: style)

        case .spectrum:
            if running, rate > 0 {
                if state.spectrum?.sampleRate != rate { state.spectrum = SpectrumAnalysis(sampleRate: rate) }
                let n = state.spectrum!.size
                let s = scope.latest(n)
                let values = state.spectrum!.analyze(s.left, s.right, channels: 2)
                for i in values.indices {
                    let target = VuAnalysis.unit(values[i], SpectrumAnalysis.floorDb)
                    state.bars[i] = VuAnalysis.smooth(state.bars[i], target, seconds)
                    VisualizerState.holdPeak(&state.peaks[i], &state.holds[i], target, seconds, false)
                }
            }
            let height = max(2, size.height - 13), step = size.width / Double(SpectrumAnalysis.bandCount), w = max(1, step - 2)
            var c = ctx // edge fade is a static view mask (see body); clipToLayer re-rendered an offscreen layer every frame
            c.stroke(Path { $0.move(to: CGPoint(x: 0, y: height)); $0.addLine(to: CGPoint(x: size.width, y: height)) }, with: .color(axis), lineWidth: 0.6)
            for i in 0..<SpectrumAnalysis.bandCount {
                let x = Double(i) * step + 1, h = state.bars[i] * (height - 2)
                if h > 0.1 { c.fill(Path(roundedRect: CGRect(x: x, y: height - h, width: w, height: h), cornerRadius: min(2, w / 2)), with: .color(primary)) }
                if state.peaks[i] > 0.002 { let y = height - state.peaks[i] * (height - 2); c.stroke(Path { $0.move(to: CGPoint(x: x, y: y)); $0.addLine(to: CGPoint(x: x + w, y: y)) }, with: .color(secondary), lineWidth: max(0.8, width * 0.7)) }
            }
            let maxF = state.spectrum?.maximumFrequency ?? 20000
            for hz in [20.0, 100, 1000, 10000, 20000] where hz <= maxF {
                let x = log(hz / 20) / log(maxF / 20) * size.width
                label(ctx, hz >= 1000 ? "\(Int(hz / 1000))k" : "\(Int(hz))", x: x, y: height + 2, centered: true, size: size, color: secondary)
            }

        case .vuMeter:
            if running {
                let s = scope.latest(Int(Double(max(rate, 8000)) * 0.3))
                state.vuLevels[0] = VuAnalysis.measure(s.left[...]); state.vuLevels[1] = VuAnalysis.measure(s.right[...])
                for i in 0..<2 {
                    let target = VuAnalysis.unit(state.vuLevels[i].rmsDb, VuAnalysis.floorDb)
                    state.vuBars[i] = VuAnalysis.smooth(state.vuBars[i], target, seconds)
                    VisualizerState.holdPeak(&state.vuPeaks[i], &state.vuHolds[i], VuAnalysis.unit(state.vuLevels[i].peakDb, VuAnalysis.floorDb), seconds, false)
                }
            }
            let start = 18.0, end = max(start + 20, size.width - 53), w = end - start, plot = max(20, size.height - 13)
            for ch in 0..<2 {
                let y = (Double(ch) + 0.5) * plot / 2 - 5
                label(ctx, ch == 0 ? "L" : "R", x: 0, y: y - 1, centered: false, size: size, color: secondary, fontSize: 9)
                let segments = Int(w / 9).clamped(24, 96), step = w / Double(segments)
                var c = ctx; c.clipToLayer { $0.fill(Path(CGRect(origin: .zero, size: size)), with: .linearGradient(edgeGradient(w, offset: start), startPoint: CGPoint(x: start, y: 0), endPoint: CGPoint(x: start + w, y: 0))) }
                for sgm in 0..<segments {
                    let r = CGRect(x: start + Double(sgm) * step, y: y, width: max(1, step - 1.5), height: 10)
                    c.fill(Path(roundedRect: r, cornerRadius: 1), with: .color(Double(sgm) / Double(segments) < state.vuBars[ch] ? primary : axis))
                }
                if state.vuPeaks[ch] > 0.002 { let px = start + w * state.vuPeaks[ch]; c.stroke(Path { $0.move(to: CGPoint(x: px, y: y - 2)); $0.addLine(to: CGPoint(x: px, y: y + 12)) }, with: .color(primary), lineWidth: width) }
                let db = state.vuLevels[ch].rmsDb <= VuAnalysis.floorDb ? "≤−60" : String(format: "%.1f", state.vuLevels[ch].rmsDb)
                label(ctx, db, x: end + 7, y: y - 1, centered: false, size: size, color: secondary, fontSize: 9)
            }
            for db in [-60, -36, -24, -12, -6, 0] { label(ctx, "\(db)", x: start + VuAnalysis.unit(Double(db), VuAnalysis.floorDb) * w, y: plot + 2, centered: true, size: size, color: secondary) }
            label(ctx, "dBFS", x: end + 7, y: plot + 2, centered: false, size: size, color: secondary)

        case .spectrogram:
            if running, rate > 0 {
                if state.spectrogramAnalysis?.sampleRate != rate { state.spectrogramAnalysis = SpectrumAnalysis(sampleRate: rate, bands: SpectrogramHistory.bands); state.history.clear() }
                let a = state.spectrogramAnalysis!
                let s = scope.latest(a.size)
                let end = Int64(s.total)
                if state.lastAnalysisFrame < 0 || end < state.lastAnalysisFrame || end - state.lastAnalysisFrame >= Int64(max(1, rate / 30)) {
                    state.spectrogramLevels = a.analyze(s.left, s.right, channels: 2); state.lastAnalysisFrame = end
                }
                if state.history.update(state.spectrogramLevels, sampleRate: rate, endFrame: end) { state.spectrogramImage = spectrogramImage(theme.palette["VisualizerAccent"]) }
            }
            let left = size.width >= 200 ? 28.0 : 0, height = max(2, size.height - 13), w = max(2, size.width - left)
            var c = ctx; c.clipToLayer { $0.fill(Path(CGRect(origin: .zero, size: size)), with: .linearGradient(edgeGradient(w, offset: left), startPoint: CGPoint(x: left, y: 0), endPoint: CGPoint(x: left + w, y: 0))) }
            if let img = state.spectrogramImage { c.draw(Image(decorative: img, scale: 1), in: CGRect(x: left, y: 0, width: w, height: height)) }
            c.stroke(Path { $0.move(to: CGPoint(x: left, y: height)); $0.addLine(to: CGPoint(x: size.width, y: height)) }, with: .color(axis), lineWidth: 0.6)
            let maxF = state.spectrogramAnalysis?.maximumFrequency ?? 20000
            if left > 0 {
                for hz in (height >= 65 ? [20.0, 100, 1000, 20000] : [20, 20000]) {
                    let f = min(hz, maxF); if hz > maxF && hz != 20000 { continue }
                    let y = height * (1 - log(f / 20) / log(maxF / 20))
                    label(ctx, f >= 1000 ? (f / 1000 == (f / 1000).rounded() ? "\(Int(f / 1000))k" : String(format: "%.1fk", f / 1000)) : "\(Int(f))", x: 0, y: (y - 5).clamped(0, max(0, height - 10)), centered: false, size: size, color: secondary)
                }
            }
            for (o, t) in [(0.0, "−10 s"), (0.5, "−5 s"), (1.0, "0 s")] { label(ctx, t, x: left + w * o, y: height + 2, centered: true, size: size, color: secondary) }
        }
    }

    func label(_ ctx: GraphicsContext, _ text: String, x: Double, y: Double, centered: Bool, size: CGSize, color: Color, fontSize: Double = 8) {
        let resolved = ctx.resolve(Text(text).font(.system(size: fontSize)).foregroundStyle(color))
        let m = resolved.measure(in: CGSize(width: 200, height: 40))
        let px = centered ? (x - m.width / 2).clamped(0, max(0, size.width - m.width)) : x
        ctx.draw(resolved, at: CGPoint(x: px, y: y), anchor: .topLeading)
    }

    /// VisualizerEdgeFade.cs: cosine-shaped fade at both ends of the plot.
    func edgeGradient(_ width: Double, offset: Double = 0) -> Gradient {
        let edge = min(0.25, 28 / max(1, width))
        var stops: [Gradient.Stop] = []
        for i in 0...8 {
            let t = Double(i) / 8, a = (1 - cos(Double.pi * t)) / 2
            stops.append(.init(color: Color.black.opacity(a), location: edge * t))
            stops.append(.init(color: Color.black.opacity(a), location: 1 - edge * t))
        }
        return Gradient(stops: stops.sorted { $0.location < $1.location })
    }

    func spectrogramImage(_ color: RGBA) -> CGImage? {
        let cols = SpectrogramHistory.columns, bands = SpectrogramHistory.bands
        var palette = [UInt32](repeating: 0, count: 256)
        for i in 0..<256 {
            let strength = Double(i) / 255, white = max(0, (strength - 0.65) / 0.35) * 0.9, light = 0.22 + 0.85 * strength
            let alpha = i == 0 ? 0.0 : (255 * (0.18 + 0.82 * sqrt(strength)) * Double(color.a) / 255).rounded()
            func ch(_ v: UInt8) -> UInt32 { UInt32(((min(255, Double(v) * light) * (1 - white) + 255 * white) * alpha / 255).rounded()) }
            palette[i] = UInt32(alpha) << 24 | ch(color.b) << 16 | ch(color.g) << 8 | ch(color.r)
        }
        var pixels = [UInt32](repeating: 0, count: cols * bands)
        let data = state.history.data
        for c in 0..<cols { for b in 0..<bands { pixels[(bands - 1 - b) * cols + c] = palette[Int(data[c * bands + b])] } }
        return pixels.withUnsafeMutableBytes { buf in
            CGContext(data: buf.baseAddress, width: cols, height: bands, bitsPerComponent: 8, bytesPerRow: cols * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)?.makeImage()
        }
    }
}

/// Hosts the visualizer in its own NSHostingView. Its 60 fps TimelineView then re-renders only this small
/// view graph instead of forcing the main window's whole SwiftUI tree through layout and display every frame.
struct IsolatedVisualizer: NSViewRepresentable {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    func makeNSView(context: Context) -> NSHostingView<AnyView> {
        let host = NSHostingView(rootView: content)
        host.sizingOptions = []
        return host
    }
    func updateNSView(_ host: NSHostingView<AnyView>, context: Context) { host.rootView = content }
    private var content: AnyView { AnyView(VisualizerView().environment(model).environment(\.theme, theme)) }
}
