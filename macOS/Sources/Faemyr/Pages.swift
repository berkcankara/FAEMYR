import SwiftUI
import AppKit
import FaemyrCore

// MARK: - Artist links (ArtistLinks.cs)

struct ArtistLinks: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Environment(\.multilineTextAlignment) var alignment
    let names: String
    var suffix: String = ""
    var size: Double = 12
    var color: Color? = nil
    var weight: Font.Weight = .regular
    var linkable = true
    @Local private var hovered: Int?

    /// Like the ArtistHyperlink style on Windows: the name under the pointer turns accent-coloured and
    /// underlined, with a hand cursor. A single credit stays one Text so wrapping and truncation still work.
    var body: some View {
        let credits = ArtistCatalog.credits(names)
        if credits.count <= 1 {
            let label = credits.first?.label ?? names
            let link = linkable && credits.first != nil
            (piece(label, link && hovered == 0) + Text(suffix))
                .font(theme.font(size, weight)).foregroundStyle(color ?? theme.ink)
                .modifier(LinkHover(enabled: link) { hovered = $0 ? 0 : nil })
                .onTapGesture { if link, let c = credits.first { model.openArtist(c.profile) } }
        } else {
            FlowRow(alignment: alignment) {
                ForEach(Array(credits.enumerated()), id: \.offset) { i, c in
                    let last = i == credits.count - 1
                    (piece(c.label, linkable && hovered == i) + Text(last ? suffix : " / "))
                        .font(theme.font(size, weight)).foregroundStyle(color ?? theme.ink)
                        .modifier(LinkHover(enabled: linkable) { hovered = $0 ? i : (hovered == i ? nil : hovered) })
                        .onTapGesture { if linkable { model.openArtist(c.profile) } }
                }
            }
        }
    }
    private func piece(_ text: String, _ active: Bool) -> Text {
        Text(text).underline(active).foregroundColor(active ? theme.accent : (color ?? theme.ink))
    }
}

/// Hand cursor while hovering a clickable artist name.
struct LinkHover: ViewModifier {
    let enabled: Bool
    let changed: (Bool) -> Void
    @Local private var inside = false
    func body(content: Content) -> some View {
        content.onHover { h in
            guard enabled, h != inside else { return }
            inside = h; changed(h)
            if h { NSCursor.pointingHand.push() } else { NSCursor.pop() }
        }
        .onDisappear { if inside { NSCursor.pop(); inside = false } }
    }
}

/// Wraps artist credits like text: pieces flow onto new lines when the row is full.
struct FlowRow: Layout {
    var alignment: TextAlignment = .leading
    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = arrange(proposal.width ?? .infinity, subviews)
        return CGSize(width: rows.map(\.width).max() ?? 0, height: rows.reduce(0) { $0 + $1.height })
    }
    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var y = bounds.minY
        for row in arrange(bounds.width, subviews) {
            var x = alignment == .center ? bounds.minX + (bounds.width - row.width) / 2 : alignment == .trailing ? bounds.maxX - row.width : bounds.minX
            for (i, size) in row.items {
                subviews[i].place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(size)); x += size.width
            }
            y += row.height
        }
    }
    private struct Row { var items: [(Int, CGSize)] = []; var width = 0.0; var height = 0.0 }
    private func arrange(_ maxWidth: Double, _ subviews: Subviews) -> [Row] {
        var rows = [Row()]
        for i in subviews.indices {
            var size = subviews[i].sizeThatFits(.unspecified)
            if size.width > maxWidth { size = subviews[i].sizeThatFits(ProposedViewSize(width: maxWidth, height: nil)) }
            if !rows[rows.count - 1].items.isEmpty && rows[rows.count - 1].width + size.width > maxWidth { rows.append(Row()) }
            rows[rows.count - 1].items.append((i, size)); rows[rows.count - 1].width += size.width
            rows[rows.count - 1].height = max(rows[rows.count - 1].height, size.height)
        }
        return rows
    }
}

// MARK: - Main content (MainContent grid)

struct MainContent: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        let p = theme.ui
        let hero = model.isHero
        let lead = p.contentPadding, trail = p.contentPadding - 4, bottom = max(10, p.contentPadding - 12)
        VStack(alignment: .leading, spacing: 0) {
            if hero { HeroHeader() }
            else if model.view == "artist" { EmptyView() }
            else { PageHeader().padding(.top, p.contentPadding - 2).padding(.leading, lead).padding(.trailing, trail) }
            Group {
                if !hero && model.view != "artist" && p.showLibraryStats {
                    Text(model.libraryStats).foregroundStyle(theme.muted).padding(.top, 14).padding(.bottom, 22)
                } else if !hero && model.view != "artist" { Color.clear.frame(height: 16) }
                if model.view != "artist" { CollectionToolbar() }
            }
            .padding(.leading, lead).padding(.trailing, trail)
            ZStack {
                content
                if model.library.isEmpty && model.libraryReady && !["playlists", "playlist", "favorites"].contains(model.view) { LibraryEmptyState() }
                else if model.showCollectionEmpty { CollectionEmpty() }
            }
            .padding(hero ? EdgeInsets(top: 8, leading: 8, bottom: 8, trailing: 8) : model.view == "artist" ? EdgeInsets() : model.isCollection ? EdgeInsets(top: 0, leading: lead, bottom: bottom, trailing: trail) : EdgeInsets())
            if model.scanning && !model.status.isEmpty {
                Text(model.status).font(theme.font(11)).foregroundStyle(theme.muted).lineLimit(2).padding(.top, 8).padding(.leading, lead).padding(.bottom, 8)
            }
        }
    }

    @ViewBuilder var content: some View {
        switch model.view {
        case "albums": AlbumGallery()
        case "artist": ArtistProfileView()
        case "favorites" where model.favoritesTab == "albums": AlbumGallery()
        case "artists": ArtistGallery()
        case "playlists": PlaylistGallery()
        default: TrackTable()
        }
    }
}

struct PageHeader: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        HStack(alignment: .center) {
            Text(model.pageTitle).font(theme.font(theme.ui.pageTitleSize, .semibold)).lineLimit(1)
            Spacer(minLength: 16)
            if model.view == "albums" { AlbumSortTools(grouping: true) }
            if model.view == "artists" { ArtistSortTools() }
        }
    }
}

struct AlbumSortTools: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var grouping: Bool
    var body: some View {
        @Bindable var model = model
        HStack(spacing: 0) {
            Text(tr("Sırala")).font(theme.font(11)).foregroundStyle(theme.muted).padding(.trailing, 10)
            FPicker(items: albumSortChoices, label: { $0.label }, selection: Binding(get: { albumSortChoices.first { $0.mode == model.albumSort.mode }! }, set: { model.albumSort.mode = $0.mode; model.albumSort.save(); model.refresh() }), height: 36)
                .frame(width: 235)
            if grouping {
                let alphabetical = model.albumSort.mode == .artist || model.albumSort.mode == .title
                GroupingToggle(on: alphabetical && model.albumSort.groupByLetter, enabled: alphabetical) { model.albumSort.groupByLetter.toggle(); model.albumSort.save(); model.refresh() }
            }
        }
    }
}

struct ArtistSortTools: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        HStack(spacing: 0) {
            Text(tr("Sırala")).font(theme.font(11)).foregroundStyle(theme.muted).padding(.trailing, 10)
            FPicker(items: artistSortChoices, label: { $0.label }, selection: Binding(get: { artistSortChoices.first { $0.mode == model.albumSort.artistMode }! }, set: { model.albumSort.artistMode = $0.mode; model.albumSort.save(); model.refresh() }), height: 36)
                .frame(width: 235)
            GroupingToggle(on: model.albumSort.groupByLetter, enabled: true) { model.albumSort.groupByLetter.toggle(); model.albumSort.save(); model.refresh() }
        }
    }
}

struct GroupingToggle: View {
    @Environment(\.theme) var theme
    let on: Bool, enabled: Bool
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            HoverWrap { h in
                Icon(name: "grouped", size: 22, color: on ? theme.onAccent : theme.ink)
                    .frame(width: 36, height: 36)
                    .background(RoundedRectangle(cornerRadius: theme.buttonRadius).fill(on ? theme.accent : h ? theme.hover : theme.control))
                    .overlay(RoundedRectangle(cornerRadius: theme.buttonRadius).strokeBorder(on ? theme.accent : theme.line, lineWidth: 1))
            }
        }
        .buttonStyle(.plain).disabled(!enabled).opacity(enabled ? 1 : 0.4).padding(.leading, 8)
        .help(!enabled ? tr("Kategorili görünüm yalnızca alfabetik sıralamada kullanılabilir.") : on ? tr("Kategorili görünümü kapat") : tr("Kategorili görünümü aç"))
    }
}

struct CollectionToolbar: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        @Bindable var model = model
        let v = model.view
        if v == "albums" || v == "artists" || model.isHero { EmptyView() }
        else {
            VStack(alignment: .leading, spacing: 0) {
                HStack {
                    if v == "favorites" {
                        HStack(spacing: 8) {
                            tab(tr("Parçalar"), "tracks"); tab(tr("Albümler"), "albums")
                        }
                    } else if v == "tracks" {
                        HStack(spacing: 10) {
                            FPicker(items: Array(smartPresets.indices), label: { smartPresets[$0].name }, selection: Binding(get: { model.smartViewIndex }, set: { model.smartViewIndex = $0; model.refresh() }), height: 44).frame(width: 215)
                            FPicker(items: Array(ratingFilterChoices.indices), label: { ratingFilterChoices[$0].label }, selection: Binding(get: { model.ratingFilterIndex }, set: { model.ratingFilterIndex = $0; model.refresh() }), height: 44).frame(width: 175)
                            Button(tr("Filtre yardımı")) { model.alert(TrackQuery.help, title: tr("Faemyr — Filtre yardımı")) }.buttonStyle(FButtonStyle()).frame(height: 44)
                        }
                    } else if !model.sectionTitle.isEmpty {
                        Text(model.sectionTitle).font(theme.font(18, .semibold))
                    }
                    Spacer(minLength: 12)
                    if v == "queue" {
                        Button(tr("Geri al")) { model.undoQueue() }.buttonStyle(FButtonStyle()).disabled(model.queueUndo.isEmpty).help(tr("Kuyruk değişikliğini geri al · Ctrl+Z"))
                    }
                    if v == "playlists" {
                        Button(tr("＋ Akıllı liste")) { PlaylistEditor.open(model, existing: nil, smart: true) }.buttonStyle(FButtonStyle())
                        Button(tr("＋ Yeni çalma listesi")) { PlaylistEditor.open(model, existing: nil, smart: false) }.buttonStyle(FButtonStyle(background: theme.accent, foreground: theme.onAccent))
                    }
                    if v == "artist" { AlbumSortTools(grouping: false) }
                }
                if !model.smartFilterError.isEmpty { Text(model.smartFilterError).foregroundStyle(theme.accent).padding(.top, 6) }
            }
            .frame(minHeight: 44)
            .padding(.bottom, 16)
            .padding(.top, v == "artist" ? 0 : 0)
        }
    }
    func tab(_ title: String, _ key: String) -> some View {
        let active = model.favoritesTab == key
        return Button(title) { model.favoritesTab = key; model.refresh() }
            .buttonStyle(FButtonStyle(background: active ? theme.accent : theme.control, foreground: active ? theme.onAccent : theme.ink, padding: EdgeInsets(top: 0, leading: 16, bottom: 0, trailing: 16)))
            .frame(height: 36)
    }
}

// MARK: - Galleries

struct AlbumGallery: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var edges = ScrollEdges()
    var body: some View {
        ScrollView {
            AlbumRows()
                .background(ScrollEdgeProbe(edges: $edges))
                .id(model.revision)
        }
        .scrollIndicators(theme.ui.autoHideScrollbars ? .automatic : .visible)
        .mask(EdgeFade(edges: edges))
    }
}

struct AlbumRows: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        let gap = theme.ui.cardGap
        LazyVStack(alignment: .leading, spacing: 0) {
            ForEach(model.albumRows) { row in
                VStack(alignment: .leading, spacing: 0) {
                    if !row.heading.isEmpty { Text(row.heading).font(theme.font(22, .semibold)).padding(EdgeInsets(top: 12, leading: 9, bottom: 5, trailing: 9)) }
                    HStack(alignment: .top, spacing: 0) {
                        ForEach(row.cards) { card in AlbumCardView(card: card).padding(EdgeInsets(top: 0, leading: gap, bottom: gap + 14, trailing: gap)).frame(maxWidth: .infinity) }
                        ForEach(0..<max(0, theme.ui.albumColumns - row.cards.count), id: \.self) { _ in Color.clear.frame(maxWidth: .infinity, maxHeight: 1) }
                    }
                }
            }
        }
    }
}

/// ArtistProfileScroll (ArtistProfile.cs): hero, toolbar, albums and the "about" card scroll together.
struct ArtistProfileView: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var edges = ScrollEdges()
    var body: some View {
        let padding = theme.ui.contentPadding
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    ArtistHero().id("top").padding(.bottom, 28)
                    CollectionToolbar().padding(.horizontal, padding)
                    AlbumRows().padding(.horizontal, padding)
                    ArtistBiographyCard().padding(EdgeInsets(top: 28, leading: padding, bottom: 32, trailing: padding))
                }
                .background(ScrollEdgeProbe(edges: $edges))
            }
            .scrollIndicators(theme.ui.autoHideScrollbars ? .automatic : .visible)
            .mask(EdgeFade(edges: edges))
            .onChange(of: model.artistName) { proxy.scrollTo("top", anchor: .top) }
        }
    }
}

struct AlbumCardView: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    let card: AlbumCard
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Button { model.openAlbum(card.key) } label: {
                VStack(alignment: .leading, spacing: 0) {
                    ZStack {
                        RoundedRectangle(cornerRadius: theme.cardRadius, style: .continuous).fill(theme.surface)
                        Text("♫").font(theme.font(66)).foregroundStyle(Color(hex: "#858585"))
                        Thumb(path: card.cover, pixels: 480, radius: theme.cardRadius)
                    }
                    .aspectRatio(1, contentMode: .fit)
                    Text(card.title).font(theme.font(15, .semibold)).lineLimit(1).truncationMode(.tail).padding(.top, 12).padding(.bottom, 5)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(card.tooltip)
            if theme.ui.showCardDetails {
                ArtistLinks(names: card.artist, suffix: card.countLabel, size: 12, color: theme.muted).lineLimit(1)
            }
        }
        .contextMenu {
            Button(tr(model.favorites.isAlbumFavorite(card.key) ? "Favorilerden çıkar" : "Favorile")) { model.toggleAlbumFavorite(card.key) }
            Button(tr("Sıraya ekle")) { model.enqueue(AlbumDiscs.order(model.library.filter { $0.albumKey == card.key }), next: false) }
            Button(tr("Hemen sonra çal")) { model.enqueue(AlbumDiscs.order(model.library.filter { $0.albumKey == card.key }), next: true) }
        }
    }
}

struct ArtistGallery: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var edges = ScrollEdges()
    var body: some View {
        let gap = theme.ui.cardGap
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 0) {
                ForEach(model.artistRows) { row in
                    VStack(alignment: .leading, spacing: 0) {
                        if !row.heading.isEmpty { Text(row.heading).font(theme.font(22, .semibold)).padding(EdgeInsets(top: 12, leading: 9, bottom: 5, trailing: 9)) }
                        HStack(alignment: .top, spacing: 0) {
                            ForEach(row.cards) { card in ArtistCardView(card: card).padding(EdgeInsets(top: 0, leading: gap, bottom: gap + 14, trailing: gap)).frame(maxWidth: .infinity) }
                            ForEach(0..<max(0, theme.ui.artistColumns - row.cards.count), id: \.self) { _ in Color.clear.frame(maxWidth: .infinity, maxHeight: 1) }
                        }
                    }
                }
            }
            .background(ScrollEdgeProbe(edges: $edges))
            .id(model.revision)
        }
        .mask(EdgeFade(edges: edges))
    }
}

struct ArtistCardView: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    let card: ArtistCard
    var body: some View {
        let radius: CGFloat = theme.ui.circularArtists ? 10000 : theme.cardRadius
        Button { model.openArtist(card.name) } label: {
            VStack(alignment: .leading, spacing: 0) {
                ArtistPhotoView(artist: card.name, fallback: card.cover, radius: radius)
                    .aspectRatio(1, contentMode: .fit)
                Text(card.name).font(theme.font(17, .semibold)).lineLimit(1).padding(.top, 13).padding(.bottom, 6)
                if theme.ui.showCardDetails { Text(card.detail).font(theme.font(12)).foregroundStyle(theme.muted).lineLimit(1) }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain).help(card.name)
        .contextMenu { ArtistPhotoMenu(artist: card.name) }
    }
}

struct ArtistPhotoView: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    let artist: String
    let fallback: String?
    var radius: CGFloat
    @Local private var photo: String?
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: min(radius, 2000), style: .continuous).fill(theme.surface)
            if photo == nil && fallback == nil { Image(systemName: "person.fill").font(.system(size: 40)).foregroundStyle(theme.muted) }
            Thumb(path: photo ?? fallback, pixels: 480, radius: 0)
        }
        .clipShape(radius >= 1000 ? AnyShape(Circle()) : AnyShape(RoundedRectangle(cornerRadius: radius, style: .continuous)))
        .task(id: artist + "\(model.photoPrefs.enabled)\(model.photoPrefs.onCards)\(model.revision)") {
            guard model.photoPrefs.enabled, model.photoPrefs.onCards, !model.photoPrefs.usesAlbumCover(artist) else { photo = nil; return }
            photo = ArtistPhotoStore.shared.cachedImagePath(artist)
            if let p = await ArtistPhotoStore.shared.get(artist), !Task.isCancelled { photo = ArtistPhotoStore.shared.imagePath(p) }
        }
    }
}

struct ArtistPhotoMenu: View {
    @Environment(AppModel.self) var model
    let artist: String
    var body: some View {
        Button(tr("Yerel fotoğraf seç…")) {
            let p = NSOpenPanel(); p.allowedContentTypes = [.image]; guard p.runModal() == .OK, let url = p.url, let data = try? Data(contentsOf: url) else { return }
            Task { _ = try? await ArtistPhotoStore.shared.importLocal(artist, data); setCover(false); model.revision &+= 1; if model.view == "artist" { model.loadArtistProfile() } }
        }
        Button(tr("Çevrimiçi fotoğrafı yenile")) { Task { _ = await ArtistPhotoStore.shared.get(artist, force: true); setCover(false); model.revision &+= 1; if model.view == "artist" { model.loadArtistProfile() } } }
        Button(tr("Albüm kapağını kullan")) { setCover(true); model.revision &+= 1; if model.view == "artist" { model.loadArtistProfile() } }
    }
    func setCover(_ on: Bool) {
        model.photoPrefs.albumCoverArtists.removeAll { $0.caseInsensitiveCompare(artist) == .orderedSame }
        if on { model.photoPrefs.albumCoverArtists.append(artist) }
        model.photoPrefs.save()
    }
}

struct PlaylistGallery: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    @Local private var edges = ScrollEdges()
    var body: some View {
        let cols = theme.ui.playlistColumns, gap = theme.ui.cardGap
        let rows = stride(from: 0, to: model.playlistCards.count, by: cols).map { Array(model.playlistCards[$0..<min($0 + cols, model.playlistCards.count)]) }
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 0) {
                ForEach(rows.indices, id: \.self) { i in
                    HStack(alignment: .top, spacing: 0) {
                        ForEach(rows[i]) { p in
                            Button { model.openPlaylist(p.id) } label: {
                                VStack(alignment: .leading, spacing: 0) {
                                    ZStack {
                                        RoundedRectangle(cornerRadius: theme.panelRadius, style: .continuous).fill(Color(hex: p.color))
                                        Text(p.initial).font(theme.font(58, .bold)).foregroundStyle(.white)
                                        Thumb(path: p.cover.isEmpty ? nil : p.cover, pixels: 480, radius: theme.cardRadius)
                                    }
                                    .aspectRatio(1, contentMode: .fit)
                                    Text(p.name).font(theme.font(17, .semibold)).lineLimit(1).padding(.top, 12).padding(.bottom, 6)
                                    if theme.ui.showCardDetails { Text(p.detail).font(theme.font(12)).foregroundStyle(theme.muted) }
                                }.contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .padding(EdgeInsets(top: 0, leading: gap, bottom: gap + 14, trailing: gap)).frame(maxWidth: .infinity)
                        }
                        ForEach(0..<max(0, cols - rows[i].count), id: \.self) { _ in Color.clear.frame(maxWidth: .infinity, maxHeight: 1) }
                    }
                }
            }
            .background(ScrollEdgeProbe(edges: $edges))
            .id(model.revision)
        }
        .mask(EdgeFade(edges: edges))
    }
}

struct ScrollEdges: Equatable { var top = false, bottom = false }

/// ScrollEdgeFade.cs: a cosine fade at an edge only while more content lies beyond it, so the first and
/// last rows stay fully opaque at their real scroll boundary. 9 stops per side over min(25%, 56 pt).
struct EdgeFade: View {
    var edges = ScrollEdges(top: true, bottom: true)
    var body: some View {
        GeometryReader { g in
            let edge = min(0.25, 56 / max(1, g.size.height))
            LinearGradient(stops: Self.stops(edge, edges), startPoint: .top, endPoint: .bottom)
        }
        .animation(.easeOut(duration: 0.32), value: edges)
    }
    static func stops(_ edge: Double, _ e: ScrollEdges) -> [Gradient.Stop] {
        var out: [Gradient.Stop] = []
        for side in 0..<2 { for i in 0...8 {
            let t = Double(side == 0 ? i : 8 - i) / 8, a = (1 - cos(Double.pi * t)) / 2
            let active = side == 0 ? e.top : e.bottom
            out.append(.init(color: Color.black.opacity(active ? a : 1), location: side == 0 ? edge * t : 1 - edge * t))
        } }
        return out
    }
}

/// Placed inside a ScrollView's content: tracks the enclosing NSScrollView and reports which edges have
/// content scrolled out of view.
struct ScrollEdgeProbe: NSViewRepresentable {
    @Binding var edges: ScrollEdges
    func makeNSView(context: Context) -> ProbeView { let v = ProbeView(); v.report = { e in if edges != e { edges = e } }; return v }
    func updateNSView(_ v: ProbeView, context: Context) { v.report = { e in if edges != e { edges = e } }; v.schedule() }
    final class ProbeView: NSView {
        var report: ((ScrollEdges) -> Void)?
        private var observed: NSScrollView?
        override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); attach() }
        override func layout() { super.layout(); schedule() }
        private func attach() {
            guard let scroll = enclosingScrollView, scroll !== observed else { schedule(); return }
            NotificationCenter.default.removeObserver(self)
            observed = scroll
            scroll.contentView.postsBoundsChangedNotifications = true
            NotificationCenter.default.addObserver(self, selector: #selector(changed), name: NSView.boundsDidChangeNotification, object: scroll.contentView)
            NotificationCenter.default.addObserver(self, selector: #selector(changed), name: NSView.frameDidChangeNotification, object: scroll.contentView)
            if let doc = scroll.documentView { doc.postsFrameChangedNotifications = true; NotificationCenter.default.addObserver(self, selector: #selector(changed), name: NSView.frameDidChangeNotification, object: doc) }
            schedule()
        }
        func schedule() { DispatchQueue.main.async { [weak self] in self?.update() } }
        @objc private func changed() { update() }
        private func update() {
            if observed == nil || observed !== enclosingScrollView { if enclosingScrollView != nil { attach() }; return }
            guard let scroll = observed, let doc = scroll.documentView else { return }
            let clip = scroll.contentView.bounds
            let offset = doc.isFlipped ? clip.minY : doc.frame.height - clip.maxY
            let remaining = doc.frame.height - clip.height - offset
            report?(ScrollEdges(top: offset > 0.5, bottom: remaining > 0.5))
        }
        deinit { NotificationCenter.default.removeObserver(self) }
    }
}

struct LibraryEmptyState: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        VStack(spacing: 0) {
            Text("♫").font(theme.font(64)).foregroundStyle(theme.accent)
            Text(tr("Arşivin burada hayat bulacak.")).font(theme.font(23)).multilineTextAlignment(.center).padding(.top, 22).padding(.bottom, 12)
            Text(tr("FLAC, WavPack ve diğer müzik dosyalarının bulunduğu klasörü ekle. Albümler, kapaklar ve etiketler otomatik okunur.")).foregroundStyle(theme.muted).multilineTextAlignment(.center).lineSpacing(6)
            Button(tr("Müzik klasörünü seç")) { model.addFolder() }.buttonStyle(FButtonStyle(background: theme.accent, foreground: theme.onAccent)).padding(.top, 25)
        }
        .frame(maxWidth: 420)
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(RoundedRectangle(cornerRadius: 16).fill(theme.surface))
    }
}

struct CollectionEmpty: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    var body: some View {
        VStack(spacing: 0) {
            Icon(name: "playlist", size: 48, color: theme.accent)
            Text(model.emptyTitle).font(theme.font(24, .semibold)).multilineTextAlignment(.center).padding(.top, 20).padding(.bottom, 10)
            Text(model.emptyHint).foregroundStyle(theme.muted).multilineTextAlignment(.center)
            if model.showEmptyPlaylistCreate {
                Button(tr("Çalma listesi oluştur")) { PlaylistEditor.open(model, existing: nil, smart: false) }.buttonStyle(FButtonStyle(background: theme.accent, foreground: theme.onAccent)).padding(.top, 22)
            }
        }
        .frame(maxWidth: 360).padding(30)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(RoundedRectangle(cornerRadius: 18).fill(theme.surface))
    }
}
