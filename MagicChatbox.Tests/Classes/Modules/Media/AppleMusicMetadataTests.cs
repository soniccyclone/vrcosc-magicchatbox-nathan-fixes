using System;
using vrcosc_magicchatbox.Classes.Modules.Lyrics;
using vrcosc_magicchatbox.Classes.Modules.Media;
using vrcosc_magicchatbox.Services.Lyrics;
using Xunit;

namespace MagicChatbox.Tests.Classes.Modules.Media;

/// <summary>
/// Apple Music for Windows copies the line under the song title in its player, "Artist — Album",
/// into both the artist and the album artist of its media session and leaves the album empty.
/// That string reached the chatbox as the artist and went into every LRCLIB request, where the
/// exact lookups can never match it. The payloads below are shaped like ones captured from the
/// app: singles, a soundtrack with a dash in its album, a station and a classical work.
/// </summary>
public class AppleMusicMetadataTests
{
    private const string StoreApp = "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App";

    private static SessionMetadata Reported(string packed, string albumTitle = "", string title = "Poker Face")
        => new(title, packed, albumTitle, packed);

    [Theory]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!APP")]
    [InlineData("appleinc.applemusicwin_nzyj5cx40ttqa!app")]
    [InlineData("AppleMusic.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\AppleInc.AppleMusicWin_1.0.0.0_x64__nzyj5cx40ttqa\AppleMusic.exe")]
    public void IsAppleMusic_recognises_the_store_app_and_the_windows_10_executable(string sourceAppId)
        => Assert.True(AppleMusicMetadata.IsAppleMusic(sourceAppId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Spotify.exe")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic")]
    [InlineData("AppleInc.iTunes_nzyj5cx40ttqa!iTunes")]
    [InlineData("49586DaveAntoine.MediaControllerforiTunes_9bzempp7dntjg!App")]
    [InlineData("music.apple.com-A5F611C_7vh1tm7h3g5s0!App")]
    [InlineData("NotAppleMusic.exe")]
    public void IsAppleMusic_leaves_every_other_player_alone(string? sourceAppId)
        => Assert.False(AppleMusicMetadata.IsAppleMusic(sourceAppId));

    [Theory]
    [InlineData("Lady Gaga — The Fame", "Lady Gaga")]
    [InlineData("Tyler, The Creator — IGOR", "Tyler, The Creator")]
    [InlineData("Kendrick Lamar & SZA — Black Panther The Album Music From And Inspired By", "Kendrick Lamar & SZA")]
    [InlineData("92914 — Summer - Single", "92914")]
    [InlineData("Tom Holkenborg — Rebel Moon — Part One: A Child of Fire (Soundtrack from the Netflix Film)", "Tom Holkenborg")]
    public void Normalize_keeps_only_the_artist_from_the_packed_line(string packed, string expected)
    {
        var metadata = AppleMusicMetadata.Normalize(StoreApp, Reported(packed));

        Assert.Equal(expected, metadata.Artist);
        Assert.Equal(expected, metadata.AlbumArtist);
        Assert.Equal("Poker Face", metadata.Title);
        Assert.Equal(string.Empty, metadata.AlbumTitle);
    }

    [Fact]
    public void Normalize_drops_a_station_name_along_with_the_album()
    {
        // Personal stations carry the listener's name in a third segment, and the chatbox is public.
        var metadata = AppleMusicMetadata.Normalize(StoreApp, Reported("SIAMES — Bounce Into The Music — Station de : Camille"));

        Assert.Equal("SIAMES", metadata.Artist);
    }

    [Fact]
    public void Normalize_works_for_the_windows_10_executable_name_too()
        => Assert.Equal("Lady Gaga", AppleMusicMetadata.Normalize("AppleMusic.exe", Reported("Lady Gaga — The Fame")).Artist);

    [Fact]
    public void Normalize_falls_back_to_the_album_artist_when_the_artist_is_empty()
    {
        // Builds from mid-2024 left the artist empty and only packed the album artist.
        var metadata = AppleMusicMetadata.Normalize(StoreApp, new SessionMetadata("Ava", string.Empty, string.Empty, "Famy — Ava"));

        Assert.Equal("Famy", metadata.Artist);
        Assert.Equal("Famy", metadata.AlbumArtist);
    }

    [Theory]
    [InlineData("Lady Gaga - The Fame")]
    [InlineData("Lady Gaga – The Fame")]
    [InlineData("Lady Gaga—The Fame")]
    [InlineData("Lady Gaga")]
    public void Normalize_only_splits_on_a_spaced_em_dash(string artist)
    {
        // Apple joins with " — ". A hyphen belongs to names and to " - Single" album suffixes, and an
        // en dash has only ever been seen inside album titles, so neither is treated as the join.
        Assert.Equal(artist, AppleMusicMetadata.Normalize(StoreApp, Reported(artist)).Artist);
    }

    [Fact]
    public void Normalize_never_empties_the_artist()
        => Assert.Equal("— The Fame", AppleMusicMetadata.Normalize(StoreApp, Reported(" — The Fame")).Artist);

    [Fact]
    public void Normalize_strips_only_the_reported_album_once_apple_fills_in_the_album_field()
    {
        Assert.Equal("Lady Gaga", AppleMusicMetadata.Normalize(StoreApp, Reported("Lady Gaga — The Fame", albumTitle: "The Fame")).Artist);
        Assert.Equal("Earth — Wind", AppleMusicMetadata.Normalize(StoreApp, Reported("Earth — Wind", albumTitle: "September")).Artist);
    }

    [Theory]
    [InlineData("Spotify.exe")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic")]
    [InlineData("music.apple.com-A5F611C_7vh1tm7h3g5s0!App")]
    public void Normalize_hands_back_other_players_metadata_untouched(string sourceAppId)
    {
        var reported = new SessionMetadata(" Song ", "Band — Live", string.Empty, "Band — Live");

        Assert.Equal(reported, AppleMusicMetadata.Normalize(sourceAppId, reported));
    }

    [Fact]
    public void Normalize_keeps_the_composer_line_of_a_classical_work_as_apple_writes_it()
    {
        // Classical works lead with "By <composer>", then the performers, then the album. Cutting at
        // the first dash keeps the composer. The "By " stays because real artists start with it too
        // ("By Storm", "By The Sea"), so stripping it would break them.
        var metadata = AppleMusicMetadata.Normalize(
            StoreApp,
            Reported(
                "By Wolfgang Amadeus Mozart — Quatuor Ébène & Antoine Tamestit — Mozart: String Quintets, K. 515 & 516",
                title: "String Quintet No. 3 in C Major, K. 515: I. Allegro"));

        Assert.Equal("By Wolfgang Amadeus Mozart", metadata.Artist);
    }

    [Fact]
    public void An_apple_music_track_reaches_lrclib_under_its_real_artist()
    {
        var metadata = AppleMusicMetadata.Normalize(StoreApp, Reported("Lady Gaga — The Fame"));
        var query = TrackQueryNormalizer.Normalize(metadata.Title, metadata.Artist, metadata.AlbumTitle, TimeSpan.FromSeconds(237));

        var steps = LrcLibLyricsProvider.BuildLookupSteps(query);

        Assert.Equal("https://lrclib.net/api/get?track_name=Poker%20Face&artist_name=Lady%20Gaga&duration=237", steps[0].Url);
        Assert.All(steps, step => Assert.DoesNotContain("%E2%80%94", step.Url, StringComparison.Ordinal));
    }
}
