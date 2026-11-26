import Foundation
import CSQLite

private let SQLITE_TRANSIENT = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

public enum StoreError: LocalizedError {
    case sqlite(String)
    public var errorDescription: String? { if case .sqlite(let m) = self { return m }; return nil }
}

/// Same schema as LibraryStore in Library.cs: `tracks(path, json)` and `roots(path)`.
public final class LibraryStore: @unchecked Sendable {
    private var db: OpaquePointer?
    private let directory: String
    private let lock = NSLock()

    public init(directory: String? = nil) throws {
        let dir = directory ?? Paths.data
        self.directory = dir
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        guard sqlite3_open_v2((dir as NSString).appendingPathComponent("library.db"), &db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, nil) == SQLITE_OK else {
            throw StoreError.sqlite(String(cString: sqlite3_errmsg(db)))
        }
        try exec("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS tracks(path TEXT PRIMARY KEY, json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS roots(path TEXT PRIMARY KEY);")
    }
    deinit { sqlite3_close(db) }

    private func exec(_ sql: String) throws {
        var err: UnsafeMutablePointer<CChar>?
        if sqlite3_exec(db, sql, nil, nil, &err) != SQLITE_OK {
            let message = err.map { String(cString: $0) } ?? "SQLite"
            sqlite3_free(err); throw StoreError.sqlite(message)
        }
    }

    private func query(_ sql: String, bind: [String] = [], row: (OpaquePointer) -> Void) throws {
        lock.lock(); defer { lock.unlock() }
        var stmt: OpaquePointer?
        guard sqlite3_prepare_v2(db, sql, -1, &stmt, nil) == SQLITE_OK else { throw StoreError.sqlite(String(cString: sqlite3_errmsg(db))) }
        defer { sqlite3_finalize(stmt) }
        for (i, v) in bind.enumerated() { sqlite3_bind_text(stmt, Int32(i + 1), v, -1, SQLITE_TRANSIENT) }
        while sqlite3_step(stmt) == SQLITE_ROW { row(stmt!) }
    }

    public func load() -> [Track] {
        let covers = (directory as NSString).appendingPathComponent("covers")
        var localCovers: [String: String] = [:]
        if let names = try? FileManager.default.contentsOfDirectory(atPath: covers) {
            for n in names { localCovers[n.lowercased()] = (covers as NSString).appendingPathComponent(n) }
        }
        var tracks: [String: Track] = [:]
        try? query("SELECT json FROM tracks") { stmt in
            guard let bytes = sqlite3_column_blob(stmt, 0) else { return }
            let data = Data(bytes: bytes, count: Int(sqlite3_column_bytes(stmt, 0)))
            guard let t = try? PlayerJson.decoder.decode(Track.self, from: data), !t.path.trimmingCharacters(in: .whitespaces).isEmpty else { return }
            // Windows covers live in data\covers; relocate when the data folder was copied over.
            let name = windowsLastComponent(t.cover).lowercased()
            if !name.isEmpty, !t.cover.fileExists, let local = localCovers[name] { t.cover = local }
            let key = t.path.lowercased()
            if let prior = tracks[key], prior.modified > t.modified { return }
            tracks[key] = t
        }
        return Array(tracks.values)
    }

    public func roots() -> [String] {
        var a: [String] = []
        try? query("SELECT path FROM roots") { a.append(String(cString: sqlite3_column_text($0, 0))) }
        return a
    }
    public func addRoot(_ path: String) { try? run("INSERT OR IGNORE INTO roots VALUES(?)", [path]) }
    public func removeRoot(_ path: String) { try? run("DELETE FROM roots WHERE path=?", [path]) }

    private func run(_ sql: String, _ bind: [String]) throws {
        try query(sql, bind: bind) { _ in }
    }

    public func save(_ t: Track) { saveMany([t]) }
    public func saveMany(_ tracks: [Track]) {
        lock.lock(); defer { lock.unlock() }
        sqlite3_exec(db, "BEGIN", nil, nil, nil)
        var stmt: OpaquePointer?
        sqlite3_prepare_v2(db, "INSERT INTO tracks VALUES(?1,?2) ON CONFLICT(path) DO UPDATE SET json=?2", -1, &stmt, nil)
        for t in tracks {
            guard let data = try? PlayerJson.encoder.encode(t), let json = String(data: data, encoding: .utf8) else { continue }
            sqlite3_reset(stmt)
            sqlite3_bind_text(stmt, 1, t.path, -1, SQLITE_TRANSIENT)
            sqlite3_bind_text(stmt, 2, json, -1, SQLITE_TRANSIENT)
            sqlite3_step(stmt)
        }
        sqlite3_finalize(stmt)
        sqlite3_exec(db, "COMMIT", nil, nil, nil)
    }
    public func deleteMany(_ paths: [String]) {
        lock.lock(); defer { lock.unlock() }
        sqlite3_exec(db, "BEGIN", nil, nil, nil)
        var stmt: OpaquePointer?
        sqlite3_prepare_v2(db, "DELETE FROM tracks WHERE path=?", -1, &stmt, nil)
        for p in paths { sqlite3_reset(stmt); sqlite3_bind_text(stmt, 1, p, -1, SQLITE_TRANSIENT); sqlite3_step(stmt) }
        sqlite3_finalize(stmt)
        sqlite3_exec(db, "COMMIT", nil, nil, nil)
    }
}

func windowsLastComponent(_ path: String) -> String {
    path.split(whereSeparator: { $0 == "/" || $0 == "\\" }).last.map(String.init) ?? ""
}
