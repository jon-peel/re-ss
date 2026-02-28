module ReSS.App

/// Marker type for WebApplicationFactory in tests.
type Marker = Marker

open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
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

    let app = builder.Build()

    if not (app.Environment.IsDevelopment()) then
        app.UseHsts() |> ignore

    app.UseGiraffe(webApp)
    app.Run()
    0
