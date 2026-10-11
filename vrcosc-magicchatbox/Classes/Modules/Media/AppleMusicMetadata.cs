using System;
using System.IO;

namespace vrcosc_magicchatbox.Classes.Modules.Media;

public readonly record struct SessionMetadata(string Title, string Artist, string AlbumTitle, string AlbumArtist);

public static class AppleMusicMetadata
{
    private const string StorePackagePrefix = "AppleInc.AppleMusicWin";
    private const string DesktopExecutable = "AppleMusic.exe";
    private const string ArtistAlbumSeparator = " — ";

    public static bool IsAppleMusic(string? sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId))
            return false;

        return sourceAppId.StartsWith(StorePackagePrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(sourceAppId), DesktopExecutable, StringComparison.OrdinalIgnoreCase);
    }

    public static SessionMetadata Normalize(string? sourceAppId, SessionMetadata reported)
    {
        if (!IsAppleMusic(sourceAppId))
            return reported;

        string artist = string.IsNullOrWhiteSpace(reported.Artist) ? reported.AlbumArtist : reported.Artist;

        return reported with
        {
            Artist = StripAlbum(artist, reported.AlbumTitle),
            AlbumArtist = StripAlbum(reported.AlbumArtist, reported.AlbumTitle),
        };
    }

    private static string StripAlbum(string? packed, string? albumTitle)
    {
        if (string.IsNullOrWhiteSpace(packed))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(albumTitle))
        {
            string trimmed = packed.Trim();
            string albumSuffix = ArtistAlbumSeparator + albumTitle.Trim();
            if (!trimmed.EndsWith(albumSuffix, StringComparison.Ordinal))
                return trimmed;

            string withoutAlbum = trimmed[..^albumSuffix.Length].Trim();
            return withoutAlbum.Length > 0 ? withoutAlbum : trimmed;
        }

        int separator = packed.IndexOf(ArtistAlbumSeparator, StringComparison.Ordinal);
        if (separator < 0)
            return packed.Trim();

        string lead = packed[..separator].Trim();
        return lead.Length > 0 ? lead : packed.Trim();
    }
}
