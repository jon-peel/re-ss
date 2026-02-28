module ReSS.E2E.FeedEndpointTests

/// Playwright tests for the GET /feed/{blob} endpoint.
/// Run with: dotnet test --filter "Category=E2E"

open System
open System.Net.Http
open System.Xml.Linq
open Microsoft.Playwright
open Xunit
open ReSS.Domain.Types
open ReSS.Domain.UrlCodec
open ReSS.E2E.Setup

// ── Helpers ───────────────────────────────────────────────────────────────────

[<Literal>]
let private FeedUrl = "https://example.com/feed"

/// Stable "today" shared with FormTests (2025-06-01).
let private today = DateOnly(2025, 6, 1)

let private makeBlob (url: string) (perDay: int) (startDate: DateOnly) =
    encode { SourceUrl = Uri(url); PerDay = perDay * 1<articles/day>; StartDate = startDate }

// ── Test class ────────────────────────────────────────────────────────────────

[<Trait("Category","E2E")>]
type FeedEndpointTests(bfx: BrowserFixture) =

    // ── Helper: server + Playwright APIRequestContext (no browser rendering) ──
    // (let bindings must precede interface and member declarations in F# types)
    //
    // Using APIRequest lets us get the raw XML response body, bypassing the
    // browser's built-in XML viewer which wraps output in HTML.

    let withApi
            (handler : HttpMessageHandler)
            (clock   : Clock)
            (test    : string -> IAPIRequestContext -> System.Threading.Tasks.Task<unit>) =
        task {
            let! (app, baseUrl) = startServer handler clock
            use _app = app
            let! apiCtx =
                bfx.Playwright.APIRequest.NewContextAsync(
                    APIRequestNewContextOptions(BaseURL = baseUrl))
            do! test baseUrl apiCtx
            do! apiCtx.DisposeAsync()
        }

    // ── Helper: server + real browser page (needed for redirect assertions) ───

    let withPage
            (handler : HttpMessageHandler)
            (clock   : Clock)
            (test    : string -> IPage -> System.Threading.Tasks.Task<unit>) =
        task {
            let! (app, baseUrl) = startServer handler clock
            use _app = app
            let! ctx  = bfx.Browser.NewContextAsync()
            let! page = ctx.NewPageAsync()
            do! test baseUrl page
            do! page.CloseAsync()
            do! ctx.CloseAsync()
        }

    // ── Parse helpers ─────────────────────────────────────────────────────────

    let items (xml: string) =
        XDocument.Parse(xml).Descendants(XName.Get("item")) |> Seq.toList

    let channelTitle (xml: string) =
        // First <title> in document order is the channel title
        XDocument.Parse(xml).Descendants(XName.Get("title")) |> Seq.head |> _.Value

    interface IClassFixture<BrowserFixture>

    // ── 11.3 Tests ────────────────────────────────────────────────────────────

    [<Fact>]
    member _.``valid blob returns correct item count`` () =
        // today=2025-06-01, startDate=2025-06-01, perDay=1, total=3
        // daysElapsed = today.DayNumber - startDate.DayNumber + 1 = 1
        // unlocked = 1×1 = 1 < 3 → ShowItems 1
        let blob    = makeBlob FeedUrl 1 today
        let handler = StubHandler [FeedUrl, makeRssResponse()]
        withApi handler (fixedClock today) (fun _ apiCtx -> task {
            let! resp = apiCtx.GetAsync(sprintf "/feed/%s" blob)
            let! body = resp.TextAsync()
            Assert.Equal(1, (items body).Length)
        })

    [<Fact>]
    member _.``feed title contains unlocked-of-total`` () =
        // same setup: 1 unlocked of 3 total → title = "Test Feed — 1/3"
        let blob    = makeBlob FeedUrl 1 today
        let handler = StubHandler [FeedUrl, makeRssResponse()]
        withApi handler (fixedClock today) (fun _ apiCtx -> task {
            let! resp  = apiCtx.GetAsync(sprintf "/feed/%s" blob)
            let! body  = resp.TextAsync()
            let title  = channelTitle body
            Assert.Contains("1", title)
            Assert.Contains("3", title)
            Assert.Contains("/", title)   // "— 1/3" or similar
        })

    [<Fact>]
    member _.``items are returned oldest-first`` () =
        // today=2025-06-01, startDate=2025-05-31 → daysElapsed=2, unlocked=2<3 → ShowItems 2
        let startDate = DateOnly(2025, 5, 31)
        let blob      = makeBlob FeedUrl 1 startDate
        let handler   = StubHandler [FeedUrl, makeRssResponse()]
        withApi handler (fixedClock today) (fun _ apiCtx -> task {
            let! resp = apiCtx.GetAsync(sprintf "/feed/%s" blob)
            let! body = resp.TextAsync()
            let its   = items body
            Assert.Equal(2, its.Length)
            let pubDates =
                its
                |> List.map (fun i ->
                    DateTimeOffset.Parse(i.Element(XName.Get("pubDate")).Value))
            Assert.True(
                pubDates.[0] <= pubDates.[1],
                sprintf "Expected oldest-first but got %A then %A" pubDates.[0] pubDates.[1])
        })

    [<Fact>]
    member _.``caught-up blob redirects browser to source URL`` () =
        // far past start date → daysElapsed huge → unlocked ≥ total → RedirectToSource (301)
        let blob    = makeBlob FeedUrl 100 (DateOnly(2020, 1, 1))
        let handler = StubHandler [FeedUrl, makeRssResponse()]
        withPage handler (fixedClock today) (fun baseUrl page -> task {
            // Intercept the redirect target so the browser doesn't hit the real network.
            // The app issues a 301 to FeedUrl; Playwright follows it and we serve a stub.
            do! page.RouteAsync(
                    FeedUrl,
                    fun (route: IRoute) ->
                        route.FulfillAsync(
                            RouteFulfillOptions(
                                Status      = 200,
                                Body        = "<html><body>source</body></html>",
                                ContentType = "text/html")))
            let! _ = page.GotoAsync(sprintf "%s/feed/%s" baseUrl blob)
            // After following the 301, the page's URL should be the original source URL
            Assert.Equal(FeedUrl, page.Url)
        })

    [<Fact>]
    member _.``malformed blob returns 400`` () =
        withApi (StubHandler []) (fixedClock today) (fun _ apiCtx -> task {
            let! resp = apiCtx.GetAsync("/feed/not_a_valid_blob!!!")
            Assert.Equal(400, resp.Status)
        })

    [<Fact>]
    member _.``future start date produces empty feed (0 items)`` () =
        // today < startDate → daysElapsed = 0 → ShowItems 0
        let futureStart = DateOnly(2025, 12, 31)
        let blob        = makeBlob FeedUrl 1 futureStart
        let handler     = StubHandler [FeedUrl, makeRssResponse()]
        withApi handler (fixedClock today) (fun _ apiCtx -> task {
            let! resp = apiCtx.GetAsync(sprintf "/feed/%s" blob)
            let! body = resp.TextAsync()
            Assert.Equal(0, (items body).Length)
        })
