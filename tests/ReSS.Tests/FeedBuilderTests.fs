module ReSS.Tests.FeedBuilderTests

open System
open System.ServiceModel.Syndication
open System.Xml
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open ReSS.Domain.FeedBuilder

// ---- helpers ----

let private makeItem (title: string) (pubDate: DateTimeOffset) =
    let item = SyndicationItem()
    item.Title    <- TextSyndicationContent(title)
    item.PublishDate <- pubDate
    item

let private baseFeed () =
    let feed = SyndicationFeed()
    feed.Title       <- TextSyndicationContent("My Feed")
    feed.Description <- TextSyndicationContent("A description")
    feed.Language    <- "en"
    feed.Links.Add(SyndicationLink(Uri("https://example.com")))
    feed

let private items () = [
    makeItem "Item A" (DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero))
    makeItem "Item B" (DateTimeOffset(2024, 1, 5,  0, 0, 0, TimeSpan.Zero))
    makeItem "Item C" (DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero))
]

// ---- 7.1 basic unit tests ----

[<Fact>]
let ``output is valid XML`` () =
    let xml = buildFeed (baseFeed()) (items()) 3 5
    use reader = XmlReader.Create(new IO.StringReader(xml))
    while reader.Read() do ()  // throws if invalid XML

[<Fact>]
let ``output is parseable as RSS 2.0`` () =
    let xml = buildFeed (baseFeed()) (items()) 3 5
    use reader = XmlReader.Create(new IO.StringReader(xml))
    let feed = SyndicationFeed.Load(reader)
    Assert.NotNull(feed)

[<Fact>]
let ``title contains n/t progress indicator`` () =
    let xml = buildFeed (baseFeed()) (items()) 3 20
    Assert.Contains("3/20", xml)

[<Fact>]
let ``source feed metadata is preserved`` () =
    let xml = buildFeed (baseFeed()) (items()) 3 5
    use reader = XmlReader.Create(new IO.StringReader(xml))
    let feed = SyndicationFeed.Load(reader)
    Assert.Contains("description", feed.Description.Text.ToLower())
    Assert.Equal("en", feed.Language)

[<Fact>]
let ``item count matches slice`` () =
    let xml = buildFeed (baseFeed()) (items()) 3 5
    use reader = XmlReader.Create(new IO.StringReader(xml))
    let feed = SyndicationFeed.Load(reader)
    Assert.Equal(3, feed.Items |> Seq.length)

[<Fact>]
let ``zero items when slice is empty`` () =
    let xml = buildFeed (baseFeed()) [] 0 5
    use reader = XmlReader.Create(new IO.StringReader(xml))
    let feed = SyndicationFeed.Load(reader)
    Assert.Equal(0, feed.Items |> Seq.length)

// ---- 7.3 oldest-first ordering (caller's responsibility to pre-sort) ----

[<Fact>]
let ``items in output are oldest-first when pre-sorted input is provided`` () =
    // buildFeed precondition: items must be pre-sorted oldest-first by the caller
    let sorted = items() |> List.sortBy (fun i -> i.PublishDate)
    let xml = buildFeed (baseFeed()) sorted 3 5
    use reader = XmlReader.Create(new IO.StringReader(xml))
    let feed = SyndicationFeed.Load(reader)
    let dates = feed.Items |> Seq.map (fun i -> i.PublishDate) |> Seq.toList
    Assert.Equal<DateTimeOffset list>(dates |> List.sort, dates)

// ---- 7.4 FsCheck property: buildFeed preserves the order it receives ----

type ItemListGen =
    static member Items() =
        gen {
            let! n = Gen.choose (0, 20)
            return!
                Gen.listOfLength n (gen {
                    let! days = Gen.choose (0, 3650)
                    let dt = DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(float days)
                    let item = makeItem "x" dt
                    return item
                })
        } |> Arb.fromGen

[<Fact>]
let ``oldest-first ordering holds when pre-sorted input is provided (property)`` () =
    let prop (itemList: SyndicationItem list) =
        // Pre-sort before passing — matches the handler's contract
        let sorted = itemList |> List.sortBy (fun i -> i.PublishDate)
        let xml = buildFeed (baseFeed()) sorted sorted.Length (sorted.Length + 5)
        use reader = XmlReader.Create(new IO.StringReader(xml))
        let feed = SyndicationFeed.Load(reader)
        let dates = feed.Items |> Seq.map (fun i -> i.PublishDate) |> Seq.toList
        dates = (dates |> List.sort)
    Prop.forAll (ItemListGen.Items()) prop |> Check.QuickThrowOnFailure
