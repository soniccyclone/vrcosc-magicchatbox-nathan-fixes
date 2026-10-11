using vrcosc_magicchatbox.ViewModels.Models;
using Xunit;

namespace MagicChatbox.Tests.ViewModels.Models;

/// <summary>
/// The name shown for each media session on the Integrations page and in the tray is derived from
/// the app id the session reports.
/// </summary>
public class MediaSessionFriendlyNameTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("chrome", "chrome")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Microsoft.ZuneMusic")]
    [InlineData("OperaSoftware.OperaWebBrowser", "Opera")]
    [InlineData("308046B0AF4A39CB", "308046B0AF4A39CB")]
    public void FriendlyAppNameFor_shortens_the_reported_app_id(string id, string expected)
        => Assert.Equal(expected, MediaSessionInfo.FriendlyAppNameFor(id));

    [Theory]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!APP")]
    [InlineData("AppleMusic.exe")]
    public void FriendlyAppNameFor_names_apple_music(string id)
    {
        // The Store app's id ends in "!App", which used to be all that was shown for it.
        Assert.Equal("Apple Music", MediaSessionInfo.FriendlyAppNameFor(id));
    }
}
