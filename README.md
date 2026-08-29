<div align="center">

<img src="https://raw.githubusercontent.com/techygeekshome/PDFGeek/main/icons/pdfgeek.png" alt="PDFGeek logo" width="96" height="96">

# PDFGeek

**A free, self-contained PDF toolkit for Windows — merge, split, rotate, watermark and protect PDFs without uploading a thing.**

[![Version](https://img.shields.io/github/v/release/techygeekshome/PDFGeek?label=version&color=4c9bff)](https://github.com/techygeekshome/PDFGeek/releases)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d4)](#-download--run)
[![License](https://img.shields.io/badge/license-GPL--3.0-3fb950)](LICENSE)
[![Made by TechyGeeksHome](https://img.shields.io/badge/made%20by-TechyGeeksHome-b191f2)](https://techygeekshome.info)
[![Support on Ko-fi](https://img.shields.io/badge/support-Ko--fi-ff5e5b)](https://ko-fi.com/techygeekshome)

[Download](#-download--run) · [Features](#-what-it-does) · [Screenshots](#-screenshots) · [Build from source](#-build-from-source) · [License](#-license)

</div>

---

PDFGeek does the everyday PDF jobs on your own machine, with none of the limits the web tools impose. Merge as many files as you like, images included, split a document into pages or fixed-size chunks, pull pages out or drop them, rotate sideways scans, reorder a document, stamp a watermark across it, and add or remove password protection.

The online alternatives cap you and then ask for money — 25 files per merge, three tasks an hour, a subscription to unlock batch mode. PDFGeek has no caps to lift, because your documents never leave your computer in the first place.

No installer bloat, no bundled offers, no telemetry. 100% free, no Pro tier, no upsells.

## 🎬 See it in action

[![PDFGeek demo video](https://img.youtube.com/vi/rRZ8JNHRGXY/maxresdefault.jpg)](https://www.youtube.com/watch?v=rRZ8JNHRGXY)

A full tour of every tool, in under a minute.

## 📸 Screenshots

<p float="left">
  <img src="https://raw.githubusercontent.com/techygeekshome/PDFGeek/main/screenshots/screenshot-merge.png" width="49%" />
  <img src="https://raw.githubusercontent.com/techygeekshome/PDFGeek/main/screenshots/screenshot-split.png" width="49%" />
</p>
<p float="left">
  <img src="https://raw.githubusercontent.com/techygeekshome/PDFGeek/main/screenshots/screenshot-extract.png" width="49%" />
  <img src="https://raw.githubusercontent.com/techygeekshome/PDFGeek/main/screenshots/screenshot-rotate.png" width="49%" />
</p>
<p float="left">
  <img src="https://raw.githubusercontent.com/techygeekshome/PDFGeek/main/screenshots/screenshot-watermark.png" width="49%" />
  <img src="https://raw.githubusercontent.com/techygeekshome/PDFGeek/main/screenshots/screenshot-password.png" width="49%" />
</p>

## ⬇️ Download & run

| What it is | Get it |
| --- | --- |
| **Installer** *(recommended)* — Start menu entry, desktop shortcut, clean uninstall | [**PDFGeekSetup.exe**](https://github.com/techygeekshome/PDFGeek/releases/latest) |
| **Portable** — a single `.exe`, nothing installed, runs from a USB stick | [**PDFGeek.exe**](https://github.com/techygeekshome/PDFGeek/releases/latest) |

The installer needs **no admin rights** and shows no UAC prompt — it installs for the current user by default. It bundles nothing, changes no file associations unless you tick the box, and uninstalls cleanly from Settings → Apps.

Prefer the portable build if you want to run it from a memory stick or leave no trace on the machine. Either way it is the same application.

> **Windows will warn you the first time you run it.** PDFGeek isn't code-signed, because a certificate costs money we'd rather not put behind a free tool. Click **More info → Run anyway**. Every release publishes SHA256 checksums for both files so you can verify what you downloaded, and the source is right here so you can see exactly what it does.
>
> A winget package is sitting in Microsoft's community review queue. Until a moderator approves it, `winget install TechyGeeksHome.PDFGeek` won't find anything — this note will go when it does.

## ✨ What it does

- 🔗 **Merge** any number of PDFs *and images* into one, in the order you set — drag them in, reorder, done. JPG, PNG and WebP go in alongside the PDFs, either keeping their own size or forced to A4 or Letter.
- ✂️ **Split** a document into one file per page, or into fixed-size chunks.
- 📄 **Extract or remove pages** using print-dialog page ranges (`1-3, 5, 9-`).
- 🔄 **Rotate** sideways scans by 90°, 180° or 270°, on selected pages or the whole document.
- 🔀 **Reorder** a document by listing the pages in the order you want them.
- 💧 **Watermark** every page with your own text, at whatever size, opacity and angle you like.
- 🔐 **Add a password** with AES-128 encryption, with control over printing and copying.
- 🔓 **Remove a password** from a document you can already open.
- 🔒 **Private** — your documents are processed locally and never uploaded, with no telemetry and no account.

### On the one network call

Clicking **Check for updates** makes a single request to GitHub's public releases API to compare version numbers. It sends no identifiers, no file names and no usage data, and it never downloads or installs anything on its own — if there's a newer version it just offers to open the page. Don't press it and PDFGeek makes no network connection at all.

## 🔧 Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build PDFGeek.sln -c Release
```

To produce the portable, self-contained `win-x64` build:

```powershell
dotnet publish src/PDFGeek/PDFGeek.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

To run the test suite (29 checks against real PDFs):

```powershell
dotnet run --project tests/PDFGeek.Smoke -c Release
```

### Project layout

| Path | What's there |
| --- | --- |
| `src/PDFGeek/Services` | Every PDF operation and the page-range parser (no UI dependencies) |
| `src/PDFGeek/Views` | The Avalonia desktop UI |
| `src/TechyGeeksHome.Common` | Shared About window and update checker, used across all TechyGeeksHome apps |
| `tests/PDFGeek.Smoke` | Console harness that runs every operation against real PDFs |
| `tools/make-icon.py` | Generates the icon set from the brand tokens |
| `icons/` | App icon assets |

## ☕ Support

PDFGeek is free and always will be. If it saved you a subscription, you can [buy us a coffee on Ko-fi](https://ko-fi.com/techygeekshome) — welcome, but never expected.

## 🐛 Support & contributing

Found a bug or have a request? [Open an issue](https://github.com/techygeekshome/PDFGeek/issues) or [get in touch](https://techygeekshome.info/contact/).

Pull requests are welcome. PDFGeek is open source under the GPL-3.0, so improvements can be taken straight in — see [CONTRIBUTING.md](CONTRIBUTING.md) for how contributions are licensed and what is most useful.

## 📄 License

PDFGeek is free software, licensed under the [GNU General Public License v3.0](LICENSE). You are free to use it, study it, change it and pass it on. Anything you distribute that is built from this code has to carry the same freedoms, which is what keeps it free for everyone downstream.

The PDFGeek name, logo and TechyGeeksHome branding are not covered by that licence and remain ours.

Built with [Avalonia](https://avaloniaui.net/) (MIT) and [PDFsharp](https://github.com/empira/PDFsharp) (MIT).

© 2026 TechyGeeksHome | Andrew Armstrong.

---

<div align="center">

Made with ❤️ by [**TechyGeeksHome**](https://techygeekshome.info)

[Website](https://techygeekshome.info) · [YouTube](https://www.youtube.com/channel/UCtEuFj1SMLiuRoucD1hv8dA) · [X](https://x.com/TechyGeeks1) · [Instagram](https://www.instagram.com/andrewarmstrongtgh/)

</div>

---
