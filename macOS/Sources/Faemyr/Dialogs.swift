import SwiftUI
import AppKit
import FaemyrCore

// MARK: - Playlist editor (EditPlaylist / EditSmartPlaylist)

@MainActor enum PlaylistEditor {
    static var windows: [NSWindow] = []
    static func open(_ model: AppModel, existing: LocalPlaylist?, smart: Bool, initial: [Track] = []) {
        let draft = existing?.copy() ?? { let p = LocalPlaylist(); p.name = ""; if smart { p.smartQuery = "plays:0" }; return p }()
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: smart ? 620 : 550, height: smart ? 730 : 650), styleMask: [.titled, .closable, .fullSizeContentView], backing: .buffered, defer: false)
        w.titlebarAppearsTransparent = true; w.isReleasedWhenClosed = false
        w.title = smart ? (existing == nil ? tr("Akıllı çalma listesi oluştur") : tr("Akıllı listeyi düzenle")) : (existing == nil ? tr("Çalma listesi oluştur") : tr("Çalma listesini düzenle"))
        w.contentView = NSHostingView(rootView: PlaylistEditorView(draft: draft, existing: existing, smart: smart, initial: initial, title: w.title) { [weak w] in
            w?.close(); windows.removeAll { $0 === w }
        }.environment(model).environment(\.theme, model.theme))
        w.center(); windows.append(w); w.makeKeyAndOrderFront(nil)
    }
}

struct PlaylistEditorView: View {
    @Environment(AppModel.self) var model
    @Environment(\.theme) var theme
    let draft: LocalPlaylist
    let existing: LocalPlaylist?
    let smart: Bool
    let initial: [Track]
    let title: String
    let close: () -> Void
    @Local private var name = ""
    @Local private var description = ""
    @Local private var query = "plays:0"
    @Local private var sort = "library"
    @Local private var color = "#53504B"
    @Local private var cover = ""
    @Local private var error = ""

    var preview: (String, Bool) {
        var e = ""; let m = TrackQuery.compile(query, model.listening, &e)
        if !e.isEmpty { return (e, false) }
        return (tr("Şu an ") + "\(model.library.filter(m).count)" + tr(" parça eşleşiyor."), true)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(smart ? tr("Akıllı çalma listesi") : title).font(.system(size: 25, weight: .bold)).padding(.bottom, smart ? 10 : 20)
            if smart { Text(tr("Koleksiyonun ve dinleme geçmişin değiştikçe liste kendini günceller.")).padding(.bottom, 20) }
            ScrollView {
                VStack(alignment: .leading, spacing: 7) {
                    HStack(spacing: 16) {
                        ZStack {
                            RoundedRectangle(cornerRadius: theme.cardRadius).fill(Color(hex: color))
                            Text(name.isEmpty ? "♪" : String(name.prefix(1)).uppercased()).font(.system(size: 40, weight: .bold)).foregroundStyle(.white)
                            Thumb(path: cover.isEmpty ? nil : cover, pixels: 300, radius: theme.cardRadius)
                        }.frame(width: 120, height: 120)
                        VStack(alignment: .leading, spacing: 8) {
                            Button(tr("Kapak seç…")) { pickCover() }.buttonStyle(FButtonStyle())
                            if !cover.isEmpty { Button(tr("Kapağı kaldır")) { cover = "" }.buttonStyle(FButtonStyle(background: .clear)) }
                            HStack(spacing: 6) {
                                ForEach(["#53504B", "#7A3E3E", "#3E5F7A", "#3E7A55", "#6B4E8C", "#8C6B3E", "#2F6F73", "#8C3E6B"], id: \.self) { c in
                                    Button { color = c } label: { Circle().fill(Color(hex: c)).frame(width: 20, height: 20).overlay(Circle().strokeBorder(theme.ink, lineWidth: color == c ? 2 : 0)) }.buttonStyle(.plain)
                                }
                            }
                        }
                    }.padding(.bottom, 18)
                    Text(tr("Liste adı"))
                    FTextField(placeholder: tr("Liste adı"), text: $name).padding(.bottom, 12)
                    if smart {
                        Text(tr("Hazır kural"))
                        FPicker(items: Array(smartPresets.indices.dropFirst()), label: { smartPresets[$0].name }, selection: Binding(get: { smartPresets.firstIndex { $0.query == query } ?? 1 }, set: { i in
                            query = smartPresets[i].query; sort = smartPresets[i].sort; if name.trimmingCharacters(in: .whitespaces).isEmpty { name = smartPresets[i].name }
                        }))
                        Text(tr("Filtre koşulları")).padding(.top, 8)
                        TextEditor(text: $query).font(.system(size: 14)).scrollContentBackground(.hidden).padding(8).frame(height: 80)
                            .background(RoundedRectangle(cornerRadius: theme.buttonRadius).fill(theme.control))
                        Text(tr("Sıralama")).padding(.top, 8)
                        FPicker(items: ["library", "added", "played", "plays", "year"], label: { ["library": tr("Sanatçı / albüm"), "added": tr("Eklenme tarihi"), "played": tr("Son dinlenme"), "plays": tr("Çalma sayısı"), "year": tr("Yıl")][$0]! }, selection: $sort)
                        Text(preview.0).foregroundStyle(theme.accent).padding(.vertical, 12)
                        Button(tr("Filtre örnekleri")) { model.alert(TrackQuery.help, title: tr("Faemyr — Filtre yardımı")) }.buttonStyle(FButtonStyle())
                    } else {
                        Text(tr("Açıklama"))
                        TextEditor(text: $description).font(.system(size: 14)).scrollContentBackground(.hidden).padding(8).frame(height: 95)
                            .background(RoundedRectangle(cornerRadius: theme.buttonRadius).fill(theme.control))
                        Text(tr("Parçalara sağ tıklayıp “Çalma listesine ekle” menüsünü kullanabilirsin.")).font(.system(size: 12)).foregroundStyle(theme.muted).padding(.top, 18)
                    }
                    if !error.isEmpty { Text(error).foregroundStyle(theme.accent) }
                }
            }
            HStack {
                Spacer()
                Button(tr("Vazgeç"), action: close).buttonStyle(FButtonStyle()).keyboardShortcut(.cancelAction)
                Button(existing == nil ? tr("Oluştur") : tr("Kaydet")) { save() }
                    .buttonStyle(FButtonStyle(background: theme.accent, foreground: theme.onAccent)).keyboardShortcut(.defaultAction)
                    .disabled(name.trimmingCharacters(in: .whitespaces).isEmpty || (smart && !preview.1))
            }.padding(.top, 20)
        }
        .padding(28)
        .background(theme.canvas).foregroundStyle(theme.ink)
        .onAppear { name = draft.name; description = draft.description; query = draft.smartQuery ?? "plays:0"; sort = draft.smartSort; color = draft.color; cover = draft.cover }
    }

    func pickCover() {
        let p = NSOpenPanel(); p.allowedContentTypes = [.image]
        guard p.runModal() == .OK, let url = p.url, let data = try? Data(contentsOf: url) else { return }
        let path = (Paths.file("playlist-covers") as NSString).appendingPathComponent(draft.id + "-" + UUID().uuidString.prefix(8) + ".png")
        do { try ArtistPhotoStore.writeImage(data, path); cover = path } catch { self.error = error.localizedDescription }
    }

    func save() {
        draft.name = name.trimmingCharacters(in: .whitespaces); draft.description = description.trimmingCharacters(in: .whitespacesAndNewlines)
        draft.color = color; draft.cover = cover
        if smart { draft.smartQuery = query.trimmingCharacters(in: .whitespacesAndNewlines); draft.smartSort = sort }
        for t in initial where !draft.tracks.contains(where: { $0.caseInsensitiveCompare(t.path) == .orderedSame }) { draft.tracks.append(t.path) }
        if let existing, let i = model.playlists.firstIndex(where: { $0 === existing }) { model.playlists[i] = draft } else { model.playlists.append(draft) }
        model.savePlaylists()
        model.playlistId = draft.id; model.view = "playlist"; model.search = ""; model.refresh()
        close()
    }
}

// MARK: - Raw technical data window (Raw_Click)

@MainActor enum RawMetadataWindow {
    static var windows: [NSWindow] = []
    static func open(_ t: Track, _ model: AppModel) {
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 860, height: 650), styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        w.title = t.title + " — metadata"; w.isReleasedWhenClosed = false
        w.contentView = NSHostingView(rootView: RawMetadataView(track: t).environment(model).environment(\.theme, model.theme))
        w.center(); windows.append(w); w.makeKeyAndOrderFront(nil)
    }
}

struct RawMetadataView: View {
    @Environment(\.theme) var theme
    let track: Track
    @Local private var tab = 1
    var json: String {
        if !track.probeJson.isEmpty { return track.probeJson }
        let e = JSONEncoder(); e.outputFormatting = [.prettyPrinted, .sortedKeys]; e.dateEncodingStrategy = .iso8601
        return (try? e.encode(track)).map { String(decoding: $0, as: UTF8.self) } ?? ""
    }
    var body: some View {
        VStack(spacing: 0) {
            Picker("", selection: $tab) {
                Text(tr("Teknik veriler / JSON")).tag(0); Text(tr("Etiketler / tekrarlanan değerler")).tag(1); Text(tr("Etiketler / ham metin")).tag(2)
            }.pickerStyle(.segmented).labelsHidden().padding(12)
            ScrollView {
                switch tab {
                case 0: Text(json).font(.system(size: 12, design: .monospaced)).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading).padding(16)
                case 1:
                    VStack(alignment: .leading, spacing: 5) {
                        ForEach(Array(track.fields.enumerated()), id: \.offset) { _, f in
                            Text(f.key).font(.system(size: 11)).foregroundStyle(theme.muted)
                            if f.isArtist { ArtistLinks(names: f.value, size: 14).padding(.bottom, 13) } else { Text(f.value).textSelection(.enabled).padding(.bottom, 13) }
                        }
                    }.frame(maxWidth: .infinity, alignment: .leading).padding(16)
                default: Text(track.fields.map { $0.key + "\n" + $0.value }.joined(separator: "\n\n")).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading).padding(16)
                }
            }
        }
        .background(theme.canvas).foregroundStyle(theme.ink)
    }
}
