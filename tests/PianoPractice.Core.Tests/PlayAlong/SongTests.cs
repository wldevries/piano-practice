using PianoPractice.Core.PlayAlong;
using PianoPractice.Core.Theory;

namespace PianoPractice.Core.Tests.PlayAlong;

public class SongTests
{
    private const string Simple = """
        title: Test
        tempo: 60   # one beat per second makes the maths easy
        time: 4/4
        RH: C4/q D4 E4/h | [C4 E4 G4]/w |
        LH: C3/w | r/h G2 |
        """;

    [Fact]
    public void Parses_notes_durations_and_chords()
    {
        var song = SongText.Parse(Simple);

        Assert.Equal("Test", song.Title);
        var rh = song.Notes.Where(n => n.Hand == Hand.Right).ToList();
        Assert.Equal(["C4", "D4", "E4", "C4", "E4", "G4"], rh.Select(n => n.Pitch.ToString()));
        Assert.Equal([0, 1, 2, 4, 4, 4], rh.Select(n => n.Start));
        Assert.Equal([1, 1, 2, 4, 4, 4], rh.Select(n => n.Duration));

        var lh = song.Notes.Where(n => n.Hand == Hand.Left).ToList();
        Assert.Equal([("C3", 0.0), ("G2", 6.0)], lh.Select(n => (n.Pitch.ToString(), n.Start)));
    }

    [Fact]
    public void Bars_and_duration()
    {
        var song = SongText.Parse(Simple);
        Assert.Equal([0, 4], song.BarStarts);
        Assert.Equal(8, song.Duration);
        Assert.Equal(4, song.BarStart(2));
        Assert.Equal(8, song.BarStart(3)); // one past the end = end of song
        Assert.Equal(2, song.BarAt(5));
    }

    [Fact]
    public void Dotted_triplet_and_sixteenth_durations()
    {
        var song = SongText.Parse("tempo: 60\nRH: C4/q. D4/8 E4/8t F4 G4 A4/16 B4 C5 D5 |");
        double[] expected = [1.5, 0.5, 1 / 3.0, 1 / 3.0, 1 / 3.0, 0.25, 0.25, 0.25, 0.25];
        Assert.Equal(expected.Length, song.Notes.Count);
        foreach (var (e, n) in expected.Zip(song.Notes)) Assert.Equal(e, n.Duration, 6);
    }

    [Fact]
    public void Pickup_bar_is_bar_one()
    {
        var song = SongText.Parse("tempo: 60\ntime: 3/4\npickup: 1\nRH: D4/q | G4/h. | A4/h. |");
        Assert.Equal([0, 1, 4], song.BarStarts);
    }

    [Fact]
    public void A_bar_with_the_wrong_length_is_reported()
    {
        var ex = Assert.Throws<FormatException>(() => SongText.Parse("RH: C4/q D4 E4 F4 | G4/q A4 B4 |"));
        Assert.Contains("bar 2", ex.Message);
        Assert.Contains("3 beats, expected 4", ex.Message);
    }

    [Fact]
    public void Sharps_are_not_comments()
    {
        var song = SongText.Parse("RH: F#4/w |   # a comment");
        Assert.Equal("F#4", song.Notes.Single().Pitch.ToString());
    }

    [Fact]
    public void Bad_notes_are_reported_with_the_bar()
    {
        var ex = Assert.Throws<FormatException>(() => SongText.Parse("RH: C4/w | H4/w |"));
        Assert.Contains("bar 2", ex.Message);
    }

    [Fact]
    public void All_built_in_songs_parse_with_both_hands()
    {
        Assert.NotEmpty(BuiltInSongs.Ids);
        foreach (var id in BuiltInSongs.Ids)
        {
            var song = BuiltInSongs.Load(id);
            Assert.True(song.Has(Hand.Right), id);
            Assert.True(song.Has(Hand.Left), id);
            Assert.All(song.Notes, n => Assert.True(n.Pitch.IsOnPiano, id));
        }
    }

    [Theory]
    [InlineData("03-ode-to-joy.song", 16)]
    [InlineData("02-twinkle.song", 12)]
    [InlineData("09-minuet-in-g.song", 16)]
    [InlineData("10-fur-elise.song", 17)]
    [InlineData("01-mary-had-a-little-lamb.song", 8)]
    [InlineData("05-jingle-bells.song", 16)]
    [InlineData("06-happy-birthday.song", 9)]
    [InlineData("07-greensleeves.song", 33)]
    [InlineData("08-canon-in-d.song", 13)]
    public void Built_in_songs_have_the_expected_bars(string id, int bars)
    {
        Assert.Equal(bars, BuiltInSongs.Load(id).BarCount);
    }

    [Fact]
    public void Hands_end_together_in_built_in_songs()
    {
        foreach (var id in BuiltInSongs.Ids)
        {
            var song = BuiltInSongs.Load(id);
            var rh = song.Notes.Where(n => n.Hand == Hand.Right).Max(n => n.End);
            var lh = song.Notes.Where(n => n.Hand == Hand.Left).Max(n => n.End);
            Assert.True(Math.Abs(rh - lh) < 1e-6, $"{id}: RH ends at {rh}, LH at {lh}");
        }
    }

    [Fact]
    public void Midi_round_trip_keeps_notes_hands_and_bars()
    {
        var song = BuiltInSongs.Load("09-minuet-in-g.song");
        using var stream = new MemoryStream();
        MidiSongFile.Write(song, stream);
        stream.Position = 0;

        var file = MidiSongFile.Read(stream, "minuet");
        Assert.Equal(2, file.Parts.Count);
        Assert.Equal(["Right hand", "Left hand"], file.Parts.Select(p => p.Name));
        Assert.Equal([PartRole.RightHand, PartRole.LeftHand], file.DefaultRoles());
        Assert.Equal(3, file.BeatsPerBar);
        Assert.Equal(1, file.Key.Fifths);

        var loaded = file.ToSong(file.DefaultRoles());
        Assert.Equal(song.Notes.Count, loaded.Notes.Count);
        foreach (var (a, b) in song.Notes.Zip(loaded.Notes))
        {
            Assert.Equal(a.Pitch, b.Pitch);
            Assert.Equal(a.Hand, b.Hand);
            Assert.Equal(a.Start, b.Start, 3);
            Assert.Equal(a.Duration, b.Duration, 3);
        }
        Assert.Equal(song.BarStarts.Count, loaded.BarStarts.Count);
        Assert.Equal(song.BarStarts[5], loaded.BarStarts[5], 3);
    }

    [Fact]
    public void Default_roles_put_the_higher_part_in_the_right_hand()
    {
        // Write LH first by swapping hands, so the higher part is the second track.
        var song = BuiltInSongs.Load("02-twinkle.song");
        var swapped = new Song(song.Title, song.Notes.Select(n => n with { Hand = n.Hand == Hand.Right ? Hand.Left : Hand.Right }), song.BarStarts, song.BeatsPerMinute);
        using var stream = new MemoryStream();
        MidiSongFile.Write(swapped, stream);
        stream.Position = 0;

        var file = MidiSongFile.Read(stream, "twinkle");
        Assert.Equal([PartRole.LeftHand, PartRole.RightHand], file.DefaultRoles());
    }

    private static MidiPart Part(string name, int channel, int? program, int lowMidi, int highMidi, int count = 10) =>
        new(channel, channel, name,
            Enumerable.Range(0, count).Select(i => new SongNote(new Pitch(i % 2 == 0 ? lowMidi : highMidi), i, 0.5, Hand.Right)).ToList(),
            program);

    [Fact]
    public void Band_arrangement_defaults_to_melody_and_bass_only()
    {
        // Shaped like a typical pop MIDI file: lots of instruments, a MELODY track and a synth bass.
        MidiPart[] parts =
        [
            Part("E.PIANO 2", 0, 5, 53, 65),
            Part("SYN BASS 2", 1, 39, 27, 42, count: 50),
            Part("MELODY", 3, 73, 56, 68),
            Part("PICCOLO", 4, 72, 56, 73, count: 80),
            Part("SYNTH DRUM", 5, 118, 55, 72),
            Part("DRUMS", 9, null, 35, 70),
            Part("STRINGS", 10, 48, 68, 92),
        ];

        Assert.Equal(
            [PartRole.Ignore, PartRole.LeftHand, PartRole.RightHand, PartRole.Ignore, PartRole.Ignore, PartRole.Ignore, PartRole.Ignore],
            MidiSongFile.DefaultRolesFor(parts));
    }

    [Fact]
    public void Without_a_melody_track_the_piano_part_is_played()
    {
        MidiPart[] parts = [Part("", 0, 33, 30, 45), Part("", 1, 0, 45, 80), Part("", 2, 48, 60, 80)];
        Assert.Equal([PartRole.Ignore, PartRole.SplitAuto, PartRole.Ignore], MidiSongFile.DefaultRolesFor(parts));
    }

    [Fact]
    public void Without_hints_the_busiest_high_part_is_the_tune()
    {
        MidiPart[] parts = [Part("", 0, 33, 30, 45), Part("", 1, 25, 60, 70, count: 40), Part("", 2, 48, 60, 80)];
        Assert.Equal([PartRole.LeftHand, PartRole.RightHand, PartRole.Ignore], MidiSongFile.DefaultRolesFor(parts));
    }

    [Fact]
    public void A_single_part_is_split_at_middle_c()
    {
        Assert.Equal([PartRole.SplitAuto, PartRole.Ignore], MidiSongFile.DefaultRolesFor([Part("Piano", 0, 0, 40, 80), Part("", 9, null, 35, 50)]));
    }

    [Fact]
    public void Parts_describe_their_instrument()
    {
        Assert.Equal("MELODY  (Flute)  ·  10 notes  ·  G#3–G#4", Part("MELODY", 3, 73, 56, 68).Describe());
        Assert.StartsWith("Flute  ·", Part("", 3, 73, 56, 68).Describe());
        Assert.Equal("Program 200", GeneralMidi.InstrumentName(199));
    }

    [Fact]
    public void Auto_split_keeps_a_low_voice_that_crosses_middle_c_in_the_left_hand()
    {
        // Like the Bubble Bobble intro: high arpeggios over a slow line D#4 D4 C4 A#3 that crosses middle C.
        var song = SongText.Parse("""
            tempo: 60
            RH: C7/8 G6 D#6 C6 C7 G6 D#6 C6 | A#6/8 F6 D6 A#5 A#6 F6 D6 A#5 |
            LH: D#4/h D4 | C4/h A#3 |
            """);
        var split = HandSplitter.Split(song.Notes.Select(n => n with { Hand = Hand.Right }));

        Assert.Equal(["D#4", "D4", "C4", "A#3"], split.Where(n => n.Hand == Hand.Left).Select(n => n.Pitch.ToString()));
        Assert.All(split.Where(n => n.Pitch.Octave >= 5), n => Assert.Equal(Hand.Right, n.Hand));
    }

    [Fact]
    public void Auto_split_keeps_a_melody_that_dips_low_in_the_right_hand()
    {
        // Bass in octaves, melody that dips to F4 while the bass is far below.
        var song = SongText.Parse("""
            tempo: 60
            RH: A#5/q F5 G5 G#5 | A5 F4 G4 G#4 |
            LH: A#1/q A#2 A#1 A#2 | A1 A2 A1 A2 |
            """);
        var split = HandSplitter.Split(song.Notes.Select(n => n with { Hand = Hand.Left }));
        foreach (var note in split)
        {
            Assert.Equal(song.Notes.Single(n => n.Start == note.Start && n.Pitch == note.Pitch).Hand, note.Hand);
        }
    }

    [Fact]
    public void Auto_split_divides_a_chord_too_wide_for_one_hand()
    {
        var song = SongText.Parse("RH: [C3 E3 G3 C5 E5]/w | [D4 E4]/w |");
        var split = HandSplitter.Split(song.Notes);
        Assert.Equal([Hand.Left, Hand.Left, Hand.Left, Hand.Right, Hand.Right], split.Take(5).Select(n => n.Hand));
        Assert.All(split.Skip(5), n => Assert.Equal(Hand.Right, n.Hand)); // a close pair stays in one hand
    }

    [Fact]
    public void Split_by_pitch_sends_middle_c_and_up_to_the_right_hand()
    {
        var song = SongText.Parse("RH: [B3 C4]/w |");
        using var stream = new MemoryStream();
        MidiSongFile.Write(song, stream);
        stream.Position = 0;

        var file = MidiSongFile.Read(stream, "split");
        var split = file.ToSong([PartRole.SplitByPitch]);
        Assert.Equal(Hand.Left, split.Notes.Single(n => n.Pitch == new Pitch(59)).Hand);
        Assert.Equal(Hand.Right, split.Notes.Single(n => n.Pitch == Pitch.MiddleC).Hand);
    }
}
