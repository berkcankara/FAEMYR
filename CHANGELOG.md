# Changelog

## 0.10.53

- Reduced library startup work and moved saved queue availability checks off the UI thread.
- Reused artist credit parsing and numeric search conditions within collection queries.
- Reduced repeated gallery refreshes during folder checks and limited edition analysis to affected albums during import.
- Reused gallery row positions while scrolling and shared repeated metadata tags in memory.
- Skipped album-wide ReplayGain lookups during playback setup in track and off modes.
- Released completed waveform work for removed tracks and recovered from null waveform bands in older or damaged cache files.
- Skipped redundant updates to the title bar, volume controls and mini player mode indicators.

## 0.10.52

- Stabilized pixel-based scrolling in album and artist galleries, including letter-grouped rows and reversing direction at the bottom.
- Kept a larger, memory-bounded artwork cache and reduced redundant artist portrait loads and edge-fade work.
- Added active-mode dots beneath shuffle and repeat in the main, fullscreen and mini players.

## 0.10.51

- Fixed stale Windows Start menu and desktop shortcut icons by introducing a dedicated versioned icon file and updating shortcut generation.

## 0.10.50

- Introduced the new Faemyr app icon: a flowing F monogram with an integrated star. Updated Windows executable, window, taskbar, shortcut and installer artwork.

## 0.10.49

- Completed the Faemyr rebrand across the project, icon, installer and repository names.
- Updated GitHub repository links and English documentation.
- Preserved library data and compatibility with existing installations and shortcuts.

## 0.10.48

- Renamed Lyra to Faemyr. The Windows application identity, interface, installer and portable package now use the new name.
- Existing libraries, settings, favorites and music files are preserved. Legacy shortcuts can open the renamed application.

## 0.10.47

- Multi-disc albums are separated into numbered disc sections. Disc metadata takes priority; CD/Disc/Disk folder names provide a fallback. Single-disc albums retain their original layout.

## 0.10.46

- Refined the grouped-view icon and matched its inactive border to the sort menu. Increased contrast in collection and settings navigation buttons.

## 0.10.45

- Added a grouped-view toggle next to the album and artist sorting menus. The shared preference persists between sessions.

## 0.10.44

- Alphabetical album and artist collections can be grouped by initial letter, with a # section for numbers and symbols. Non-alphabetical album sorts retain the flat grid.

## 0.10.43

- Added a module transparency option that lets the blurred album backdrop show through high-opacity panels without reducing text or artwork contrast.

## 0.10.42

- Album and artist collection pages use the currently playing album's dynamic artwork backdrop. Album details prioritize the selected album; other pages fade back to the theme color.

## 0.10.41

- Clicking the current track title in the right panel opens its album without changing playback or the queue.

## 0.10.40

- Fixed incorrectly split artist names containing ampersands, including Above & Beyond. Full group names are retained, guest artists do not create duplicate profiles, and existing navigation is restored to the correct profile. Music tags remain unchanged.

## 0.10.39

- Redesigned the interface with modular panels and soft fade effects at content edges.
- Optimized the Albums and Artists pages.
- Added click-to-seek to synchronized lyrics.
- Redesigned artist profiles with better imagery and localized biographies.
- Repositioned fullscreen and mini-player controls symmetrically.
- Added smooth artwork movement when fullscreen lyrics are toggled.
- Added editable cover artwork for smart playlists.
- Introduced the live spectrogram visualizer.
- Restored native Windows minimize, restore and maximize animations.
- Added a hideable right-hand now-playing panel.
- Redesigned the top bar with back navigation, search, the panel toggle and now-playing information when the panel is hidden.
- Improved OLED theme button accessibility.
- Fixed blank fullscreen lyrics when the next track has no cached lyrics.
- Added a Windows x64 setup wizard with a selectable install location and Start menu/desktop shortcuts. A portable ZIP is also available.
