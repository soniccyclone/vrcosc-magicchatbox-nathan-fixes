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
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "App")]
    [InlineData("AppleMusic.exe", "AppleMusic")]
    public void FriendlyAppNameFor_shortens_the_reported_app_id(string id, string expected)
        => Assert.Equal(expected, MediaSessionInfo.FriendlyAppNameFor(id));
}
