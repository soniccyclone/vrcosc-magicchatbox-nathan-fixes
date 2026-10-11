using System;
using vrcosc_magicchatbox.Classes.Modules.Lyrics;
using vrcosc_magicchatbox.Classes.Modules.Media;
using Xunit;

namespace MagicChatbox.Tests.Classes.Modules.Lyrics;

public class LyricsLookupPolicyTests
{
    private static LyricsQuery Query(string title = "Circles", string artist = "Post Malone", double minutes = 3.5, string album = "")
        => new()
        {
            Title = title,
            Artist = artist,
            Album = album,
            Duration = TimeSpan.FromMinutes(minutes),
        };

    [Fact]
    public void AnOrdinarySongIsLookedUp()
    {
        Assert.True(LyricsLookupPolicy.ShouldLookUp(Query(), out _));
    }

    [Fact]
    public void APodcastNeverHitsTheNetwork()
    {
        var podcast = Query(
            title: "(NEW!) Emergency Interview Before The Arrest | Some Podcast",
            artist: "Some Channel",
            minutes: 74);

        Assert.False(LyricsLookupPolicy.ShouldLookUp(podcast, out string reason));
        Assert.NotEqual(string.Empty, reason);
    }

    [Fact]
    public void ALongUploadIsSkippedOnDurationAlone()
    {
        Assert.False(LyricsLookupPolicy.ShouldLookUp(Query(minutes: 62), out _));
    }

    [Fact]
    public void AShortClipIsSkipped()
    {
        Assert.False(LyricsLookupPolicy.ShouldLookUp(Query(minutes: 0.2), out _));
    }

    [Fact]
    public void SpokenWordMarkersAreCaughtEvenAtSongLength()
    {
        Assert.False(LyricsLookupPolicy.ShouldLookUp(Query(title: "Episode 12 - podcast", minutes: 9), out _));
        Assert.False(LyricsLookupPolicy.ShouldLookUp(Query(title: "Chapter one audiobook", minutes: 12), out _));
    }

    [Fact]
    public void ALongButRealSongIsStillAllowed()
    {
        Assert.True(LyricsLookupPolicy.ShouldLookUp(Query(title: "Stairway to Heaven", artist: "Led Zeppelin", minutes: 8), out _));
        Assert.True(LyricsLookupPolicy.ShouldLookUp(Query(title: "Echoes", artist: "Pink Floyd", minutes: 14), out _));
    }

    [Fact]
    public void AnUnknownDurationDoesNotBlockTheLookup()
    {
        var noDuration = new LyricsQuery { Title = "Circles", Artist = "Post Malone" };

        Assert.True(LyricsLookupPolicy.ShouldLookUp(noDuration, out _));
    }

    [Fact]
    public void AnUnusableQueryIsRejectedWithAReason()
    {
        var empty = new LyricsQuery { Title = "", Artist = "" };

        Assert.False(LyricsLookupPolicy.ShouldLookUp(empty, out string reason));
        Assert.NotEqual(string.Empty, reason);
    }

    [Fact]
    public void SongTitlesContainingMixOrLiveAreNotBlocked()
    {
        Assert.True(LyricsLookupPolicy.ShouldLookUp(Query(title: "Levels - Extended Mix"), out _));
        Assert.True(LyricsLookupPolicy.ShouldLookUp(Query(title: "Alive"), out _));
    }

    private static readonly DateTime Start = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private static TrackLengthEvidence Length(double seconds, double titleAt, double? previousTitleAt, double? lengthAt)
        => new(
            TimeSpan.FromSeconds(seconds),
            Start.AddSeconds(titleAt),
            previousTitleAt is double previous ? Start.AddSeconds(previous) : default,
            lengthAt is double changed ? Start.AddSeconds(changed) : default);

    private static TrackLengthTrust Judge(TrackLengthEvidence evidence, double nowAt)
        => LyricsLookupPolicy.JudgeTrackLength(evidence, Start.AddSeconds(nowAt));

    [Fact]
    public void A_length_reported_after_the_new_title_belongs_to_it()
    {
        // Apple Music sends the new title first and its length a moment later.
        Assert.Equal(TrackLengthTrust.Trusted, Judge(Length(237, titleAt: 200, previousTitleAt: 0, lengthAt: 200.4), nowAt: 200.5));
    }

    [Fact]
    public void A_length_reported_just_before_the_new_title_belongs_to_it()
    {
        // Other players send the new timeline first, and MediaLink only hears about the title after
        // it has asked Windows for it.
        Assert.Equal(TrackLengthTrust.Trusted, Judge(Length(213, titleAt: 225, previousTitleAt: 0, lengthAt: 224.95), nowAt: 225.1));
    }

    [Fact]
    public void The_previous_tracks_length_is_waited_out_and_then_dropped()
    {
        var evidence = Length(200, titleAt: 200, previousTitleAt: 0, lengthAt: 0.5);

        Assert.Equal(TrackLengthTrust.Waiting, Judge(evidence, nowAt: 200.3));
        Assert.Equal(TrackLengthTrust.Unknown, Judge(evidence, nowAt: 200 + LyricsLookupPolicy.TimelineSettleWindow.TotalSeconds));
    }

    [Fact]
    public void A_skip_right_after_a_length_arrived_does_not_hand_that_length_to_the_next_song()
    {
        // The length landed half a second after the last title and the next title followed straight
        // after, so the length is the skipped song's own.
        Assert.Equal(TrackLengthTrust.Waiting, Judge(Length(200, titleAt: 0.8, previousTitleAt: 0, lengthAt: 0.5), nowAt: 0.9));
    }

    [Fact]
    public void A_player_that_never_reports_a_length_is_searched_without_one_after_the_settle_window()
        => Assert.Equal(TrackLengthTrust.Unknown, Judge(Length(0, titleAt: 10, previousTitleAt: 0, lengthAt: null), nowAt: 13.5));

    [Fact]
    public void A_session_with_no_recorded_title_change_keeps_its_length()
        => Assert.Equal(TrackLengthTrust.Trusted, LyricsLookupPolicy.JudgeTrackLength(new TrackLengthEvidence(TimeSpan.FromSeconds(237), default, default, default), Start));
}
