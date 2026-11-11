# Dungeon Run: .NET port parity ledger

Source: DrawnUi.Rust `examples/dungeon` at main 742cb8e. Target: this repo, web head first (DrawnUi.Web, pure WebAssembly),
DrawnUi referenced as source (`C:\Dev\Cases\GitHub\DrawnUi`, branch Cells). Status: `done`, `partial` (reason), `blocked`
(what it waits for), `changed` (how and why it differs).

## Files

| Rust | C# | Status |
|---|---|---|
| `src/scene.rs` World (rules, rows, pilot, palette) | `src/Dungeon.Shared/Game/World.cs` | done |
| `src/scene.rs` Scene + Builder (mesh) | `src/Dungeon.Shared/Game/Scene.cs` | done |
| `src/scene.rs` tests | `tests/Dungeon.Tests/WorldTests.cs` | done (+ hash pins, euclid, row cache) |
| `src/shaders.rs` | `src/Dungeon.Shared/Game/Shaders.cs` | done, generated verbatim |
| `src/plate.rs` | `src/Dungeon.Shared/UI/Plate.cs` | done |
| `src/lib.rs` App, build | `src/Dungeon.Shared/UI/DungeonApp.cs` | done |
| `src/lib.rs` tick, show_prompt, flash_score | `src/Dungeon.Shared/UI/DungeonApp.Tick.cs` | done |
| `src/lib.rs` act, key_down/up, gesture | `src/Dungeon.Shared/UI/DungeonApp.Input.cs` | done |
| `src/lib.rs` dialogs | `src/Dungeon.Shared/UI/DungeonApp.Dialogs.cs` | done (SkiaShell in the Web head, DrawnUi bc44b28f, and in the Blazor head, DrawnUi 548d2a14) |
| `src/main.rs`, `run()` | `src/Dungeon.Web/Program.cs`, `src/Dungeon.Blazor/Program.cs` | done (web; Blazor: the standard DrawnUI Blazor startup, keyboard on, port 5873) |
| `web/index.html` | `src/Dungeon.Web/wwwroot/index.html`, `src/Dungeon.Blazor/wwwroot/index.html` | done; canonical/og URLs left out until a domain exists |
| `web/favicon.ico`, `og.jpg`, `robots.txt` | `wwwroot/` | done; `sitemap.xml` waits for a domain |
| `assets/*.ttf` | `wwwroot/fonts/` | done |

## Rules of the run (World)

| Feature | Status |
|---|---|
| `hash` / `hash01` bit-exact (u64 wrapping, `>> 33`), div/rem euclid | done, pinned by tests against values of the Rust formula |
| rows: orbs, pillars, beams, lava, power-ups, ghost lanes, ramp, landing rule | done; a per-thread row cache (pure function, same results, test RowCacheIsTransparent) |
| speed, drain, damage, shield, surge, ghost kill, pillar knock-back to the nearest free lane (first of equals) | done |
| countdown, dead, over, escape at 3000, glare, burn | done |
| title pilot | done (test ThePilotSurvives: 30 s, no hit, > 20 orbs) |
| distance double, the rest float | done |

## Scene (one SKMesh a frame)

| Feature | Status |
|---|---|
| SKMesh through `DrawnUi.Draw.Meshes` (stand-in), same spec: position float2 + a/b/c float4, 3 varyings, 56-byte stride | done |
| varyings pre-divided by depth, a.w = 1/depth | done |
| near-plane clip at 0.2 on the CPU, fan triangulation | done; each polygon corner projected once and copied into its triangles (same values) |
| far-to-near order, pillars farthest-to-the-side first (stable sort) | done |
| camera shake, bob, fall, bend, roll, straightening before the exit | done |
| rebase by 40, bricks keyed by mod(P.z, 40) | done (shader verbatim) |
| fog-color rect first, mesh with a white paint, clip to the control | done |
| exit: sky quad, terrace, the four long quads | done (not yet seen on screen: 3000 m run) |
| post effect POST with uFx every frame, uFx2/uRay/uShield/uOrb only on change | done; pixel-identical to Rust on a fixed input in 6 states (idle, orb peak, hit peak, surge, shield, ghost blackout), `tools/Dungeon.Profile fx` |

## Interface

| Feature | Status |
|---|---|
| HUD: 10 slanted cells (skewX -18), status role, live polite, whole-cell updates | done |
| orb pickup: border flash in the orbs' color (POST uOrb * uFx2.z) and the score's violet GLOW while it fades | done: checked in the browser on natural pickups (violet letters, halo, border tint) against run.drawnui.net; Nick's report came from an earlier build |
| score (distance), violet glow while it jumps (GLOW added and removed at runtime), streak x2/x3 | done; one glow effect object reused (no shader compile per orb) |
| prompt (lessons, power-ups, cheers, time), pop scale 1 + 0.7 pop², glow beat | done |
| countdown, GO, GAME OVER (lift 20), YOU ESCAPED!, restart caption after 0.6 s | done |
| GAME OVER curtain: CURTAIN shader, UseBackground Once, ReleaseFrozenSnapshot when hidden | done, checked in the browser; pixel-identical to Rust at burn 0.3, 0.6, 0.9 on a fixed input |
| title: name in a gold gradient under GLOW 0.5, caption, best escape, play caption | done; names the framework of the head ("DRAWNUI FOR .NET SAMPLE" on the web head, "DRAWNUI FOR BLAZOR SAMPLE" on Blazor), set by each head with DungeonApp.Framework |
| frame rate label on the title only | changed: hidden with IsVisible, not opacity (here a control at opacity 0 still draws and SkiaLabelFps lays out its text every frame) |
| buttons ? and II: plates with hover/press light (OnHover, Down, touch Up), ripple, button role | done |
| veil: SkiaBackdrop blur 1.5, 0x73000000 | done |
| layout: narrow < 620 (HUD margin 62), title 32 under 440, prompt 28 and GAME OVER 38 under 520 | done |
| caches: Rust `Image` (an offscreen GPU surface there) | changed: `UseCache = GPU` (in DrawnUi.Net `Image` is a CPU bitmap; GPU is what Rust's Image is) |
| dialogs: help (scroll with max height, sections, swatches, link), pause (help, home, resume in a SkiaWrap: spacing 14, centered, as Rust 004e49d), DIALOG shader Never / no input texture, animated while open | done: open/close, pause stops the run (shell Navigated + at open), help from pause comes back to pause, Enter/Space press the main button, F1 toggles; DIALOG panel needed DrawnUi 901f64e9 (iOffset of a shader without input) |
| link "MADE WITH DRAWNUI FOR .NET" opens drawnui.net | done, per head: "MADE WITH DRAWNUI FOR <framework>" opens that framework's DrawnUI docs (.NET: drawnui.net, Blazor: articles/blazor, OpenTK and WPF: their articles) in a new tab (Super.OpenLink, DrawnUi bc44b28f on the Web head) |

## Input

| Feature | Status |
|---|---|
| keys: arrows/WASD lanes, Up/W/Space/Enter jump, Escape/P menu, F1 help, any key leaves GAME OVER (after 0.6 s) | done |
| held lane key repeat 0.2 s then 0.13 s | done |
| keys the canvas navigation used are skipped (KeyboardManager.IsKeyHandled) | done |
| NumpadEnter in a dialog | done (numpad Enter arrives as Enter, DrawnUi 2e596cb9) |
| BrowserBack (Android) | n/a on the web head |
| drag: a lane per 44 pt there and back, past 16 pt it is a drag; tap/click anywhere jumps or starts | done |
| gestures Lock | done |

## Accessibility

| Feature | Status |
|---|---|
| labels read as text (SkiaLabel.DefaultAccessibilityRole), button captions presentation | done |
| health status (polite), distance "Distance N meters", countdown/prompt assertive | done |
| controls at opacity 0 (HUD on the title, x2 mark) | done: left out of the screen-reader tree, as Rust (DrawnUi 4bd759e8) |

## Performance (Chrome, 1394x964 CSS px at 1.25, shared machine)

Main thread ms per rAF tick (median / p99), frames over 20 ms in 10 s, managed bytes allocated per frame.

| Milestone | Title Debug | Run Debug | Title Release |
|---|---|---|---|
| 1, first run | 7.8 / 12.6, 0, 14 KB | 8.4 / 18.1, 3, 8 KB | |
| after stand-in fix + FPS label + builder | 7.5 / 12.8, 0, 12 KB (FPS label 5.5 KB of it) | 8.5 / 16.2, 4, 8 KB | 8.6 / 15.0, 1, 11 KB |

Rust live on the same machine, same script: title 0.7 / 1.7 ms, run 1.0 / 2.6 ms with 4 frames over 20 ms (the key presses).
Both hold 60 FPS. Scene build (CPU projection, ~490 triangles on the title) about 2.0 ms of the Debug frame, mesh draw 0.8 ms.

## Differences seen against the Rust game (running list)

| What | .NET | Rust | Status |
|---|---|---|---|
| GLOW over GAME OVER (fixed input, 256x160) | the glow itself matches; the text sits 1 px lower | 1 px higher | engine difference in label line height: .NET rounds the line height (48 for Orbitron 38) and puts the baseline at its bottom minus the descent, 0.35 px lower than Rust's unrounded line; Skia then rounds the glyphs to whole pixels. Reported |
| frame cost | 7.5-8.5 ms main thread per frame (Debug, interpreted) | 0.7-1 ms | both 60 FPS |
| pause dialog on a 400 pt wide screen | HELP and HOME on line 1, RESUME on line 2 at the left of the centered block; one centered line on desktop | the same (Rust 004e49d) | resolved: both games use a SkiaWrap (Nick's order), checked at 400x760 and 1280x760 |
