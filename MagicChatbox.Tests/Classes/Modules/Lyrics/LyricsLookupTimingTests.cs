using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using vrcosc_magicchatbox.Classes.Modules;
using vrcosc_magicchatbox.Classes.Modules.Lyrics;
using vrcosc_magicchatbox.Classes.Modules.Media;
using vrcosc_magicchatbox.Core.Configuration;
using vrcosc_magicchatbox.Core.Privacy;
using vrcosc_magicchatbox.Services.Lyrics;
using vrcosc_magicchatbox.ViewModels.Models;
using vrcosc_magicchatbox.ViewModels.State;
using Windows.Media.Control;
using Xunit;

namespace MagicChatbox.Tests.Classes.Modules.Lyrics;

/// <summary>
/// When the song changes, a player reports the new title and the new length separately and in
/// either order: Apple Music sends the title first and the length a moment later, other players
/// the other way round. A lookup started on the title change used to search with whatever length
/// was stored, which after Apple Music's title was still the previous song's, so the right lyrics
/// were rejected on duration and the miss was cached. A lookup asked for while another was still
/// running was dropped for good. These drive the real module tick against a session fed the way
/// MediaLink feeds it.
/// </summary>
public class LyricsLookupTimingTests
{
    private static readonly MethodInfo TickMethod =
        typeof(LyricsModule).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly string[] ClockFields = ["_titleChangedTicks", "_previousTitleChangedTicks", "_lengthChangedTicks"];

    private static readonly TimeSpan NoLookupGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(5);

    private sealed class StubSettingsProvider<T>(T value) : ISettingsProvider<T> where T : class, new()
    {
        public T Value { get; } = value;
        public void Save() { }
        public void FlushPendingSave() { }
        public void Reload() { }
        public event EventHandler SettingsChanged { add { } remove { } }
    }

    private sealed class ApprovedConsent : IPrivacyConsentService
    {
        public bool IsApproved(PrivacyHook hook) => true;
        public ConsentState GetState(PrivacyHook hook) => ConsentState.Approved;
        public void Approve(PrivacyHook hook) { }
        public void Deny(PrivacyHook hook) { }
        public void Reset(PrivacyHook hook) { }
        public IReadOnlyList<PrivacyHook> GetHooksRequiringConsent(IEnumerable<PrivacyHook> hooks) => Array.Empty<PrivacyHook>();
        public event EventHandler<ConsentChangedEventArgs> ConsentChanged { add { } remove { } }
    }

    private sealed class RecordingProvider : ILyricsProvider
    {
        private readonly ConcurrentQueue<LyricsQuery> _queries = new();
        private readonly SemaphoreSlim _arrivals = new(0);
        private TaskCompletionSource? _hold;

        public string Name => "Recording";
        public bool RequiresInternet => false;

        public TaskCompletionSource HoldNextLookup()
        {
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _hold, hold);
            return hold;
        }

        public async Task<LyricTrack?> TryGetAsync(LyricsQuery query, CancellationToken ct = default)
        {
            var hold = Interlocked.Exchange(ref _hold, null);
            _queries.Enqueue(query);
            _arrivals.Release();

            if (hold != null)
                await hold.Task.ConfigureAwait(false);

            return null;
        }

        public async Task<LyricsQuery?> NextQueryAsync(TimeSpan timeout)
        {
            if (!await _arrivals.WaitAsync(timeout).ConfigureAwait(false))
                return null;

            _queries.TryDequeue(out var query);
            return query;
        }
    }

    private sealed class Harness
    {
        public required LyricsModule Module { get; init; }
        public required MediaSessionInfo Session { get; init; }
        public required RecordingProvider Provider { get; init; }

        public void Tick() => TickMethod.Invoke(Module, null);

        public void ReportTitle(string title)
            => Session.ApplyReportedMetadata(null, new SessionMetadata(title, "Lady Gaga", string.Empty, "Lady Gaga"), markTimelineStale: true);

        public void ReportLength(double seconds)
        {
            Session.FullTime = TimeSpan.FromSeconds(seconds);
            Session.MarkTimelineFresh();
        }

        // The session stamps title and length changes from the wall clock, so the stamps are moved
        // back instead of the test sleeping while a song plays.
        public void PlayFor(double seconds)
        {
            foreach (string name in ClockFields)
            {
                var field = typeof(MediaSessionInfo).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
                long ticks = (long)field.GetValue(Session)!;
                if (ticks != 0)
                    field.SetValue(Session, ticks - TimeSpan.FromSeconds(seconds).Ticks);
            }
        }

        public async Task<LyricsQuery> SearchAsync()
        {
            Tick();
            var query = await Provider.NextQueryAsync(LookupTimeout);
            Assert.NotNull(query);
            return query!;
        }
    }

    private static Harness Create()
    {
        var mediaLink = new MediaLinkDisplayState();
        var session = new MediaSessionInfo(new MediaLinkSettings(), mediaLink)
        {
            IsActive = true,
            PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
        };
        mediaLink.MediaSessions.Add(session);

        var provider = new RecordingProvider();
        var module = new LyricsModule(
            new StubSettingsProvider<LyricsSettings>(new LyricsSettings()),
            new IntegrationSettings { IntgrLyrics = true, IntgrLyrics_MediaLink = true },
            new LyricsResolver(new ILyricsProvider[] { provider }, () => true),
            mediaLink,
            new SpotifyDisplayState(),
            new LyricsDisplayState(),
            new ApprovedConsent());

        return new Harness { Module = module, Session = session, Provider = provider };
    }

    private static async Task<Harness> PlayingPokerFace(double seconds = 200)
    {
        var harness = Create();
        harness.ReportTitle("Poker Face");
        harness.ReportLength(seconds);
        await harness.SearchAsync();
        return harness;
    }

    [Fact]
    public async Task The_first_song_is_searched_with_its_length_straight_away()
    {
        var harness = Create();
        harness.ReportTitle("Poker Face");
        harness.ReportLength(237);

        var query = await harness.SearchAsync();

        Assert.Equal("Poker Face", query.Title);
        Assert.Equal(TimeSpan.FromSeconds(237), query.Duration);
    }

    [Fact]
    public async Task A_title_reported_before_its_length_waits_for_the_length()
    {
        var harness = await PlayingPokerFace(seconds: 200);
        harness.PlayFor(200);

        harness.ReportTitle("Bad Romance");
        harness.Tick();
        Assert.Null(await harness.Provider.NextQueryAsync(NoLookupGrace));

        harness.ReportLength(295);
        var query = await harness.SearchAsync();

        Assert.Equal("Bad Romance", query.Title);
        Assert.Equal(TimeSpan.FromSeconds(295), query.Duration);
    }

    [Fact]
    public async Task A_length_reported_before_its_title_is_used_straight_away()
    {
        // Players that send the timeline first leave it marked stale behind the title, sometimes
        // for the whole song, so waiting on the stale mark would throw a good length away.
        var harness = await PlayingPokerFace(seconds: 225);
        harness.PlayFor(225);

        harness.ReportLength(213);
        harness.ReportTitle("Bad Romance");

        var query = await harness.SearchAsync();

        Assert.Equal("Bad Romance", query.Title);
        Assert.Equal(TimeSpan.FromSeconds(213), query.Duration);
    }

    [Fact]
    public async Task A_length_that_never_comes_is_left_out_of_the_search()
    {
        var harness = await PlayingPokerFace(seconds: 200);
        harness.PlayFor(200);

        harness.ReportTitle("Bad Romance");
        harness.PlayFor(LyricsLookupPolicy.TimelineSettleWindow.TotalSeconds + 1);

        var query = await harness.SearchAsync();

        Assert.Equal("Bad Romance", query.Title);
        Assert.Equal(TimeSpan.Zero, query.Duration);
    }

    [Fact]
    public async Task A_quick_skip_does_not_hand_the_skipped_songs_length_to_the_next_one()
    {
        var harness = await PlayingPokerFace(seconds: 200);

        harness.ReportTitle("Bad Romance");
        harness.Tick();
        Assert.Null(await harness.Provider.NextQueryAsync(NoLookupGrace));

        harness.ReportLength(295);
        var query = await harness.SearchAsync();

        Assert.Equal(TimeSpan.FromSeconds(295), query.Duration);
    }

    [Fact]
    public async Task A_song_change_during_a_lookup_is_searched_once_that_lookup_ends()
    {
        var harness = Create();
        var firstLookup = harness.Provider.HoldNextLookup();
        harness.ReportTitle("Poker Face");
        harness.ReportLength(237);
        Assert.Equal("Poker Face", (await harness.SearchAsync()).Title);

        harness.ReportTitle("Bad Romance");
        harness.ReportLength(295);
        harness.Tick();

        // This used to be the end of it: the second lookup was refused while the first was in
        // flight and never asked for again.
        firstLookup.SetResult();

        LyricsQuery? second = null;
        for (int attempt = 0; attempt < 50 && second == null; attempt++)
        {
            harness.Tick();
            second = await harness.Provider.NextQueryAsync(TimeSpan.FromMilliseconds(100));
        }

        Assert.NotNull(second);
        Assert.Equal("Bad Romance", second!.Title);
        Assert.Equal(TimeSpan.FromSeconds(295), second.Duration);
    }
}
