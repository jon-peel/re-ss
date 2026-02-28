module ReSS.Domain.FeedFetcher

open System
open System.Diagnostics
open System.Net.Http
open System.Threading.Tasks
open System.ServiceModel.Syndication
open System.Xml
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Logging
open ReSS.Domain.Types

let private cacheTtlMinutes =
    match System.Environment.GetEnvironmentVariable("CACHE_TTL_MINUTES") with
    | null -> 15
    | s    -> match System.Int32.TryParse(s) with
              | true, n -> n
              | _       -> 15

let private isXmlContentType (response: HttpResponseMessage) =
    let ct = response.Content.Headers.ContentType
    if ct = null then false
    else
        let mt = ct.MediaType
        mt <> null &&
        (mt.Contains("xml") || mt.Contains("rss") || mt.Contains("atom"))

let fetchFeed (client: HttpClient) (cache: IMemoryCache) (url: Uri) (logger: ILogger option) : Async<Result<SyndicationFeed, FetchError>> =
    async {
        let cacheKey = url.AbsoluteUri
        match cache.TryGetValue<SyndicationFeed>(cacheKey) with
        | true, feed ->
            logger |> Option.iter (fun l -> l.LogDebug("Cache hit for {sourceUrl}", url))
            return Ok feed
        | _ ->
            let sw = Stopwatch.StartNew()
            try
                let! response = client.GetAsync(url) |> Async.AwaitTask  // Uri overload
                if not response.IsSuccessStatusCode then
                    logger |> Option.iter (fun l ->
                        l.LogWarning("Fetch error for {sourceUrl}: HTTP {statusCode}", url, int response.StatusCode))
                    return Error (HttpError (int response.StatusCode))
                elif not (isXmlContentType response) then
                    logger |> Option.iter (fun l ->
                        l.LogWarning("Fetch error for {sourceUrl}: not XML", url))
                    return Error NotXml
                else
                    let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    try
                        use reader = XmlReader.Create(new IO.StringReader(content))
                        let feed = SyndicationFeed.Load(reader)
                        let itemCount = feed.Items |> Seq.length
                        cache.Set(cacheKey, feed, TimeSpan.FromMinutes(float cacheTtlMinutes)) |> ignore
                        sw.Stop()
                        logger |> Option.iter (fun l ->
                            l.LogInformation("Fetched {sourceUrl}: {itemCount} items in {elapsed}ms", url, itemCount, sw.ElapsedMilliseconds))
                        return Ok feed
                    with ex ->
                        logger |> Option.iter (fun l ->
                            l.LogWarning("Parse failure for {sourceUrl}: {message}", url, ex.Message))
                        return Error (ParseFailure ex.Message)
            with
            | :? HttpRequestException
            | :? TaskCanceledException
            | :? OperationCanceledException ->
                logger |> Option.iter (fun l ->
                    l.LogWarning("Unreachable URL: {sourceUrl}", url))
                return Error UnreachableUrl
            // On .NET 10, Async.AwaitTask does not always unwrap AggregateException;
            // this arm is required when HttpClient wraps the inner exception.
            | :? AggregateException as ae
                when ae.InnerExceptions |> Seq.exists (fun e -> e :? HttpRequestException) ->
                logger |> Option.iter (fun l ->
                    l.LogWarning("Unreachable URL: {sourceUrl}", url))
                return Error UnreachableUrl
    }
