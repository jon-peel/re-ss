module ReSS.Tests.FeedFetcherTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Caching.Memory
open Xunit
open ReSS.Domain.Types
open ReSS.Domain.FeedFetcher

// ---- stub HTTP handler ----

type StubHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    let mutable callCount = 0
    member _.CallCount = callCount
    override _.SendAsync(request, _cancellationToken) =
        callCount <- callCount + 1
        Task.FromResult(respond request)

let private validRssXml = """<?xml version="1.0" encoding="UTF-8"?>
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
  </channel>
</rss>"""

let private newCache () =
    new MemoryCache(MemoryCacheOptions())

let private makeClient (handler: HttpMessageHandler) =
    new HttpClient(handler)

// ---- 6.1 tests ----

[<Fact>]
let ``valid RSS URL returns Ok`` () =
    let handler = StubHandler(fun _ ->
        new HttpResponseMessage(HttpStatusCode.OK,
            Content = new StringContent(validRssXml, Encoding.UTF8, "application/rss+xml")))
    let cache  = newCache ()
    let client = makeClient handler
    let result = fetchFeed client cache (Uri("https://example.com/feed")) None |> Async.RunSynchronously
    match result with
    | Ok feed -> Assert.NotNull(feed)
    | Error e -> Assert.Fail(sprintf "Expected Ok, got %A" e)

[<Fact>]
let ``non-XML content type returns NotXml`` () =
    let handler = StubHandler(fun _ ->
        new HttpResponseMessage(HttpStatusCode.OK,
            Content = new StringContent("<html></html>", Encoding.UTF8, "text/html")))
    let cache  = newCache ()
    let client = makeClient handler
    let result = fetchFeed client cache (Uri("https://example.com/feed")) None |> Async.RunSynchronously
    Assert.Equal(Error NotXml, result)

[<Fact>]
let ``HTTP 404 returns HttpError 404`` () =
    let handler = StubHandler(fun _ ->
        new HttpResponseMessage(HttpStatusCode.NotFound))
    let cache  = newCache ()
    let client = makeClient handler
    let result = fetchFeed client cache (Uri("https://example.com/feed")) None |> Async.RunSynchronously
    Assert.Equal(Error (HttpError 404), result)

type FaultingHandler() =
    inherit HttpMessageHandler()
    override _.SendAsync(_, _) =
        let tcs = TaskCompletionSource<HttpResponseMessage>()
        tcs.SetException(HttpRequestException("simulated network error"))
        tcs.Task

[<Fact>]
let ``network exception returns UnreachableUrl`` () =
    let handler = FaultingHandler()
    let cache  = newCache ()
    let client = new HttpClient(handler)
    let result = fetchFeed client cache (Uri("https://example.com/feed")) None |> Async.RunSynchronously
    Assert.Equal(Error UnreachableUrl, result)

[<Fact>]
let ``invalid XML content returns ParseFailure`` () =
    let handler = StubHandler(fun _ ->
        new HttpResponseMessage(HttpStatusCode.OK,
            Content = new StringContent("this is not xml", Encoding.UTF8, "application/xml")))
    let cache  = newCache ()
    let client = makeClient handler
    let result = fetchFeed client cache (Uri("https://example.com/feed")) None |> Async.RunSynchronously
    match result with
    | Error (ParseFailure _) -> ()
    | other -> Assert.Fail(sprintf "Expected ParseFailure, got %A" other)

// ---- 6.3 cache hit: handler called only once ----

[<Fact>]
let ``second call within TTL uses cache (handler called once)`` () =
    let handler = StubHandler(fun _ ->
        new HttpResponseMessage(HttpStatusCode.OK,
            Content = new StringContent(validRssXml, Encoding.UTF8, "application/rss+xml")))
    let cache  = newCache ()
    let client = makeClient handler
    let url = Uri("https://example.com/feed")
    fetchFeed client cache url None |> Async.RunSynchronously |> ignore
    fetchFeed client cache url None |> Async.RunSynchronously |> ignore
    Assert.Equal(1, handler.CallCount)

// ---- 6.4 different URL is a cache miss ----

[<Fact>]
let ``different URLs each trigger a fetch`` () =
    let handler = StubHandler(fun _ ->
        new HttpResponseMessage(HttpStatusCode.OK,
            Content = new StringContent(validRssXml, Encoding.UTF8, "application/rss+xml")))
    let cache  = newCache ()
    let client = makeClient handler
    fetchFeed client cache (Uri("https://example.com/feed1")) None |> Async.RunSynchronously |> ignore
    fetchFeed client cache (Uri("https://example.com/feed2")) None |> Async.RunSynchronously |> ignore
    Assert.Equal(2, handler.CallCount)
