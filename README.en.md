# Foto Düzenleyici

[Türkçe](README.md) · **English**

A Windows desktop app that gathers photos and videos from scattered folders into `Year-Month` folders by
**capture date** (EXIF / video metadata) and finds files with identical content.

> The app's interface is currently in **Turkish** only. The installer offers Turkish and English.

```
2026-01\
  2026-01-Foto\   (optional: 2026-01-Foto\2026-01-Fuji, 2026-01-Canon …)
  2026-01-Raw\
  2026-01-Video\
  2026-01-Pdf\
_Tekrarlar\       (exact duplicates)
```

## Installation

Download one of the two packages from the [Releases](../../releases) page. You don't need .NET or anything else.

- **`FotoDuzenleyici-Setup-x.y.z.exe`** (recommended): double-click it. First you choose *just for me* (no
  admin rights needed) or *for all users*. Optional: a desktop shortcut, **Open with Foto Düzenleyici** in the
  folder and drive right-click menu (on Windows 11 under *Show more options*; the folder you open is
  preselected on the home screen and becomes the source in Organize or the folder to clean in Clean), and
  adding the command-line tool (`fotoduzenle`) to PATH. To update, install the new Setup.exe over the old
  one; your settings are kept. When uninstalling you are asked whether to delete the settings too (default:
  keep). Installing or uninstalling never touches your photos or the journals in your archive.
- **`FotoDuzenleyici-win-x64.zip`** (portable): extract it to a folder and double-click `FotoDuzenleyici.exe`.

The app never connects to the internet, not even to check for updates.

> If Windows shows *"Windows protected your PC"* or an "unknown publisher" warning: *More info → Run anyway*.
> (The app is not digitally signed yet.)

The app opens on the **home screen** (Ana ekran). **Organize** (Düzenle) runs in four steps: *Folders → Scan
and review → Confirm → Applying / Done*. In *Scan and review* the left menu lets you go through
**Duplicates** (Yinelenenler: choose which copy to keep), **Similar** (Benzerler: same frame, downscaled copy,
burst shots) and **Flawed** (Kusurlu: blurry / dark). Your decisions are applied to the plan when you move on
to Confirm.

**Clean** (Temizle) uses the same steps but does not move files into date folders: it moves duplicates,
similar and flawed shots in the selected folders into an `_Ayiklananlar` folder inside that folder (no
copying, no deleting). You can mark "preferred folders for duplicates"; Undo puts everything back. After
checking `_Ayiklananlar` you can delete the folder yourself.

**Timeline** (Zaman çizelgesi) shows the photos in the selected folder as a year × month table: how many
photos each month has and which months are empty. There are two ways to count: **From folder names** for an
organized archive (`Year-Month` folders, takes seconds) and **From capture date** for an unorganized folder
(each file's EXIF date is read). Photo / Raw / Video / All filter. The table only reads.

Clicking a month opens the **month view**: that month's photos as thumbnails, either as a **Grid** split by
day or a **Calendar** split by week. **Triage ▶** (Ayıkla) shows the photos one by one, large: move with ← →,
Delete or dragging the image left marks it "discard", dragging right says "keep". When a JPG is discarded its
RAW pair goes with it; **Keep RAW** leaves the RAW in place. Marked files are listed on the **Review** screen
(Gözden geçir), and only when you confirm there are they sent to the Windows **Recycle Bin** (restore them
from there with "Restore"). Files the bin can't take (USB stick, SD card, network drive, file larger than the
bin) are not deleted; they are moved to `_Ayiklananlar\Ayikla` and can be undone with Undo. Back on the
timeline, that month's count updates without recounting. "Open in Explorer" is in the ⋯ menu.

The old window is available as **Classic view** in the ⚙ menu at the top right. To open it directly:
`FotoDuzenleyici.exe --klasik`.

## Safety

- File contents are **never changed**: EXIF/GPS/video metadata stays as it is.
- Every file is first copied, verified with SHA-256, and its created/modified dates are preserved; only then
  is the original sent to the **Recycle Bin** (no permanent deletion).
- No existing file at the destination is ever overwritten. On a name clash a date is added.
- Every operation is written to a journal; **Undo** moves files back.
- A **Confirm** screen is always shown before applying: what will be done, free space, and a check for
  unfinished operations.

## Building from source

Requires the .NET 8 SDK.

```powershell
dotnet test
./publish.ps1        # dist\FotoDuzenleyici-win-x64.zip and (if Inno Setup 6 is installed) dist\FotoDuzenleyici-Setup-x.y.z.exe
```

The installer script is `installer\FotoDuzenleyici.iss`; `installer\test-install.ps1` silently installs,
checks and uninstalls the Setup.exe (GitHub Actions runs it on every push; if you run it on your own PC it
uninstalls the installed Foto Düzenleyici).

## Project structure

- `FotoDuzenleyici.Core` – scanning, date reading, duplicate finding, planning, safe applying
- `FotoDuzenleyici.ViewModels` – screen logic (no WPF, tested): home screen and Organize flow
- `FotoDuzenleyici.App` – WPF interface: new shell (WPF-UI) and Classic view
- `FotoDuzenleyici.Cli` – command-line tool (`fotoduzenle.exe`); touches no files unless `--apply` is given, undoes with `--undo <journal>`
- `FotoDuzenleyici.Tests` – unit tests

## Plans and changes (in Turkish)

- [YOL_HARITASI.md](YOL_HARITASI.md) – what's done and what's next
- [CHANGELOG.md](CHANGELOG.md) – changes per release

Metadata reading: [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) (Apache-2.0).

This project was developed with the help of Claude (Anthropic).
