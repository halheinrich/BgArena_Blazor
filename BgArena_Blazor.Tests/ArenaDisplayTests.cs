using BgArena_Blazor.Components.Shared;
using BgTournament.Api;

namespace BgArena_Blazor.Tests;

/// <summary>
/// Unit tests for the shared presentation rules — the timestamp/duration
/// formats (deterministic absolutes, never wall-clock-relative), the ended
/// line's three shapes, the time-control wording, and the forfeit-cause
/// wording — at their boundaries.
/// </summary>
public class ArenaDisplayTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TimestampText_IsInvariantUtcMinutePrecision()
    {
        // A non-UTC offset normalizes to the UTC instant.
        var zoned = new DateTimeOffset(2026, 7, 5, 14, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal("2026-07-05 12:00", ArenaDisplay.TimestampText(zoned));
    }

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(34, "34s")]
    [InlineData(60, "1m 0s")]
    [InlineData(754, "12m 34s")]
    [InlineData(3600, "1h 0m")]
    [InlineData(3720, "1h 2m")]
    [InlineData(93600, "1d 2h")]
    public void DurationText_UsesTheLargestTwoUnits(int totalSeconds, string expected) =>
        Assert.Equal(expected, ArenaDisplay.DurationText(Start, Start.AddSeconds(totalSeconds)));

    [Fact]
    public void WhenText_ShowsOnlyTheStartUntilThereIsAnEnd()
    {
        Assert.Equal("2026-07-05 12:00", ArenaDisplay.WhenText(Start, endedAtUtc: null));
        Assert.Equal("2026-07-05 12:00 · 30m 0s", ArenaDisplay.WhenText(Start, Start.AddMinutes(30)));
    }

    [Fact]
    public void EndedText_HasThreeShapes_RecordedRunningAndLost()
    {
        Assert.Equal("2026-07-05 12:30 · 30m 0s",
            ArenaDisplay.EndedText(isTerminal: true, Start, Start.AddMinutes(30)));
        Assert.Equal("—", ArenaDisplay.EndedText(isTerminal: false, Start, endedAtUtc: null));
        // Terminal with no end time: only an Interrupted record — honest wording.
        Assert.Equal("unknown — the end time died with the server",
            ArenaDisplay.EndedText(isTerminal: true, Start, endedAtUtc: null));
    }

    [Fact]
    public void TimeControlText_WordsBothRegimes()
    {
        Assert.Equal("flat per-decision timeout", ArenaDisplay.TimeControlText(null));
        Assert.Equal("Fischer 120s + 8s/decision", ArenaDisplay.TimeControlText(new TimeControl(120, 8)));
    }

    [Theory]
    [InlineData(ForfeitCause.ContractViolation, "contract violation", "cause cause-contractviolation")]
    [InlineData(ForfeitCause.Timeout, "timeout", "cause cause-timeout")]
    [InlineData(ForfeitCause.FlagFall, "flag fall", "cause cause-flagfall")]
    [InlineData(ForfeitCause.Disconnect, "disconnect", "cause cause-disconnect")]
    [InlineData(ForfeitCause.NeverConnected, "never connected", "cause cause-neverconnected")]
    public void ForfeitCause_EveryMemberHasWordingAndCss(ForfeitCause cause, string text, string css)
    {
        Assert.Equal(text, ArenaDisplay.ForfeitCauseText(cause));
        Assert.Equal(css, ArenaDisplay.ForfeitCauseCss(cause));
    }

    [Fact]
    public void LengthText_SingularizesTheOnePointMatch()
    {
        Assert.Equal("1 point", ArenaDisplay.LengthText(1, maxGames: null));
        Assert.Equal("3 points", ArenaDisplay.LengthText(3, maxGames: null));
        Assert.Equal("money · 50 games", ArenaDisplay.LengthText(0, maxGames: 50));
    }
}
