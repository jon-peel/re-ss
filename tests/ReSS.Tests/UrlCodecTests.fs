module ReSS.Tests.UrlCodecTests

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open ReSS.Domain.Types
open ReSS.Domain.UrlCodec

// --- helpers ---

let validParams = {
    SourceUrl = Uri("https://example.com/feed")
    PerDay    = 3<articles/day>
    StartDate = DateOnly(2025, 1, 1)
}

// ---- 3.1 encode produces a non-empty string ----

[<Fact>]
let ``encode produces non-empty string`` () =
    let blob = encode validParams
    Assert.NotEmpty(blob)

// ---- 3.3 no padding ----

[<Fact>]
let ``encode produces no = padding`` () =
    let blob = encode validParams
    Assert.DoesNotContain("=", blob)

// ---- 3.4 decode invalid Base64 → InvalidBase64 ----

[<Fact>]
let ``decode of invalid base64 returns InvalidBase64`` () =
    let result = decode "not valid base64!!!"
    Assert.Equal(Error InvalidBase64, result)

// ---- 3.5 wrong segment count → MalformedSegments ----

[<Fact>]
let ``decode with wrong segment count returns MalformedSegments`` () =
    // Encode a string with only 2 segments
    let bad = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes("v1::only_two")) |> fun s -> s.TrimEnd('=').Replace('+','-').Replace('/','_')
    let result = decode bad
    Assert.Equal(Error MalformedSegments, result)

// ---- 3.6 unknown version → UnsupportedVersion ----

[<Fact>]
let ``decode with unknown version returns UnsupportedVersion`` () =
    let payload = "v99::https%3A%2F%2Fexample.com::3::2025-01-01"
    let bad = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes(payload)) |> fun s -> s.TrimEnd('=').Replace('+','-').Replace('/','_')
    let result = decode bad
    match result with
    | Error (UnsupportedVersion "v99") -> ()
    | other -> Assert.Fail(sprintf "Expected UnsupportedVersion v99, got %A" other)

// ---- 3.7 non-integer perDay → InvalidPerDay ----

[<Fact>]
let ``decode with non-integer perDay returns InvalidPerDay`` () =
    let payload = "v1::https%3A%2F%2Fexample.com::abc::2025-01-01"
    let bad = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes(payload)) |> fun s -> s.TrimEnd('=').Replace('+','-').Replace('/','_')
    let result = decode bad
    Assert.Equal(Error InvalidPerDay, result)

// ---- 3.7b perDay ≤ 0 → InvalidPerDay ----

[<Fact>]
let ``decode with perDay = 0 returns InvalidPerDay`` () =
    let payload = "v1::https%3A%2F%2Fexample.com::0::2025-01-01"
    let bad = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes(payload)) |> fun s -> s.TrimEnd('=').Replace('+','-').Replace('/','_')
    let result = decode bad
    Assert.Equal(Error InvalidPerDay, result)

[<Fact>]
let ``decode with perDay = -1 returns InvalidPerDay`` () =
    let payload = "v1::https%3A%2F%2Fexample.com::-1::2025-01-01"
    let bad = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes(payload)) |> fun s -> s.TrimEnd('=').Replace('+','-').Replace('/','_')
    let result = decode bad
    Assert.Equal(Error InvalidPerDay, result)

// ---- 3.8 unparseable date → InvalidDate ----

[<Fact>]
let ``decode with invalid date returns InvalidDate`` () =
    let payload = "v1::https%3A%2F%2Fexample.com::3::not-a-date"
    let bad = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes(payload)) |> fun s -> s.TrimEnd('=').Replace('+','-').Replace('/','_')
    let result = decode bad
    Assert.Equal(Error InvalidDate, result)

// ---- 3.9 round-trip property ----

type ValidFeedParams = ValidFeedParams of FeedParams

type Generators =
    static member FeedParams() =
        gen {
            let! url = Gen.elements [
                Uri("https://example.com/feed")
                Uri("https://blog.example.org/rss?format=xml&lang=en")
                Uri("http://feeds.feedburner.com/test-feed")
                Uri("https://example.com/path/with%20spaces/feed")
            ]
            let! perDay = Gen.choose (1, 100)
            let! year   = Gen.choose (2000, 2099)
            let! month  = Gen.choose (1, 12)
            let! day    = Gen.choose (1, 28)
            return {
                SourceUrl = url
                PerDay    = perDay * 1<articles/day>
                StartDate = DateOnly(year, month, day)
            }
        } |> Arb.fromGen

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``encode→decode round-trips all fields`` (p: FeedParams) =
    match decode (encode p) with
    | Ok decoded ->
        decoded.SourceUrl = p.SourceUrl &&
        decoded.PerDay    = p.PerDay    &&
        decoded.StartDate = p.StartDate
    | Error e -> false

// ---- 3.10 URLs with special characters survive round-trip ----

[<Fact>]
let ``URL with query string and special chars round-trips`` () =
    let p = { validParams with SourceUrl = Uri("https://example.com/feed?a=1&b=foo%20bar&c=%3Ctest%3E") }
    match decode (encode p) with
    | Ok decoded -> Assert.Equal(p.SourceUrl, decoded.SourceUrl)
    | Error e    -> Assert.Fail(sprintf "Expected Ok, got %A" e)
