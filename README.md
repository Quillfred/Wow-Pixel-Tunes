# PixelTunes

Control Spotify from inside World of Warcraft: Play/Pause, Next and Previous buttons in a small
in-game panel (plus optional keybinds), and a "Now Playing" popup over the game when the song changes.

It has two parts:

| Part | What it does |
|---|---|
| **WoW addon** (`PixelTunes` folder) | Shows the buttons. When you click one, it changes the colors of a tiny 28×4-pixel strip in the top-left corner of the screen. That's the only thing it can do, because WoW addons can't talk to other programs. |
| **Companion app** (`PixelTunes.exe`) | Runs in your system tray. About 20 times a second it looks at that corner of the WoW window, reads the colors, and tells Spotify what to do through the Windows media controls (the same system your keyboard's media keys use). |

**What it never does:** send keystrokes or clicks to WoW, read WoW's memory, or touch WoW's files.
It only looks at pixels the addon drew on purpose, the same way a screenshot or streaming tool sees
the screen. See "Is this allowed?" below.

---

## Install

### 1. The addon
1. Unzip `PixelTunes-Addon.zip`.
2. Copy the `PixelTunes` folder into
   `World of Warcraft\_retail_\Interface\AddOns\`
   (for WoW Forever/Classic, also copy it into that game's folder, e.g. `_classic_era_\Interface\AddOns\`).
   The result should be `...\Interface\AddOns\PixelTunes\PixelTunes.toc`.
3. On the character select screen, click **AddOns**, make sure **PixelTunes** is ticked. If it
   says "Out of date", tick **Load out of date AddOns** at the top.

### 2. The companion app
1. Put `PixelTunes.exe` anywhere you like (e.g. `Documents\PixelTunes\`).
2. Double-click it. Windows may show **"Windows protected your PC"** because the app isn't
   code-signed. Click **More info → Run anyway**. (Your antivirus may also scan it the first time.)
3. A green icon appears in the system tray (bottom-right, you may need to click the **^** arrow).
   Right-click it for options, including **Start with Windows**.

No install, no .NET download, no Spotify login and no Spotify Premium needed.

### 3. WoW display mode
Use **Windowed (Maximized)** or **Windowed** (Esc → System → Graphics → Display Mode).
Exclusive **Fullscreen** may hide the screen from the companion and the popup can't appear over it.

---

## What's new in addon 0.5.0
- Compact one-row panel: `<< | Play / Pause | >> | Vol - +`. Hover the panel's edge for its name and tips.
- `/ptunes scale 0.8` (0.5 to 2) makes the panel smaller or bigger.

## What's new in 0.4.3
- Volume: if Windows refuses access to an app's volume slider, PixelTunes now changes the app's
  per-speaker volumes instead (same effect on loudness; the mixer slider itself may not move).
- Diagnostics shows, for each app in the volume mixer, exactly which volume controls Windows allows.

## What's new in 0.4.2
- Rewrote the volume code to talk to the Windows volume mixer directly. If a volume change still
  fails, the popup now says exactly which step Windows refused.
- Diagnostics shows a **Volume mixer** line listing the apps Windows reports and their volumes.

## What's new in 0.4.1
- First attempt at fixing "Couldn't change volume: Specified cast is not valid".

## What's new in 0.4.0
- **Volume buttons:** `-` and `+` on the panel (plus keybinds and `/ptunes vol+` / `/ptunes vol-`)
  move the player's own slider in the Windows volume mixer by 5%. No Spotify login or Premium
  needed, and WoW's volume and the main system volume are never touched.
- The popup briefly shows the new volume with a bar.
- **Update both the addon (in Retail and Forever) and the app**: the old app doesn't understand the
  new volume buttons.

## What's new in 0.3.0
- **Control** menu in the tray: choose **Spotify only** or **Any media player** (YouTube in a
  browser, Apple Music, etc.). Takes effect immediately.

## What's new in 0.2.0
- **Move popup...** in the tray menu: drag the Now Playing popup anywhere and resize it.
- Better WoW detection, so WoW Forever is found as well as Retail.

## Using it

- **Panel:** drag to move. Buttons: `<<` previous, `Play / Pause`, `>>` next, `Vol -` / `+` volume.
  `/ptunes scale 0.8` (0.5 to 2) resizes it.
- **Keybinds:** Esc → Options → Keybindings → AddOns → PixelTunes.
- **Slash commands:** `/ptunes` toggles the panel, `/ptunes help` lists everything.
  `/ptunes next`, `/ptunes prev`, `/ptunes play`, `/ptunes vol+` and `/ptunes vol-` also work inside macros.
- **Now Playing popup:** appears over the WoW window when the song or play/pause state changes
  (only while WoW is the active window). Turn it off from the tray menu.
- **Move or resize the popup:** with WoW open, right-click the tray icon → **Move popup...**.
  Drag the preview where you want it, scroll the mouse wheel (or click **Smaller**/**Bigger**) to
  resize, then click **Done** or press **Enter**. **Esc** cancels, **Reset** goes back to the default.
  The position is saved relative to the WoW window, so it stays put if you change resolution.
- **Other players:** tray menu → **Control → Any media player** makes the buttons work with any
  app that shows up in the Windows media controls (the media box in the taskbar volume flyout),
  e.g. YouTube in a browser or Apple Music. Spotify is still used first when it's open.
- **Volume:** `-` / `+` change only the player's slider in Settings → System → Sound → Volume
  mixer, 5% per press (`VolumeStepPercent` in the settings file). For a browser this is the whole
  browser's volume, since Windows has one slider per app.
- Other options (how long the popup stays, etc.) are in the settings file (tray menu →
  *Open settings file*; edit it while the app is closed).

---

## First test (please run this and report back)

1. **Spotify alone:** Start Spotify and play something. Start `PixelTunes.exe`, then double-click
   the tray icon to open **Diagnostics**. Click **Test Next**.
   - ✅ Spotify skips → the Spotify half works.
   - ❌ Note what the **Media** line says.
2. **WoW connection:** Start WoW with the addon, log in to a character. Keep Diagnostics open
   (move it to a second monitor if you have one, or just alt-tab to look).
   - The **Pixel strip** line should say `FOUND  block size 4px`.
   - The enlarged preview at the bottom should start with a **magenta** and a **cyan** square.
3. **The real thing:** In WoW, click `>>` on the PixelTunes panel. Spotify should skip within
   about a tenth of a second. Try `<<` and `Play / Pause`, then a keybind.
4. **Popup:** When the song changes while you're in WoW, a "Now Playing" popup should appear and
   fade out without taking focus from the game.

**Whatever happens, send me:**
- what worked and what didn't,
- the Diagnostics text (click **Copy status** and paste it),
- if step 2 failed: click **Save capture snapshot** and send the `...-zoom8x.png` file it shows you,
- the log file if anything crashed (tray menu → *Open log folder* → `pixeltunes.log`).

---

## Troubleshooting

| Symptom | Try |
|---|---|
| **Media: "Spotify not found"** | Make sure the Spotify desktop app is open. In Spotify: Settings → Display → turn on **Show desktop overlay when using media keys** (Spotify may not register with the Windows media controls when this is off), then restart Spotify. Or pick tray menu → **Control → Any media player** to control whatever is playing. |
| **WoW window: not found** | Make sure WoW is running and not minimized. Works with Retail and WoW Forever. Send me the Diagnostics text. |
| **Pixel strip: no start marker** | Is the addon enabled (`/ptunes status` in chat should answer)? Is something covering the very top-left corner of WoW? Is WoW in Windowed (Maximized) mode? |
| **Preview is all black while WoW is open** | Screen capture can't see the game. Send me a snapshot; there's a second capture method I can add. |
| **Strip found only sometimes / wrong colors** | Try `/ptunes size 8` for bigger blocks. If you use HDR, Night Light or a colour profile, tell me. |
| **Volume: "isn't in the Windows volume mixer yet"** | The player only appears in the volume mixer once it has played sound. Start playback, then try again. |
| **Commands work but popup doesn't show** | It only shows while WoW is the active window and in Windowed/Windowed (Maximized) mode. Check the tray menu toggle. |

Files:
- Settings: `%APPDATA%\PixelTunes\settings.json`
- Log and snapshots: `%LOCALAPPDATA%\PixelTunes\`

---

## Is this allowed?

There's no official Blizzard statement about tools like this, so nobody can promise it's safe. It's
built to stay in the lowest-risk category: the addon uses only normal addon features, and the
companion app is read-only towards WoW. It never sends input to the game, never reads game memory,
and never automates gameplay, which is what anti-cheat and the Terms of Use target. Use at your own
discretion.

---

## How the pixel strip works

Seven square blocks (4×4 physical pixels each by default) at the very top-left of the WoW window.
Each block is pure black/red/green/blue/cyan/magenta/yellow/white, i.e. 3 bits:

| Block | Meaning |
|---|---|
| 1, 2 | start marker: magenta, cyan |
| 3 | command: 0 none, 1 play/pause, 2 next, 3 previous |
| 4, 5 | 6-bit sequence number (goes up by 1 on every press) |
| 6 | checksum |
| 7 | end marker: yellow |

The companion acts only when the sequence number changes, and only after reading the same frame
twice in a row, so a half-drawn frame, a `/reload`, or alt-tabbing never triggers a command. The
addon spaces rapid presses 0.2 s apart so none are lost. The companion finds the block size
automatically.

---

## Building from source

Requires the .NET 8 SDK.

```
cd companion
dotnet test
dotnet publish src/PixelTunes.Companion -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

`tools/simulate_addon.lua` runs the real addon code against a fake WoW API (Lua 5.1) and records
the frames it draws. The C# tests use that file to prove the addon and the app agree on the
protocol. `tools/FakeWow` is a test window that pretends to be WoW with the addon running.

```
lua5.1 tools/simulate_addon.lua addon/PixelTunes > companion/tests/PixelTunes.Tests/addon_vectors.txt
```
