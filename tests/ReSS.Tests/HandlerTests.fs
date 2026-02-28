module ReSS.Tests.HandlerTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open Microsoft.AspNetCore.Mvc.Testing
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Caching.Memory
open Xunit
open ReSS.Domain.Types
open ReSS.Domain.UrlCodec

// ---- RSS XML fixture ----

let private rssXml = """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
  <channel>
    <title>Test Feed</title>
    <link>https://example.com</link>
    <description>A test feed</description>
    <item>
      <title>Item 1</title>
      <link>https://example.com/1</link>
      <pubDate>Mon, 01 Jan 2024 00:00:00 GMT</pubDate>
    </item>
    <item>
      <title>Item 2</title>
      <link>https://example.com/2</link>
      <pubDate>Tue, 02 Jan 2024 00:00:00 GMT</pubDate>
    </item>
  </channel>
</rss>"""

// ---- Stub handler ----

type TestHandler(urlResponses: (string * HttpResponseMessage) list) =
    inherit HttpMessageHandler()
    let responses = Dictionary<string, HttpResponseMessage>(dict urlResponses)
    override _.SendAsync(req, _) =
        let key = req.RequestUri.ToString()
        match responses.TryGetValue(key) with
        | true, r -> Task.FromResult(r)
        | _ ->
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))

// ---- Factory helpers ----

let private makeRssResponse () =
    new HttpResponseMessage(HttpStatusCode.OK,
        Content = new StringContent(rssXml, Encoding.UTF8, "application/rss+xml"))

let private makeErrorResponse code =
    new HttpResponseMessage(enum<HttpStatusCode> code)

let private makeFactory (clock: Clock) (stubHandler: HttpMessageHandler) =
    let factory = new WebApplicationFactory<ReSS.App.Marker>()
    factory.WithWebHostBuilder(fun b ->
        b.ConfigureServices(fun services ->
            // Remove existing HttpClient registrations
            services.AddHttpClient() |> ignore
            // Override with a named client that uses our stub
            services.AddSingleton<HttpClient>(new HttpClient(stubHandler)) |> ignore
            services.AddSingleton<Clock>(clock) |> ignore
        ) |> ignore
    )

let private fixedClock (d: DateOnly) : Clock = fun () -> d
let private todayClock = fixedClock (DateOnly(2025, 1, 15))

// ---- 1.7 Smoke tests ----

[<Fact>]
let ``GET / returns 200`` () =
    use factory = new WebApplicationFactory<ReSS.App.Marker>()
    use client = factory.CreateClient()
    let response = client.GetAsync("/").Result
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)

[<Fact>]
let ``GET / response contains form element`` () =
    use factory = new WebApplicationFactory<ReSS.App.Marker>()
    use client = factory.CreateClient()
    let response = client.GetAsync("/").Result
    let content  = response.Content.ReadAsStringAsync().Result
    Assert.Contains("<form", content)

// ---- 10.2 POST / integration tests ----

[<Fact>]
let ``POST / with valid RSS URL returns 200 with generated URL`` () =
    let feedUrl = "https://example.com/feed"
    let handler = new TestHandler([feedUrl, makeRssResponse()])
    use factory = makeFactory todayClock handler
    use client  = factory.CreateClient()
    let form = new FormUrlEncodedContent([
        KeyValuePair("sourceUrl", feedUrl)
        KeyValuePair("perDay", "1")
        KeyValuePair("startDate", "2025-01-01")
    ])
    let response = client.PostAsync("/", form).Result
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let content = response.Content.ReadAsStringAsync().Result
    Assert.Contains("/feed/", content)

[<Fact>]
let ``POST / with missing URL returns 200 with inline error`` () =
    use factory = new WebApplicationFactory<ReSS.App.Marker>()
    use client  = factory.CreateClient()
    let form = new FormUrlEncodedContent([
        KeyValuePair("sourceUrl", "")
        KeyValuePair("perDay", "3")
    ])
    let response = client.PostAsync("/", form).Result
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let content = response.Content.ReadAsStringAsync().Result
    Assert.Contains("Please enter", content)

[<Fact>]
let ``POST / with private IP URL returns 200 with guard error`` () =
    let handler = new TestHandler([])
    use factory = makeFactory todayClock handler
    use client  = factory.CreateClient()
    let form = new FormUrlEncodedContent([
        KeyValuePair("sourceUrl", "http://127.0.0.1/feed")
        KeyValuePair("perDay", "3")
    ])
    let response = client.PostAsync("/", form).Result
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let content = response.Content.ReadAsStringAsync().Result
    Assert.Contains("private", content.ToLower())

[<Fact>]
let ``POST / with unreachable URL returns 200 with fetch error`` () =
    let handler = new TestHandler(["https://example.com/feed", makeErrorResponse 503])
    use factory = makeFactory todayClock handler
    use client  = factory.CreateClient()
    let form = new FormUrlEncodedContent([
        KeyValuePair("sourceUrl", "https://example.com/feed")
        KeyValuePair("perDay", "3")
    ])
    let response = client.PostAsync("/", form).Result
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    let content = response.Content.ReadAsStringAsync().Result
    Assert.Contains("503", content)

// ---- 10.3 GET /feed/{blob} integration tests ----

let private makeBlob (url: string) (perDay: int) (startDate: DateOnly) =
    encode { SourceUrl = Uri(url); PerDay = perDay * 1<articles/day>; StartDate = startDate }

[<Fact>]
let ``GET /feed with valid blob returns 200 application/rss+xml`` () =
    let feedUrl = "https://example.com/feed"
    // start date = today → daysElapsed = 1 → unlocked = 1 < 2 total → ShowItems
    let startDate = todayClock()
    let blob    = makeBlob feedUrl 1 startDate
    let handler = new TestHandler([feedUrl, makeRssResponse()])
    use factory = makeFactory todayClock handler
    use client  = factory.CreateClient()
    let response = client.GetAsync(sprintf "/feed/%s" blob).Result
    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
    Assert.Contains("rss", response.Content.Headers.ContentType.MediaType)

[<Fact>]
let ``GET /feed when caught up returns 301 redirect`` () =
    let feedUrl  = "https://example.com/feed"
    // Start date far in the past so unlocked >= total (feed has 2 items, 100/day)
    let blob     = makeBlob feedUrl 100 (DateOnly(2020, 1, 1))
    let handler  = new TestHandler([feedUrl, makeRssResponse()])
    use factory  = makeFactory todayClock handler
    use client   = factory.CreateClient(WebApplicationFactoryClientOptions(AllowAutoRedirect = false))
    let response = client.GetAsync(sprintf "/feed/%s" blob).Result
    Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode)

[<Fact>]
let ``GET /feed with malformed blob returns 400`` () =
    use factory = new WebApplicationFactory<ReSS.App.Marker>()
    use client  = factory.CreateClient()
    let response = client.GetAsync("/feed/not_a_valid_blob!!!").Result
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)

[<Fact>]
let ``GET /feed with private IP in blob returns 400`` () =
    let blob = makeBlob "http://127.0.0.1/feed" 1 (DateOnly(2025, 1, 1))
    use factory = new WebApplicationFactory<ReSS.App.Marker>()
    use client  = factory.CreateClient()
    let response = client.GetAsync(sprintf "/feed/%s" blob).Result
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)

[<Fact>]
let ``GET /feed with unreachable source returns 502`` () =
    let feedUrl = "https://example.com/feed"
    let blob    = makeBlob feedUrl 1 (DateOnly(2025, 1, 1))
    let handler = new TestHandler([feedUrl, makeErrorResponse 500])
    use factory = makeFactory todayClock handler
    use client  = factory.CreateClient()
    let response = client.GetAsync(sprintf "/feed/%s" blob).Result
    Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode)
