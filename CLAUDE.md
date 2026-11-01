# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

A WinUI 3 desktop app (.NET 10, Windows only) for practising piano on a Roland FP-10 over USB or Bluetooth MIDI. Modes: Live (held notes plus chord names), note-reading flashcards, interval ear training, and play-along (falling notes or sheet music, with wait and flow modes). `PLAN.md` holds the milestones, the backlog and the Bluetooth MIDI setup notes. Keep it updated when a milestone changes.

## Commands

- Build: `dotnet build PianoPractice.slnx`. Build the solution, not the App csproj on its own: the solution builds x64 into `src/PianoPractice.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/PianoPractice.exe`, while building the csproj alone writes a different (AnyCPU) output, which leaves a stale exe at that path.
- Close the running app before building, or the exe stays locked: `Get-Process PianoPractice | Stop-Process`.
- Tests (xUnit v3 on Microsoft.Testing.Platform, set in `global.json`):
  - All: `dotnet test --project tests/PianoPractice.Core.Tests`
  - One class: `... --filter-class "*ScoreBuilderTests"`
  - One test: `... --filter-method "*Looping*"`
- Debug-only environment variables:
  - `PIANOPRACTICE_START_PAGE=<NavTag>` (Live, NoteReading, Intervals, PlayAlong) opens that page on launch.
  - `PIANOPRACTICE_DEMO_NOTES=60,64,67` fakes held notes on the Live page.
- Runtime data lives in `%LocalAppData%\PianoPractice\`: `settings.json`, `*-stats.json` per exercise, and `crash.log`.
- The FP-10 is usually connected. Playing in the app, or clicking on-screen keys, makes the real piano sound.

## Architecture

**Core vs App.** Everything musical lives in `PianoPractice.Core`, a plain `net10.0` library with no UI references, and is unit-tested: theory, notation, exercise state, scoring and play-along. `PianoPractice.App` (WinUI 3, unpackaged, MVVM with CommunityToolkit.Mvvm and DI in `App.xaml.cs`) only draws and wires things up. When adding logic, put it in Core with tests.

**Pitch model.** `Pitch` is a MIDI number only. `SpelledPitch` adds letter, accidental and octave, and the octave follows the letter, so B#3 sounds as C4. Spelling depends on context: `KeySignature.Spell`, or the interval being drilled.

**MIDI.** `IMidiService` wraps DryWetMIDI (WinMM). It raises events on a background thread, so ViewModels marshal them to the UI thread through `DispatcherQueue`. Devices are found by polling and name matching (`LooksLikePiano`); no app code is specific to Bluetooth. The piano itself makes all the sound, so there is no audio engine.

**Exercises.** Session classes (`NoteReadingSession`, `IntervalSession`) take a `TimeProvider` and a `Random`, so tests are deterministic. They record results in an `ExerciseStatsStore`: one JSON file per exercise, registered in DI under a key from `StatsKeys`.

**Play-along pipeline** (Core `PlayAlong/` and `Notation/`):
- A `Song` is notes in seconds at 100% tempo, each with a `Hand`, plus bar start times. It comes from either:
  - `SongText`: built-in `.song` files in `PlayAlong/Songs/`, embedded resources ordered by file name. Every `|` checks the bar's length, and the tests parse every built-in song, so mistakes fail the build.
  - `MidiSongFile`: reads a MIDI file into parts (track × channel), each with a `PartRole`. `DefaultRolesFor` picks roles (melody/bass for band files, `HandSplitter` auto split for single parts). The key is guessed with `KeyGuess` when the file has none or says C major.
- `PlayAlongEngine` has no clock of its own. The page calls `Update(realSeconds)` every frame from `CompositionTarget.Rendering`. The engine returns `NoteAction`s for the piano to play, and `Press(pitch)` scores the player's notes. Wait mode holds the position at the next chord and holds back that chord's accompaniment until the chord is played.
- `ScoreBuilder` turns a `Song` into notation (`Score`): rhythms quantized to sixteenths per bar, accidentals that carry through the bar, rests, beams per beat, and 8va.

**Rendering.** Win2D `CanvasControl`s are redrawn on demand. Nothing uses `CanvasAnimatedControl`, so all drawing stays on the UI thread.
- `PianoKeyboard` and `FallingNotes` share `KeyboardLayout`, which lines up the note columns with the keys. Give both controls the same `LowestKey`/`HighestKey`.
- Notation (`GrandStaffRenderer`, `SheetMusic`) uses the Bravura SMuFL font, with code points in `Rendering/Smufl.cs`.
  - One em is four staff spaces, and glyph origins sit on their line or space; `SmuflText` positions glyphs by that origin.
  - Staff positions come from `StaffPosition` and are counted in steps: half staff spaces, with 0 on the bottom line.
  - The font loads from an absolute path next to the exe, because unpackaged apps can't resolve `ms-appx:` URIs.

## Conventions

- Visual checks: take screenshots with PrintWindow (`PW_RENDERFULLCONTENT`) and click with UI Automation `InvokePattern` by element name. Buttons whose content is an icon plus text need `AutomationProperties.Name`, or the search finds the inner TextBlock.
- Commit directly on `main`, and push only when asked.
- `git` is not on PATH. Use the copy that ships with Visual Studio: `C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe`.
- Built-in songs must be public domain. Copyrighted songs (pop and rock) can only come from the user's own MIDI files.
