using Casefile.Core;
using Casefile.Core.Seed;
using Casefile.Search;
using Casefile.Search.Querying;

// Offline demo over the same seeded corpus the API uses. No server needed.
//   search "<query>" [--limit N]      ranked results with snippets and facets
//   related <CASE-ID>                 cases linked through shared entities
//   stats                             corpus and entity counts

using var service = new CaseService(SyntheticData.BuildExtractor());
Seeder.Seed(service);

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

try
{
    return args[0] switch
    {
        "search" when args.Length >= 2 => Search(args[1], ParseLimit(args)),
        "related" when args.Length >= 2 => Related(args[1]),
        "stats" => Stats(),
        _ => Fail($"Unknown or incomplete command: {string.Join(' ', args)}"),
    };
}
catch (Exception ex) when (ex is QueryParseException or UnknownFieldException or Casefile.Core.Domain.CasefileException)
{
    return Fail(ex.Message);
}

int Search(string query, int limit)
{
    var r = service.Search(new SearchRequest
    {
        Query = query,
        Limit = limit,
        Facets = [CaseIndexMapper.Fields.Status, CaseIndexMapper.Fields.Tag, CaseIndexMapper.Fields.EntityType],
    });
    Console.WriteLine($"Query : {query}");
    Console.WriteLine($"Parsed: {r.ParsedQuery}");
    Console.WriteLine($"Hits  : {r.Total} of {service.Count} cases");
    Console.WriteLine();
    var rank = 1;
    foreach (var h in r.Hits)
    {
        Console.WriteLine($"{rank++,2}. {h.Case.Id}  {h.Score,7:F3}  [{h.Case.Status}]  {h.Case.Title}");
        foreach (var (field, snippet) in h.Snippets)
            Console.WriteLine($"      {field,-8} {snippet}");
    }
    Console.WriteLine();
    foreach (var (field, values) in r.Facets)
        Console.WriteLine($"facet {field,-7}: {string.Join(", ", values.Take(8).Select(v => $"{v.Value} ({v.Count})"))}");
    return 0;
}

int Related(string id)
{
    var c = service.Get(id);
    Console.WriteLine($"{c.Id} [{c.Status}] {c.Title}");
    Console.WriteLine($"Entities: {string.Join(", ", c.Entities.Select(e => $"{e.Type}:{e.Display}"))}");
    Console.WriteLine();
    foreach (var r in service.Related(id))
    {
        var other = service.Get(r.CaseId);
        Console.WriteLine($"{r.Score,7:F3}  {r.CaseId} [{other.Status}] {other.Title}");
        foreach (var s in r.SharedEntities)
            Console.WriteLine($"           shares {s.Type,-12} {s.Display}  (w={s.Weight:F3}, in {s.CaseFrequency} cases)");
    }
    return 0;
}

int Stats()
{
    var all = service.All();
    Console.WriteLine($"Cases: {all.Count}, documents: {all.Sum(c => c.Documents.Count)}");
    foreach (var g in all.GroupBy(c => c.Status).OrderBy(g => g.Key)) Console.WriteLine($"  {g.Key,-9} {g.Count()}");
    Console.WriteLine("Distinct entities by type:");
    foreach (var g in all.SelectMany(c => c.Entities).DistinctBy(e => e.NodeId).GroupBy(e => e.Type).OrderBy(g => g.Key))
        Console.WriteLine($"  {g.Key,-12} {g.Count()}");
    return 0;
}

static int ParseLimit(string[] args)
{
    var i = Array.IndexOf(args, "--limit");
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) ? n : 5;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    PrintUsage();
    return 2;
}

static void PrintUsage() => Console.WriteLine("""
    usage:
      casefile search "<query>" [--limit N]
      casefile related <CASE-ID>
      casefile stats

    query syntax: words, "exact phrases", AND / OR / NOT (uppercase), -term, parentheses,
                  field:value with text fields title, summary, body and
                  keyword fields status, tag, priority, assignee, etype, entity
    """);
