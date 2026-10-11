using System;
using vrcosc_magicchatbox.Classes.Modules.Media;

namespace vrcosc_magicchatbox.Classes.Modules.Lyrics;

public enum TrackLengthTrust
{
    Trusted,

    Waiting,

    Unknown,
}

public static class LyricsLookupPolicy
{
    public static readonly TimeSpan MaxTrackLength = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MinTrackLength = TimeSpan.FromSeconds(25);
    public static readonly TimeSpan TimelineSettleWindow = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan TimelineLeadWindow = TimeSpan.FromSeconds(1);

    private static readonly string[] NotMusicMarkers =
    {
        "podcast",
        "audiobook",
        "audio book",
        "livestream",
        "live stream",
        "full album",
        "full movie",
        "full episode",
        "full match",
        "gameplay",
        "walkthrough",
        "tutorial",
        "interview",
        "documentary",
    };

    public static bool ShouldLookUp(LyricsQuery query, out string reason)
    {
        reason = string.Empty;

        if (query == null || !query.IsUsable)
        {
            reason = "Not enough track information to search";
            return false;
        }

        if (query.Duration > TimeSpan.Zero)
        {
            if (query.Duration > MaxTrackLength)
            {
                reason = $"Too long to be a song ({query.Duration.TotalMinutes:F0} min), not searching";
                return false;
            }

            if (query.Duration < MinTrackLength)
            {
                reason = "Too short to be a song, not searching";
                return false;
            }
        }

        string haystack = $"{query.Title} {query.Album}".ToLowerInvariant();

        foreach (string marker in NotMusicMarkers)
        {
            if (haystack.Contains(marker, StringComparison.Ordinal))
            {
                reason = "This looks like spoken word rather than music, so not searching";
                return false;
            }
        }

        return true;
    }

    public static TrackLengthTrust JudgeTrackLength(in TrackLengthEvidence evidence, DateTime nowUtc)
    {
        if (evidence.TitleChangedUtc == default)
            return TrackLengthTrust.Trusted;

        if (evidence.LengthChangedUtc > evidence.TitleChangedUtc)
            return TrackLengthTrust.Trusted;

        bool arrivedJustBeforeTitle = evidence.LengthChangedUtc != default
            && evidence.TitleChangedUtc - evidence.LengthChangedUtc <= TimelineLeadWindow;
        bool belongsToPreviousTitle = evidence.PreviousTitleChangedUtc != default
            && evidence.LengthChangedUtc - evidence.PreviousTitleChangedUtc < TimelineSettleWindow;

        if (arrivedJustBeforeTitle && !belongsToPreviousTitle)
            return TrackLengthTrust.Trusted;

        return nowUtc - evidence.TitleChangedUtc < TimelineSettleWindow
            ? TrackLengthTrust.Waiting
            : TrackLengthTrust.Unknown;
    }
}
