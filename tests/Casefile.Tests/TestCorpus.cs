using Casefile.Core;
using Casefile.Core.Domain;
using Casefile.Core.Seed;

namespace Casefile.Tests;

internal static class TestCorpus
{
    public static ManualClock Clock() => new(new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero));

    public static CaseService EmptyService(ManualClock? clock = null) => new(SyntheticData.BuildExtractor(), clock ?? Clock());

    public static Case NewCase(CaseService s, string title, string body, string assignee = "analyst.a", params string[] tags)
    {
        var c = s.Create(new CreateCaseCommand(title, null, Priority.Medium, assignee, tags), assignee);
        return s.AddDocument(c.Id, new AddDocumentCommand("note", body), assignee);
    }
}
