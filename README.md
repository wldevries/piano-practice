# Piano Practice

A Windows desktop app for practising piano on a digital piano connected over MIDI. It was built for a Roland FP-10 and should work with any MIDI piano. The piano makes all the sound: the app sends notes to it and listens to what you play.

## Features

- **Live:** shows the notes you hold on a keyboard and a grand staff, and names the chord (e.g. "C/E").
- **Note reading:** a note appears on the staff and you play it. Notes you get wrong or answer slowly come up more often. Each session ends with a summary and a per-key heatmap.
- **Intervals:** the piano plays two notes and you play them back or name the interval. Levels progress from P5/P8 up to all intervals, there's a "learn the intervals" panel, and wrong answers are classified (near miss, wrong octave, wrong direction).
- **Play-along:**
  - Songs: ten built-in public-domain songs, or your own `.mid` files.
  - Views: falling notes or scrolling sheet music.
  - **Wait for me** stops at each chord until you play it, and **Keep going** scores your timing.
  - You can practise one hand while the piano plays the other.
  - Tempo is adjustable, and you can loop a range of bars.

## Requirements

- Windows 10 (1809) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A MIDI piano connected by USB or Bluetooth. The app connects on its own to a device whose name looks like a piano; otherwise pick the device in Settings.

## Build and run

```
dotnet build PianoPractice.slnx
src\PianoPractice.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\PianoPractice.exe
```

Or open `PianoPractice.slnx` in Visual Studio and press F5. The app is unpackaged, so there is no installer and no MSIX.

Run the tests:

```
dotnet test --project tests/PianoPractice.Core.Tests
```

Settings, practice statistics and a crash log are kept in `%LocalAppData%\PianoPractice\`.

## Bluetooth MIDI

Windows' built-in Bluetooth MIDI support didn't work with the FP-10. What does work:

1. Don't pair the piano in Windows Settings.
2. Install the Tools and Transports of [Windows MIDI Services](https://github.com/microsoft/MIDI/releases) (Preview 13 or later).
3. Connect the piano once in **Windows MIDI Bluetooth Setup**. After that it reconnects by itself and shows up as an ordinary MIDI port.

See `PLAN.md` for fallbacks.

## MIDI files

Open a `.mid` file with **Open MIDI…**. Use **Tracks** to choose which track is played by which hand. The app's first guess:
- A file with one track is split between the hands automatically.
- A file with two tracks becomes right hand (the higher part) and left hand.
- In band arrangements, the melody goes to the right hand, the bass to the left, and the rest is ignored.

Sheet music for MIDI files is worked out from the timing, so it's readable but approximate. Built-in songs are exact.

## Adding songs

Built-in songs are small text files in `src/PianoPractice.Core/PlayAlong/Songs/`, listed in file-name order:

```
title: Ode to Joy
composer: Beethoven
tempo: 96          # quarter notes per minute
time: 4/4
key: 0             # fifths: 1 = G major, -1 = F major
pickup: 0          # beats before the first full bar (optional)
RH: E4/q E4 F4 G4 | G4 F4 E4 D4 |
LH: C3/w | G2/w |
```

- Durations are `w h q 8 16 32`. Add `.` for a dotted note and `t` for a triplet (e.g. `8t`).
- A duration carries over to the following notes until a new one is given.
- `r` is a rest, and `[C3 E3 G3]/h` is a chord.
- Each `|` checks that the bar has the right length, and the tests load every song. A miscounted bar is reported with the hand and the bar number.

Built-in songs must be in the public domain.

## Project layout

- `src/PianoPractice.Core`: music theory, notation, exercises, play-along engine, MIDI. It has no UI code and is unit-tested.
- `src/PianoPractice.App`: the WinUI 3 user interface, drawn with Win2D.
- `tests/PianoPractice.Core.Tests`: xUnit tests for Core.
- `PLAN.md`: milestones and backlog.

## License

Piano Practice is free software, licensed under the [GNU General Public License v3.0](LICENSE).

## Credits

- Music notation uses the [Bravura](https://github.com/steinbergmedia/bravura) font (SIL Open Font License).
- MIDI is handled by [DryWetMIDI](https://github.com/melanchall/drywetmidi).
