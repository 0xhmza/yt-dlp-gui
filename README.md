# yt-dlp GUI

A lightweight, portable Windows GUI frontend for [yt-dlp](https://github.com/yt-dlp/yt-dlp).

Built with Windows Forms targeting the in-box .NET Framework runtime, so it runs on any modern Windows machine without requiring runtime installations or heavy dependencies.

---

## Features

- **Zero-Install & Portable**: Single standalone executable. Settings are saved right next to the executable (`ytdlp-gui.settings`), or in `%APPDATA%` if read-only.
- **Auto-Discovery**: Automatically locates `yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe`, and JS runtimes (`deno.exe`, `node.exe`, `bun.exe`) in the application directory, a `bin/` subfolder, or system `PATH`.
- **Format Inspection & Selection**: Preset profiles (Best, Audio only, Video only, Custom) plus an interactive **Available Formats** browser dialog.
- **Subtitles & Metadata**: Download or embed subtitles, chapters, descriptions, and thumbnail art.
- **Playlists & Filtering**: Download full playlists, specific items/ranges, date filters, filesize limits, and chapter splits.
- **Post-Processing**: Integrated [SponsorBlock](https://sponsor.ajay.app/) marking/removal, container remuxing, recoding, and custom FFmpeg arguments.
- **Authentication & Cookies**: Extract cookies directly from installed browsers (Chrome, Firefox, Edge, Brave, etc.) or provide custom cookie/credentials.
- **Real-Time Progress**: Live progress bars, transfer speeds, ETA calculation, output log viewer, and cancellation support.
- **Drag & Drop**: Paste or drag URLs directly into the application.

---

## Quick Start

1. Download or build `yt-dlp-gui.exe`.
2. Place `yt-dlp.exe` (and optionally `ffmpeg.exe`) next to `yt-dlp-gui.exe` or inside a `bin/` subfolder (or ensure they are in your system `PATH`).
3. Run `yt-dlp-gui.exe`, paste your URL, configure options as desired, and click **Download**.

---

## Building from Source

No third-party packages or visual studio installation required. You can build using modern .NET SDK (Roslyn) or the in-box Windows .NET Framework compiler:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The compiled binary will be placed at `yt-dlp-gui.exe` with the application icon embedded.

---

## Requirements

- **OS**: Windows 7 / 8 / 10 / 11 (.NET Framework 4.5+ is included in Windows by default)
- **Downloader**: [yt-dlp](https://github.com/yt-dlp/yt-dlp) (`yt-dlp.exe`)
- **Remuxer/Encoder (Optional)**: [FFmpeg](https://ffmpeg.org/) (`ffmpeg.exe`, `ffprobe.exe`)
- **JS Runtime (Optional)**: [Deno](https://deno.land/), [Node.js](https://nodejs.org/), or [Bun](https://bun.sh/)
