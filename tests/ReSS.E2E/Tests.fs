module ReSS.E2E.Setup

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open System.Collections.Generic
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection
open Microsoft.Playwright
open Giraffe
open ReSS.Domain.Types

// ── Playwright browser installation ──────────────────────────────────────────
//
// When CHROMIUM_PATH is set (e.g. on NixOS where pwsh isn't available),
// we skip Playwright's own download and use the system-installed binary.
// Otherwise, we auto-install via Playwright's installer (needs pwsh/curl).

let private chromiumPath =
    let v = Environment.GetEnvironmentVariable("CHROMIUM_PATH")
    if String.IsNullOrWhiteSpace(v) then None else Some v

do
    match chromiumPath with
    | Some p -> printfn "[E2E] Using system Chromium: %s" p
    | None   ->
        let exitCode = Microsoft.Playwright.Program.Main([| "install"; "chromium" |])
        if exitCode <> 0 then
            failwithf "Playwright browser install failed (exit code %d)" exitCode

// ── RSS fixture ───────────────────────────────────────────────────────────────

/// Three-item RSS feed with ascending pubDates so "oldest-first" ordering is testable.
let rssXml = """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>Test Feed</title>
    <link>https://example.com</link>
    <description>A test feed</description>
    <item>
      <title>Item 1 (oldest)</title>
      <link>https://example.com/1</link>
      <pubDate>Mon, 01 Jan 2024 00:00:00 GMT</pubDate>
    </item>
    <item>
      <title>Item 2</title>
      <link>https://example.com/2</link>
      <pubDate>Tue, 02 Jan 2024 00:00:00 GMT</pubDate>
    </item>
    <item>
      <title>Item 3 (newest)</title>
      <link>https://example.com/3</link>
      <pubDate>Wed, 03 Jan 2024 00:00:00 GMT</pubDate>
    </item>
  </channel>
</rss>"""

// ── Stub HTTP handler ─────────────────────────────────────────────────────────

/// Stub HttpMessageHandler that serves pre-configured responses by URL key.
type StubHandler(responses: (string * HttpResponseMessage) list) =
    inherit HttpMessageHandler()
    let dict = Dictionary<string, HttpResponseMessage>(dict responses)
    override _.SendAsync(req, _) =
        match dict.TryGetValue(req.RequestUri.ToString()) with
        | true, r -> Task.FromResult(r)
        | _       -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))

let makeRssResponse () =
    new HttpResponseMessage(
        HttpStatusCode.OK,
        Content = new StringContent(rssXml, Encoding.UTF8, "application/rss+xml"))

let makeErrorResponse (code: int) =
    new HttpResponseMessage(enum<HttpStatusCode> code)

let makeHtmlResponse () =
    new HttpResponseMessage(
        HttpStatusCode.OK,
        Content = new StringContent("<html><body>Not RSS</body></html>", Encoding.UTF8, "text/html"))

// ── Clock helpers ─────────────────────────────────────────────────────────────

let fixedClock (d: DateOnly) : Clock = fun () -> d

/// Stable "today" used across E2E tests.
let todayClock : Clock = fixedClock (DateOnly(2025, 6, 1))

// ── Real Kestrel server startup ───────────────────────────────────────────────

/// Starts the ReSS app on a random localhost port with the given stub handler and clock.
/// Returns the WebApplication (IDisposable) and the actual base URL (e.g. "http://127.0.0.1:54321").
let startServer (stubHandler: HttpMessageHandler) (clock: Clock) = task {
    let builder = WebApplication.CreateBuilder()
    builder.Services.AddMemoryCache()                                                   |> ignore
    builder.Services.AddSingleton<Net.Http.HttpClient>(new Net.Http.HttpClient(stubHandler)) |> ignore
    builder.Services.AddSingleton<Clock>(clock)                                         |> ignore
    builder.Services.AddGiraffe()                                                       |> ignore
    // ConfigureWebHostBuilder doesn't expose UseUrls directly; UseSetting works instead.
    builder.WebHost.UseSetting("urls", "http://127.0.0.1:0")                            |> ignore

    let app = builder.Build()
    app.UseGiraffe(ReSS.App.webApp)
    do! app.StartAsync()

    let addrs =
        app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
    let baseUrl = addrs.Addresses |> Seq.head
    return (app :> IDisposable, baseUrl)
}

// ── Playwright browser fixture ────────────────────────────────────────────────

/// Shared Playwright fixture: creates one browser process per test class.
/// Expose both Browser (for page tests) and Playwright (for APIRequest tests).
type BrowserFixture() =
    let pw =
        Playwright.CreateAsync()
        |> Async.AwaitTask
        |> Async.RunSynchronously

    let browser =
        // Set HEADED=1 (or any non-empty value) to watch the browser during tests.
        let headless = String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEADED"))
        let opts = BrowserTypeLaunchOptions(Headless = headless, SlowMo = (if headless then 0f else 500f))
        match chromiumPath with
        | Some p -> opts.ExecutablePath <- p
        | None   -> ()
        pw.Chromium.LaunchAsync(opts)
        |> Async.AwaitTask
        |> Async.RunSynchronously

    member _.Browser   = browser
    member _.Playwright = pw

    interface IDisposable with
        member _.Dispose() =
            browser.CloseAsync() |> Async.AwaitTask |> Async.RunSynchronously
            pw.Dispose()
