# MediaSorter

Windows desktop application: C# / WinUI 3, portable, x64. User-facing languages: Russian and English, system defaults for locale and theme.

- Preserve user media. Never overwrite destination files. Destructive UI actions require confirmation and use the Recycle Bin.
- Sorting is plan-first, cancellable between files, and records a journal. Cross-volume moves verify copied bytes before source deletion.
- Do not follow junctions/symlinks. Exclude the output tree from recursive source scans.
- Keep disk and metadata work off the UI thread. Show partial failures; never report failed moves as successful.
- All UI strings belong in `MediaSorter/Locales/*.json`; new locales are loaded from files.
- Keep pure sorting logic in `Sorter.Core`; verify safety behavior with `Sorter.Tests`.
- Build and test before committing. Do not commit generated binaries, user settings, media, or credentials.
