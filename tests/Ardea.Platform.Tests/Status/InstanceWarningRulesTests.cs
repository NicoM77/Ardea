using Ardea.Platform.Persistence.Entities;
using Ardea.Platform.Status;

namespace Ardea.Platform.Tests.Status;

public sealed class InstanceWarningRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly StatusWarning Stalled = new("worker_stalled", "critical", "The worker has stopped: nothing is fetched or assessed.");
    private static readonly StatusWarning Stale = new("source_stale", "warning", "OSV has not been fetched since it was added.", Subject: "osv");

    private readonly List<InstanceWarningRow> _rows = [];

    private WarningMail Round(TimeSpan after, bool claim, params StatusWarning[] current)
    {
        var outcome = InstanceWarningRules.Apply([.. _rows], current, Start + after, claim);
        _rows.RemoveAll(r => outcome.Removed.Contains(r));
        _rows.AddRange(outcome.Added);
        return outcome.Mail;
    }

    private static TimeSpan Minutes(int minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void New_warnings_go_into_one_mail_and_the_next_round_claims_nothing()
    {
        var first = Round(Minutes(0), claim: true, Stalled, Stale);
        var second = Round(Minutes(5), claim: true, Stalled, Stale);

        Assert.Equal(["source_stale:osv", "worker_stalled:"], first.Raised.Select(n => $"{n.Code}:{n.Subject}").Order(StringComparer.Ordinal));
        Assert.Empty(first.Reminded);
        Assert.True(second.IsEmpty);
    }

    [Fact]
    public void A_lasting_warning_is_mailed_again_a_day_after_the_last_mail()
    {
        Round(Minutes(0), claim: true, Stalled);
        Assert.True(Round(TimeSpan.FromHours(24) - Minutes(5), claim: true, Stalled).IsEmpty);

        var reminder = Round(TimeSpan.FromHours(24), claim: true, Stalled);
        Assert.Equal("worker_stalled", Assert.Single(reminder.Reminded).Code);
        Assert.Equal(Start, reminder.Reminded[0].FirstSeenAt);
        Assert.Empty(reminder.Raised);

        Assert.True(Round(TimeSpan.FromHours(47), claim: true, Stalled).IsEmpty);
        Assert.Single(Round(TimeSpan.FromHours(48), claim: true, Stalled).Reminded);
    }

    [Fact]
    public void A_cleared_warning_is_resolved_only_when_the_next_round_still_does_not_see_it()
    {
        Round(Minutes(0), claim: true, Stalled);

        Assert.True(Round(Minutes(5), claim: true).IsEmpty);
        var over = Round(Minutes(10), claim: true);
        Assert.Equal("worker_stalled", Assert.Single(over.Resolved).Code);

        Assert.True(Round(Minutes(15), claim: true).IsEmpty);
        Assert.Empty(_rows);
    }

    [Fact]
    public void A_warning_that_comes_back_before_it_stayed_clear_sends_neither_new_nor_resolved()
    {
        Round(Minutes(0), claim: true, Stalled);

        Assert.True(Round(Minutes(5), claim: true).IsEmpty);
        Assert.True(Round(Minutes(10), claim: true, Stalled).IsEmpty);
        Assert.True(Round(Minutes(15), claim: true).IsEmpty);

        Assert.True(Round(Minutes(16), claim: true).IsEmpty);
        Assert.Single(Round(Minutes(20), claim: true).Resolved);
    }

    [Fact]
    public void A_warning_that_returns_after_its_resolved_mail_is_new_again()
    {
        Round(Minutes(0), claim: true, Stalled);
        Round(Minutes(5), claim: true);
        Round(Minutes(10), claim: true);

        var again = Round(Minutes(15), claim: true, Stalled);

        Assert.Equal(Start + Minutes(15), Assert.Single(again.Raised).FirstSeenAt);
    }

    [Fact]
    public void Without_anyone_to_tell_nothing_is_claimed_and_a_later_round_tells()
    {
        Assert.True(Round(Minutes(0), claim: false, Stalled).IsEmpty);
        Assert.True(Round(Minutes(5), claim: false, Stalled).IsEmpty);

        var told = Round(Minutes(10), claim: true, Stalled);

        Assert.Equal(Start, Assert.Single(told.Raised).FirstSeenAt);
    }

    [Fact]
    public void A_long_message_is_cut_to_the_column_without_splitting_a_surrogate_pair()
    {
        var split = new string('a', 999) + "\U0001F426" + "tail";
        var plain = new string('b', 1200);

        Assert.Equal(new string('a', 999), InstanceWarningRules.Truncate(split));
        Assert.Equal(1000, InstanceWarningRules.Truncate(plain).Length);
        Assert.Equal("short", InstanceWarningRules.Truncate("short"));
    }

    [Fact]
    public void A_warning_nobody_was_told_about_ends_without_a_mail()
    {
        Round(Minutes(0), claim: false, Stalled);
        Round(Minutes(5), claim: true);

        Assert.True(Round(Minutes(10), claim: true).IsEmpty);
        Assert.Empty(_rows);
    }
}
