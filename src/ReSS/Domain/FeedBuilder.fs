module ReSS.Domain.FeedBuilder

open System
open System.IO
open System.ServiceModel.Syndication
open System.Xml

// Precondition: `items` must already be sorted oldest-first by PublishDate.
// The caller (getFeedHandler) is responsible for sorting before passing items in.
let buildFeed (sourceFeed: SyndicationFeed) (items: SyndicationItem list) (unlockedCount: int) (totalCount: int) : string =
    let outFeed = SyndicationFeed()
    outFeed.Title       <- TextSyndicationContent(sprintf "%s \u2014 %d/%d" sourceFeed.Title.Text unlockedCount totalCount)
    outFeed.Description <- sourceFeed.Description
    outFeed.Language    <- sourceFeed.Language
    for link in sourceFeed.Links do outFeed.Links.Add(link)
    outFeed.Items <- items

    use sw = new StringWriter()
    use writer = XmlWriter.Create(sw, XmlWriterSettings(Indent = false))
    let formatter = Rss20FeedFormatter(outFeed)
    formatter.WriteTo(writer)
    writer.Flush()
    sw.ToString()
