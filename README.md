# OCR Overlay

A frameless, always-on-top, transparent WPF overlay that reads text from whatever is **behind** its capture zone in real time — fully local, no internet, no model downloads.

```
┌────────────────────────────────────────┐  ← top bar (drag to move)
│  ● OCR Overlay   Interval: [800] ms  ■ Stop  ▾  ✕ │
├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┤
│                                        │
│   TRANSPARENT  (you see the desktop    │  ← capture zone
│   / any window behind here)            │
│                                        │
├────────────────────────────────────────┤
│ Recognized Text        Copy  Clear     │
│ The quick brown fox…                   │  ← OCR output panel
└────────────────────────────────────────┘
```

## How it works

1. The window is `AllowsTransparency="True"`, `Topmost="True"`, `WindowStyle="None"` — so it floats above everything with a transparent middle section.
2. On startup, **`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`** is called on the HWND. This tells Windows to exclude our window from any screen grab (BitBlt / PrintScreen) while still showing it on screen to the user.
3. Every tick, `BitBlt` captures the physical screen rectangle that corresponds to the transparent zone — because our window is excluded, only the content **behind** it is captured.
4. The bitmap is sent to **`Windows.Media.Ocr`** (the same engine used by Windows Snipping Tool) — 100 % local, no network.

## Requirements

| | |
|---|---|
| **OS** | Windows 10 build **19041** (20H1) or later for `WDA_EXCLUDEFROMCAPTURE` |
| **Runtime** | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **OCR language** | English (`en-US`) language pack — comes pre-installed on most Windows machines |

## Build & Run

```bash
cd WindowCaptureOcr
dotnet run

# Single-file publish
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Usage

- **Drag** the top bar to position the overlay over the text you want to read.
- **Resize** from any edge/corner — the transparent capture zone resizes with the window.
- **▾ / ▸** collapses or expands the OCR text panel.
- **■ Stop / ▶ Start** pauses and resumes the capture loop.
- **Interval** — how often (ms) to capture and OCR. Lower = faster but more CPU.
- **Copy** — copies current OCR result to clipboard.
