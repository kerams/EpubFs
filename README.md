# EpubFs

EPUB authoring library for F#.

## Nuget package
[![Nuget](https://img.shields.io/nuget/v/EpubFs.svg?colorB=green)](https://www.nuget.org/packages/EpubFs)

## Project status

This library has been extracted from a personal project of mine and represents the bare minimum necessary to create EPUB files for my purposes. As such, I do not intend to continue developing any new major features.

However, if you or your company have a need for a particular feature or require that some other part of the EPUB spec be implemented, and you are willing to pay for the development, I would be more than happy to work with you!

Bug reports and PRs will naturally still be looked at.

## Usage

In order to create an EPUB, you need to supply some metadata and a file manifest including the title page, list of files with the actual book content as well as any CSS files you want to use.

### Content files

Content files are accepted in the form of `XmlNode list`, which you can construct using the [`Giraffe.ViewEngine` DSL](https://github.com/giraffe-fsharp/Giraffe.ViewEngine), or as a `Stream` containing an XHTML that you have obtained in some other way.

### CSS files

You can also include stylesheets to be used in the book. If a content file is provided as `XmlNode list`, every CSS file will automatically be referenced therein. On the other hand, if you're supplying a full XHTML file, you need to import the stylesheet (in the traditional HTML fashion) yourself by using the `FileName` of a matching CSS file.

### Table of contents (navigation document)

Every EPUB requires a navigation document. There are two choices here - either you supply an XHTML (it has to conform to the navigation spec!) directly, or you let the library generate a simple table of contents page with hyperlinks to each of your content files (whose `Navigation` is not set to `None`).

### Media overlays (SMIL)

Starting from v0.4 the library supports [EPUB Media Overlays](https://www.w3.org/TR/epub-33/#sec-media-overlays), which allow you to synchronize audio narration with text content using SMIL files.

To add media overlays:

1. Include your audio files in the manifest's `OtherFiles` list
2. For each content file that should have audio synchronization, set the `Smil` property with a `SmilFile` record
3. Add `MediaOverlay` metadata to your `Metadata` record, including the total duration

SMIL content can be provided in three ways:
- `SmilInput.Raw` - A pre-built SMIL file as a stream
- `SmilInput.Structured` - A list of `XmlNode` representing the SMIL head and body content
- `SmilInput.ParNodes` - A simplified list of `ParNode` records, where each generates a `<par>` element with text and audio synchronization

For the sake of simplicity, the API assumes that every SMIL file references exactly one audio file (but many SMIL files can reference the same audio file) despite the fact that the spec allows references to multiple audio files. 

### Example

```fsharp
open EpubFs
open Giraffe.ViewEngine
open System.IO
open System.Text

let metadata = {
    // A unique book identifier, could be ISBN or URL...
    Id = "my-book"
    Title = "Ὀδύσσεια"
    // List of language tags - you should use ISO 639-2
    // If you specify more than one, the first is considered the primary language of the book
    Languages = [ "grc" ]
    // `None` defaults to DateTimeOffset.UtcNow
    ModifiedAt = None
    Source = Some "https://el.wikisource.org/wiki/%CE%9F%CE%B4%CF%8D%CF%83%CF%83%CE%B5%CE%B9%CE%B1"
    Creators = [ "John Doe"; "Ὅμηρος" ]
    Description = Some "The epic tale of Odysseus's journey home."
    Publisher = Some "Ancient Greek Press"
    Subjects = [ "Epic Poetry"; "Greek Literature"; "Mythology" ]
    Rights = Some "Public Domain"
    // Media overlay metadata - required when using SMIL
    MediaOverlay = Some {
        TotalDuration = Choice2Of2 (TimeSpan.FromSeconds 5.0)
        ActiveClass = Some "-epub-media-overlay-active"
        PlaybackActiveClass = None
        Narrators = [ "Narrator Name" ]
    }
}

// The CSS Stream you would like to use throughout the book
let css = new MemoryStream (".red-font { color: red }"B)

// Audio file for narration
use audioStream = File.OpenRead "chapter1.mp3"

// Let's declare the title page document using `Giraffe.ViewEngine`
// Use the `red-font` class from our stylesheet
let titlePage = {
    FileName = "title.xhtml"
    Title = "Title"
    Input = Structured [
        h1 [ _class "red-font" ] [
            str "Ὀδύσσεια"
        ]
    ]
    Navigation = None
    Smil = None
}

// We already have a pre-built XHTML of the first content document, so pass it as a stream
// Notice the manual reference to our stylesheet
use chapter1Stream =
    """<!DOCTYPE html><html xmlns="http://www.w3.org/1999/xhtml"><head><link href="main.css" type="text/css" rel="stylesheet"/><title>Page one</title></head><body><div id="line1" class="red-font">Ἄνδρα μοι ἔννεπε, Μοῦσα, πολύτροπον, ὃς μάλα πολλὰ</div><div id="line2">πλάγχθη, ἐπεὶ Τροίης ἱερὸν πτολίεθρον ἔπερσε·</div></body></html>"""
    |> Encoding.UTF8.GetBytes
    |> MemoryStream

let chapter1 = {
    FileName = "chapter1.xhtml"
    Title = "Chapter 1"
    Input = ContentInput.Raw page1Stream
    // Setting navigation to `Some` so that this document appears in the table of contents
    Navigation = Some Linear
    // Associate a SMIL file for audio synchronization
    // Notice references to div IDs of the chapter file above, and audio references to the file added in the manifest below
    Smil = Some {
        Duration = Choice2Of2 (TimeSpan.FromSeconds 5.0)
        Input = SmilInput.ParNodes ("chapter1.mp3", [
            {
                TextFragmentReference = "#line1"
                ClipBegin = Choice2Of2 TimeSpan.Zero
                ClipEnd = Choice2Of2 (TimeSpan.FromSeconds 2.5)
            }
            {
                TextFragmentReference = "#line2"
                ClipBegin = Choice1Of2 "00:00:02.500"
                ClipEnd = Choice2Of2 (TimeSpan.FromSeconds 5.0)
            }
        ])
    }
}

let manifest = {
    // Usually used by e-book readers as the book thumbnail
    CoverImage = None
    // Table of contents will be generated for us
    NavigationFile = Autogenerated
    TitlePage = titlePage
    ContentFiles = [ chapter1 ]
    CssFiles = [
        // The file will be stored in the archive under this name
        // It's also what you use to reference the stylesheet in XHTML
        { FileName = "main.css"; Input = css }
    ]
    OtherFiles = [
        { FileName = "chapter1.mp3"; MediaType = "audio/mpeg"; Input = audioStream; Compress = false }
    ]
}

use fs = File.OpenWrite "odyssey.epub"
do! Write.write fs metadata manifest
```
