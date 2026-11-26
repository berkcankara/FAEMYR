import Foundation
import AppKit
import CoreServices
import MediaPlayer
import FaemyrCore

/// FolderWatching.cs equivalent backed by FSEvents; changes are debounced into one rescan.
final class FolderWatcher {
    private var stream: FSEventStreamRef?
    private let handler: () -> Void
    private var pending: DispatchWorkItem?

    init(paths: [String], handler: @escaping () -> Void) {
        self.handler = handler
        var ctx = FSEventStreamContext(version: 0, info: Unmanaged.passUnretained(self).toOpaque(), retain: nil, release: nil, copyDescription: nil)
        let callback: FSEventStreamCallback = { _, info, _, _, _, _ in
            guard let info else { return }
            Unmanaged<FolderWatcher>.fromOpaque(info).takeUnretainedValue().changed()
        }
        stream = FSEventStreamCreate(nil, callback, &ctx, paths as CFArray, FSEventStreamEventId(kFSEventStreamEventIdSinceNow), 2.0, UInt32(kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagNoDefer))
        if let stream { FSEventStreamSetDispatchQueue(stream, .main); FSEventStreamStart(stream) }
    }
    private func changed() {
        pending?.cancel()
        let w = DispatchWorkItem { [weak self] in self?.handler() }
        pending = w; DispatchQueue.main.asyncAfter(deadline: .now() + 3, execute: w)
    }
    func stop() { pending?.cancel(); if let stream { FSEventStreamStop(stream); FSEventStreamInvalidate(stream); FSEventStreamRelease(stream) }; stream = nil }
    deinit { stop() }
}

/// Media keys, Control Center and lock-screen Now Playing (WindowsMediaSession.cs).
@MainActor final class MediaControls {
    static let shared = MediaControls()
    weak var model: AppModel?
    private var configured = false
    private var artworkPath: String?
    private var artwork: MPMediaItemArtwork?

    func configure(_ m: AppModel) {
        model = m
        guard !configured else { return }; configured = true
        let c = MPRemoteCommandCenter.shared()
        c.playCommand.addTarget { [weak self] _ in Task { @MainActor in if self?.model?.isPlaying == false { await self?.model?.togglePlayback() } }; return .success }
        c.pauseCommand.addTarget { [weak self] _ in Task { @MainActor in if self?.model?.isPlaying == true { await self?.model?.togglePlayback() } }; return .success }
        c.togglePlayPauseCommand.addTarget { [weak self] _ in Task { @MainActor in await self?.model?.togglePlayback() }; return .success }
        c.nextTrackCommand.addTarget { [weak self] _ in Task { @MainActor in await self?.model?.advance(natural: false) }; return .success }
        c.previousTrackCommand.addTarget { [weak self] _ in Task { @MainActor in await self?.model?.previous() }; return .success }
        c.changePlaybackPositionCommand.addTarget { [weak self] e in
            if let e = e as? MPChangePlaybackPositionCommandEvent { Task { @MainActor in self?.model?.seek(e.positionTime) } }
            return .success
        }
    }

    func update(_ m: AppModel) {
        configure(m)
        let center = MPNowPlayingInfoCenter.default()
        guard let t = m.now else { center.nowPlayingInfo = nil; center.playbackState = .stopped; return }
        var info: [String: Any] = [MPMediaItemPropertyTitle: t.title, MPMediaItemPropertyArtist: t.artist, MPMediaItemPropertyAlbumTitle: t.album,
                                   MPMediaItemPropertyPlaybackDuration: t.seconds, MPNowPlayingInfoPropertyElapsedPlaybackTime: m.audio.position,
                                   MPNowPlayingInfoPropertyPlaybackRate: m.audio.isPlaying ? 1.0 : 0.0]
        let cover = m.coverPath(t)
        if cover != artworkPath {
            artworkPath = cover
            if let cover, let image = ImageCache.decode(cover, 600) { artwork = MPMediaItemArtwork(boundsSize: image.size) { _ in image } } else { artwork = nil }
        }
        if let artwork { info[MPMediaItemPropertyArtwork] = artwork }
        center.nowPlayingInfo = info
        center.playbackState = m.audio.isPlaying ? .playing : m.audio.isPaused ? .paused : .stopped
    }
}
