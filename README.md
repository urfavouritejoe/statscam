# StatsCam

Live match stats for the **VRFS Camera** client — the PC spectator build used to record
and stream VRFS lobbies. Everything renders inside the camera window. No browser, no
server, nothing to configure.

**Press `Y` for the full stats board.**

```
Camera.exe (Unity 6000.3.20f1, IL2CPP)
└─ MelonLoader v0.7.3
   └─ Mods/StatsCam.dll
      ├─ capture   MatchEventSink.Write hook · Photon roster
      ├─ model     MatchState reducer · per-player accumulators
      └─ overlay   live panel · goal flash · Y stats board
```

---

## What it shows

**Always** — a panel top-right: score, clock, possession bar, shots, on target, xG,
passes, pass accuracy, saves, touches, and a rotating player spotlight.

**On a goal** — a card with the scorer, assist, minute, distance, ball speed and xG, plus
`BRACE` / `HAT-TRICK` / `WORLDIE` badges where they apply.

**On `Y`** — a full-screen board: every player ranked, team comparison bars, and a shot
map where filled markers are goals and marker size is xG.

Match records are written to `Camera\StatsCam\matches\*.json` when a match ends. A file,
not a service — nothing leaves the machine.

### Controls

| Key | Action |
|---|---|
| <kbd>Y</kbd> | Full stats board |
| <kbd>F1</kbd> | Toggle the whole overlay |
| <kbd>F2</kbd> | Panel density: full · compact · scoreline only |
| <kbd>F3</kbd> | Pin / cycle the player spotlight |
| <kbd>F4</kbd> | Unpin the spotlight |
| <kbd>F8</kbd> | Replay the last goal flash |
| <kbd>F10</kbd> | Dump roster + Photon `CustomProperties` to the log |
| <kbd>F11</kbd> | Re-run the analytics type catalogue |
| <kbd>F12</kbd> | Print the event summary |

---

## Install

1. Install [MelonLoader v0.7.3](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3)
   into your Camera folder — either run the installer against `Camera.exe`, or extract
   `MelonLoader.x64.zip` over the folder.

   **Version matters.** v0.7.3 ships Cpp2IL `2022.1.0-pre-release.21`, the first release
   supporting IL2CPP metadata **v39**. Camera.exe is Unity 6000.3.20f1 / v39; older
   MelonLoader builds fail with *"not a supported version[39]"*.

2. Launch `Camera.exe` once and let it finish. First run generates interop assemblies
   from a ~100 MB `GameAssembly.dll` — several minutes is normal, and happens once.

3. Drop `StatsCam.dll` into `Camera\Mods\`.

---

## Build

Needs the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer.

```bash
dotnet build StatsCam.csproj -c Release -p:GameDir="C:\Path\To\Camera"
```

`GameDir` can also come from a `STATSCAM_GAMEDIR` environment variable, and defaults to a
`Camera\` folder beside the repo. The build copies `StatsCam.dll` into `Camera\Mods\`
automatically, and fails with a clear message if MelonLoader isn't installed there — it
references `MelonLoader.dll` and `0Harmony.dll` from the game folder.

### Building without the game

`tools/refstub/proj/` holds minimal API stubs for `MelonLoader.dll` and `0Harmony.dll`,
so the project compiles with no game install at all — useful for CI or a quick syntax
check:

```bash
dotnet build tools/refstub/proj/melon/MelonLoader.csproj -c Release -o tools/refstub/MelonLoader/net6
dotnet build StatsCam.csproj -c Release -p:GameDir=tools/refstub
```

The stubs are compile-time only. They are not a reimplementation and are never shipped.

---

## Tests

```bash
dotnet run --project tools/harness/Harness.csproj -c Release
```

The harness drives a synthetic match through the real `MatchState` reducer and renders
the **real overlay layout code** through an SVG backend, so the geometry that ships is the
geometry that gets checked. It asserts score/shot/pass/possession consistency, JSON
well-formedness, and that nothing draws outside the screen, then writes `board.svg`,
`live.svg` and `goal.svg` to `tools/harness/bin/Release/net8.0/render/`.

---

## How it works

**Pure reflection, no interop references.** Il2CppInterop emits ordinary managed classes
whose properties marshal to unmanaged memory, so `System.Reflection` reads them correctly.
Every game type is resolved by string at runtime, which means the project builds before
the interop assemblies exist and keeps working when VRFS re-obfuscates private members.
Only two assembly references, both from MelonLoader.

**Public surface only.** VRFS obfuscates private fields and methods (`bqvn`, `bdms`,
`ott`) but leaves class names, enum members, public DTO fields and property getters
intact. StatsCam touches only the stable half, and maps enums by member *name* rather than
ordinal so a renumbering upstream is a no-op.

**One reducer.** Every mutation goes through `MatchState.Apply(StatEvent)`, so live
capture and replaying a recorded log produce identical state.

**Two draw backends, one layout.** `IDraw` has a Unity IMGUI implementation for the game
and an SVG implementation for tests. The layout code doesn't know which it's talking to.

**No network.** StatsCam makes no HTTP calls and needs no VRFS account, token or endpoint.
Everything is read in-process from assemblies already loaded by the game.

---

## Status

Working: the loader attaches, interop generation succeeds on metadata v39, and the
overlay's reflective Unity binding initialises.

Unresolved: whether `MatchEventSink.Write` fires on a *spectator* client. If it does,
capture is fully automatic. If it doesn't — the log will say
`HEARTBEAT in a room, still ZERO events` — events have to be derived from ball and player
transforms instead. `IEventSource` exists so that change touches one file.

Not implemented: corners, fouls, cards, distance covered and body-part attribution. These
were deliberately removed rather than displayed as permanent zeros. Corners/fouls need the
rule events; distance needs a transform sampler; body part isn't carried on `MatchEvent`.

`RTG` in the player table is StatsCam's own ranking score, not the game's
`CardStats.get_Composite`.

---

## Layout

```
src/
  Core.cs            MelonMod entry, install retry, match flow, hotkeys
  Config.cs          key=value config
  Journal.cs         dual console/file logging
  Reflect.cs         name-based type and member resolution
  PhotonProbe.cs     one-off CustomProperties dump (diagnostics)
  capture/
    EventSource.cs   IEventSource + MatchEventSink.Write hook
    Roster.cs        Photon identity sync
    Catalogue.cs     startup type check + PitchGeometry read
  model/
    StatEvent.cs     normalized event, EventKind, Vec3
    PlayerStats.cs   per-player accumulator
    MatchState.cs    the reducer, GoalMoment, team rollups
    MatchRecord.cs   match JSON written to disk
  overlay/
    IDraw.cs         draw surface interface, Col, Align, Gui facade
    UnityDraw.cs     reflective Unity IMGUI backend
    Theme.cs         palette and geometry
    StatPanel.cs     top-right live panel
    GoalFlash.cs     goal card
    StatsBoard.cs    the Y board
    Overlay.cs       orchestration, spotlight rotation
  util/
    Json.cs          minimal JSON writer
tools/
  refstub/           MelonLoader + 0Harmony API stubs
  harness/           synthetic-match harness + SVG draw backend
```

---

## Notes

Not affiliated with, endorsed by, or supported by VRFS. StatsCam reads game state in
memory to draw an overlay; it changes no gameplay and sends nothing anywhere. Game updates
will break it periodically — that's why it builds against the public, non-obfuscated
surface rather than internals.

No game files, binaries, or data extracted from them are included in this repository, and
`.gitignore` is set up to keep it that way.
