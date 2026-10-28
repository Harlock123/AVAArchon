# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its pieces and
controls; this file covers how the code is put together and the rules that keep it working.

## What this is

A strategy game with real-time battles in the style of Free Fall Associates' 1983 Archon, in C# on .NET 10 and
Avalonia 12, drawn through Avalonia's `DrawingContext`. Two armies, Dawn and Dusk, take turns on a 9 x 9 board; when a piece moves onto
an enemy, the two fight it out in an arena. One self-contained executable per platform (Windows, macOS and Linux,
x64 and ARM). The pieces are drawn in code (`Views/Sprites.cs`) and the sounds are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAArchon.slnx                       # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAArchon.slnx                        # every test runs headless: no window opens
dotnet run --project src/AVAArchon                # play it; --no-audio for silence
ARCHON_SHOTS=$PWD/Images dotnet test tests/AVAArchon.Tests --filter ScreenshotTests   # refresh the README's pictures
ARCHON_ODDS=1 dotnet test tests/AVAArchon.Tests --filter OddsTableTests              # re-measure Engine/OddsTable.cs
python3 tools/make_sounds.py                      # rebuild src/AVAArchon/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAArchon/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, game over) and the fixed-step loop;
    `GameEngine.Turn.cs` handles a turn: cursors, picking and moving pieces, the spellbook, starting a fight.
  - `Board.cs` is the 9 x 9 board, its light, dark and shifting squares, and the lumen cycle.
  - `Pieces.cs` has the sides, the piece kinds and their numbers, and the `Controls` record.
  - `Arena.cs` is the real-time battle: `Fight`, its `Fighter`s, missiles and pillars.
  - `Spells.cs` holds the leaders' seven spells and what each may target.
  - `Computer.cs` plans the computer's turns on the board; `Brain.cs` drives its fighter in the arena.
  - `OddsTable.cs` is generated: how often each piece beats each other one, used by `Computer`.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads, one per player) produce `Controls`.
  - `SoundManager.cs` plays the WAV files from `Sounds/`.
- `src/AVAArchon/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024 x 768 picture scaled to the
  window and runs the 60 Hz timer; `Sprites.cs` holds the 16 x 16 piece drawings. Ctrl+S saves a screenshot to
  the Pictures folder.
- `src/AVAArchon/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAArchon.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `Helpers.cs` can fight whole arena battles
  between two computer brains.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`GameEngine.Step`) from an accumulator,
  independent of the frame rate, so tests can play deterministically from a seed. Draw random numbers from the
  engine's seeded `_rng` (`new Random(seed)`, passed on to `Computer` and `Fight`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Keys and gamepad must produce the same `Dy` sign the engine expects
  (negative is up the screen). Test new controls through `InputHandler`, not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests run silent.** Tests build engines with `Silent = true`. There is no high-score table or save file.
- **Balance is measured.** After changing any piece's numbers, regenerate `OddsTable.cs` with `ARCHON_ODDS=1`
  (the command above) instead of editing it by hand.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (pieces, spells, controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release; either then asks the portfolio site to rebuild.
