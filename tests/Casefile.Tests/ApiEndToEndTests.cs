using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Casefile.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Casefile.Tests;

/// <summary>
/// Starts the real application (Kestrel on 127.0.0.1, port 0 so the OS picks a free one) with the seeded
/// corpus and a temporary snapshot path, then drives it over HTTP.
/// </summary>
public class ApiEndToEndTests : IAsyncDisposable
{
    private WebApplication? _app;
    private HttpClient? _http;
    private readonly string _snapshot = Path.Combine(Path.GetTempPath(), $"casefile-e2e-{Guid.NewGuid():N}.json");

    private async Task<HttpClient> StartAsync()
    {
        _app = CasefileApp.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"], o =>
        {
            o.SnapshotPath = _snapshot;
            o.Seed = true;
        });
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient { BaseAddress = new Uri(address) };
        _http.DefaultRequestHeaders.Add("X-Actor", "analyst.e2e");
        return _http;
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        File.Delete(_snapshot);
    }

    [Test]
    public async Task Search_crud_workflow_related_and_snapshot_over_http()
    {
        var http = await StartAsync();

        var health = await http.GetFromJsonAsync<JsonElement>("/health");
        Assert.True(health.GetProperty("cases").GetInt32() >= 35);

        // Search with facets and a keyword clause.
        var search = await PostJson(http, "/search", new { query = "wire transfer AND status:Open", limit = 5 });
        Assert.True(search.GetProperty("total").GetInt32() > 0);
        var hits = search.GetProperty("hits").EnumerateArray().ToList();
        Assert.True(hits.All(h => h.GetProperty("status").GetString() == "Open"));
        var scores = hits.Select(h => h.GetProperty("score").GetDouble()).ToList();
        Assert.Sequence(scores.OrderByDescending(x => x), scores, "hits are sorted by score");
        Assert.True(hits[0].GetProperty("snippets").EnumerateObject().Any(p => p.Value.GetString()!.Contains("[wire]", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("Open", search.GetProperty("facets").GetProperty("status")[0].GetProperty("value").GetString());

        // Bad query and unknown field map to 400 problem responses.
        var bad = await http.PostAsJsonAsync("/search", new { query = "wire \"open" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/search", new { query = "colour:red" })).StatusCode);

        // Create two cases that share a synthetic phone number, then check the link.
        var a = await PostJson(http, "/cases", new { title = "Courier payments via Trieste", assignee = "analyst.e2e", tags = new[] { "E2E", "courier" } }, HttpStatusCode.Created);
        var idA = a.GetProperty("id").GetString()!;
        Assert.Sequence(["courier", "e2e"], a.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!));
        await PostJson(http, $"/cases/{idA}/documents", new { title = "Call log", body = "Courier dispatcher reachable on +44 7700 900555, paid in cash." }, HttpStatusCode.Created);

        var b = await PostJson(http, "/cases", new { title = "Parcel forwarding ring" }, HttpStatusCode.Created);
        var idB = b.GetProperty("id").GetString()!;
        await PostJson(http, $"/cases/{idB}/documents", new { title = "Tip", body = "Anonymous tip names the number +44 7700 900555." }, HttpStatusCode.Created);

        var related = await http.GetFromJsonAsync<JsonElement>($"/cases/{idA}/related");
        var first = related.GetProperty("related")[0];
        Assert.Equal(idB, first.GetProperty("id").GetString());
        Assert.Equal("Phone", first.GetProperty("sharedEntities")[0].GetProperty("type").GetString());

        var byEntity = await http.GetFromJsonAsync<JsonElement>("/entities/Phone/+447700900555/cases");
        Assert.Sequence(new[] { idA, idB }.Order(StringComparer.Ordinal), byEntity.GetProperty("cases").EnumerateArray().Select(x => x.GetString()!));

        // New content is searchable right away.
        var found = await PostJson(http, "/search", new { query = "courier dispatcher", filters = new Dictionary<string, string[]> { ["tag"] = ["e2e"] } });
        Assert.Equal(idA, found.GetProperty("hits")[0].GetProperty("id").GetString());

        // Workflow: B has no assignee, so submitting for review is refused with the rule code.
        var refused = await http.PostAsJsonAsync($"/cases/{idB}/transitions", new { to = "InReview" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("submit-for-review", problem.GetProperty("rule").GetString());

        // A: review, then the assignee may not close it (four-eyes), a different actor can.
        await PostJson(http, $"/cases/{idA}/transitions", new { to = "InReview" });
        await PostJson(http, $"/cases/{idA}/documents", new { title = "Report", body = "Confirmed.", kind = "Report" }, HttpStatusCode.Created);
        var selfClose = await http.PostAsJsonAsync($"/cases/{idA}/transitions", new { to = "Closed", reason = "done" });
        Assert.Equal(HttpStatusCode.Conflict, selfClose.StatusCode);

        using var lead = new HttpRequestMessage(HttpMethod.Post, $"/cases/{idA}/transitions") { Content = JsonContent.Create(new { to = "Closed", reason = "Reviewed" }) };
        lead.Headers.Add("X-Actor", "lead.e2e");
        var closed = await http.SendAsync(lead);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        var transitions = await http.GetFromJsonAsync<JsonElement>($"/cases/{idA}/transitions");
        Assert.Equal("Closed", transitions.GetProperty("current").GetString());
        var audit = await http.GetFromJsonAsync<JsonElement>($"/cases/{idA}/audit");
        Assert.Equal("lead.e2e", audit.EnumerateArray().Last().GetProperty("actor").GetString());

        // Closed case is read-only; deleting B (Open) works; unknown case is 404.
        Assert.Equal(HttpStatusCode.Conflict, (await http.PatchAsJsonAsync($"/cases/{idA}", new { title = "edit" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/cases/{idB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/cases/{idB}")).StatusCode);

        // Snapshot endpoint writes a loadable file.
        var saved = await PostJson(http, "/admin/snapshot", new { });
        Assert.True(File.Exists(_snapshot));
        Assert.Equal(health.GetProperty("cases").GetInt32() + 1, saved.GetProperty("saved").GetInt32());
    }

    private static async Task<JsonElement> PostJson(HttpClient http, string url, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await http.PostAsJsonAsync(url, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(expected, response.StatusCode, $"POST {url}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
