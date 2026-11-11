# Dungeon Run for DrawnUI .NET

Dungeon Run is a first-person dungeon runner you play in the browser. It is a port of the
[DrawnUI for Rust example](https://run.drawnui.net) to DrawnUI for .NET: the same game and the same look. The 3D is
one `SKMesh` with SkSL shaders on Skia 2D, no 3D API. It runs as pure .NET WebAssembly (DrawnUi.Web).

## How to play

You run forward by yourself. Stay alive as long as you can.

| Action      | Keyboard                                  | Touch                                |
| ----------- | ----------------------------------------- | ------------------------------------ |
| Start       | Space, Enter                              | Tap                                  |
| Change lane | Left / Right, A / D (hold to keep moving) | Drag left or right (finger or mouse) |
| Jump        | Up, W, Space                              | Tap or click anywhere                |

- **Rune pillars**: change lane.
- **Red beam** and **lava**: jump.
- **Orbs**: a little health each (ten fill one cell). They come in rows of three: the third of a row taken without a
  miss counts double. The orbs lead to the lane the next pillars leave free.
- **The distance is the score**: how far you got, top left, in meters. The dungeon ends at 3000 m: the last hazards
  stand at 2900 m, the corridor straightens and brightens toward the daylight at its end, and the outside shows
  through the open doorway. Out of the door the run is over: YOU ESCAPED! with your time; the title keeps the
  session's best. That screen stays until a key or a tap.
- **Ghost**: hangs in any lane. Far away only its eyes burn; as you come near the torches dim, the fog thickens, then
  it reveals itself and lunges. It takes three tenths of your health in a blackout; the one that takes the last of it
  stays over you while you lie there. Change lane.
- **Green cross**: gives 15 percent of your health back, in a green aura.
- **Surge** (cyan arrows): four seconds much faster, and nothing hurts you; rays of light fly past along the borders
  of the screen and fade as the time runs out.
- **Shield** (golden): SHIELD ON, and for ten seconds the next pillar or ghost costs nothing (beams and lava still
  burn); a golden aura at the borders while it holds, a golden flash and ABSORBED! when it takes the hit.
- Running itself wears you down: a cell of health every four seconds, so the orbs are what keeps you going. A pillar
  takes two tenths of your health, a beam or lava one tenth, a ghost three. The run ends at zero. After a hit nothing
  hurts you for a moment.
- The run gets faster with distance, and the colors change every 400 units.

A run starts calm: few hazards, pillars before the first jumps, the full share after about 40 seconds. In that start
every hazard comes with a prompt, JUMP or CHANGE LANE; after it, the first three of each kind in a session. Once a
session: YOU TIRE AS YOU RUN at the second cell lost, COLLECT ORBS TO HEAL after the first hit, AVOID GHOSTS after the
first ghost gone by; LOW HEALTH under four cells. Every twelve seconds without a hit a word of praise shows instead.

A run starts with a countdown. When the health is gone, GAME OVER stays over the still living dungeon until a key or
a tap (TAP OR PRESS SPACE TO CONTINUE), then its picture burns away over the title's run.

The title screen plays the game by itself until you start.

## Run it

You need the .NET 10 SDK and the `wasm-tools` workload. Check both before the first build:

```
dotnet --version
dotnet workload list
```

`dotnet --version` must show 10.x, and `dotnet workload list` must list `wasm-tools`
(`dotnet workload install wasm-tools` adds it).

For now the game references the DrawnUi source, not the NuGet: clone [DrawnUi](https://github.com/DrawnUi/DrawnUi.Net)
next to this folder as `..\DrawnUi` and check out its branch `Cells`.

Run the web head from this folder:

```
dotnet run --project src/Dungeon.Web
```

Then open http://localhost:5871. The first build takes a few minutes (it links Skia into the WebAssembly runtime).

Run the tests:

```
dotnet test tests/Dungeon.Tests
```

## Project layout

- `src/Dungeon.Shared`: the game, shared by every head (the rules, the mesh scene, the SkSL, the HUD and dialogs).
- `src/Dungeon.Web`: the browser head on DrawnUi.Web.
- `tests/Dungeon.Tests`: tests of the rules of the run.
- `tools/Dungeon.Profile`: a headless profile of the frame's allocations on DrawnUi.Net.

## Status

Works: the whole run (rules, mesh, lighting, fog, lava, ghosts, power-ups, the exit and YOU ESCAPED!), the post
effect, the HUD, the prompts and lessons, the countdown, GAME OVER and its burning curtain, the title's own run,
keyboard, drag and tap, hover on the buttons, and the screen-reader tree.

Still being ported: the help and pause dialogs, the link in the help, the flash of an orb pickup.

Frame rate in Chrome (Debug build): a steady 60 FPS on the title and in a run.

[PARITY.md](PARITY.md) lists every feature of the Rust game and where it is here.

## Credits and license

The game is ported from the Dungeon Run example of
[DrawnUI for Rust](https://github.com/DrawnUi/DrawnUi.Rust/tree/main/examples/dungeon). Fonts: Orbitron
(SIL Open Font License) and Noto Sans Math (SIL Open Font License).

MIT License, see [LICENSE](LICENSE).
