<div align="center">
  <h1><strong>CustomHUD</strong></h1>
  <p>Every SwiftlyS2 menu on your server drawn as a CS2-styled Panorama HUD, with no change to your plugins.</p>
</div>

<p align="center">
  <a href="https://github.com/shmitzas/CustomHUD/actions/workflows/build-and-release.yml"><img src="https://img.shields.io/github/actions/workflow/status/shmitzas/CustomHUD/build-and-release.yml?branch=main&event=push&style=flat&logo=github&cacheSeconds=3600" alt="Build"></a>
  <a href="https://github.com/shmitzas/CustomHUD/releases/latest"><img src="https://img.shields.io/github/v/release/shmitzas/CustomHUD?style=flat&logo=github&cacheSeconds=3600" alt="Latest Release"></a>
  <a href="https://github.com/shmitzas/CustomHUD/releases"><img src="https://img.shields.io/github/downloads/shmitzas/CustomHUD/total?style=flat&logo=github&cacheSeconds=3600" alt="Downloads"></a>
  <a href="https://github.com/shmitzas/CustomHUD"><img src="https://img.shields.io/github/stars/shmitzas/CustomHUD?style=flat&logo=github&cacheSeconds=3600" alt="Stars"></a>
</p>

> [!NOTE]
> Every player needs the [CustomHUD Workshop addon](https://steamcommunity.com/sharedfiles/filedetails/?id=3834965450). Serve it with [AddonsManager](https://github.com/SwiftlyS2-Plugins/AddonsManager) — see [Installation](../../wiki/Installation).

---

## ✨ Features

One renderer for every plugin's menus.

- 🎨 **Drop-in** — one line in `menus.jsonc` redraws every `Core.Menu` menu; plugins need no changes
- 🖼️ **Designed pages** — WeaponSkins, ShopCore, MapChooser, Ranks, VIPCore, Admins and MixScrims, with prices, votes, ranks and item art
- 📋 **The list** — every other plugin's menus, with switches, sliders and the keys that work
- 🕹️ **Four navigation modes** — each player picks `buttons`, `wasd`, `numbers` or `mouse`
- 🔍 **Menu scale** — each player resizes menus with `!hudscale`
- 🍪 **Remembered choices** — mode and scale kept across restarts with Cookies
- 🧱 **Layouts per menu** — the list, the classic look, or your own Panorama layout
- 💬 **Native text capture** — the game's centre text and alerts in the same style
- 🧩 **Panels API and directives** — other plugins show panels and style their rows
- 🌐 **Translations** — English, Lithuanian and partial Brazilian Portuguese

## 🚀 Getting Started

| Guide | Description |
|-------|-------------|
| **[Features](../../wiki/Features)** | Everything CustomHUD does |
| **[Installation](../../wiki/Installation)** | Install and verify |
| **[Commands](../../wiki/Commands)** | `!setnav`, `!hudscale` and the number keys |
| **[Navigation](../../wiki/Navigation)** | Modes, keys, pages and scale |
| **[Configuration](../../wiki/Configuration)** | Every setting, with defaults |

## ⚙️ Customising the HUD

| Topic | Description |
|-------|-------------|
| **[Supported Plugins](../../wiki/Supported-Plugins)** | The designed pages, and their views |
| **[Layouts](../../wiki/Layouts)** | Which layout draws each menu |
| **[Native Text Capture](../../wiki/Native-Text-Capture)** | The game's HUD text in CustomHUD's layouts |
| **[Translations](../../wiki/Translations)** | Languages, and the gold line over each plugin |

---

## 📚 Reference

| Topic | Description |
|-------|-------------|
| **[API Reference](../../wiki/API-Reference)** | The Panels API and menu directives |
| **[API Integration](../../wiki/API-Integration)** | Show panels and style your plugin's menus |
| **[Building](../../wiki/Building)** | Build from source |
| **[Troubleshooting](../../wiki/Troubleshooting)** | Symptoms, causes and fixes |

---

## 🤝 Report issues or request features

- **Issues**: [GitHub Issues](https://github.com/shmitzas/CustomHUD/issues)
- **Designed pages for another plugin**: [Plugin menu support request](https://github.com/shmitzas/CustomHUD/issues/new?template=plugin-menu-support.yml)
