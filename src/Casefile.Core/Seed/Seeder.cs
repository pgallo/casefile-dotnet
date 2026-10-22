using Casefile.Core.Domain;
using Casefile.Core.Entities;

namespace Casefile.Core.Seed;

/// <summary>
/// Generates about 40 synthetic cases organised in scenario clusters. Cases in the same cluster share
/// some identifiers (an account, a phone, a person), drawn at random from the cluster's pool, so the
/// related-cases ranking has graded structure to recover. Everything runs through <see cref="CaseService"/>,
/// so seeded cases carry a genuine audit trail. Fixed random seed: same data on every start.
/// </summary>
public static class Seeder
{
    private sealed record Cluster(
        string Name,
        string[] Tags,
        string[] Titles,
        string[] Openers,
        string[] People,
        string[] Orgs,
        string[] Places,
        string[] Ibans,
        string[] Phones,
        string[] Emails);

    private static readonly string[] Analysts = ["analyst.moretti", "analyst.lindqvist", "analyst.serra", "analyst.okafor"];
    private const string Supervisor = "lead.conti";

    public static void Seed(CaseService service, int seed = 42)
    {
        var rng = new Random(seed);
        var n = 0;
        foreach (var cluster in Clusters())
        {
            var size = cluster.Name == "misc" ? 6 : 5;
            for (var i = 0; i < size; i++, n++)
                SeedCase(service, cluster, i, n, rng);
        }
    }

    private static void SeedCase(CaseService service, Cluster k, int i, int n, Random rng)
    {
        var assignee = Analysts[n % Analysts.Length];
        var person = Pick(rng, k.People);
        var org = Pick(rng, k.Orgs);
        var place = Pick(rng, k.Places);
        var title = string.Format(k.Titles[i % k.Titles.Length], org, place, person);
        var summary = $"{string.Format(Pick(rng, k.Openers), org, place, person)} Opened after an automated alert on {Pick(rng, Triggers)}.";
        var priority = (Priority)rng.Next(0, 4);
        var tags = k.Tags.OrderBy(_ => rng.Next()).Take(rng.Next(1, k.Tags.Length + 1)).ToList();

        var c = service.Create(new CreateCaseCommand(title, summary, priority, assignee, tags), assignee);

        // Note 1: who and where.
        var lines = new List<string>
        {
            $"Subject of interest: {person}, linked to {WithLegalSuffix(org, rng)}.",
            $"Activity concentrated around {place} and {Pick(rng, SyntheticData.Locations)}.",
        };
        if (k.Phones.Length > 0 && rng.NextDouble() < 0.7) lines.Add($"Contact number used in the onboarding form: {Pick(rng, k.Phones)}.");
        if (k.Emails.Length > 0 && rng.NextDouble() < 0.6) lines.Add($"Correspondence from {Pick(rng, k.Emails)} was attached to the file.");
        service.AddDocument(c.Id, new AddDocumentCommand("Initial triage", string.Join(' ', lines)), assignee);

        // Note 2: money flow, sometimes in Italian.
        if (k.Ibans.Length > 0 && rng.NextDouble() < 0.8)
        {
            var iban = Iban.Pretty(Pick(rng, k.Ibans));
            var amount = rng.Next(9, 480) * 1000 + rng.Next(0, 999);
            var body = rng.NextDouble() < 0.4
                ? $"Rilevati bonifici ripetuti verso il conto {iban} per un totale di EUR {amount:N0}. La società {org} risulta beneficiaria; attività sospetta segnalata da {Pick(rng, Branches)}."
                : $"Repeated wire transfers to account {iban} totalling EUR {amount:N0}. Funds were split into amounts just under the reporting threshold and moved on within 48 hours via {Pick(rng, SyntheticData.Locations)}.";
            service.AddDocument(c.Id, new AddDocumentCommand("Transaction review", body), assignee);
        }

        // Extra people seen with the subject create second-degree links across clusters.
        if (rng.NextDouble() < 0.35)
        {
            var other = Pick(rng, SyntheticData.People);
            service.AddDocument(c.Id, new AddDocumentCommand("Associates",
                $"Open-source checks place {person} at a meeting with {other} in {Pick(rng, k.Places)}. Relationship not yet confirmed."), assignee);
        }

        // Status mix: roughly half Open, a quarter InReview, a quarter Closed.
        var target = (n * 7 + 3) % 4;
        if (target >= 2)
        {
            service.Transition(c.Id, new TransitionCommand(CaseStatus.InReview, "Triage complete"), assignee);
            if (target == 3)
            {
                service.AddDocument(c.Id, new AddDocumentCommand("Final report",
                    $"Findings: the pattern involving {org} is {(rng.NextDouble() < 0.5 ? "confirmed and referred" : "explained by legitimate trade activity")}. No further action in {place}.",
                    DocumentKind.Report), assignee);
                service.Transition(c.Id, new TransitionCommand(CaseStatus.Closed, "Reviewed and signed off"), Supervisor);
            }
        }
    }

    private static string WithLegalSuffix(string org, Random rng) =>
        org.StartsWith("Società", StringComparison.Ordinal) ? org : $"{org} {Pick(rng, ["Srl", "S.p.A.", "Ltd", "GmbH", "B.V."])}";

    private static readonly string[] Triggers =
    [
        "structured cash deposits", "a sanctions screening near-match", "a sudden change in counterparties",
        "dormant account reactivation", "round-amount transfers", "an adverse media hit",
    ];

    private static readonly string[] Branches = ["la filiale di Trieste", "l'ufficio di Milano", "un intermediario di Lugano"];

    private static T Pick<T>(Random rng, IReadOnlyList<T> items) => items[rng.Next(items.Count)];

    private static IEnumerable<Cluster> Clusters()
    {
        string It(string bban) => Iban.Create("IT", bban);
        string Mt(string bban) => Iban.Create("MT", bban);
        string Cy(string bban) => Iban.Create("CY", bban);
        string Nl(string bban) => Iban.Create("NL", bban);

        yield return new Cluster("wire-ring", ["wire-transfer", "structuring", "aml"],
            ["Wire transfer structuring via {0}", "Layered transfers through {1}", "Rapid fund movement, {2}", "Threshold avoidance by {0}", "Wire transfer chain to {1}"],
            ["Possible structuring of wire transfers by {0}.", "Transfers linked to {2} appear layered through {1}."],
            ["Aldo Ferrantelli", "Giulia Marchetto"], ["Pellara Trading", "Credito Collinare"], ["Valletta", "Milano"],
            [Mt("VALL12345000000000012345678"), It("X0999901000000000071234")],
            ["+44 7700 900417", "+39 06 5550 1290"], ["a.ferrantelli@pellara.example"]);

        yield return new Cluster("carousel", ["vat", "invoice-fraud", "trade"],
            ["Carousel invoicing between {0} and partners", "Missing trader pattern, {1}", "Circular metal shipments via {0}", "Invoice mismatch reported in {1}", "VAT refund anomaly, {0}"],
            ["Invoices issued by {0} do not match customs records in {1}.", "Goods appear to circulate between {0} and the same buyers."],
            ["Helena Strand", "Tomas Wexley"], ["Halvard Metals", "Kestrel Dunmore"], ["Rotterdam", "Antwerp"],
            [Nl("ZZBK0417255301"), Nl("ZZBK0099812210")],
            ["+44 7700 900982"], ["t.wexley@halvard.example", "billing@kestrel-dunmore.example"]);

        yield return new Cluster("shipping", ["customs", "trade", "logistics"],
            ["Undeclared cargo on vessels chartered by {0}", "Container discrepancy at {1}", "Customs seal tampering, {1}", "Transshipment anomaly involving {0}", "Manifest edits by {2}"],
            ["Manifests filed by {0} in {1} were amended after departure.", "Customs flagged a weight mismatch on containers handled by {0}."],
            ["Fabrizio Ottonello", "Nikos Paravas"], ["Brisco Marittima", "Nordquay Freight"], ["Genova", "Piraeus", "Trieste"],
            [It("Y0888802000000000045522")],
            ["+39 010 555 0177", "+30 210 555 0198"], ["ops@brisco.example"]);

        yield return new Cluster("account-takeover", ["phishing", "fraud", "cyber"],
            ["Account takeover after phishing, {1}", "Credential reset abuse reported by {2}", "Mailbox compromise at {0}", "Payee change fraud, {0}", "Spoofed supplier email to {0}"],
            ["A supplier email to {0} requested a change of bank details.", "Login from a new device preceded a payee change at {0}."],
            ["Rhea Talbot", "Oskar Lindhagen"], ["Lumetra Consulting", "Halvard Metals"], ["Lugano", "Milano"],
            [Cy("002001280000001200527600")],
            ["+44 7700 900311"], ["accounts@lumetra.example", "payments-update@lumetra-billing.example"]);

        yield return new Cluster("subsidies", ["subsidy", "agriculture", "public-funds"],
            ["Subsidy claims by {0}", "Land parcel double counting near {1}", "Grant application anomalies, {0}", "Phantom harvest declarations in {1}", "Shell beneficiaries of {0}"],
            ["Claims submitted by {0} cover parcels near {1} that satellite imagery shows unused.", "Payments to {0} were forwarded to a third party within days."],
            ["Pietro Sanguedolce", "Chiara Benvenuti"], ["Società Agricola Fontebella"], ["Bari", "Napoli"],
            [It("Z0777703000000000090018")],
            ["+39 080 555 0143"], ["amministrazione@fontebella.example"]);

        yield return new Cluster("mules", ["money-mule", "aml", "fraud"],
            ["Money mule recruitment in {1}", "Student accounts receiving transfers, {1}", "Mule network linked to {2}", "Rapid cash-out after inbound transfers, {1}", "Recruitment ads pointing to {2}"],
            ["Several young account holders in {1} received and forwarded similar amounts.", "Accounts opened in {1} share a contact number with {2}."],
            ["Dario Montefusco", "Mirela Dushku"], ["Lumetra Consulting"], ["Napoli", "Tirana"],
            [It("X0999901000000000071234"), It("W0666604000000000033301")], // first account is shared with the wire ring
            ["+39 081 555 0166", "+355 4 555 0102"], ["jobs.remote@mailbox.example"]);

        yield return new Cluster("real-estate", ["real-estate", "aml", "pep"],
            ["Property purchases by {0}", "Cash-funded villa in {1}", "Nominee ownership, {0}", "Mortgage paid from {1} accounts", "Undervalued sale involving {2}"],
            ["{0} bought three properties in {1} below market value.", "Funds for the purchase arrived from {1} through {0}."],
            ["Sven Aaltonen", "Irene Calvisi"], ["Orvane Holdings", "Vescari Immobiliare"], ["Limassol", "Milano"],
            [Cy("005008000000000400228811")],
            ["+357 22 555 019"], ["legal@orvane.example"]);

        yield return new Cluster("misc", ["review", "low-signal"],
            ["Unusual card usage in {1}", "Dormant account reactivated, {1}", "Adverse media check on {2}", "Cash deposits at {1} branch", "Sanctions near-match: {2}", "Duplicate onboarding, {1}"],
            ["Low-signal alert involving {2} in {1}.", "Routine review triggered for activity in {1}."],
            SyntheticData.People, SyntheticData.Organisations, SyntheticData.Locations,
            [], [], []);
    }
}
