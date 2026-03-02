module ReSS.E2E.FormTests

/// Playwright browser tests for the form UI (GET / and POST /).
/// Run with: dotnet test --filter "Category=E2E"

open System
open System.Net.Http
open Microsoft.Playwright
open Xunit
open ReSS.E2E.Setup

// ── Shared URL used for stub responses ────────────────────────────────────────

[<Literal>]
let private FeedUrl = "https://example.com/feed"

// ── Test class ────────────────────────────────────────────────────────────────

/// Each test spins up its own Kestrel instance (random port) with the required stub,
/// then navigates a Playwright page.  The BrowserFixture is shared for the process.
[<Trait("Category","E2E")>]
type FormTests(bfx: BrowserFixture) =

    // ── Helper: start server + open a fresh page ──────────────────────────────
    // (let bindings must precede interface and member declarations in F# types)

    let withPage
            (handler : HttpMessageHandler)
            (clock   : ReSS.Domain.Types.Clock)
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

    interface IClassFixture<BrowserFixture>

    // ── 11.2 Tests ────────────────────────────────────────────────────────────

    [<Fact>]
    member _.``page loads with form visible`` () =
        withPage (StubHandler []) todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            let! visible = page.Locator("form").IsVisibleAsync()
            Assert.True(visible)
        })

    [<Fact>]
    member _.``empty submit shows validation errors`` () =
        withPage (StubHandler []) todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            // Submit without entering any values
            do! page.Locator("button[type=submit]").ClickAsync()
            // At least one .error element must be present
            let! count = page.Locator(".error").CountAsync()
            Assert.True(count > 0, "Expected at least one .error element after empty submit")
        })

    [<Fact>]
    member _.``valid RSS URL produces generated feed URL`` () =
        let handler = StubHandler [FeedUrl, makeRssResponse()]
        withPage handler todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            do! page.Locator("input[name=sourceUrl]").FillAsync(FeedUrl)
            do! page.Locator("input[name=perDay]").FillAsync("1")
            do! page.Locator("button[type=submit]").ClickAsync()
            // Result section should show a generated /feed/... URL
            let! codeText = page.Locator(".result code").TextContentAsync()
            Assert.Contains("/feed/", codeText)
        })

    [<Fact>]
    member _.``copy button is present in result section`` () =
        let handler = StubHandler [FeedUrl, makeRssResponse()]
        withPage handler todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            do! page.Locator("input[name=sourceUrl]").FillAsync(FeedUrl)
            do! page.Locator("input[name=perDay]").FillAsync("1")
            do! page.Locator("button[type=submit]").ClickAsync()
            let! visible = page.Locator(".result .copy-btn").IsVisibleAsync()
            Assert.True(visible, "Expected .copy-btn to be visible in .result section")
        })

    [<Fact>]
    member _.``summary message shows article counts`` () =
        // today = 2025-06-01, startDate defaults to today, perDay=1, total=3 items
        // daysElapsed = 1, unlocked = 1 × 1 = 1 < 3 → ShowItems 1
        let handler = StubHandler [FeedUrl, makeRssResponse()]
        withPage handler todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            do! page.Locator("input[name=sourceUrl]").FillAsync(FeedUrl)
            do! page.Locator("input[name=perDay]").FillAsync("1")
            do! page.Locator("button[type=submit]").ClickAsync()
            // First <p> inside .result is "N of T articles ready"
            let! summaryText = page.Locator(".result p").First.TextContentAsync()
            Assert.Contains("articles today", summaryText)
            Assert.Contains("of", summaryText)
        })

    [<Fact>]
    member _.``advanced section is collapsed by default`` () =
        withPage (StubHandler []) todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            // <details> must not have the "open" attribute
            let! openAttr = page.Locator("details").GetAttributeAsync("open")
            Assert.Null(openAttr)
        })

    [<Fact>]
    member _.``expanding advanced section reveals start date input`` () =
        withPage (StubHandler []) todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            do! page.Locator("details summary").ClickAsync()
            let! visible = page.Locator("input[name=startDate]").IsVisibleAsync()
            Assert.True(visible)
        })

    [<Fact>]
    member _.``future start date shows 0 articles ready`` () =
        // today = 2025-06-01, startDate = 2025-12-31 (future) → today < startDate → daysElapsed = 0 → ShowItems 0
        let handler = StubHandler [FeedUrl, makeRssResponse()]
        withPage handler todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            do! page.Locator("input[name=sourceUrl]").FillAsync(FeedUrl)
            do! page.Locator("input[name=perDay]").FillAsync("1")
            do! page.Locator("details summary").ClickAsync()
            do! page.Locator("input[name=startDate]").FillAsync("2025-12-31")
            do! page.Locator("button[type=submit]").ClickAsync()
            let! summaryText = page.Locator(".result p").First.TextContentAsync()
            Assert.Contains("0 of", summaryText)
            Assert.Contains("articles today", summaryText)
        })

    [<Fact>]
    member _.``unreachable URL shows fetch error`` () =
        // Stub returns 503 → FetchError (HttpError 503) → form-level error message
        let handler = StubHandler [FeedUrl, makeErrorResponse 503]
        withPage handler todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            do! page.Locator("input[name=sourceUrl]").FillAsync(FeedUrl)
            do! page.Locator("button[type=submit]").ClickAsync()
            let! errorText = page.Locator(".form-error").TextContentAsync()
            Assert.Contains("503", errorText)
        })

    [<Fact>]
    member _.``non-RSS URL shows feed parse error`` () =
        // Stub returns HTML → NotXml error → form-level error message
        let nonRssUrl = "https://example.com/html"
        let handler   = StubHandler [nonRssUrl, makeHtmlResponse()]
        withPage handler todayClock (fun baseUrl page -> task {
            let! _ = page.GotoAsync(baseUrl)
            do! page.Locator("input[name=sourceUrl]").FillAsync(nonRssUrl)
            do! page.Locator("button[type=submit]").ClickAsync()
            let! visible = page.Locator(".form-error").IsVisibleAsync()
            Assert.True(visible, "Expected .form-error to be visible for non-RSS URL")
        })
