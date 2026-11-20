using System.Text.Json;
using Ardea.Assessment.Assessing;
using Ardea.Assessment.Features;
using Ardea.Assessment.Matching;
using Ardea.Assessment.Persistence;
using Ardea.Intel.Contracts;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Tests;

/// <summary>
/// The coarse path of master plan 17 (task 5a.3, ADR 044, ADR 047): components without an exact version raise notices instead
/// of findings and reminders; «affects us» makes a normal reminder; «doesn't affect us» comes back only once.
/// </summary>
public sealed class NoticeTests(PostgresServer server)
{
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    private const string Notices = "SELECT entry_key, state, cardinality(component_ids), is_backfill FROM assess.notice ORDER BY entry_key";

    [Fact]
    public async Task A_coarse_hit_raises_one_notice_per_entry_naming_every_component_and_no_reminder()
    {
        var handedOn = new HandedOn();
        await using var host = await AssessmentTestHost.StartAsync(server, handedOn.Register);
        var acme = AssessmentTestHost.NewOrganization();
        var (stack, postgres) = await StackAsync(host, acme, "postgresql", "database");
        var shop = await host.AddProjectAsync(acme, "Shop");
        await host.AddComponentAsync(acme, shop, c => (c.ProductKey, c.Kind) = ("postgresql", "database"));
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();

        await EntryAsync(host, "CVE-2026-8001", 9.1);
        await EntryAsync(host, "CVE-2026-8002", 7.5);

        Assert.Equal(["CVE-2026-8001\topen\t2\tFalse"], await host.RowsAsync(Notices));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.alert WHERE number IS NOT NULL"));

        // The findings are the notice's: closed at level 1, with the level the rules would give on the precise path.
        Assert.Equal(
            ["CVE-2026-8001\tnot_affected\tmoved_to_notice\t1\tmoved_to_notice\tTrue", "CVE-2026-8001\tnot_affected\tmoved_to_notice\t1\tmoved_to_notice\tTrue",
             "CVE-2026-8002\tnot_affected\tmoved_to_notice\t1\tmoved_to_notice\tTrue", "CVE-2026-8002\tnot_affected\tmoved_to_notice\t1\tmoved_to_notice\tTrue"],
            await host.RowsAsync("""
                SELECT a.entry_key, f.status, f.status_reason, f.level, f.factors->>'exclusion', (f.factors->>'preciseLevel')::int >= 2
                FROM assess.finding f JOIN assess.alert a ON a.id = f.alert_id ORDER BY 1
                """));

        // One event per notice, handed to the notifications in its unit of work (task 5a.6, ADR 048).
        Assert.Equal(["notice.raised\tCVE-2026-8001"], await host.RowsAsync("SELECT kind, data->>'entry' FROM assess.assess_event WHERE kind LIKE 'notice.%'"));
        Assert.Equal(["notice.raised"], handedOn.Kinds.Where(k => k.StartsWith("notice.", StringComparison.Ordinal)));
        Assert.Contains(postgres, await ComponentIdsAsync(host));
        Assert.NotEqual(Guid.Empty, stack);
    }

    [Theory]
    [InlineData("only_important", false, 9.0, "none", false, null, true)]
    [InlineData("only_important", false, 8.9, "none", false, null, false)]
    [InlineData("only_important", false, null, "active", false, null, true)]
    [InlineData("only_important", false, 5.0, "poc", false, null, false)]
    [InlineData("only_important", false, 5.0, "none", true, null, true)]
    [InlineData("only_important", false, 5.0, "none", false, "high", true)]
    [InlineData("only_important", true, 9.8, "none", false, "high", false)]
    [InlineData("only_important", true, 4.3, "active", false, null, true)]
    [InlineData("only_important", true, 4.3, "none", true, null, true)]
    [InlineData("everything", false, null, "none", false, null, true)]
    [InlineData("everything", true, 2.0, "none", false, null, true)]
    public void Only_important_is_critical_exploited_warned_or_likely_and_for_noisy_products_exploited_or_warned(
        string threshold, bool noisy, double? score, string exploitation, bool authority, string? epss, bool meets)
    {
        var entry = new EntryFacts
        {
            Id = Guid.CreateVersion7(),
            Key = "CVE-2026-1",
            Kind = "vulnerability",
            State = "published",
            Revision = 1,
            ChangedAt = DateTimeOffset.UnixEpoch,
            CvssScore = score,
            Exploitation = exploitation,
            AuthorityWarning = authority,
            EpssClass = epss,
            FixAvailable = false,
            Affected = [],
        };
        Assert.Equal(meets, NoticeRules.Meets(threshold, noisy, entry));
    }

    [Fact]
    public async Task The_catalogue_marks_the_browsers_as_noisy_so_a_critical_entry_needs_exploitation()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await StackAsync(host, acme, "chrome", "other");

        var entry = await EntryAsync(host, "CVE-2026-8101", 9.6, productKey: "chrome");
        Assert.Empty(await host.RowsAsync(Notices));

        await ExploitAsync(host, entry, "active");
        Assert.Equal(["CVE-2026-8101\topen\t1\tFalse"], await host.RowsAsync(Notices));
    }

    [Fact]
    public async Task A_line_narrows_only_what_the_entry_rules_out_and_a_weak_hit_raises_nothing()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var stack = await host.AddStackAsync(acme);
        await host.AddComponentAsync(acme, stack, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "17"));
        await host.AddComponentAsync(acme, stack, c => (c.ProductKey, c.Kind) = ("wordpress", "cms"));
        await host.ProfileChangedAsync(acme, stack);
        await host.RunJobsAsync();
        Assert.Equal(["line", "none"], await host.RowsAsync("SELECT version_precision FROM assets.component ORDER BY version_precision"));

        // Only 16 is affected: the line 17 rules it out. 17.0 to 17.6 and an unplaceable bound stay.
        await EntryAsync(host, "CVE-2026-8201", 9.8, introduced: "16.0", fixedAt: "16.11");
        await EntryAsync(host, "CVE-2026-8202", 9.8, introduced: "17.0", fixedAt: "17.7");
        await EntryAsync(host, "CVE-2026-8203", 9.8, introduced: "0", fixedAt: "*");

        // A plugin of the platform is a weak «ecosystem» hit: «WordPress» covers WordPress itself (master plan 17.2).
        await EntryAsync(host, "CVE-2026-8204", 9.8, productKey: "woocommerce");

        Assert.Equal(["CVE-2026-8202\topen\t1\tFalse", "CVE-2026-8203\topen\t1\tFalse"], await host.RowsAsync(Notices));
        Assert.Equal(["ecosystem\tweak_match"], await host.RowsAsync("""
            SELECT f.match_type, f.status_reason FROM assess.finding f JOIN assess.alert a ON a.id = f.alert_id WHERE a.entry_key = 'CVE-2026-8204'
            """));
    }

    [Fact]
    public async Task Doesnt_affect_us_comes_back_once_when_the_entry_is_exploited_and_never_again()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await StackAsync(host, acme, "postgresql", "database");
        var entry = await EntryAsync(host, "CVE-2026-8301", 9.1);
        var notice = await NoticeIdAsync(host);

        Assert.Null((await AnswerAsync(host, acme, notice, new AnswerNoticeRequest("not_affected", "open"))).Refusal);

        // A later change of the entry that is not exploitation leaves the answer alone.
        await host.ScalarAsync<int>($"UPDATE intel.security_entry SET cvss_score = 9.8, exploitation = 'poc', revision = 2 WHERE id = '{entry}' RETURNING 1");
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal(["not_affected\t-"], await host.RowsAsync("SELECT state, coalesce(reappeared_reason, '-') FROM assess.notice"));

        // Exploited now: the notice comes back once, with the earlier answer kept for the card (E22).
        await ExploitAsync(host, entry, "active");
        Assert.Equal(["open\texploited\tTrue"], await host.RowsAsync("SELECT state, reappeared_reason, decided_by IS NOT NULL FROM assess.notice"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'notice.reappeared'"));

        // Answered again, it does not come back: not with KEV, not with any other change.
        Assert.Null((await AnswerAsync(host, acme, notice, new AnswerNoticeRequest("not_affected", "open"))).Refusal);
        await host.ScalarAsync<int>($"UPDATE intel.security_entry SET exploitation = 'none', revision = 4 WHERE id = '{entry}' RETURNING 1");
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        await ExploitAsync(host, entry, "active", revision: 5);
        Assert.Equal(["not_affected"], await host.RowsAsync("SELECT state FROM assess.notice"));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation"));
    }

    [Fact]
    public async Task An_entry_exploited_when_answered_does_not_come_back()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await StackAsync(host, acme, "postgresql", "database");
        var entry = await EntryAsync(host, "CVE-2026-8302", 7.5, exploitation: "active");
        Assert.Null((await AnswerAsync(host, acme, await NoticeIdAsync(host), new AnswerNoticeRequest("not_affected", "open"))).Refusal);

        await ExploitAsync(host, entry, "active", revision: 3);
        Assert.Equal(["not_affected\tactive"], await host.RowsAsync("SELECT state, decided_exploitation FROM assess.notice"));
    }

    [Fact]
    public async Task Affects_us_opens_a_reminder_on_the_stack_entry_and_the_next_answer_joins_it()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var (_, postgres) = await StackAsync(host, acme, "postgresql", "database");
        await EntryAsync(host, "CVE-2026-8401", 9.1);
        await EntryAsync(host, "CVE-2026-8402", 9.8);
        var (first, second) = (await NoticeIdAsync(host, "CVE-2026-8401"), await NoticeIdAsync(host, "CVE-2026-8402"));

        Assert.Null((await AnswerAsync(host, acme, first, new AnswerNoticeRequest("affects_us", "open"))).Refusal);
        Assert.Equal([$"{postgres}\topen\t1"], await host.RowsAsync("SELECT component_id, status, open_findings FROM assess.remediation"));
        Assert.Equal(["affected\thuman\topen\tTrue"], await host.RowsAsync("""
            SELECT f.verdict, f.verdict_source, f.status, f.level >= 2 FROM assess.finding f JOIN assess.alert a ON a.id = f.alert_id WHERE a.entry_key = 'CVE-2026-8401'
            """));

        // The same product in the same place: the second answer joins the open reminder.
        Assert.Null((await AnswerAsync(host, acme, second, new AnswerNoticeRequest("affects_us", "open"))).Refusal);
        Assert.Equal([$"{postgres}\topen\t2"], await host.RowsAsync("SELECT component_id, status, open_findings FROM assess.remediation"));
        Assert.Equal(["affects_us\tTrue", "affects_us\tTrue"], await host.RowsAsync("""
            SELECT n.state, n.remediation_id = r.id FROM assess.notice n, assess.remediation r ORDER BY n.entry_key
            """));
        Assert.Equal(["action.raised", "finding.joined", "finding.joined"], await host.RowsAsync("""
            SELECT kind FROM assess.assess_event WHERE kind IN ('action.raised', 'finding.joined') ORDER BY kind
            """));

        // A reassessment keeps the person's reminder open.
        await host.EntryChangedAsync(await host.ScalarAsync<Guid>("SELECT entry_id FROM assess.notice WHERE entry_key = 'CVE-2026-8401'"));
        await host.RunJobsAsync();
        Assert.Equal([$"{postgres}\topen\t2"], await host.RowsAsync("SELECT component_id, status, open_findings FROM assess.remediation"));
    }

    [Fact]
    public async Task Affects_us_with_a_project_and_a_version_checks_the_entry_precisely()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await StackAsync(host, acme, "postgresql", "database");
        var shop = await host.AddProjectAsync(acme, "Shop");
        var billing = await host.AddProjectAsync(acme, "Billing");
        await host.AddComponentAsync(acme, billing, c => (c.ProductKey, c.Kind) = ("postgresql", "database"));
        await host.ProfileChangedAsync(acme, shop);
        await host.ProfileChangedAsync(acme, billing);
        await host.RunJobsAsync();
        await EntryAsync(host, "CVE-2026-8501", 9.1, introduced: "17.0", fixedAt: "17.7");
        await EntryAsync(host, "CVE-2026-8502", 9.1, introduced: "17.0", fixedAt: "17.7");
        var (first, second) = (await NoticeIdAsync(host, "CVE-2026-8501"), await NoticeIdAsync(host, "CVE-2026-8502"));

        // The shop gets PostgreSQL 17.4, exact: the precise path finds it in range and opens a reminder there.
        var inRange = await AnswerAsync(host, acme, first, new AnswerNoticeRequest("affects_us", "open", shop, "17.4"));
        Assert.Null(inRange.Refusal);
        Assert.Equal(["Shop\t17.4\texact\tin_range\topen\trules"], await host.RowsAsync("""
            SELECT p.name, c.version, c.version_precision, f.version_verdict, f.status, f.verdict_source
            FROM assess.finding f JOIN assets.component c ON c.id = f.component_id JOIN assets.project p ON p.id = c.project_id
            JOIN assess.alert a ON a.id = f.alert_id WHERE a.entry_key = 'CVE-2026-8501' AND c.version IS NOT NULL
            """));
        Assert.NotNull(await host.ScalarAsync<Guid?>($"SELECT remediation_id FROM assess.notice WHERE id = '{first}'"));

        // The billing database, without a version so far, learns 17.9: fixed, so the answer stands but no reminder opens.
        var fixedAlready = await AnswerAsync(host, acme, second, new AnswerNoticeRequest("affects_us", "open", billing, "17.9"));
        Assert.Null(fixedAlready.Refusal);
        Assert.Equal(["affects_us\t-"], await host.RowsAsync($"SELECT state, coalesce(remediation_id::text, '-') FROM assess.notice WHERE id = '{second}'"));
        Assert.Equal(["Billing\t17.9\texact", "Shop\t17.4\texact", "Stack\t-\tnone"], await host.RowsAsync("""
            SELECT p.name, coalesce(c.version, '-'), c.version_precision FROM assets.component c JOIN assets.project p ON p.id = c.project_id ORDER BY p.name
            """));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.remediation WHERE status = 'open'"));

        // A project of another organisation, or one that does not exist, is not found; a notice takes one answer.
        await EntryAsync(host, "CVE-2026-8503", 9.1, introduced: "17.0", fixedAt: "17.7");
        var third = await NoticeIdAsync(host, "CVE-2026-8503");
        Assert.Equal("project_not_found", (await AnswerAsync(host, acme, third, new AnswerNoticeRequest("affects_us", "open", Guid.CreateVersion7()))).Refusal?.Code);
        Assert.Equal("invalid_answer", (await AnswerAsync(host, acme, third, new AnswerNoticeRequest("not_affected", "open", shop))).Refusal?.Code);
        Assert.Equal("open", await host.ScalarAsync<string>($"SELECT state FROM assess.notice WHERE id = '{third}'"));
    }

    [Fact]
    public async Task An_open_notice_is_withdrawn_when_its_component_gets_an_exact_version_and_the_finding_takes_over()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var (stack, postgres) = await StackAsync(host, acme, "postgresql", "database");
        await EntryAsync(host, "CVE-2026-8601", 9.1, introduced: "17.0", fixedAt: "17.7");
        Assert.Equal(["CVE-2026-8601\topen\t1\tFalse"], await host.RowsAsync(Notices));

        await host.ChangeComponentAsync(acme, postgres, c => c.Version = "17.4");
        await host.ProfileChangedAsync(acme, stack);
        await host.RunJobsAsync();

        Assert.Equal(["CVE-2026-8601\twithdrawn\t1\tFalse"], await host.RowsAsync(Notices));
        Assert.Equal(["open\tin_range"], await host.RowsAsync("SELECT status, version_verdict FROM assess.finding"));
        Assert.Equal(["open"], await host.RowsAsync("SELECT status FROM assess.remediation"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'notice.withdrawn'"));

        // Without the version again, the notice is raised anew, as a new one: it takes over the open reminder, which is withdrawn.
        await host.ChangeComponentAsync(acme, postgres, c => c.Version = null);
        await host.ProfileChangedAsync(acme, stack);
        await host.RunJobsAsync();
        Assert.Equal(["CVE-2026-8601\topen\t1\tFalse"], await host.RowsAsync(Notices));
        Assert.Equal(["closed\tmoved_to_notice"], await host.RowsAsync("SELECT status, close_reason FROM assess.remediation"));
    }

    [Fact]
    public async Task Taking_an_answer_back_reopens_the_notice_and_withdraws_the_reminder_it_opened()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await StackAsync(host, acme, "postgresql", "database");
        await EntryAsync(host, "CVE-2026-8701", 9.1);
        var notice = await NoticeIdAsync(host);
        Assert.Null((await AnswerAsync(host, acme, notice, new AnswerNoticeRequest("affects_us", "open"))).Refusal);

        // The answer the person saw is checked under the lock: a second answer to the same notice is refused.
        Assert.Equal("status_conflict", (await AnswerAsync(host, acme, notice, new AnswerNoticeRequest("not_affected", "open"), Bob)).Refusal?.Code);
        Assert.Equal("status_conflict", (await ReopenAsync(host, acme, notice, "not_affected")).Refusal?.Code);

        Assert.Null((await ReopenAsync(host, acme, notice, "affects_us")).Refusal);
        Assert.Equal(["open\t-\t-"], await host.RowsAsync("SELECT state, coalesce(decided_by::text, '-'), coalesce(remediation_id::text, '-') FROM assess.notice"));
        Assert.Equal(["not_affected\tmoved_to_notice\trules"], await host.RowsAsync("SELECT status, status_reason, verdict_source FROM assess.finding"));
        Assert.Equal(["closed\tmoved_to_notice"], await host.RowsAsync("SELECT status, close_reason FROM assess.remediation"));
        Assert.Equal(
            ["notice.raised", "notice.answered", "notice.reopened"],
            await host.RowsAsync("SELECT kind FROM assess.assess_event WHERE kind LIKE 'notice.%' ORDER BY at, id"));
        Assert.Equal(["notice.answered", "notice.reopened"], await host.RowsAsync("SELECT action FROM platform.audit_event WHERE action LIKE 'notice.%' ORDER BY id"));

        // An open notice has nothing to take back.
        Assert.Equal("status_conflict", (await ReopenAsync(host, acme, notice, "affects_us")).Refusal?.Code);
    }

    [Fact]
    public async Task A_stack_entry_added_later_raises_its_notices_quietly_with_one_summary()
    {
        var handedOn = new HandedOn();
        await using var host = await AssessmentTestHost.StartAsync(server, handedOn.Register);
        var acme = AssessmentTestHost.NewOrganization();
        var (stack, _) = await StackAsync(host, acme, "nginx", "network");
        await EntryAsync(host, "CVE-2026-8801", 9.1);
        await EntryAsync(host, "CVE-2026-8802", 9.8);
        await EntryAsync(host, "CVE-2026-8803", 6.1);

        await host.AddComponentAsync(acme, stack, c => (c.ProductKey, c.Kind) = ("postgresql", "database"));
        await host.ProfileChangedAsync(acme, stack);
        await host.RunJobsAsync();

        Assert.Equal(["CVE-2026-8801\topen\t1\tTrue", "CVE-2026-8802\topen\t1\tTrue"], await host.RowsAsync(Notices));
        Assert.Equal(0L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'notice.raised'"));
        Assert.Equal([$"{stack}\t2\t0"], await host.RowsAsync("SELECT project_id, data->>'notices', data->>'important' FROM assess.assess_event WHERE kind = 'notice.backfilled'"));
        // One summary reaches the notifications, not one event per notice (ADR 048).
        Assert.Equal(["notice.backfilled"], handedOn.Kinds.Where(k => k.StartsWith("notice.", StringComparison.Ordinal)));

        // They are open but not new: the list shows them as earlier ones (ADR 047).
        Assert.Empty(await ListKeysAsync(host, acme, "new"));
        Assert.Equal(["CVE-2026-8802", "CVE-2026-8801"], await ListKeysAsync(host, acme, "earlier"));

        // An entry that arrives later is new.
        await EntryAsync(host, "CVE-2026-8804", 9.3);
        Assert.Equal(["CVE-2026-8804"], await ListKeysAsync(host, acme, "new"));
    }

    [Fact]
    public async Task A_reminder_on_a_component_that_becomes_a_line_hands_over_to_a_notice_whatever_the_threshold()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var shop = await host.AddProjectAsync(acme, "Shop");
        var postgres = await host.AddComponentAsync(acme, shop, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "17.4"));
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();
        await EntryAsync(host, "CVE-2026-9001", 7.5, introduced: "17.0", fixedAt: "17.7");
        Assert.Equal(["open"], await host.RowsAsync("SELECT status FROM assess.remediation"));

        // A person confirmed the finding; then the version is only a line, below the threshold of «only important».
        await host.ScalarAsync<int>("UPDATE assess.finding SET verdict = 'affected', verdict_source = 'human' RETURNING 1");
        await host.ChangeComponentAsync(acme, postgres, c => (c.Version, c.VersionPrecision) = ("17", "line"));
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();

        // The reminder goes, but not silently: the notice asks (E25, ADR 047), as a new one although a reverse search raised
        // it, with its event, so the person who had the reminder sees it.
        Assert.Equal(["closed\tmoved_to_notice"], await host.RowsAsync("SELECT status, close_reason FROM assess.remediation"));
        Assert.Equal(["CVE-2026-9001\topen\t1\tFalse"], await host.RowsAsync(Notices));
        Assert.Equal(["CVE-2026-9001"], await ListKeysAsync(host, acme, "new"));
        Assert.Equal(["notice.raised"], await host.RowsAsync("SELECT kind FROM assess.assess_event WHERE kind LIKE 'notice.%'"));

        // Reassessed later, it stays new and raises nothing more.
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();
        Assert.Equal(["CVE-2026-9001\topen\t1\tFalse"], await host.RowsAsync(Notices));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind LIKE 'notice.%'"));
    }

    [Fact]
    public async Task An_earlier_notice_that_takes_over_a_reminder_becomes_new()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var (stack, _) = await StackAsync(host, acme, "nginx", "network");
        var shop = await host.AddProjectAsync(acme, "Shop");
        var postgres = await host.AddComponentAsync(acme, shop, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "17.4"));
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();
        await EntryAsync(host, "CVE-2026-9011", 9.1, introduced: "17.0", fixedAt: "17.7");
        Assert.Equal(["open"], await host.RowsAsync("SELECT status FROM assess.remediation"));

        // A stack entry added later raises the notice quietly, as an earlier one; the shop's reminder still stands.
        await host.AddComponentAsync(acme, stack, c => (c.ProductKey, c.Kind) = ("postgresql", "database"));
        await host.ProfileChangedAsync(acme, stack);
        await host.RunJobsAsync();
        Assert.Equal(["CVE-2026-9011"], await ListKeysAsync(host, acme, "earlier"));

        // The shop's version becomes a line: its reminder hands over to that notice, which becomes new.
        await host.ChangeComponentAsync(acme, postgres, c => (c.Version, c.VersionPrecision) = ("17", "line"));
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();
        Assert.Equal(["closed\tmoved_to_notice"], await host.RowsAsync("SELECT status, close_reason FROM assess.remediation"));
        Assert.Equal(["CVE-2026-9011"], await ListKeysAsync(host, acme, "new"));
        Assert.Empty(await ListKeysAsync(host, acme, "earlier"));
        Assert.Equal(["notice.raised"], await host.RowsAsync("SELECT kind FROM assess.assess_event WHERE kind = 'notice.raised'"));
    }

    [Fact]
    public async Task A_notice_raised_anew_names_what_raises_it_now_and_the_answer_lands_there()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var (stack, entryComponent) = await StackAsync(host, acme, "postgresql", "database");
        await EntryAsync(host, "CVE-2026-9101", 9.1, introduced: "17.0", fixedAt: "17.7");
        await host.ChangeComponentAsync(acme, entryComponent, c => c.Version = "16.2");
        await host.ProfileChangedAsync(acme, stack);
        await host.RunJobsAsync();
        Assert.Equal(["withdrawn"], await host.RowsAsync("SELECT state FROM assess.notice"));

        var shop = await host.AddProjectAsync(acme, "Shop");
        var shopPostgres = await host.AddComponentAsync(acme, shop, c => (c.ProductKey, c.Kind) = ("postgresql", "database"));
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();
        Assert.Equal([$"open\t{shopPostgres}"], await host.RowsAsync("SELECT state, array_to_string(component_ids, ',') FROM assess.notice"));

        Assert.Null((await AnswerAsync(host, acme, await NoticeIdAsync(host), new AnswerNoticeRequest("affects_us", "open"))).Refusal);
        Assert.Equal([$"{shopPostgres}\topen"], await host.RowsAsync("SELECT component_id, status FROM assess.remediation"));
    }

    [Fact]
    public async Task A_version_for_a_component_a_repository_reads_is_refused_and_nothing_is_added()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var shop = await host.AddProjectAsync(acme, "Shop");
        await host.AddComponentAsync(acme, shop, c => (c.ProductKey, c.Kind, c.Version, c.Origin) = ("postgresql", "database", "17", "repository"));
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();
        await EntryAsync(host, "CVE-2026-9201", 9.1, introduced: "17.0", fixedAt: "17.7");
        var notice = await NoticeIdAsync(host);

        var refused = await AnswerAsync(host, acme, notice, new AnswerNoticeRequest("affects_us", "open", shop, "17.4"));
        Assert.Equal("invalid_answer", refused.Refusal?.Code);
        Assert.Contains("version", refused.Refusal!.Errors!.Keys);
        Assert.Equal(["repository\t17\tline"], await host.RowsAsync("SELECT origin, version, version_precision FROM assets.component"));
        Assert.Equal(["open"], await host.RowsAsync("SELECT state FROM assess.notice"));

        // Without a version the answer opens the reminder on the repository's component.
        Assert.Null((await AnswerAsync(host, acme, notice, new AnswerNoticeRequest("affects_us", "open", shop))).Refusal);
        Assert.Equal(["open\t1"], await host.RowsAsync("SELECT status, open_findings FROM assess.remediation"));
    }

    [Fact]
    public async Task A_version_without_a_project_needs_one_when_the_notice_names_several_projects()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        foreach (var name in new[] { "Shop", "Billing" })
        {
            var project = await host.AddProjectAsync(acme, name);
            await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind) = ("postgresql", "database"));
            await host.ProfileChangedAsync(acme, project);
        }

        await host.RunJobsAsync();
        await EntryAsync(host, "CVE-2026-9301", 9.1, introduced: "17.0", fixedAt: "17.7");

        var refused = await AnswerAsync(host, acme, await NoticeIdAsync(host), new AnswerNoticeRequest("affects_us", "open", null, "17.4"));
        Assert.Equal("invalid_answer", refused.Refusal?.Code);
        Assert.Contains("projectId", refused.Refusal!.Errors!.Keys);
        Assert.Equal(["-", "-"], await host.RowsAsync("SELECT coalesce(version, '-') FROM assets.component"));
    }

    [Fact]
    public async Task Doesnt_affect_us_comes_back_once_when_the_entry_is_added_to_KEV_after_an_answer_while_exploited()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await StackAsync(host, acme, "postgresql", "database");
        var entry = await EntryAsync(host, "CVE-2026-9401", 7.5, exploitation: "active");
        Assert.Null((await AnswerAsync(host, acme, await NoticeIdAsync(host), new AnswerNoticeRequest("not_affected", "open"))).Refusal);
        Assert.Equal(["active\tFalse"], await host.RowsAsync("SELECT decided_exploitation, decided_kev FROM assess.notice"));

        // CISA lists it now: the plan names KEV on its own (master plan 17.3), so it comes back once.
        await host.ScalarAsync<int>($$"""
            UPDATE intel.security_entry SET revision = 2,
                exploitation_evidence = '[{"source": "cisa-kev", "state": "active", "evidence": "https://www.cisa.gov/known-exploited-vulnerabilities-catalog", "since": null}]'
            WHERE id = '{{entry}}' RETURNING 1
            """);
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        Assert.Equal(["open\tkev"], await host.RowsAsync("SELECT state, reappeared_reason FROM assess.notice"));

        // Answered again with KEV known, it stays.
        Assert.Null((await AnswerAsync(host, acme, await NoticeIdAsync(host), new AnswerNoticeRequest("not_affected", "open"))).Refusal);
        await ExploitAsync(host, entry, "active", revision: 3);
        Assert.Equal(["not_affected\tTrue"], await host.RowsAsync("SELECT state, decided_kev FROM assess.notice"));
    }

    [Fact]
    public async Task Taking_back_a_version_answer_withdraws_the_notice_its_component_left()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await StackAsync(host, acme, "postgresql", "database");
        await EntryAsync(host, "CVE-2026-9501", 9.1, introduced: "17.0", fixedAt: "17.7");
        var notice = await NoticeIdAsync(host);

        // The stack entry learns 17.4 and takes the precise path; the reminder is the precise one.
        Assert.Null((await AnswerAsync(host, acme, notice, new AnswerNoticeRequest("affects_us", "open", null, "17.4"))).Refusal);
        Assert.Equal(["17.4\texact"], await host.RowsAsync("SELECT version, version_precision FROM assets.component"));
        Assert.Equal(["open"], await host.RowsAsync("SELECT status FROM assess.remediation"));

        Assert.Null((await ReopenAsync(host, acme, notice, "affects_us")).Refusal);
        Assert.Equal(["withdrawn"], await host.RowsAsync("SELECT state FROM assess.notice"));
        Assert.Equal(["open"], await host.RowsAsync("SELECT status FROM assess.remediation"));
    }

    [Fact]
    public async Task A_notice_is_withdrawn_when_its_project_is_archived()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var shop = await host.AddProjectAsync(acme, "Shop");
        await host.AddComponentAsync(acme, shop, c => (c.ProductKey, c.Kind) = ("postgresql", "database"));
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();
        await EntryAsync(host, "CVE-2026-9601", 9.1);

        await host.InOrganizationAsync(acme, async services =>
        {
            var db = services.GetRequiredService<Assets.Persistence.AssetsDbContext>();
            db.Projects.Single(p => p.Id == shop).ArchivedAt = host.Clock.GetUtcNow();
            await db.SaveChangesAsync();
        });
        await host.ProfileChangedAsync(acme, shop);
        await host.RunJobsAsync();

        Assert.Equal(["CVE-2026-9601\twithdrawn\t1\tFalse"], await host.RowsAsync(Notices));
    }

    [Fact]
    public async Task Two_answers_at_once_one_is_taken_and_the_other_sees_the_conflict()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        await StackAsync(host, acme, "postgresql", "database");
        await EntryAsync(host, "CVE-2026-9701", 9.1);
        var notice = await NoticeIdAsync(host);

        var results = await Task.WhenAll(
            AnswerAsync(host, acme, notice, new AnswerNoticeRequest("affects_us", "open"), Alice),
            AnswerAsync(host, acme, notice, new AnswerNoticeRequest("not_affected", "open"), Bob));

        Assert.Single(results, r => r.Refusal is null);
        Assert.Single(results, r => r.Refusal?.Code == "status_conflict");
        Assert.Single(await host.RowsAsync("SELECT state FROM assess.notice WHERE state <> 'open'"));
        Assert.Equal(1L, await host.ScalarAsync<long>("SELECT count(*) FROM assess.assess_event WHERE kind = 'notice.answered'"));
    }

    [Fact]
    public async Task The_list_puts_exploited_first_then_the_score_then_the_newest_and_pages_through()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var (_, postgres) = await StackAsync(host, acme, "postgresql", "database");
        await host.ChangeComponentAsync(acme, postgres, c => c.NoticeThreshold = "everything");
        await EntryAsync(host, "CVE-2026-8901", 9.1);
        await EntryAsync(host, "CVE-2026-8902", 9.8);
        await EntryAsync(host, "CVE-2026-8903", 5.0, exploitation: "active");
        await EntryAsync(host, "CVE-2026-8904", null);
        await EntryAsync(host, "CVE-2026-8905", 9.1);

        var keys = new List<string>();
        string? cursor = null;
        do
        {
            await host.InOrganizationAsync(acme, async services =>
            {
                var db = services.GetRequiredService<AssessDbContext>();
                var reader = services.GetRequiredService<NoticeReader>();
                var result = await NoticeEndpoints.ListAsync(db, reader, null, postgres, 2, cursor, CancellationToken.None);
                var page = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<ListPage<NoticeView>>>(result).Value!;
                keys.AddRange(page.Items.Select(n => n.Entry.Key));
                Assert.All(page.Items, n => Assert.Equal("PostgreSQL", Assert.Single(n.Components).Name));
                cursor = page.NextCursor;
            });
        }
        while (cursor is not null);

        Assert.Equal(["CVE-2026-8903", "CVE-2026-8902", "CVE-2026-8905", "CVE-2026-8901", "CVE-2026-8904"], keys);
    }

    [Theory]
    [InlineData("""{"introduced":"17.0","fixed":"17.7","status":"affected"}""", "17", true)]
    [InlineData("""{"introduced":"16","fixed":"16.11","status":"affected"}""", "17", false)]
    [InlineData("""{"introduced":"0","fixed":"16.11","status":"affected"}""", "17", false)]
    [InlineData("""{"introduced":"8.3.0","fixed":"8.3.27","status":"affected"}""", "8.3", true)]
    [InlineData("""{"introduced":"8.3.0","fixed":"8.3.27","status":"affected"}""", "8.4", false)]
    [InlineData("""{"introduced":"0","fixed":"17.0","status":"affected"}""", "17", false)]
    [InlineData("""{"lastAffected":"16.4","status":"affected"}""", "17", false)]
    [InlineData("""{"introduced":"18.0","status":"affected"}""", "17", false)]
    [InlineData("""{"introduced":"15.0","status":"affected"}""", "17", true)]
    [InlineData("""{"introduced":"0","fixed":"*","status":"affected"}""", "17", true)]
    [InlineData("""{"exact":"17","status":"affected"}""", "17", true)]
    [InlineData("""{"exact":"9.6","status":"affected"}""", "17", false)]
    [InlineData("""{"exact":"0","status":"affected"}""", "17", null)]
    [InlineData("""{"introduced":"8.0","fixed":"8.1","status":"affected"}""", "8.x", true)]
    public void A_range_touches_a_line_when_its_bounds_overlap_it(string range, string line, bool? touches) =>
        Assert.Equal(touches, LineNarrowing.Touches(Range(range), Ardea.Catalog.Contracts.VersionLines.LeadingNumbers(line)));

    [Fact]
    public void An_entry_stays_unless_every_affected_range_lies_outside_the_line()
    {
        static bool Keeps(params string[] ranges) =>
            LineNarrowing.Keeps([new VersionStatement(Guid.Empty, null, "unknown", "no_installed_version", "unknown", [.. ranges.Select(Range)])], [17]);

        Assert.False(Keeps("""{"introduced":"16","fixed":"16.11","status":"affected"}""", """{"introduced":"15","fixed":"15.15","status":"affected"}"""));
        Assert.True(Keeps("""{"introduced":"16","fixed":"16.11","status":"affected"}""", """{"introduced":"17","fixed":"17.7","status":"affected"}"""));
        Assert.True(Keeps("""{"introduced":"16","fixed":"16.11","status":"affected"}""", """{"exact":"n/a","status":"affected"}"""));
        Assert.True(Keeps("""{"introduced":"17.7","status":"unaffected"}"""));
        Assert.True(Keeps());
    }

    private static RangeView Range(string json) => JsonSerializer.Deserialize<RangeView>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>The organisation's stack with one entry without a version, its first fill done.</summary>
    private static async Task<(Guid Stack, Guid Component)> StackAsync(AssessmentTestHost host, Guid organization, string product, string kind)
    {
        var stack = await host.AddStackAsync(organization);
        var component = await host.AddComponentAsync(organization, stack, c => (c.ProductKey, c.Kind) = (product, kind));
        await host.ProfileChangedAsync(organization, stack);
        await host.RunJobsAsync();
        return (stack, component);
    }

    private static async Task<Guid> EntryAsync(
        AssessmentTestHost host, string key, double? score, string productKey = "postgresql", string? introduced = null, string? fixedAt = "17.7", string exploitation = "none")
    {
        var entry = await host.AddEntryAsync(new TestEntry(key, new TestAffected { ProductKey = productKey, Introduced = introduced, Fixed = fixedAt })
        {
            Score = score,
            Exploitation = exploitation,
        });
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
        return entry;
    }

    private static async Task ExploitAsync(AssessmentTestHost host, Guid entry, string exploitation, int revision = 3)
    {
        await host.ScalarAsync<int>($"UPDATE intel.security_entry SET exploitation = '{exploitation}', revision = {revision} WHERE id = '{entry}' RETURNING 1");
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();
    }

    private static async Task<Guid> NoticeIdAsync(AssessmentTestHost host, string? key = null) =>
        await host.ScalarAsync<Guid>(key is null ? "SELECT id FROM assess.notice" : $"SELECT id FROM assess.notice WHERE entry_key = '{key}'");

    private static async Task<IReadOnlyList<string>> ListKeysAsync(AssessmentTestHost host, Guid organization, string state)
    {
        var keys = new List<string>();
        await host.InOrganizationAsync(organization, async services =>
        {
            var result = await NoticeEndpoints.ListAsync(
                services.GetRequiredService<AssessDbContext>(), services.GetRequiredService<NoticeReader>(), state, null, null, null, CancellationToken.None);
            keys.AddRange(Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<ListPage<NoticeView>>>(result).Value!.Items.Select(n => n.Entry.Key));
        });
        return keys;
    }

    private static async Task<IReadOnlyList<Guid>> ComponentIdsAsync(AssessmentTestHost host) =>
        [.. (await host.RowsAsync("SELECT unnest(component_ids) FROM assess.notice")).Select(Guid.Parse)];

    private static async Task<CommandResult<Notice>> AnswerAsync(AssessmentTestHost host, Guid organization, Guid notice, AnswerNoticeRequest request, Guid? user = null)
    {
        CommandResult<Notice> result = default;
        await host.InOrganizationAsync(organization, async services =>
            result = await services.GetRequiredService<NoticeCommands>().AnswerAsync(notice, request, user ?? Alice, CancellationToken.None));
        return result;
    }

    private static async Task<CommandResult<Notice>> ReopenAsync(AssessmentTestHost host, Guid organization, Guid notice, string expected)
    {
        CommandResult<Notice> result = default;
        await host.InOrganizationAsync(organization, async services =>
            result = await services.GetRequiredService<NoticeCommands>().ReopenAsync(notice, new ReopenNoticeRequest(expected), Bob, CancellationToken.None));
        return result;
    }

    /// <summary>Stands in for the notifications: records the events the history hands on before a unit of work commits.</summary>
    private sealed class HandedOn
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _kinds = new();

        public IReadOnlyList<string> Kinds => [.. _kinds];

        public void Register(IServiceCollection services) => services.AddScoped<Contracts.IActionEventSubscriber>(_ => new Subscriber(_kinds));

        private sealed class Subscriber(System.Collections.Concurrent.ConcurrentQueue<string> kinds) : Contracts.IActionEventSubscriber
        {
            public Task OnActionEventsAsync(IReadOnlyList<Contracts.NotifiableEvent> events, CancellationToken cancellationToken)
            {
                foreach (var e in events)
                {
                    kinds.Enqueue(e.Kind);
                }

                return Task.CompletedTask;
            }
        }
    }
}
