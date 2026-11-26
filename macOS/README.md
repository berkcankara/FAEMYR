# Faemyr · macOS

> **Preview — in development.** The macOS version is still being built. Most features of the Windows
> release work, but some are missing (DoP output, appearance profiles) and others may behave
> unexpectedly. Please report problems on GitHub.

Native SwiftUI / Core Audio port of the Windows (WPF) build in `Source/`. The interface follows
`MainWindow.xaml` and `App.xaml` (same layout metrics, themes, colour roles, SVG icons and the
`en.json` translation table). No .NET, Rosetta or Xcode is required — Command Line Tools are enough.

## Build

```sh
xcode-select --install          # once
bash macOS/build.sh             # → App-macOS/Faemyr.app and a versioned ZIP
open App-macOS/Faemyr.app
```

`build.sh` copies `Source/Assets/en.json` and `Source/Assets/Icons/*.svg` into the package, so
interface strings and icons stay shared with Windows.

## Data

Library and settings live in `~/Library/Application Support/Faemyr` and use the same file names and
JSON layout as the Windows `data` folder (`library.db`, `favorites.json`, `playlists.json`,
`interface-settings.json`, `appearance.json`, …). `FAEMYR_DATA_DIRECTORY=/path` isolates a test run.

## Audio

- FLAC, ALAC, WAV, AIFF, MP3 and AAC are decoded by macOS.
- Ogg Vorbis, Opus, APE, WavPack and DSD need FFmpeg (`brew install ffmpeg`, or bundle `ffmpeg` and
  `ffprobe` with `FAEMYR_AUDIO_HELPERS=/dir bash macOS/build.sh`).
- Output: macOS default or a chosen device; “automatic/exclusive” matches the device sample rate to the
  source and restores it afterwards; optional Hog Mode. ReplayGain (track/album, preamp, clipping guard),
  gapless transitions for tracks with the same format, CUE ranges.

## Differences from Windows

- Window controls are the native macOS traffic lights (top left) instead of the Windows caption buttons.
- WASAPI-specific options are replaced by their Core Audio equivalents; DoP output and the appearance
  profile manager are not ported yet.

## Development notes

- The Command Line Tools SDK declares SwiftUI's `@State` as a macro whose plugin only ships with Xcode, so
  views use `@Local` (an alias of the `SwiftUI.State` property wrapper).
- `withTaskGroup` crashed in optimized builds with this toolchain; the library scan uses detached tasks.
- `Faemyr --snapshot out.png --view albums|album|artist|tracks|fullscreen|settings [--play]` renders the real
  window offscreen to a PNG without screen-recording permission.
- `swift run --package-path macOS FaemyrSelfTest [files…]` runs the core checks; `--play file` tests output.
