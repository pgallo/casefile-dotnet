using Casefile.Core.Entities;

namespace Casefile.Core.Seed;

/// <summary>
/// Fictional reference data. All names are invented; email domains use the reserved ".example" TLD;
/// UK phone numbers use the 07700 900xxx range set aside for fiction; account numbers are generated
/// with a valid checksum over made-up bank codes.
/// </summary>
public static class SyntheticData
{
    public static readonly string[] People =
    [
        "Aldo Ferrantelli", "Giulia Marchetto", "Lorenzo Vittadello", "Helena Strand", "Tomas Wexley",
        "Irene Calvisi", "Pietro Sanguedolce", "Rhea Talbot", "Dario Montefusco", "Sven Aaltonen",
        "Chiara Benvenuti", "Nikos Paravas", "Mirela Dushku", "Oskar Lindhagen", "Fabrizio Ottonello",
    ];

    public static readonly string[] Organisations =
    [
        "Pellara Trading", "Halvard Metals", "Kestrel Dunmore", "Brisco Marittima", "Orvane Holdings",
        "Società Agricola Fontebella", "Nordquay Freight", "Credito Collinare", "Lumetra Consulting",
        "Vescari Immobiliare",
    ];

    public static readonly string[] Locations =
    [
        "Genova", "Napoli", "Milano", "Rotterdam", "Valletta", "Limassol", "Tirana", "Trieste", "Bari",
        "Lugano", "Antwerp", "Piraeus",
    ];

    public static Gazetteer BuildGazetteer() => new(
        People.Select(p => new GazetteerEntry(EntityType.Person, p, [])) // full names only: surnames alone are too ambiguous
            .Concat(Organisations.Select(o => new GazetteerEntry(EntityType.Organisation, o, OrgAliases(o))))
            .Concat(Locations.Select(l => new GazetteerEntry(EntityType.Location, l, LocationAliases(l)))));

    private static IReadOnlyList<string> OrgAliases(string org) => org switch
    {
        "Società Agricola Fontebella" => ["Fontebella"],
        "Credito Collinare" => ["Banca Credito Collinare"],
        _ => [],
    };

    private static IReadOnlyList<string> LocationAliases(string loc) => loc switch
    {
        "Genova" => ["Genoa"],
        "Napoli" => ["Naples"],
        "Milano" => ["Milan"],
        "Antwerp" => ["Anversa"],
        "Piraeus" => ["Pireo"],
        _ => [],
    };

    public static EntityExtractor BuildExtractor() => new(BuildGazetteer());
}
