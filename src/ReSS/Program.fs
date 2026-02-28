module ReSS.App

/// Marker type for WebApplicationFactory in tests.
type Marker = Marker

open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open OpenTelemetry.Logs
open OpenTelemetry.Metrics
open OpenTelemetry.Resources
open OpenTelemetry.Trace
open Giraffe
open ReSS.Domain.Types
open ReSS.Handlers

let webApp =
    choose [
        GET  >=> route  "/"        >=> getIndexHandler
        POST >=> route  "/"        >=> postIndexHandler
        GET  >=> routef "/feed/%s" getFeedHandler
        RequestErrors.notFound (text "Not found")
    ]

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    builder.Services.AddMemoryCache()                     |> ignore
    builder.Services.AddHttpClient(fun (client: Net.Http.HttpClient) ->
        client.Timeout <- TimeSpan.FromSeconds(15.0)
    ) |> ignore
    let clock : Clock = fun () -> DateOnly.FromDateTime(DateTime.Today)
    builder.Services.AddSingleton<Clock>(clock) |> ignore
    builder.Services.AddGiraffe()                         |> ignore

    // OpenTelemetry — tracing + metrics, OTLP export driven by standard env vars.
    // ActivitySource and Meter are module-level singletons in Telemetry.fs;
    // the OTEL SDK picks them up by name ("ReSS") at startup.
    builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(fun r ->
            r.AddService(Telemetry.SourceName) |> ignore)
        .WithTracing(fun t ->
            t.AddSource(Telemetry.SourceName)
             .AddAspNetCoreInstrumentation()
             .AddHttpClientInstrumentation()
             .AddOtlpExporter()
             |> ignore)
        .WithMetrics(fun m ->
            m.AddMeter(Telemetry.SourceName)
             .AddAspNetCoreInstrumentation()
             .AddHttpClientInstrumentation()
             .AddOtlpExporter()
             |> ignore)
        |> ignore

    let app = builder.Build()

    if not (app.Environment.IsDevelopment()) then
        app.UseHsts() |> ignore

    app.UseGiraffe(webApp)
    app.Run()
    0
