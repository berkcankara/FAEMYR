import SwiftUI
import AppKit
import FaemyrCore

// MARK: - Track table (DataGrid "Tracks")

struct TrackColumns {
    let favorite: Bool, number: Bool, artist: Bool, format: Bool, bitrate: Bool, waveform: Bool, duration: Bool
    let narrow: Bool
    let waveformWidth: CGFloat, artistWidth: CGFloat
    var numberWidth: CGFloat { narrow ? 36 : 45 }
    var formatWidth: CGFloat { narrow ? 55 : 75 }
    var durationWidth: CGFloat { narrow ? 60 : 70 }
    var bitrateWidth: CGFloat { narrow ? 80 : 92 }
}

struct TrackTable: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var lastClicked: String?

    var body: some View {
        let hero = model.isHero
        GeometryReader { geo in
            let p = theme.ui
            let cols = TrackColumns(favorite: p.showTrackFavorites, number: p.showTrackNumbers, artist: p.showArtists && (!hero || p.artistsInAlbums), format: p.showFormats,
                                    bitrate: p.showBitrate && (hero || p.technicalColumnsEverywhere), waveform: p.showWaveforms && (hero || p.technicalColumnsEverywhere),
                                    duration: p.showDurations, narrow: geo.size.width < 700, waveformWidth: (geo.size.width * 0.17).clamped(85, p.waveformMaximum), artistWidth: p.artistColumnWidth)
            let rowHeight = max(p.rowHeight, 25 * p.fontScale / 100 + 10) + (hero ? 6 : 0)
            let rows = model.trackRows
            ZStack {
                if hero { GlassBackdrop() }
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 0, pinnedViews: [.sectionHeaders]) {
                            Section(header: TrackHeader(cols: cols)) {
                                ForEach(Array(rows.enumerated()), id: \.offset) { i, t in
                                    if model.discGrouped && (i == 0 || rows[i - 1].albumDisc != t.albumDisc) {
                                        HStack(spacing: 10) {
                                            Icon(name: "disc", size: 17, color: theme.muted)
                                            Text(tr("Disk") + " \(t.albumDisc)").font(theme.font(14, .semibold)).foregroundStyle(theme.muted)
                                        }.padding(EdgeInsets(top: 16, leading: 12, bottom: 12, trailing: 12))
                                    }
                                    TrackRow(track: t, index: i, cols: cols, height: rowHeight, alternate: p.alternatingRows && i % 2 == 1, selected: model.selectedPaths.contains(t.path))
                                        .id(t.path)
                                        .onTapGesture(count: 2) { Task { await model.startFromView(t) } }
                                        .simultaneousGesture(TapGesture().onEnded { select(t, rows) })
                                        .contextMenu { TrackMenu(track: t) }
                                }
                            }
                        }
                        .id(model.revision)
                    }
                    .onChange(of: model.scrollTarget) { if let t = model.scrollTarget { proxy.scrollTo(t, anchor: .center); model.scrollTarget = nil } }
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: hero ? theme.panelRadius : 0, style: .continuous))
            .padding(hero ? 8 : 0)
            .accentPanel(.clear, rim: hero ? theme.c("ListFrameAccent") : .clear, radius: theme.panelRadius, plain: !hero)
        }
        .onKeyPress(.return) { if let t = model.trackRows.first(where: { model.selectedPaths.contains($0.path) }) { Task { await model.startFromView(t) } }; return .handled }
        .onKeyPress(.delete) { if model.view == "queue" { model.removeFromQueue(model.selectedPaths) }; return .handled }
    }

    func select(_ t: Track, _ rows: [Track]) {
        let flags = NSEvent.modifierFlags
        if flags.contains(.command) { if model.selectedPaths.contains(t.path) { model.selectedPaths.remove(t.path) } else { model.selectedPaths.insert(t.path) } }
        else if flags.contains(.shift), let anchor = lastClicked, let a = rows.firstIndex(where: { $0.path == anchor }), let b = rows.firstIndex(where: { $0 === t }) {
            model.selectedPaths = Set(rows[min(a, b)...max(a, b)].map(\.path))
        } else { model.selectedPaths = [t.path] }
        lastClicked = t.path
    }
}

/// Track list "glass": the blurred backdrop seen through a translucent surface.
struct GlassBackdrop: View {
    @Environment(\.theme) var theme
    var body: some View {
        ZStack {
            Rectangle().fill(.ultraThinMaterial).opacity(theme.palette.colors["GlassSurface"].map { Double($0.a) / 255 > 0.98 ? 0 : 1 } ?? 1)
            theme.c("GlassSurface")
        }
    }
}

struct TrackHeader: View {
    @Environment(\.theme) var theme
    let cols: TrackColumns
    var body: some View {
        HStack(spacing: 0) {
            if cols.favorite { Icon(name: "heart", size: 18, color: theme.muted).frame(width: 36).help(tr("Favoriler")) }
            if cols.number { cell("#", cols.numberWidth) }
            HStack(spacing: 0) {
                Text(tr("Başlık")).frame(maxWidth: .infinity, alignment: .leading)
                if theme.ui.showRatings { Text(tr("Yıldız")).frame(width: 84).padding(.leading, 8) }
            }.padding(.horizontal, 12).frame(minWidth: 150, maxWidth: .infinity)
            if cols.artist { cell(tr("Sanatçı"), cols.artistWidth) }
            if cols.format { cell(tr("Biçim"), cols.formatWidth) }
            if cols.bitrate { cell("Bitrate", cols.bitrateWidth) }
            if cols.waveform { cell(tr("Dalga formu"), cols.waveformWidth) }
            if cols.duration { cell(tr("Süre"), cols.durationWidth) }
        }
        .font(theme.font(14)).foregroundStyle(theme.muted).lineLimit(1)
        .frame(height: 40)
        .background(theme.c("HeaderSurface"))
    }
    func cell(_ t: String, _ w: CGFloat) -> some View { Text(t).padding(.horizontal, 12).frame(width: w, alignment: .leading) }
}

struct TrackRow: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    let track: Track
    let index: Int
    let cols: TrackColumns
    let height: CGFloat
    let alternate: Bool
    let selected: Bool
    @Local private var hovered = false

    var body: some View {
        let isCurrent = model.now?.path == track.path && (model.isPlaying || model.isPaused || model.resumePrepared)
        let animating = isCurrent && model.isPlaying
        HStack(spacing: 0) {
            if cols.favorite {
                Button { model.toggleTrackFavorite(track) } label: { Icon(name: track.isFavorite ? "heart-filled" : "heart", size: 18, color: track.isFavorite ? theme.accent : theme.muted).frame(width: 28, height: 28) }
                    .buttonStyle(.plain).frame(width: 36).help(tr(track.isFavorite ? "Favorilerden çıkar" : "Favorilere ekle"))
            }
            if cols.number {
                ZStack(alignment: .leading) {
                    if hovered {
                        Button {
                            Task { if isCurrent { await model.togglePlayback() } else { await model.startFromView(track) } }
                        } label: { Icon(name: animating ? "pause" : "play", size: 18, color: isCurrent ? theme.playing : theme.ink) }
                            .buttonStyle(TransportStyle(size: 28)).help(tr(animating ? "Duraklat" : "Parçayı oynat"))
                    } else if isCurrent {
                        PlayingIndicator(running: animating, color: theme.playing).frame(width: 16, height: 18).padding(.leading, 8)
                    } else {
                        Text(track.number > 0 ? "\(track.number)" : "").padding(.leading, 8)
                    }
                }
                .frame(width: cols.numberWidth, alignment: .leading).padding(.horizontal, 4)
            }
            VStack(alignment: .leading, spacing: 3) {
                Text(track.title).foregroundStyle(isCurrent ? theme.playing : theme.ink).lineLimit(1)
                HStack(spacing: 0) {
                    ArtistLinks(names: track.artist, size: 11, color: theme.muted).lineLimit(1).frame(maxWidth: .infinity, alignment: .leading)
                    if theme.ui.showRatings { StarRating(value: track.rating) { model.rate(track, $0) }.frame(width: 84, height: 18).padding(.leading, 8) }
                }
            }
            .padding(.horizontal, 12).frame(minWidth: 150, maxWidth: .infinity, alignment: .leading)
            if cols.artist { ArtistLinks(names: track.artist, size: 14).lineLimit(1).padding(.horizontal, 12).frame(width: cols.artistWidth, alignment: .leading) }
            if cols.format { Text(track.codec).lineLimit(1).padding(.horizontal, 12).frame(width: cols.formatWidth, alignment: .leading) }
            if cols.bitrate { Text(track.bitRateLabel).font(theme.font(12)).lineLimit(1).padding(.horizontal, 12).frame(width: cols.bitrateWidth, alignment: .leading).help(track.bitRateHint) }
            if cols.waveform {
                WaveformView(data: model.waveforms[track.path], hint: model.waveformStatus[track.path] ?? "").frame(height: 30).padding(.horizontal, 12).frame(width: cols.waveformWidth)
                    .help(model.waveforms[track.path] != nil ? tr("Yerel analiz · krem: bas / turuncu: orta / mavi: tiz") : (model.waveformStatus[track.path] ?? tr("Sırada")))
            }
            if cols.duration { Text(track.duration).lineLimit(1).padding(.horizontal, 12).frame(width: cols.durationWidth, alignment: .leading) }
        }
        .font(theme.font(14))
        .frame(height: height)
        .background(selected ? theme.hover : alternate ? theme.c("AlternateSurface") : Color.clear)
        .contentShape(Rectangle())
        .onHover { hovered = $0 }
    }
}

struct PlayingIndicator: View {
    let running: Bool
    let color: Color
    var body: some View {
        TimelineView(.animation(minimumInterval: 1 / 30, paused: !running)) { ctx in
            let t = ctx.date.timeIntervalSinceReferenceDate
            HStack(alignment: .bottom, spacing: 2) {
                ForEach(0..<3, id: \.self) { i in
                    let phase = running ? (sin(t * (5 + Double(i) * 1.7) + Double(i) * 1.3) + 1) / 2 : [0.45, 0.8, 0.6][i]
                    RoundedRectangle(cornerRadius: 1).fill(color).frame(width: 3, height: 4 + 12 * phase)
                }
            }.frame(height: 18, alignment: .bottom)
        }
    }
}

struct StarRating: View {
    @Environment(\.theme) var theme
    let value: Int
    let onRate: (Int) -> Void
    @Local private var preview = -1
    static let star = SvgIcons.parse("M12,2 L15.09,8.26 22,9.27 17,14.14 18.18,21.02 12,17.77 5.82,21.02 7,14.14 2,9.27 8.91,8.26 Z")
    var body: some View {
        GeometryReader { g in
            let active = preview >= 0 ? preview : value
            let step = g.size.width / 5, size = max(1, min(step - 2, g.size.height - 4))
            Canvas { ctx, _ in
                let brush = theme.c("RatingAccent")
                for i in 0..<5 {
                    var c = ctx
                    c.translateBy(x: CGFloat(i) * step + (step - size) / 2, y: (g.size.height - size) / 2)
                    c.scaleBy(x: size / 24, y: size / 24)
                    if i < active { c.fill(Self.star, with: .color(brush)) }
                    else { c.stroke(Self.star, with: .color(brush.opacity(0.26 * 2.5)), lineWidth: 1.6) }
                }
            }
            .contentShape(Rectangle())
            .onContinuousHover { phase in
                if case .active(let p) = phase { preview = Int(p.x / max(1, g.size.width) * 5 + 1).clamped(1, 5) } else { preview = -1 }
            }
            .onTapGesture { loc in let r = Int(loc.x / max(1, g.size.width) * 5 + 1).clamped(1, 5); preview = -1; onRate(r == value ? 0 : r) }
        }
        .help(value == 0 ? tr("Yıldız ver · Aynı yıldıza tekrar tıklayarak kaldır") : "\(value)/5" + tr(" yıldız · Aynı yıldıza tekrar tıklayarak kaldır"))
    }
}

struct WaveformView: View {
    @Environment(\.theme) var theme
    let data: WaveformData?
    let hint: String
    static let low = Color(.sRGB, red: 242 / 255, green: 232 / 255, blue: 207 / 255), mid = Color(.sRGB, red: 191 / 255, green: 123 / 255, blue: 53 / 255), high = Color(.sRGB, red: 37 / 255, green: 68 / 255, blue: 181 / 255)
    var body: some View {
        Canvas { ctx, size in
            guard let data, data.valid else {
                ctx.draw(Text(hint.hasPrefix("Oluşturulamadı") ? tr("Önizleme yok") : tr("Hazırlanıyor…")).font(.system(size: 10)).foregroundStyle(Color(red: 0.44, green: 0.5, blue: 0.56)), at: CGPoint(x: 0, y: size.height / 2), anchor: .leading)
                return
            }
            func layer(_ n: Int, _ color: Color) {
                var p = Path(); p.move(to: CGPoint(x: 0, y: size.height))
                for i in 0..<data.low.count {
                    let e = Double(data.low[i]) + (n >= 1 ? Double(data.mid[i]) : 0) + (n >= 2 ? Double(data.high[i]) : 0)
                    p.addLine(to: CGPoint(x: Double(i) * size.width / Double(data.low.count - 1), y: size.height - max(1, e * (size.height - 2))))
                }
                p.addLine(to: CGPoint(x: size.width, y: size.height)); p.closeSubpath()
                ctx.fill(p, with: .color(color))
            }
            layer(2, Self.high); layer(1, Self.mid); layer(0, Self.low)
        }
    }
}

// MARK: - Context menu (BuildTrackMenu)

struct TrackMenu: View {
    @Environment(AppModel.self) var model
    let track: Track
    var selection: [Track] {
        let sel = model.trackRows.filter { model.selectedPaths.contains($0.path) }
        return sel.contains { $0 === track } ? sel : [track]
    }
    var body: some View {
        let t = track
        Button(tr(model.favorites.isTrackFavorite(t.path) ? "Favorilerden çıkar" : "Favorilere ekle")) { model.toggleTrackFavorite(t) }
        Menu(tr("Yıldız ver")) {
            ForEach(1...5, id: \.self) { s in
                Button((model.favorites.rating(t.path) == s ? "✓  " : "    ") + String(repeating: "★", count: s) + " · \(s)/5") { model.rate(t, s) }
            }
            Button(tr("Yıldızı kaldır")) { model.rate(t, 0) }.disabled(model.favorites.rating(t.path) == 0)
        }
        Button(tr(model.favorites.isAlbumFavorite(t.albumKey) ? "Albümü favorilerden çıkar" : "Albümü favorilere ekle")) { model.toggleAlbumFavorite(t.albumKey) }
        Divider()
        let sel = selection
        Button(sel.count > 1 ? tr("Seçili ") + "\(sel.count)" + tr(" parçayı sıraya ekle") : tr("Sıraya ekle")) { model.enqueue(sel, next: false) }
        Button(tr("Hemen sonra çal")) { model.enqueue(sel, next: true) }
        if model.view == "queue" {
            Button(tr("Seçilenleri kuyruktan çıkar")) { model.removeFromQueue(Set(sel.map(\.path))) }
            Button(tr("Kuyruk değişikliğini geri al · Ctrl+Z")) { model.undoQueue() }.disabled(model.queueUndo.isEmpty)
        }
        if model.view == "album" || model.view == "playlist" {
            Button(model.view == "album" ? tr("Albümün tamamını sıraya ekle") : tr("Listenin tamamını sıraya ekle")) { model.enqueue(model.trackRows, next: false) }
        }
        Menu(tr("Çalma listesine ekle")) {
            ForEach(model.playlists.filter { $0.smartQuery == nil }.sorted { $0.name.ciCompare($1.name) == .orderedAscending }) { p in
                Button(p.name) { model.addToPlaylist(p, sel) }
            }
            Button(tr("＋ Yeni çalma listesi…")) { PlaylistEditor.open(model, existing: nil, smart: false, initial: sel) }
        }
        Button(tr("Albümü aç")) { model.openTrackAlbum(t) }
        Button(tr("Finder’da göster")) { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: t.audioPath)]) }
        if model.view == "queue" { Button(tr("Kuyruğu temizle")) { model.clearQueue() } }
        if model.view == "playlist", let current = model.currentPlaylist, current.smartQuery == nil {
            let idx = current.tracks.firstIndex { $0.caseInsensitiveCompare(t.path) == .orderedSame } ?? -1
            Button(tr("Listede yukarı taşı")) { PlaylistStore.move(current, t.path, -1); model.savePlaylists(); model.refresh() }.disabled(idx <= 0)
            Button(tr("Listede aşağı taşı")) { PlaylistStore.move(current, t.path, 1); model.savePlaylists(); model.refresh() }.disabled(idx < 0 || idx >= current.tracks.count - 1)
            Button(tr("Bu listeden çıkar")) { current.tracks.removeAll { $0.caseInsensitiveCompare(t.path) == .orderedSame }; model.savePlaylists(); model.refresh() }
        }
    }
}

// MARK: - Album / playlist hero

struct HeroHeader: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        GeometryReader { geo in
            let ui = theme.ui
            let width = geo.size.width
            let cover = (width * 0.32).clamped(206, ui.coverMaximum)
            let compact = model.view == "album" && width < 850
            HStack(alignment: compact ? .center : .bottom, spacing: 0) {
                coverView.frame(width: cover, height: cover)
                VStack(alignment: .leading, spacing: 0) {
                    Spacer(minLength: 0)
                    Text(model.view == "album" ? tr("ALBÜM") : tr("ÇALMA LİSTESİ")).font(theme.font(12, .semibold))
                    Text(title).font(theme.font(ui.albumTitleSize, .bold)).lineLimit(3).minimumScaleFactor(0.45).padding(.top, 12).padding(.bottom, 14)
                    summary
                    if compact { tools.padding(.top, 16) }
                }
                .padding(.leading, 22)
                .frame(maxWidth: .infinity, alignment: .leading)
                if !compact { tools.padding(.leading, 20) }
            }
            .padding(26)
            .frame(width: width, height: geo.size.height, alignment: .bottomLeading)
        }
        .frame(height: theme.ui.coverMaximum + 52)
    }

    var album: [Track] { model.library.filter { $0.albumKey == model.albumKey } }
    var title: String { model.view == "album" ? (album.first?.album ?? "") : (model.currentPlaylist?.name ?? "") }

    @ViewBuilder var coverView: some View {
        if model.view == "playlist", let p = model.currentPlaylist {
            ZStack {
                RoundedRectangle(cornerRadius: theme.cardRadius).fill(Color(hex: p.color))
                Text("♫").font(theme.font(60)).foregroundStyle(theme.muted)
                Thumb(path: p.cover.isEmpty ? nil : p.cover, pixels: 720, radius: theme.cardRadius)
            }
        } else {
            ZStack {
                RoundedRectangle(cornerRadius: theme.cardRadius).fill(theme.surface)
                Text("♫").font(theme.font(60)).foregroundStyle(theme.muted)
                Thumb(path: album.lazy.map(\.cover).first { $0.fileExists }, pixels: 720, radius: theme.cardRadius)
            }
        }
    }

    @ViewBuilder var summary: some View {
        if model.view == "album", let first = album.first {
            let year = { () -> String in var y = first.fields.first { $0.key.uppercased().hasSuffix("/ DATE") || $0.key.uppercased().hasSuffix("/ YEAR") }?.value ?? ""; if y.count > 4 { y = String(y.prefix(4)) }; return y }()
            let total = album.reduce(0) { $0 + $1.seconds }
            let hours = Int(total / 3600), minutes = Int(total.truncatingRemainder(dividingBy: 3600) / 60)
            let suffix = (year.isEmpty ? "" : " · " + year) + " · \(album.count)" + tr(" parça · ") + (hours >= 1 ? "\(hours)" + tr(" sa. ") : "") + "\(minutes)" + tr(" dk.")
            ArtistLinks(names: first.albumArtist, suffix: suffix, size: 15, color: theme.muted)
        } else if let p = model.currentPlaylist {
            let songs = model.trackRows
            Text((p.description.isEmpty ? "" : p.description + "\n") + "\(songs.count)" + tr(" parça · ") + "\(Int(songs.reduce(0) { $0 + $1.seconds } / 60))" + tr(" dk.") + (songs.count < p.tracks.count && p.smartQuery == nil ? " · \(p.tracks.count - songs.count)" + tr(" parça arşivde bulunamadı") : ""))
                .font(theme.font(15)).foregroundStyle(theme.muted)
        }
    }

    @ViewBuilder var tools: some View {
        let h = theme.ui.albumButtonHeight
        VStack(alignment: .trailing, spacing: 10) {
            if model.view == "playlist" {
                HStack(spacing: 8) {
                    Button { if let p = model.currentPlaylist { PlaylistEditor.open(model, existing: p, smart: p.smartQuery != nil) } } label: { Icon(name: "edit", size: 19, color: theme.ink) }
                        .buttonStyle(FButtonStyle(padding: EdgeInsets(top: 12, leading: 12, bottom: 12, trailing: 12))).help(tr("Listeyi düzenle"))
                    Button { if let p = model.currentPlaylist { model.deletePlaylist(p) } } label: { Icon(name: "trash", size: 19, color: theme.ink) }
                        .buttonStyle(FButtonStyle(padding: EdgeInsets(top: 12, leading: 12, bottom: 12, trailing: 12))).help(tr("Listeyi sil"))
                }
                Spacer(minLength: 0)
            }
            if model.view == "album" && theme.ui.showAlbumQuality {
                let q = AlbumAudioInfo.from(album)
                Text(q.text).font(theme.font(12, .medium)).multilineTextAlignment(.center).padding(.horizontal, 8)
                    .frame(width: 136 + 44 + 12, height: h)
                    .background(RoundedRectangle(cornerRadius: theme.cardRadius).fill(theme.control)).help(q.details)
            }
            HStack(spacing: 12) {
                if model.view == "album" {
                    let fav = model.favorites.isAlbumFavorite(model.albumKey)
                    Button { model.toggleAlbumFavorite(model.albumKey) } label: { Icon(name: fav ? "heart-filled" : "heart", size: 24, color: theme.ink).frame(width: 44, height: h) }
                        .buttonStyle(FButtonStyle(padding: EdgeInsets())).help(tr(fav ? "Albümü favorilerden çıkar" : "Albümü favorilere ekle"))
                }
                Button {
                    let songs = model.trackRows
                    if !songs.isEmpty { Task { await model.startFromView(songs[model.shuffle ? Int.random(in: 0..<songs.count) : 0]) } }
                } label: {
                    HStack(spacing: 10) { Icon(name: "play", size: 20, color: theme.onAccent); Text(tr("Oynat")).font(theme.font(15, .semibold)).foregroundStyle(theme.onAccent) }
                        .frame(width: 136, height: h)
                }
                .buttonStyle(TransportStyle(size: 0, radius: theme.cardRadius, fill: theme.accent)).frame(width: 136, height: h)
                .help(tr("Oynat"))
            }
        }
    }
}

// MARK: - Artist hero + profile (ArtistProfile.cs)

struct ArtistHero: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        let tracks = model.library.filter { ArtistCatalog.matches($0, model.artistName) }
        let fallback = tracks.lazy.map(\.cover).first { $0.fileExists }
        let backdrop = model.artistPhotoPath ?? fallback
        ZStack(alignment: .bottomLeading) {
            theme.surface
            if let backdrop {
                Thumb(path: backdrop, pixels: 1400).opacity(model.artistPhotoPath != nil ? 1 : 0.45).blur(radius: model.artistPhotoPath != nil ? 0 : 17)
            }
            LinearGradient(colors: [Color.black.opacity(model.artistPhotoPath != nil ? 0 : 0.38), Color.black.opacity(0.85)], startPoint: .topLeading, endPoint: .bottomTrailing)
            HStack(alignment: .center, spacing: 0) {
                if model.artistPhotoPath == nil {
                    Thumb(path: fallback, pixels: 400).frame(width: 144, height: 144).clipShape(theme.ui.circularArtists ? AnyShape(Circle()) : AnyShape(RoundedRectangle(cornerRadius: theme.cardRadius))).padding(.trailing, 24)
                }
                VStack(alignment: .leading, spacing: 0) {
                    if model.artistPhotoPath == nil { Text(tr("SANATÇI")).font(theme.font(11, .semibold)).foregroundStyle(.white.opacity(0.87)) }
                    Text(model.artistName).font(theme.font(54, .bold)).foregroundStyle(.white).lineLimit(2).minimumScaleFactor(0.5).padding(.top, 8).padding(.bottom, 9)
                    if model.artistPhotoPath == nil {
                        Text("\(Set(tracks.map(\.albumKey)).count)" + tr(" albüm · ") + "\(tracks.count)" + tr(" parça")).font(theme.font(14)).foregroundStyle(.white.opacity(0.87))
                    }
                }
            }
            .padding(.horizontal, 30).padding(.vertical, 26)
            if let credit = model.artistPhotoCredit, !credit.local, let url = URL(string: credit.sourceUrl) {
                Button(tr("Fotoğraf kaynağı ↗")) { NSWorkspace.shared.open(url) }
                    .buttonStyle(FButtonStyle(background: Color.black.opacity(0.5), foreground: .white, padding: EdgeInsets(top: 6, leading: 9, bottom: 6, trailing: 9)))
                    .font(theme.font(10)).help(credit.credit)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topTrailing).padding(12)
            }
        }
        .frame(height: 245).clipped()
        .contextMenu { ArtistPhotoMenu(artist: model.artistName) }
    }
}

/// ArtistAboutCard (MainWindow.xaml): always present on the profile; shows loading / missing / offline text,
/// and the Wikipedia + licence buttons only when an article was found.
struct ArtistBiographyCard: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        let bio = model.artistBiography
        let found = bio.map { !$0.text.isEmpty } ?? false
        VStack(alignment: .leading, spacing: 0) {
            Text(tr("Sanatçı hakkında")).font(theme.font(20, .semibold)).padding(.bottom, 16)
            Text(model.artistBiographyText).font(theme.font(15)).foregroundStyle(theme.muted)
                .lineSpacing(26 - 15 * 1.2).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
            if found, let bio {
                HStack(spacing: 0) {
                    Button(tr("Wikipedia'da oku ↗")) {
                        if ArtistBiographyService.articleUrl(bio.url, L.current), let url = URL(string: bio.url) { NSWorkspace.shared.open(url) }
                    }
                    .buttonStyle(FButtonStyle(padding: EdgeInsets(top: 8, leading: 12, bottom: 8, trailing: 12))).padding(.trailing, 12)
                    Button("CC BY-SA 4.0 ↗") { NSWorkspace.shared.open(URL(string: "https://creativecommons.org/licenses/by-sa/4.0/")!) }
                        .buttonStyle(FButtonStyle(background: .clear, foreground: theme.muted, padding: EdgeInsets(top: 8, leading: 12, bottom: 8, trailing: 12)))
                }
                .font(theme.font(12)).padding(.top, 20)
            }
        }
        .padding(26).frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: theme.panelRadius).fill(theme.surface).overlay(RoundedRectangle(cornerRadius: theme.panelRadius).strokeBorder(theme.line, lineWidth: 1)))
    }
}
