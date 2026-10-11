using vrcosc_magicchatbox.Classes.Modules;
using vrcosc_magicchatbox.Classes.Modules.Media;
using vrcosc_magicchatbox.ViewModels.Models;
using vrcosc_magicchatbox.ViewModels.State;
using Xunit;

namespace MagicChatbox.Tests.ViewModels.Models;

/// <summary>
/// MediaLink hands every metadata report from Windows to the session, which cleans it, decides
/// whether the track changed and stores it. Players re-send the same report, and the session
/// resync re-reads it every couple of seconds, so a repeat must never look like a new track:
/// that would restart the stale timeline, hide the seekbar and announce the song again.
/// </summary>
public class MediaSessionMetadataTests
{
    private const string AppleMusic = "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App";

    private static MediaSessionInfo NewSession() => new(new MediaLinkSettings(), new MediaLinkDisplayState());

    private static SessionMetadata Packed(string title, string packed) => new(title, packed, string.Empty, packed);

    [Fact]
    public void An_apple_music_report_is_stored_with_the_album_split_off()
    {
        var session = NewSession();

        Assert.True(session.ApplyReportedMetadata(AppleMusic, Packed("Poker Face", "Lady Gaga — The Fame"), markTimelineStale: true));

        Assert.Equal("Poker Face", session.Title);
        Assert.Equal("Lady Gaga", session.Artist);
        Assert.Equal("Lady Gaga", session.AlbumArtist);
        Assert.Equal(string.Empty, session.AlbumTitle);
    }

    [Fact]
    public void The_same_apple_music_report_again_is_not_a_new_track()
    {
        // The stored artist is the cleaned one, so the comparison has to be against the cleaned
        // report. Against the raw "Lady Gaga — The Fame" every repeat would count as a change.
        var session = NewSession();
        session.ApplyReportedMetadata(AppleMusic, Packed("Poker Face", "Lady Gaga — The Fame"), markTimelineStale: true);
        session.MarkTimelineFresh();

        Assert.False(session.ApplyReportedMetadata(AppleMusic, Packed("Poker Face", "Lady Gaga — The Fame"), markTimelineStale: true));
        Assert.False(session.IsTimelineStale);
    }

    [Fact]
    public void A_new_track_marks_the_timeline_stale_only_when_asked_to()
    {
        var session = NewSession();

        Assert.True(session.ApplyReportedMetadata(AppleMusic, Packed("Poker Face", "Lady Gaga — The Fame"), markTimelineStale: false));
        Assert.False(session.IsTimelineStale);

        Assert.True(session.ApplyReportedMetadata(AppleMusic, Packed("Bad Romance", "Lady Gaga — The Fame Monster"), markTimelineStale: true));
        Assert.True(session.IsTimelineStale);
    }

    [Fact]
    public void Other_players_are_stored_exactly_as_reported()
    {
        var session = NewSession();

        session.ApplyReportedMetadata("Spotify.exe", new SessionMetadata("Song", "Band — Live", "Album", "Band"), markTimelineStale: true);

        Assert.Equal("Band — Live", session.Artist);
        Assert.Equal("Band", session.AlbumArtist);
        Assert.Equal("Album", session.AlbumTitle);
    }
}
