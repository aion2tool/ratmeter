# RATmeter (aion2tool edition): AION 2 DPS meter

A DPS meter for **AION 2 Global** with fight result cards, boss timers and a character Control Panel. It reads the game's network traffic on your own PC. It doesn't touch the game files or memory, and it doesn't inject anything into the game.

**[⬇ Download the latest version](https://github.com/aion2tool/ratmeter/releases/latest)** · Windows 10/11 (64-bit) · Free and open source (GPL-3.0)

> Based on **[Kuroukihime/AIon2-Dps-Meter](https://github.com/Kuroukihime/AIon2-Dps-Meter)** (the original RATmeter). This edition adds the features below and some accuracy fixes. It's an unofficial fan tool, not made or approved by NCSOFT. Use it at your own discretion and in line with the game's terms of service.

---

## Install (about 2 minutes)

1. **Install Npcap** from **[npcap.com](https://npcap.com/#download)**. During setup, tick **"Install Npcap in WinPcap API-compatible Mode"**. Restart your PC if it asks.
2. **Download `RATmeter.zip`** from **[Releases](https://github.com/aion2tool/ratmeter/releases/latest)** and unzip it to its own folder, e.g. `Documents\RATmeter`.
3. **Run `AionDpsMeter.UI.exe`**, then start AION 2 and log in. The meter finds the game by itself.

Nothing else to install: .NET is included. Windows SmartScreen may warn about an unknown app the first time. Click **More info → Run anyway**.

**Updates:** the meter checks this page when it starts and offers new versions with one click. Your settings and history are kept.

## Use

| | |
|---|---|
| **Fight anything** | Damage, DPS and share appear per player. The meter hides itself out of combat and when you're in game menus. |
| **Left-click a player** | Opens their official character page (gear) in your browser. |
| **Right-click a player** | Skills, crits, back attacks, a DPS graph and the combat log. |
| **⋯ menu** (top of the meter) | Control Panel, fight history, last fight, recording, settings. |
| **🔒 lock** | Makes the meter click-through and ghost-transparent while you play. |

### Hotkeys (change them in Settings → Hotkeys)

| Keys | Does |
|---|---|
| `Ctrl+Alt+M` | Show / hide the meter |
| `Ctrl+Alt+L` | Lock / unlock (click-through) |
| `Ctrl+Alt+C` | Clear the meter |
| `Ctrl+Alt+F` | Show the last fight's result card (◀ ▶ for earlier ones) |
| `Ctrl+Alt+T` | Open the Control Panel |
| `Ctrl+Alt+B` | Show the boss alerts again |
| `Ctrl+Alt+R` | Start / stop recording a video (needs ffmpeg, see below) |

## Features

**Meter**
- Three layouts (Standard, Compact, Slim), class colors, gear score / combat power per player, live ping.
- DPS is damage over the whole fight, and a fight ends when the boss dies, so numbers match a log you'd keep yourself.
- **Alliance / field bosses:** the game only sends you your own hits there, so others show as estimates (~) and shares as % of the boss's HP.
- **Lag check:** tells you when the *server* stopped sending data during a fight (so the lag wasn't your connection).

**After a boss fight**
- **Result card:** ranking, DPS, crit % per player, and your kill time against your last kill ("▼ 0:12 faster", kill #, best time).
- **Copy image / Copy text** for Discord, or post kills automatically to your own Discord channel when a friend you list was in the party (Settings → Notifications → Discord; off until you add a webhook).
- **Boss history & why:** your fights on each boss and level, with what changed (hits per second, damage per hit, crits, skills).
- **Fight history:** every fight is saved and searchable by player. **Compare with best** shows what you did differently.

**Timers & alerts**
- Pop-ups before scheduled bosses and events, field boss respawns, and kill timers (the region is detected automatically).
- **Field boss map:** when you open the world map, the meter learns where each field boss is. A "5 MIN · GET THERE" card then shows its spot.
- **Skill ready alert:** a round icon next to your character when a skill you pick comes off cooldown (Settings → Notifications).

**Control Panel** (`Ctrl+Alt+T`)
- Every character you play, with last login, this week's boss clears, Pure Odyle gathered, Odyle Energy crafted and your own task list.
- **Pets:** counts pet soul drops toward each pet's next level, with a pop-up when one levels up.

**Recording (optional)**
- Records boss fights automatically (with the 15 seconds before the pull), or any time with `Ctrl+Alt+R`. Old videos are deleted after the days you choose.
- Needs **ffmpeg**: open a terminal and run `winget install Gyan.FFmpeg`, or put `ffmpeg.exe` in the meter's folder. Turn it on in Settings → Recording.

**Comfort**
- Start with the game: the meter can start itself when AION 2 starts and close with it (Settings → Window).
- Stays on top of the game window. Nickname hiding for streams and screenshots.

## Your data & privacy

Everything (settings, fight history, recordings) stays in the meter's folder and `%LOCALAPPDATA%\AionDpsMeter` on your PC. The meter only goes online to:

- check **this GitHub page** for updates;
- download boss and event **timers** (shugo.gg), **field boss map** images (allthings.how) and the **pet list** (aion2data.com);
- look up **gear scores and character pages** on NCSOFT's official AION 2 site, for the players in your fight;
- post to **your own Discord webhook**, only if you set one.

## Troubleshooting

- **No damage shows up:** make sure Npcap is installed in *WinPcap API-compatible mode*, then restart the PC. If it still shows nothing, right-click `AionDpsMeter.UI.exe` → **Run as administrator**.
- **Using a VPN or ping booster (ExitLag etc.):** that's supported. The meter finds the game on any network adapter, including loopback.
- **The meter disappeared:** press `Ctrl+Alt+M`. It also hides by itself out of combat and in menus (Settings → Window → Auto-hide).
- **Logs:** the `Logs` folder next to the exe. Attach a log when you report a problem in [Issues](https://github.com/aion2tool/ratmeter/issues).

**Uninstall:** turn off "Start with AION 2" in Settings, close the meter, delete its folder and `%LOCALAPPDATA%\AionDpsMeter`. To remove Npcap, use Windows' "Add or remove programs".

## Build from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download):

```
dotnet test AionDpsMeter.Tests
dotnet publish AionDpsMeter.UI/AionDpsMeter.UI.csproj -c Release -r win-x64 --self-contained true -o dist/RATmeter
```

## Also from aion2tool

- **[Craft Bench](https://aion2tool.github.io/craft-bench/)**: what you need for any AION 2 craft, step by step, with your materials and market prices.

## License & credits

GPL-3.0, same as the original project (see [LICENSE](LICENSE)). The meter's core is by **Kuroukihime** and contributors: [Kuroukihime/AIon2-Dps-Meter](https://github.com/Kuroukihime/AIon2-Dps-Meter). AION is a trademark of NCSOFT Corporation.
