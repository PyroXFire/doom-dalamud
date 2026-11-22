# Doom in FFXIV

A Doom engine running inside a Dalamud window. Classic engine, in-process, no external window capture.
Built on [ManagedDoom](https://github.com/sinshu/managed-doom) (pure C# Doom port, GPLv2), rendered into an
ImGui texture in the game's own D3D11 device.

**Requires your own `DOOM.WAD` / `DOOM2.WAD` / `FREEDOOM2.WAD`.** No game data ships with this plugin.

## What works

- Doom 1 / Doom II, vanilla and limit-removing WADs (`-file` PWAD list in the settings panel)
- Autosave: on window close, on plugin unload, on `/doom close`, plus a timer (default 5 min)
- Autosave is slot 0 (`doomsav0.dsg`) and can be auto-loaded on start ("Continue from autosave")
- Mouse look, WASD + mouse fire/use, arrows for turning, number keys for weapons
- 320x200 or 640x400 rendering, in-window scaling

## What does not work (yet)

- **No Boom/MBF.** ManagedDoom is a vanilla-format port: no Boom generalized linedefs, no voodoo dolls,
  no `MAPINFO`/`ANIMDEFS`. Vanilla and limit-removing maps are fine.
- **No sound.** Audio interfaces are wired to null implementations in this build. OpenAL + a soundfont
  can be added later; it needs a native dependency in the plugin folder.
- **Autosave only in-game.** Saving uses the engine's save action, which only runs during a live level.
  Closing at the title screen or main menu writes nothing (there is no game state to save there).
- **Linear texture filtering.** Dalamud's ImGui backend samples with a linear filter, so non-integer
  window scales look slightly soft. Integer scales (3x, 4x) look crisp.

## Install (plugin repository)

1. `/xlsettings` → Experimental → Custom Plugin Repositories
2. Add: `https://raw.githubusercontent.com/PyroXFire/doom-dalamud/main/repo.json`
3. `/xlplugins` → search **Doom in FFXIV** → install
4. `/doom` to open. Point it at your IWAD inside the window.

## Install (manual, for development)

```bash
dotnet build DalamudDoom/DalamudDoom.csproj -c Release
# copy the unpacked plugin into the Dalamud plugin directory
mkdir -p ~/.xlcore/installedPlugins/DalamudDoom/0.1.0.0
unzip -o DalamudDoom/bin/Release/DalamudDoom/latest.zip -d ~/.xlcore/installedPlugins/DalamudDoom/0.1.0.0
```
(On Windows the directory is `%APPDATA%\XIVLauncher\installedPlugins\DalamudDoom\0.1.0.0`.)

## Controls

| Action | Binding |
|---|---|
| Move / strafe | W A S D |
| Fire | Left mouse / Ctrl |
| Use | Space / Right mouse |
| Run modifier | Shift |
| Turn | Arrow keys |
| Weapons | 1-7 |
| Menu | Escape, Enter |
| Automap | Tab |

Mouse look is on by default and is only active while the cursor is over the Doom window. FFXIV does not
receive clicks or keystrokes while the Doom window has focus (Dalamud blocks them via
`WantCaptureMouse` / `WantCaptureKeyboard`).

## Architecture

- `ManagedDoom/` — vendored engine source (engine core only: no Silk.NET, no GLFW). Two patches:
  a settable save directory (`ConfigUtilities.SaveDirectory`) and a `PolledUserInput` that polls an
  external input source instead of a window's event queue.
- `DalamudDoom/DoomVideo.cs` — `IVideo` implementation. Renders `byte[]` RGBA frames, double buffered.
- `DalamudDoom/DoomHost.cs` — the engine thread: 35 Hz tic loop, key event posting, autosave, clean shutdown.
- `DalamudDoom/DoomWindow.cs` — ImGui window: frame upload (`ITextureProvider.CreateFromRaw`),
  input capture, settings.
- `tests/HeadlessTest/` — no-ImGui smoke test: boots the engine on Freedoom II, renders 400 frames,
  writes and reloads a save. Runs in CI.

## License

GPL-2.0, inherited from ManagedDoom (which is a translation of the GPL'd Doom source release).
See `LICENSE` and `ManagedDoom/licenses/`.