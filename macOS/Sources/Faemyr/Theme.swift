import SwiftUI
import AppKit
import FaemyrCore

extension Color {
    init(_ c: RGBA) { self.init(.sRGB, red: Double(c.r) / 255, green: Double(c.g) / 255, blue: Double(c.b) / 255, opacity: Double(c.a) / 255) }
    init(hex: String) { self.init(RGBA(hex: hex) ?? RGBA(128, 128, 128)) }
}

/// Resolved palette + interface metrics, the equivalent of WPF DynamicResources.
struct Theme {
    var palette: Palette
    var ui: InterfacePreferences
    func c(_ key: String) -> Color { Color(palette[key]) }
    var canvas: Color { c("Canvas") }
    var surface: Color { c("Surface") }
    var ink: Color { c("Ink") }
    var muted: Color { c("Muted") }
    var accent: Color { c("Accent") }
    var onAccent: Color { c("OnAccent") }
    var control: Color { c("ControlSurface") }
    var hover: Color { c("HoverSurface") }
    var selected: Color { c("SelectedSurface") }
    var line: Color { c("Line") }
    var playing: Color { c("PlayingAccent") }
    func font(_ size: Double, _ weight: Font.Weight = .regular) -> Font {
        let s = CGFloat(ui.font(size))
        if ui.fontFamily != "Segoe UI", NSFont(name: ui.fontFamily, size: s) != nil { return .custom(ui.fontFamily, size: s).weight(weight) }
        return .system(size: s, weight: weight)
    }
    var buttonRadius: CGFloat { CGFloat(ui.buttonRadius) }
    var cardRadius: CGFloat { CGFloat(ui.cardRadius) }
    var panelRadius: CGFloat { CGFloat(ui.panelRadius) }
}

private struct ThemeKey: EnvironmentKey { static let defaultValue = Theme(palette: Themes.resolve(theme: "dark", appearance: AppearancePreferences()), ui: InterfacePreferences()) }
extension EnvironmentValues { var theme: Theme { get { self[ThemeKey.self] } set { self[ThemeKey.self] = newValue } } }

// MARK: - SVG icons (SvgIcon.cs): the bundled 24×24 paths drawn as vectors.

struct SvgShape { let path: Path; let fill: Bool; let stroke: CGFloat }

enum SvgIcons {
    static var cache: [String: [SvgShape]] = [:]
    static func shapes(_ name: String) -> [SvgShape] {
        if let s = cache[name] { return s }
        var result: [SvgShape] = []
        if let url = Paths.resource(name, "svg", subdirectory: "Icons"), let text = try? String(contentsOf: url, encoding: .utf8) {
            let pathTags = text.components(separatedBy: "<path").dropFirst()
            for tag in pathTags {
                func attr(_ n: String) -> String? {
                    guard let r = tag.range(of: n + "=\"") else { return nil }
                    let rest = tag[r.upperBound...]; guard let end = rest.firstIndex(of: "\"") else { return nil }
                    return String(rest[..<end])
                }
                guard let d = attr("d") else { continue }
                result.append(SvgShape(path: parse(d), fill: attr("fill") != "none", stroke: CGFloat(Double(attr("stroke-width") ?? "") ?? 1.8)))
            }
        }
        cache[name] = result
        return result
    }

    /// Minimal SVG path parser for the commands used by Faemyr's icons (M L H V C A Z).
    static func parse(_ d: String) -> Path {
        var tokens: [String] = []; var cur = ""
        func flush() { if !cur.isEmpty { tokens.append(cur); cur = "" } }
        for ch in d {
            if ch.isLetter && ch != "e" { flush(); tokens.append(String(ch)) }
            else if ch == "," || ch == " " { flush() }
            else if ch == "-" && !cur.isEmpty && cur.last != "e" { flush(); cur.append(ch) }
            else if ch == "." && cur.contains(".") { flush(); cur.append(ch) }
            else { cur.append(ch) }
        }
        flush()
        var p = Path(); var i = 0; var cmd = "M"; var pt = CGPoint.zero; var start = CGPoint.zero
        func num() -> CGFloat { defer { i += 1 }; return i < tokens.count ? CGFloat(Double(tokens[i]) ?? 0) : 0 }
        func isNum() -> Bool { i < tokens.count && !(tokens[i].first?.isLetter ?? false) }
        while i < tokens.count {
            if let f = tokens[i].first, f.isLetter { cmd = tokens[i]; i += 1 }
            let rel = cmd.lowercased() == cmd
            switch cmd.uppercased() {
            case "M":
                var x = num(), y = num(); if rel { x += pt.x; y += pt.y }
                pt = CGPoint(x: x, y: y); start = pt; p.move(to: pt); cmd = rel ? "l" : "L"
            case "L": var x = num(), y = num(); if rel { x += pt.x; y += pt.y }; pt = CGPoint(x: x, y: y); p.addLine(to: pt)
            case "H": var x = num(); if rel { x += pt.x }; pt = CGPoint(x: x, y: pt.y); p.addLine(to: pt)
            case "V": var y = num(); if rel { y += pt.y }; pt = CGPoint(x: pt.x, y: y); p.addLine(to: pt)
            case "C":
                var c1 = CGPoint(x: num(), y: num()), c2 = CGPoint(x: num(), y: num()), e = CGPoint(x: num(), y: num())
                if rel { c1.x += pt.x; c1.y += pt.y; c2.x += pt.x; c2.y += pt.y; e.x += pt.x; e.y += pt.y }
                p.addCurve(to: e, control1: c1, control2: c2); pt = e
            case "A":
                let rx = num(), ry = num(), rot = num(), large = num() != 0, sweep = num() != 0
                var e = CGPoint(x: num(), y: num()); if rel { e.x += pt.x; e.y += pt.y }
                arc(&p, from: pt, to: e, rx: rx, ry: ry, rotation: rot, large: large, sweep: sweep); pt = e
            case "Z": p.closeSubpath(); pt = start; if isNum() { cmd = "L" } else { continue }
            default: i += 1
            }
        }
        return p
    }

    static func arc(_ p: inout Path, from p0: CGPoint, to p1: CGPoint, rx rxIn: CGFloat, ry ryIn: CGFloat, rotation: CGFloat, large: Bool, sweep: Bool) {
        var rx = abs(rxIn), ry = abs(ryIn)
        if rx == 0 || ry == 0 { p.addLine(to: p1); return }
        let phi = rotation * .pi / 180, cp = cos(phi), sp = sin(phi)
        let dx = (p0.x - p1.x) / 2, dy = (p0.y - p1.y) / 2
        let x1 = cp * dx + sp * dy, y1 = -sp * dx + cp * dy
        let lambda = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry)
        if lambda > 1 { rx *= sqrt(lambda); ry *= sqrt(lambda) }
        let num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
        let den = rx * rx * y1 * y1 + ry * ry * x1 * x1
        var coef = sqrt(max(0, num / den)); if large == sweep { coef = -coef }
        let cx1 = coef * rx * y1 / ry, cy1 = -coef * ry * x1 / rx
        let cx = cp * cx1 - sp * cy1 + (p0.x + p1.x) / 2, cy = sp * cx1 + cp * cy1 + (p0.y + p1.y) / 2
        func ang(_ ux: CGFloat, _ uy: CGFloat, _ vx: CGFloat, _ vy: CGFloat) -> CGFloat {
            let a = atan2(ux * vy - uy * vx, ux * vx + uy * vy); return a
        }
        let t1 = ang(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry)
        var dt = ang((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry)
        if !sweep && dt > 0 { dt -= 2 * .pi } else if sweep && dt < 0 { dt += 2 * .pi }
        let segments = max(1, Int(ceil(abs(dt) / (.pi / 8))))
        for s in 1...segments {
            let t = t1 + dt * CGFloat(s) / CGFloat(segments)
            let x = cx + rx * cos(t) * cp - ry * sin(t) * sp, y = cy + rx * cos(t) * sp + ry * sin(t) * cp
            p.addLine(to: CGPoint(x: x, y: y))
        }
    }
}

struct Icon: View {
    let name: String
    var size: CGFloat = 18
    var color: Color
    var body: some View {
        Canvas { ctx, sz in
            let scale = min(sz.width, sz.height) / 24
            ctx.translateBy(x: (sz.width - scale * 24) / 2, y: (sz.height - scale * 24) / 2)
            ctx.scaleBy(x: scale, y: scale)
            for s in SvgIcons.shapes(name) {
                if s.fill { ctx.fill(s.path, with: .color(color)) }
                else { ctx.stroke(s.path, with: .color(color), style: StrokeStyle(lineWidth: s.stroke, lineCap: .round, lineJoin: .round)) }
            }
        }
        .frame(width: size, height: size)
        .allowsHitTesting(false)
    }
}

// MARK: - InnerAccentBorder: surface + soft accent glow fading inward + fine rim.

struct AccentPanel: ViewModifier {
    var surface: Color
    var rim: Color
    var glow: Color?
    var radius: CGFloat
    var plain = false
    func body(content: Content) -> some View {
        content.background {
            if plain { RoundedRectangle(cornerRadius: radius, style: .continuous).fill(surface) }
            else {
                ZStack {
                    RoundedRectangle(cornerRadius: radius, style: .continuous).fill(surface)
                    if let glow {
                        // exp(-d/4.5) * 0.12 inner glow, approximated by blurred inner strokes.
                        RoundedRectangle(cornerRadius: radius, style: .continuous)
                            .strokeBorder(glow.opacity(0.12), lineWidth: 9).blur(radius: 4.5)
                            .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
                    }
                    RoundedRectangle(cornerRadius: radius, style: .continuous).strokeBorder(rim.opacity(0.30), lineWidth: 1)
                }
            }
        }
    }
}
extension View {
    func accentPanel(_ surface: Color, rim: Color, glow: Color? = nil, radius: CGFloat, plain: Bool = false) -> some View {
        modifier(AccentPanel(surface: surface, rim: rim, glow: glow ?? rim, radius: radius, plain: plain))
    }
}

// MARK: - WPF-equivalent controls

/// Default Button template: rounded ControlSurface, opacity 0.8 on hover, 0.4 disabled.
struct FButtonStyle: ButtonStyle {
    @Environment(\.theme) var theme
    @Environment(\.isEnabled) var enabled
    var background: Color?
    var foreground: Color?
    var padding = EdgeInsets(top: 10, leading: 16, bottom: 10, trailing: 16)
    var radius: CGFloat?
    func makeBody(configuration: Configuration) -> some View {
        HoverWrap { hovered in
            configuration.label
                .font(theme.font(14))
                .foregroundStyle(foreground ?? theme.ink)
                .padding(padding)
                .background(RoundedRectangle(cornerRadius: radius ?? theme.buttonRadius, style: .continuous).fill(background ?? theme.control))
                .opacity(!enabled ? 0.4 : (configuration.isPressed ? 0.7 : (hovered ? 0.8 : 1)))
                .contentShape(Rectangle())
        }
    }
}

/// TransportButton: transparent circle, HoverSurface on hover.
struct TransportStyle: ButtonStyle {
    @Environment(\.theme) var theme
    @Environment(\.isEnabled) var enabled
    var size: CGFloat = 40
    var radius: CGFloat?
    var fill: Color? = nil
    func makeBody(configuration: Configuration) -> some View {
        HoverWrap { hovered in
            configuration.label
                .frame(width: size > 0 ? size : nil, height: size > 0 ? size : nil)
                .background(RoundedRectangle(cornerRadius: radius ?? size / 2, style: .continuous).fill(fill ?? (hovered ? theme.hover : Color.clear)))
                .opacity(!enabled ? 0.35 : configuration.isPressed ? 0.6 : (fill != nil && hovered ? 0.86 : 1))
                .contentShape(Rectangle())
        }
    }
}

struct HoverWrap<Content: View>: View {
    @Local private var hovered = false
    @ViewBuilder var content: (Bool) -> Content
    var body: some View { content(hovered).onHover { hovered = $0 } }
}

/// ModernSeek slider: 4 px Ink/SelectedSurface track, thumb appears on hover.
struct SeekBar: View {
    @Environment(\.theme) var theme
    var value: Double
    var maximum: Double
    var onChange: (Double) -> Void
    var onCommit: (Double) -> Void = { _ in }
    var thumbAlways = false
    @Local private var hovered = false
    @Local private var dragging = false
    var body: some View {
        GeometryReader { g in
            let w = max(1, g.size.width)
            let frac = maximum > 0 ? (value / maximum).clamped(0, 1) : 0
            ZStack(alignment: .leading) {
                Capsule().fill(theme.selected).frame(height: 4)
                Capsule().fill(theme.ink).frame(width: w * frac, height: 4)
                Circle().fill(theme.ink).frame(width: 10, height: 10).offset(x: w * frac - 5).opacity(hovered || dragging || thumbAlways ? 1 : 0)
            }
            .frame(maxHeight: .infinity)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0).onChanged { v in
                dragging = true
                onChange(((v.location.x - 5) / max(1, w - 10)).clamped(0, 1) * maximum)
            }.onEnded { v in
                dragging = false
                onCommit(((v.location.x - 5) / max(1, w - 10)).clamped(0, 1) * maximum)
            })
            .onHover { hovered = $0 }
        }
        .frame(height: 22)
    }
}

/// Styled ComboBox: a rounded field with a chevron; clicking pops up a native menu under it.
struct FPicker<T: Hashable>: View {
    @Environment(\.theme) var theme
    let items: [T]
    let label: (T) -> String
    @Binding var selection: T
    var height: CGFloat = 42
    @Local private var anchor = MenuAnchor()
    var body: some View {
        Button {
            anchor.popUp(items.map { item in (label(item), item == selection, { selection = item }) }, height: height)
        } label: {
            HoverWrap { h in
                HStack {
                    Text(label(selection)).font(theme.font(14)).foregroundStyle(theme.ink).lineLimit(1)
                    Spacer(minLength: 8)
                    Chevron().stroke(Color(hex: "#B5B5B5"), lineWidth: 1.5).frame(width: 8, height: 4)
                }
                .padding(.leading, 12).padding(.trailing, 14)
                .frame(height: height)
                .background(RoundedRectangle(cornerRadius: theme.buttonRadius, style: .continuous).fill(h ? theme.hover : theme.control))
                .overlay(RoundedRectangle(cornerRadius: theme.buttonRadius, style: .continuous).strokeBorder(theme.line, lineWidth: 1))
                .contentShape(Rectangle())
            }
        }
        .buttonStyle(.plain)
        .background(AnchorView(anchor: anchor))
    }
}

final class MenuAnchor: NSObject {
    weak var view: NSView?
    var actions: [() -> Void] = []
    func popUp(_ entries: [(String, Bool, () -> Void)], height: CGFloat) {
        guard let view else { return }
        let menu = NSMenu(); actions = []
        for (i, e) in entries.enumerated() {
            let item = NSMenuItem(title: e.0, action: #selector(choose(_:)), keyEquivalent: "")
            item.target = self; item.tag = i; item.state = e.1 ? .on : .off
            menu.addItem(item); actions.append(e.2)
        }
        menu.minimumWidth = view.bounds.width
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: view.isFlipped ? view.bounds.height + 4 : -4), in: view)
    }
    @objc func choose(_ sender: NSMenuItem) { if sender.tag < actions.count { actions[sender.tag]() } }
}

struct AnchorView: NSViewRepresentable {
    let anchor: MenuAnchor
    func makeNSView(context: Context) -> NSView { let v = NSView(); anchor.view = v; return v }
    func updateNSView(_ v: NSView, context: Context) { anchor.view = v }
}

struct Chevron: Shape {
    func path(in r: CGRect) -> Path { var p = Path(); p.move(to: CGPoint(x: r.minX, y: r.minY)); p.addLine(to: CGPoint(x: r.midX, y: r.maxY)); p.addLine(to: CGPoint(x: r.maxX, y: r.minY)); return p }
}

/// AppearanceSwitch: 44×24 track, accent when on.
struct FSwitch: View {
    @Environment(\.theme) var theme
    let title: String
    @Binding var isOn: Bool
    var body: some View {
        Button { withAnimation(.easeOut(duration: 0.15)) { isOn.toggle() } } label: {
            HStack {
                Text(title).font(theme.font(14)).foregroundStyle(theme.ink).frame(maxWidth: .infinity, alignment: .leading)
                ZStack(alignment: isOn ? .trailing : .leading) {
                    Capsule().fill(isOn ? theme.accent : theme.control).overlay(Capsule().strokeBorder(isOn ? theme.accent : theme.line, lineWidth: 1))
                    Circle().fill(isOn ? theme.onAccent : theme.muted).frame(width: 16, height: 16).padding(.horizontal, 3)
                }
                .frame(width: 44, height: 24)
            }
            .padding(.vertical, 4).contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// Text field matching the WPF TextBox style.
struct FTextField: View {
    @Environment(\.theme) var theme
    let placeholder: String
    @Binding var text: String
    var body: some View {
        TextField("", text: $text, prompt: Text(placeholder).foregroundStyle(theme.muted.opacity(0.65)))
            .textFieldStyle(.plain).font(theme.font(14)).foregroundStyle(theme.ink)
            .padding(.horizontal, 12).padding(.vertical, 9)
            .background(RoundedRectangle(cornerRadius: theme.buttonRadius).fill(theme.control))
            .overlay(RoundedRectangle(cornerRadius: theme.buttonRadius).strokeBorder(theme.line, lineWidth: 1))
    }
}

// MARK: - Images

final class ImageCache {
    static let shared = ImageCache()
    private let cache = NSCache<NSString, NSImage>()
    private let queue = DispatchQueue(label: "faemyr.thumbnails", qos: .userInitiated, attributes: .concurrent)
    init() { cache.countLimit = 600 }
    func cached(_ path: String, _ px: Int) -> NSImage? { cache.object(forKey: "\(px)|\(path)" as NSString) }
    func load(_ path: String, _ px: Int) async -> NSImage? {
        if let i = cached(path, px) { return i }
        return await withCheckedContinuation { cont in
            queue.async {
                let img = Self.decode(path, px)
                if let img { self.cache.setObject(img, forKey: "\(px)|\(path)" as NSString) }
                cont.resume(returning: img)
            }
        }
    }
    static func decode(_ path: String, _ px: Int) -> NSImage? {
        guard let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil) else { return nil }
        let opts: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: px, kCGImageSourceCreateThumbnailWithTransform: true]
        guard let cg = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary) else { return nil }
        return NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
    }
}

/// ThumbnailImage: async decoded, UniformToFill, rounded.
struct Thumb: View {
    let path: String?
    var pixels = 480
    var radius: CGFloat = 0
    @Local private var image: NSImage?
    @Local private var loaded: String?
    var body: some View {
        GeometryReader { g in
            ZStack {
                if let image {
                    Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fill)
                        .frame(width: g.size.width, height: g.size.height).clipped()
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
        }
        .task(id: path) {
            guard let path, path.fileExists else { image = nil; return }
            if let c = ImageCache.shared.cached(path, pixels) { image = c; return }
            let img = await ImageCache.shared.load(path, pixels)
            if !Task.isCancelled { image = img }
        }
    }
}

extension NSImage {
    /// Average color used by the album backdrop gradient (PageBackdrop.cs).
    var averageRGB: (Double, Double, Double)? {
        guard let cg = cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
        let w = 32, h = 32
        var data = [UInt8](repeating: 0, count: w * h * 4)
        guard let ctx = CGContext(data: &data, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.draw(cg, in: CGRect(x: 0, y: 0, width: w, height: h))
        var r = 0.0, g = 0.0, b = 0.0
        for i in stride(from: 0, to: data.count, by: 4) { r += Double(data[i]); g += Double(data[i + 1]); b += Double(data[i + 2]) }
        let n = Double(w * h); return (r / n, g / n, b / n)
    }
}

/// The CLT SDK declares `State` as a macro whose plugin ships only with Xcode;
/// this alias selects the identical property wrapper so the app builds with
/// Command Line Tools alone.
typealias Local<Value> = SwiftUI.State<Value>
