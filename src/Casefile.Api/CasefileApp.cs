using System.Text.Json.Serialization;
using Casefile.Core;
using Casefile.Core.Domain;
using Casefile.Core.Persistence;
using Casefile.Core.Seed;
using Casefile.Search.Querying;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;

namespace Casefile.Api;

public sealed class CasefileOptions
{
    /// <summary>JSON snapshot path. Loaded at startup if present, saved on shutdown and on POST /admin/snapshot.</summary>
    public string? SnapshotPath { get; set; }

    /// <summary>Seed synthetic cases when no snapshot is loaded.</summary>
    public bool Seed { get; set; } = true;
}

/// <summary>Builds the web application. Kept separate from Program so tests can host it in-process on a random port.</summary>
public static class CasefileApp
{
    public static WebApplication Build(string[] args, Action<CasefileOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        var options = builder.Configuration.GetSection("Casefile").Get<CasefileOptions>() ?? new CasefileOptions();
        configure?.Invoke(options);
        if (options.SnapshotPath is { } rel && !Path.IsPathRooted(rel))
            options.SnapshotPath = Path.Combine(builder.Environment.ContentRootPath, rel);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(_ => new CaseService(SyntheticData.BuildExtractor()));
        builder.Services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        // Expected domain errors become problem responses here, without being logged as server faults.
        // Anything else falls through to the generic handler, which logs it and returns a 500.
        app.UseExceptionHandler(errorApp => errorApp.Run(ctx => WriteProblem(ctx, ctx.Features.Get<IExceptionHandlerFeature>()?.Error)));
        app.Use(async (ctx, next) =>
        {
            try { await next(ctx); }
            catch (Exception ex) when (IsExpected(ex) && !ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                await WriteProblem(ctx, ex);
            }
        });

        InitializeAsync(app, options).GetAwaiter().GetResult();
        app.MapCasefileEndpoints();
        return app;
    }

    private static async Task InitializeAsync(WebApplication app, CasefileOptions options)
    {
        var service = app.Services.GetRequiredService<CaseService>();
        var log = app.Logger;
        if (options.SnapshotPath is { } path)
        {
            var store = new SnapshotStore(path);
            if (store.Exists)
            {
                await service.LoadAsync(store);
                log.LogInformation("Loaded {Count} cases from {Path}", service.Count, path);
            }
            app.Lifetime.ApplicationStopping.Register(() =>
            {
                service.SaveAsync(store).GetAwaiter().GetResult();
                log.LogInformation("Saved {Count} cases to {Path}", service.Count, path);
            });
        }
        if (service.Count == 0 && options.Seed)
        {
            Seeder.Seed(service);
            log.LogInformation("Seeded {Count} synthetic cases", service.Count);
        }
    }

    /// <summary>Maps domain exceptions to RFC 7807 problem responses. Workflow violations carry the rule code.</summary>
    private static bool IsExpected(Exception ex) =>
        ex is CasefileException or QueryParseException or UnknownFieldException or BadHttpRequestException;

    private static async Task WriteProblem(HttpContext ctx, Exception? ex)
    {
        var (status, title, extra) = ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found", null),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", null),
            QueryParseException q => (StatusCodes.Status400BadRequest, "Invalid query", new Dictionary<string, object?> { ["position"] = q.Position }),
            UnknownFieldException f => (StatusCodes.Status400BadRequest, "Unknown field", new Dictionary<string, object?> { ["field"] = f.Field }),
            WorkflowViolationException w => (StatusCodes.Status409Conflict, "Workflow rule violated", new Dictionary<string, object?> { ["rule"] = w.Rule }),
            ConcurrencyException => (StatusCodes.Status409Conflict, "Version conflict", null),
            BadHttpRequestException b => (b.StatusCode, "Bad request", null),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", (Dictionary<string, object?>?)null),
        };
        ctx.Response.StatusCode = status;
        var detail = status == 500 ? "See server log." : ex?.Message;
        await Results.Problem(detail: detail, statusCode: status, title: title, extensions: extra).ExecuteAsync(ctx);
    }
}
