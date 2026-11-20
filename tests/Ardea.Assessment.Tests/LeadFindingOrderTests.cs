using System.Text.Json;
using Ardea.Assessment.Features;
using Ardea.Assessment.Persistence;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Tests;

public sealed class LeadFindingOrderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Equally_ranked_findings_lead_with_the_lowest_entry_key_whatever_their_ids(bool lowestKeyHasLowestId)
    {
        var low = Guid.Parse("00000000-0000-7000-8000-000000000001");
        var high = Guid.Parse("00000000-0000-7000-8000-000000000002");
        var earlier = Finding(lowestKeyHasLowestId ? low : high);
        var later = Finding(lowestKeyHasLowestId ? high : low);
        var texts = new Dictionary<Guid, EntryText>
        {
            [earlier.EntryId] = Text(earlier.EntryId, "CVE-2026-1001"),
            [later.EntryId] = Text(later.EntryId, "CVE-2026-1002"),
        };

        var answers = AnswerTemplates.For([later, earlier], texts, _ => (new ActionProjectRef(Guid.Empty, "Shop"), new ActionComponentRef(Guid.Empty, "php", "8.4", false)), null);

        Assert.Equal("CVE-2026-1001", answers.WhatHappened.EntryKey);
    }

    private static Finding Finding(Guid id) => new()
    {
        Id = id,
        EntryId = Guid.CreateVersion7(),
        MatchType = "product",
        Verdict = "affected",
        Level = 4,
        Status = "open",
        Evidence = JsonDocument.Parse("{}"),
        Factors = JsonDocument.Parse("{}"),
    };

    private static EntryText Text(Guid id, string key) => new(id, key, key, null, null, [], [], null);
}
