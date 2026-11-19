using Ardea.Assessment.Features;
using Ardea.Assessment.Persistence;
using Ardea.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Assessment.Tests;

/// <summary>
/// The three answers from templates, without a language model (task 3.6; master plan 7.5): what happened
/// in the source's words, whether the installed version lies in a stated range, and what to do.
/// </summary>
public sealed class AnswerTests(PostgresServer server)
{
    [Fact]
    public async Task An_action_answers_what_happened_whether_it_concerns_us_and_what_to_do()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var project = await host.AddProjectAsync(acme, "Platform Ops", criticality: "high");
        await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind, c.Version) = ("postgresql", "database", "17.11"));
        await host.ProfileChangedAsync(acme, project);
        await host.RunJobsAsync();
        var description = "A missing authorization check in PostgreSQL lets an authenticated user read another user's large objects. "
            + "Versions before 18.7, 17.12, 16.16 and 15.20 are affected. The PostgreSQL project thanks the reporter for the report and the patch, "
            + "and recommends that every user update without delay.";
        var first = await host.AddEntryAsync(new TestEntry(
            "CVE-2026-116303",
            new TestAffected { ProductKey = "postgresql", Introduced = "18", Fixed = "18.7" },
            new TestAffected { ProductKey = "postgresql", Introduced = "17", Fixed = "17.12" },
            new TestAffected { ProductKey = "postgresql", Introduced = "16", Fixed = "16.16" })
        {
            Score = 8.8,
            Description = description,
        });
        var second = await host.AddEntryAsync(new TestEntry("CVE-2026-116304", new TestAffected { ProductKey = "postgresql", Introduced = "17", Fixed = "17.12" }) { Score = 5.4 });
        await host.EntryChangedAsync(first);
        await host.EntryChangedAsync(second);
        await host.RunJobsAsync();

        var answers = await AnswersAsync(host, acme);
        Assert.Equal("template", answers.Origin);

        // What happened: the highest finding's entry, its description cut at a sentence end, quoted with its source.
        Assert.Equal("CVE-2026-116303", answers.WhatHappened.EntryKey);
        Assert.Equal(
            "A missing authorization check in PostgreSQL lets an authenticated user read another user's large objects. Versions before 18.7, 17.12, 16.16 and 15.20 are affected.",
            answers.WhatHappened.Excerpt);
        Assert.Equal("Test source", answers.WhatHappened.Source);
        Assert.Equal(1, answers.WhatHappened.MoreEntries);

        // Are we affected: 17.11 against the one range it lies in.
        Assert.Equal("affected", answers.Affected.Verdict);
        var item = Assert.Single(answers.Affected.Items);
        Assert.Equal(("Platform Ops", "17.11", "in_range"), (item.Project.Name, item.Component.Version, item.VersionVerdict));
        Assert.Equal([new AffectedRange("17", "17.12", null, null)], item.Ranges);

        // What to do: the fix of that range.
        Assert.Equal(("update", "17.12", "17.11"), (answers.ToDo.Kind, answers.ToDo.TargetVersion, answers.ToDo.InstalledVersion));
    }

    [Fact]
    public async Task Without_a_version_or_a_fix_the_answers_say_so_and_quote_the_sources_guidance()
    {
        await using var host = await AssessmentTestHost.StartAsync(server);
        var acme = AssessmentTestHost.NewOrganization();
        var project = await host.AddProjectAsync(acme, "Shop");
        await host.AddComponentAsync(acme, project, c => (c.ProductKey, c.Kind) = ("nginx", "network"));
        await host.ProfileChangedAsync(acme, project);
        await host.RunJobsAsync();
        var entry = await host.AddEntryAsync(new TestEntry("CVE-2026-6100", new TestAffected { ProductKey = "nginx", Introduced = "1.25.0", LastAffected = "1.27.3" })
        {
            Solutions = ["Disable the mp4 module until a fixed release is available; see the advisory for the configuration line to remove from every server block that uses it."],
        });
        await host.EntryChangedAsync(entry);
        await host.RunJobsAsync();

        var answers = await AnswersAsync(host, acme);
        Assert.Null(answers.WhatHappened.Excerpt);
        Assert.Equal("unknown", answers.Affected.Verdict);
        Assert.Equal("unknown", Assert.Single(answers.Affected.Items).VersionVerdict);
        Assert.Equal("source_guidance", answers.ToDo.Kind);
        Assert.Null(answers.ToDo.TargetVersion);
        var step = Assert.Single(answers.ToDo.Steps);
        Assert.True(step.Length <= AnswerTemplates.StepLength, step);
        Assert.StartsWith("Disable the mp4 module until a fixed release is available;", step, StringComparison.Ordinal);
        Assert.EndsWith("…", step, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Short text.", 240, "Short text.")]
    [InlineData("First sentence ends here. Second sentence is much longer than the limit allows.", 40, "First sentence ends here.")]
    [InlineData("Version 2.4.1 is affected and the fix is 2.4.2. More text follows here.", 50, "Version 2.4.1 is affected and the fix is 2.4.2.")]
    [InlineData("Attackers can, e.g. via crafted input, crash the parser. Then more.", 60, "Attackers can, e.g. via crafted input, crash the parser.")]
    [InlineData("One long sentence without any end that goes on and on beyond the limit", 30, "One long sentence without…")]
    [InlineData("  Spaces\nand\tlines are   folded.  ", 240, "Spaces and lines are folded.")]
    public void Source_texts_are_cut_at_a_sentence_end(string text, int length, string expected)
    {
        var cut = AnswerTemplates.Cut(text, length);
        Assert.Equal(expected, cut);
        Assert.True(cut!.Length <= length);
    }

    private static async Task<AnswersView> AnswersAsync(AssessmentTestHost host, Guid organization)
    {
        AnswersView? answers = null;
        await host.InOrganizationAsync(organization, async services =>
        {
            var db = services.GetRequiredService<AssessDbContext>();
            var action = await db.Remediations.SingleAsync(r => r.Status != "closed");
            var findings = await db.Findings.Where(f => f.RemediationId == action.Id).ToListAsync();
            answers = await services.GetRequiredService<ActionReader>().AnswersAsync(findings, action.TargetVersion, CancellationToken.None);
        });
        return answers!;
    }
}
