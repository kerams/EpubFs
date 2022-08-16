module EpubFs.Write

open Giraffe.ViewEngine
open System
open System.IO
open System.IO.Compression

module private Package =
    let item href id typ =
        voidTag "item" [ _href href; _id id; attr "media-type" typ ]

    let itemNav href typ =
        voidTag "item" [ _href href; _id "nav"; attr "properties" "nav"; attr "media-type" typ ]

    let itemCoverImage typ =
        voidTag "item" [ _href "cover"; _id "cover-img"; attr "properties" "cover-image"; attr "media-type" typ ]

    let itemRef idref linear =
        voidTag "itemref" [ attr "idref" idref; attr "linear" (if linear then "yes" else "no") ]

    let package metadata manifest =
        let modifiedAt = Option.defaultWith (fun () -> DateTimeOffset.UtcNow) metadata.ModifiedAt
        let contentFiles = List.indexed manifest.ContentFiles 

        tag "package" [ attr "xmlns" "http://www.idpf.org/2007/opf"; attr "xmlns:dc" "http://purl.org/dc/elements/1.1/"; attr "version" "3.0"; attr "unique-identifier" "id" ] [
            tag "metadata" [] [
                tag "dc:identifier" [ _id "id" ] [ str metadata.Id ]
                tag "dc:title" [] [ str metadata.Title ]

                for i, c in List.indexed metadata.Creators do
                    tag "dc:creator" [ _id $"creator{i + 1}" ] [ str c ]

                for l in metadata.Languages do
                    tag "dc:language" [] [ rawText l ]

                tag "meta" [ _property "dcterms:modified" ] [ rawText (modifiedAt.ToUniversalTime().ToString "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'") ]

                match metadata.Source with
                | Some source -> tag "dc:source" [] [ str source ]
                | _ -> ()
            ]
            tag "manifest" [] [
                item manifest.TitlePage.FileName "title" "application/xhtml+xml"
                itemNav "_nav.xhtml" "application/xhtml+xml"

                match manifest.CoverImage with
                | Some x -> itemCoverImage x.MediaType
                | _ -> ()

                for index, x in contentFiles do
                    item x.FileName $"item{index + 1}" "application/xhtml+xml"

                for index, css in List.indexed manifest.CssFiles do
                    item css.FileName $"css{index + 1}" "text/css"
            ]
            tag "spine" [] [
                itemRef "title" false
                itemRef "nav" false

                for index, x in contentFiles do
                    itemRef $"item{index + 1}" (x.Navigation.IsSome && x.Navigation.Value = Linear)
            ]
        ]

module private Container =
    let container () =
        tag "container" [ attr "version" "1.0"; attr "xmlns" "urn:oasis:names:tc:opendocument:xmlns:container" ] [
            tag "rootfiles" [] [
                voidTag "rootfile" [ attr "media-type" "application/oebps-package+xml"; attr "full-path" "EPUB/package.opf" ]
            ]
        ]

[<AutoOpen>]
module private WriteInt =
    let xhtmlDoctype = rawText """<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">"""
    let xhtmlXmlns = attr "xmlns" "http://www.w3.org/1999/xhtml"

    let writeEntryStream (archive: ZipArchive) level name (inStream: Stream) =
        let e = archive.CreateEntry (name, level)
        use stream = e.Open ()
        inStream.CopyTo stream

    let writeEntryBytes (archive: ZipArchive) level name (bytes: byte[]) =
        use ms = new MemoryStream (bytes)
        writeEntryStream archive level name ms

    let generateNav manifest =
        html [ xhtmlXmlns; attr "xmlns:epub" "http://www.idpf.org/2007/ops" ] [
            head [] [
                title [] [ str "Table of Contents" ]
            ]
            body [] [
                nav [ attr "role" "doc-toc"; attr "epub:type" "toc"; _id "toc" ] [
                    h2 [] [ str "Table of Contents" ]
                    ol [] [
                        for x in manifest.ContentFiles do
                            if x.Navigation.IsSome then
                                li [] [ a [ _href x.FileName ] [ str x.Title ] ]
                    ]
                ]
            ]
        ]
        |> RenderView.AsBytes.xmlNode

    let renderXhtmlFromBody cssFiles title' body' =
        [
            xhtmlDoctype
            html [ xhtmlXmlns ] [
                head [] [
                    title [] [ str title' ]

                    for css: CssFile in cssFiles do
                        link [ _href css.FileName; _type "text/css"; _rel "stylesheet" ]
                ]
                body [] body'
            ]
        ]
        |> RenderView.AsBytes.xmlNodes

    let writeContainerEntry archive =
        Container.container ()
        |> RenderView.AsBytes.xmlNode
        |> writeEntryBytes archive CompressionLevel.Fastest "META-INF/container.xml"

    let writePackageEntry archive metadata manifest =
        Package.package metadata manifest
        |> RenderView.AsBytes.xmlNode
        |> writeEntryBytes archive CompressionLevel.Fastest "EPUB/package.opf"

let write (stream: Stream) metadata manifest =
    use archive = new ZipArchive (stream, ZipArchiveMode.Create, true)
    writeEntryBytes archive CompressionLevel.NoCompression "mimetype" (System.Text.Encoding.UTF8.GetBytes "application/epub+zip")
    writeContainerEntry archive
    writePackageEntry archive metadata manifest

    match manifest.NavXhtml with
    | Raw (Stream s) -> writeEntryStream archive CompressionLevel.Fastest "EPUB/_nav.xhtml" s
    | Autogenerated ->
        generateNav manifest
        |> writeEntryBytes archive CompressionLevel.Fastest "EPUB/_nav.xhtml"

    match manifest.CoverImage with
    | Some { Input = RawInput.Stream s } ->
        writeEntryStream archive CompressionLevel.NoCompression "EPUB/cover" s
    | _ -> ()

    for x in manifest.TitlePage :: manifest.ContentFiles do
        let name = "EPUB/" + x.FileName

        match x.Input with
        | ContentInput.Raw (Stream s) -> writeEntryStream archive CompressionLevel.Fastest name s
        | ContentInput.Structured body ->
            renderXhtmlFromBody manifest.CssFiles x.Title body
            |> writeEntryBytes archive CompressionLevel.Fastest name

    for css in manifest.CssFiles do
        match css.Input with
        | RawInput.Stream s -> writeEntryStream archive CompressionLevel.Fastest ("EPUB/" + css.FileName) s
