# Faemyr — Third-party notices

| Component | Version | License / source |
| --- | --- | --- |
| .NET / Windows Desktop Runtime | 8.0.33, x64 | MIT and third-party notices — https://github.com/dotnet/runtime ; https://github.com/dotnet/wpf |
| NAudio | 2.2.1 | MIT — https://github.com/naudio/NAudio/tree/v2.2.1 |
| TagLibSharp | 2.3.0 | LGPL-2.1-only — https://github.com/mono/taglib-sharp |
| Microsoft.Data.Sqlite | 8.0.11 | MIT — https://github.com/dotnet/efcore/tree/v8.0.11 |
| SQLitePCLRaw | Pinned in NuGet dependencies | Apache-2.0 — https://github.com/ericsink/SQLitePCL.raw |
| SQLite | Native SQLitePCLRaw distribution | Public domain — https://sqlite.org |
| WavPack CLI | 5.9.0, official Win64 | BSD — https://github.com/dbry/WavPack/releases/tag/5.9.0 |
| FFmpeg / FFprobe | 9.0.2 essentials, gyan.dev | GPLv3 build — https://www.gyan.dev/ffmpeg/builds/ ; https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz |

FFmpeg and WavPack run as separate helper processes. TagLibSharp is loaded as a separate, replaceable DLL. The FFmpeg build configuration is in `App\licenses\FFmpeg-build.txt`. Additional FFmpeg dependencies are listed in the configuration output; source and build information can be obtained from the distributor. This package is a local development prototype; the applicable corresponding-source and licensing obligations must be completed before public distribution.

Original NuGet package version and license records are stored in `App\licenses\nuget`. All components remain copyright their respective owners.


## LRCLIB — optional online lyrics service

Faemyr can query the public LRCLIB API on explicit user request for plain or line-synchronized lyrics. Documentation: https://lrclib.net/docs . LRCLIB source code and lyrics are not bundled in this distribution. Retrieved lyrics remain attributed to LRCLIB in the player and are stored only in the user's local data directory. Network availability and record coverage vary.


## lyrics.ovh — optional alternate online lyrics service

When LRCLIB fails or has no matching record, an explicit user lookup can query the lyrics.ovh public API by artist and title. Documentation and service source: https://github.com/NTag/lyrics.ovh . Faemyr uses the HTTP API; service code and lyric content are not bundled. Results are plain text without recording duration or synchronization metadata, identified by source and cached in local user data.

## Artist photographs

Artist photographs are obtained on demand from Wikipedia / Wikimedia Commons. Individual images retain their own license. The application stores the source page, author, and license with each cached image and exposes attribution and a source link in the artist profile. No artist photographs or user cache are bundled in the release archive.
