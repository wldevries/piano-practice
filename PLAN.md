# Piano Practice — v1 Plan

A WinUI 3 desktop app for practising on a Roland FP-10 over USB-MIDI. It covers note reading, ear training and MIDI play-along.

## Tech stack

| Concern | Choice | Notes |
|---|---|---|
| UI | WinUI 3 (Windows App SDK), .NET 10 | Local hobby project with no installer or distribution. Unpackaged (`WindowsPackageType=None`) only so F5 starts quickly |
| MVVM | CommunityToolkit.Mvvm | `[ObservableProperty]` and `[RelayCommand]` source generators |
| MIDI | Melanchall.DryWetMidi | Device input/output plus MIDI file parsing |
| Drawing | Microsoft.Graphics.Win2D | `CanvasControl` for static views, `CanvasAnimatedControl` for falling notes |
| Notation | Hand-drawn with the Bravura font (SMuFL, SIL OFL) | Glyphs drawn as text; staff lines, stems and ledger lines drawn as lines |
| Tests | xUnit | Core logic only |

Sound comes from the FP-10 itself: the app sends Note On/Off to the piano, so v1 needs no audio engine.

## Solution layout

```
PianoPractice.sln
├─ src/PianoPractice.Core/        net10.0 class library with no UI references
│   ├─ Theory/      Pitch, NoteName, Interval, KeySignature, ChordDetector
│   ├─ Midi/        IMidiService, MidiNoteEvent (DryWetMIDI wrapped behind an interface)
│   ├─ Exercises/   NoteReadingExercise, IntervalExercise, PlayAlongSession
│   └─ Stats/       session results, per-note accuracy, JSON persistence
├─ src/PianoPractice.App/         WinUI 3
│   ├─ Controls/    PianoKeyboard, GrandStaff, FallingNotes (Win2D-backed UserControls)
│   ├─ Rendering/   KeyboardRenderer, StaffRenderer, Smufl glyph constants
│   ├─ Pages/       Live, NoteReading, Intervals, PlayAlong, Settings
│   ├─ ViewModels/
│   └─ Assets/Fonts/Bravura.otf
└─ tests/PianoPractice.Core.Tests/
```

Rule: everything musical (theory, exercise state, scoring) lives in Core and gets unit tests. The App project only draws and wires things up.

## Cross-cutting design

- **MIDI service:** a singleton that lists the inputs and outputs, auto-selects anything whose name contains "FP-10", and remembers the last device. It raises `NoteOn(pitch, velocity)` and `NoteOff(pitch)` and has `PlayNote(pitch, velocity, duration)`. It also detects when the device is unplugged or plugged back in. Events arrive on a MIDI thread, so ViewModels marshal them to the UI thread with `DispatcherQueue`.
- **Pitch model:** a MIDI number (21–108 on the FP-10) plus a spelling (letter + accidental). Spelling depends on context (key signature, or the interval being drilled), so `Pitch` carries both.
- **Settings:** JSON in `%LocalAppData%\PianoPractice\` (an unpackaged app has no `ApplicationData`). Holds the device, note-name language (C D E or Do Re Mi) and exercise defaults.
- **Navigation:** a `NavigationView` with one page per mode, plus a persistent status chip showing the MIDI connection.

## Milestones

### M0: Skeleton ✅
- `git init`; the solution with three projects; NuGet references.
- Bundle the Bravura font and confirm it renders in a `CanvasControl`.
- NavigationView shell with empty pages.
- **Done when:** the app launches and shows a "Hello" staff glyph (treble clef) drawn with Win2D.

### M1: MIDI and live keyboard ✅
- Device picker in Settings, auto-connect, and the connection status chip.
- `PianoKeyboard` control: 88 keys (range configurable) with pressed keys highlighted. Velocity can tint the highlight.
- Clicking an on-screen key sends it to the FP-10, which plays it.
- Live page: names of the notes currently held, plus chord detection (major, minor, dim, aug, sus, 7ths; inversions shown as "C/E").
- **Done when:** playing a C major chord on the FP-10 lights C-E-G and shows "C".

### M2: Grand staff renderer ✅
- `GrandStaff` control: treble and bass clefs, ledger lines, accidentals, whole/half/quarter noteheads with stems, and key signatures.
- Core: `StaffPosition` maps a spelled pitch and clef to a line/space index (unit-tested), and middle C goes to treble or bass according to a split point.
- Per-note colour, used for correct/wrong feedback.
- Live page shows the held notes on the staff, spelled using the current key.
- **Done when:** every key on the FP-10 shows up at the correct staff position.

### M3: Note-reading flashcards ✅
- A note appears on the staff; you play it on the piano.
- Options: clef (treble/bass/both), range, naturals only or with accidentals, key signature, and whether octave must match.
- Feedback: green or red, the correct key flashed on the keyboard, and reaction time.
- Adaptive selection: weight notes by recent error rate and slowness.
- Session summary showing accuracy and average time, with a per-note heatmap on the keyboard.
- **Done when:** a five-minute session runs end to end and the stats are saved.

### M4: Interval ear training ✅
- The FP-10 plays an interval (ascending, descending or harmonic) from a random root.
- Two answer modes: **play it back** (judged on the piano) or **name it** (buttons m2 … P8).
- Choose which intervals are in the pool, with presets that progress: P5/P8 → add M3/P4 → all.
- Replay button; optional reference mode that plays the root again.
- Interval stats feed the shared stats store.
- **Done when:** you can drill an interval set and see your accuracy for each interval.

### M5: MIDI play-along with wait mode 🧪 (built 2026-11-01, needs a run on the FP-10)
- Load a `.mid` file and choose which tracks or channels count as right hand / left hand. Defaults: one part is auto-split between the hands (`HandSplitter` follows where each hand is, so a voice crossing middle C stays in one hand); two parts become RH (higher) and LH; a band file gets melody → RH, bass → LH and everything else ignored. Drums are always ignored. Track list shows General MIDI instrument names.
- Falling notes over the keyboard, coloured by hand (RH blue, LH orange). Drawn with a `CanvasControl` redrawn on `CompositionTarget.Rendering`, not `CanvasAnimatedControl`, so everything stays on the UI thread.
- **Wait mode:** the song pauses at each chord until you play all the notes it expects. **Flow mode:** it plays at tempo and scores your timing (±200 ms window).
- Practise one hand while the app plays the other hand on the FP-10, or mutes it. Also "just listen": the piano plays both hands.
- Tempo slider (25–120%) and A–B loop over a bar range.
- Built-in public-domain songs in a small text format (`Core/PlayAlong/Songs/*.song`; bar lengths are checked by tests), easiest first: Mary Had a Little Lamb, Twinkle Twinkle, Ode to Joy, Frère Jacques, Jingle Bells, Happy Birthday, Greensleeves, Canon in D (simplified), Minuet in G, Für Elise (opening).
- **Sheet music view** (instead of falling notes): a scrolling grand staff, RH on treble and LH on bass, with spacing proportional to time. `ScoreBuilder` derives notation from any song: rhythms quantized to sixteenths per bar, key spelling (key guessed from the notes when a MIDI file has none or says C major), accidentals carried through the bar, rests, stems, beams per beat, and 8va for high passages. It is an approximation for MIDI files; no ties, triplets or multiple voices per staff.
- **Done when:** a simple two-hand MIDI file can be practised hands-separately in wait mode.

## Out of scope for v1 (backlog)
- MusicXML import and full score view with a cursor
- Melody dictation, chord-quality ear training, rhythm drills
- Built-in SoundFont synth (MeltySynth) for practising without the piano connected
- Practice history and streaks dashboard
- Note reading **landmarks** hint: on a mistake (or optionally always), show the nearest landmark note and the distance to it (e.g. "C5 + 2 steps"). Configurable landmark set; default C/G: C4, G4, C5, (F5), G5, C6 and bass F3, C3, C2. Wouter already reads via C and G. His first session's fastest notes were exactly C5 and G4, and the slowest were the furthest from them (E5, D5, B4). Possibly also a drill that only asks landmarks, then expands outward.
- More songs. Rock and pop are copyrighted, so they can't be built in, but your own `.mid` files work. More public-domain pieces can be added as `.song` files.
- Play-along: per-song best score, sustain pedal as play/pause, metronome count-in, and showing the notes on the grand staff.
- Measure Bluetooth latency against USB. It matters for timing scores in M5 flow mode.
- Optional: replace DryWetMIDI's WinMM device layer with the Windows MIDI Services SDK (`Microsoft.Windows.Devices.Midi2`: multi-client, MIDI 2.0, richer device info). Not needed for anything so far.

## Bluetooth MIDI ✅ (2026-11-01)
The piano can't sit next to the PC, so it connects over Bluetooth. What works:
1. **Don't** pair the FP-10 in Windows Settings (the old in-box BLE stack didn't work).
2. Install Windows MIDI Services **Preview 13** Tools + Transports from https://github.com/microsoft/MIDI/releases. It has a BLE MIDI 1.0 transport.
3. Connect the FP-10 once in **Windows MIDI Bluetooth Setup**. The MIDI service keeps the connection and reconnects, also after a reboot.
4. The FP-10 then shows up as an ordinary MIDI port named **"FP-10"** (USB shows up as "Roland Digital Piano"). Auto-detect matches both; no app code was needed.

Fallbacks if the preview breaks: the [Perfect-Bluetooth-MIDI-For-Windows](https://github.com/mayerwin/Perfect-Bluetooth-MIDI-For-Windows) bridge (port "BT-MIDI Bridge", also auto-detected), or BLE MIDI in-app via `Windows.Devices.Bluetooth` (GATT service `03B80E5A-EDE8-4B33-A751-6CE34EC4C700`).

## Risks and open questions
- **MIDI file quality:** many free MIDI files have messy tracks. Keep a small curated set of known-good files for testing M5.
- **Win2D DPI and resizing:** draw in device-independent pixels and re-layout on `SizeChanged`. Check it on a high-DPI display early (M0/M1).
- **Device exclusivity:** a WinMM MIDI port can only be opened by one app at a time unless Windows MIDI Services is active. The app should fail gracefully when another app holds the FP-10.
