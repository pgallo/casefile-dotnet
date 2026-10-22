using Casefile.Core.Domain;
using Casefile.Search.Indexing;

namespace Casefile.Core;

/// <summary>Maps the domain model onto the search schema. The only place that knows both sides.</summary>
public static class CaseIndexMapper
{
    public static class Fields
    {
        public const string Title = "title";
        public const string Summary = "summary";
        public const string Body = "body";
        public const string Status = "status";
        public const string Tag = "tag";
        public const string Priority = "priority";
        public const string Assignee = "assignee";
        public const string EntityType = "etype";
        public const string Entity = "entity";
    }

    public static IndexSchema Schema { get; } = new(
        new Dictionary<string, double>
        {
            [Fields.Title] = 2.0,
            [Fields.Summary] = 1.2,
            [Fields.Body] = 1.0,
        },
        [Fields.Status, Fields.Tag, Fields.Priority, Fields.Assignee, Fields.EntityType, Fields.Entity]);

    public static IndexDocument ToIndexDocument(Case c)
    {
        var doc = new IndexDocument(c.Id)
            .AddText(Fields.Title, c.Title)
            .AddText(Fields.Summary, c.Summary)
            .AddKeyword(Fields.Status, c.Status.ToString())
            .AddKeyword(Fields.Priority, c.Priority.ToString())
            .AddKeyword(Fields.Assignee, c.Assignee);

        foreach (var d in c.Documents)
        {
            doc.AddText(Fields.Body, d.Title);
            doc.AddText(Fields.Body, d.Body);
        }
        foreach (var t in c.Tags) doc.AddKeyword(Fields.Tag, t);
        foreach (var e in c.Entities)
        {
            doc.AddKeyword(Fields.EntityType, e.Type.ToString());
            doc.AddKeyword(Fields.Entity, e.Key);
        }
        return doc;
    }
}
