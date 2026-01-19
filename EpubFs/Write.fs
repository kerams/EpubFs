namespace EpubFs

open Giraffe.ViewEngine
open System
open System.Globalization
open System.IO
open System.IO.Compression

[<AutoOpen>]
module internal Internal =
    let smilTimeStamp (x: Choice<string, TimeSpan>) =
        match x with
        | Choice1Of2 s -> s
        | Choice2Of2 ts ->
            ts.ToString ((if ts.Milliseconds = 0 then @"hh\:mm\:ss" else @"hh\:mm\:ss\.fff"), CultureInfo.InvariantCulture)

    let xmlDecl = rawText """<?xml version="1.0" encoding="UTF-8"?>"""
    let xhtmlDoctype = rawText "<!DOCTYPE html>"

    let _refines value = attr "refines" value

    let item href id typ hasSmil =
        voidTag "item" [
            _href href
            _id id
            attr "media-type" typ

            if hasSmil then
                attr "media-overlay" $"{id}_smil"
        ]

    let itemNav href typ =
        voidTag "item" [ _href href; _id "nav"; attr "properties" "nav"; attr "media-type" typ ]

    let itemCoverImage ext typ =
        voidTag "item" [ _href ("cover" + ext); _id "cover-img"; attr "properties" "cover-image"; attr "media-type" typ ]

    let itemRef idref linear =
        voidTag "itemref" [ attr "idref" idref; attr "linear" (if linear then "yes" else "no") ]

    let package metadata manifest =
        let modifiedAt = Option.defaultWith (fun () -> DateTimeOffset.UtcNow) metadata.ModifiedAt
        let contentFiles = Seq.indexed manifest.ContentFiles |> Seq.toArray

        [
            xmlDecl
            tag "package" [
                attr "xmlns" "http://www.idpf.org/2007/opf"
                attr "xmlns:dc" "http://purl.org/dc/elements/1.1/"
                attr "prefix" "media: http://www.idpf.org/epub/vocab/overlays/#"
                attr "version" "3.0"
                attr "unique-identifier" "id"
            ] [
                tag "metadata" [] [
                    tag "dc:identifier" [ _id "id" ] [ str metadata.Id ]
                    tag "dc:title" [] [ str metadata.Title ]

                    for i, c in Seq.indexed metadata.Creators do
                        tag "dc:creator" [ _id $"creator{i + 1}" ] [ str c ]

                    for l in metadata.Languages do
                        tag "dc:language" [] [ rawText l ]

                    tag "meta" [ _property "dcterms:modified" ] [ rawText (modifiedAt.ToUniversalTime().ToString ("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) ]

                    match metadata.Source with
                    | Some source -> tag "dc:source" [] [ str source ]
                    | _ -> ()

                    match metadata.Description with
                    | Some desc -> tag "dc:description" [] [ str desc ]
                    | _ -> ()

                    match metadata.Publisher with
                    | Some pub -> tag "dc:publisher" [] [ str pub ]
                    | _ -> ()

                    for subj in metadata.Subjects do
                        tag "dc:subject" [] [ str subj ]

                    match metadata.Rights with
                    | Some rights -> tag "dc:rights" [] [ str rights ]
                    | _ -> ()

                    match metadata.MediaOverlay with
                    | Some mo ->
                        tag "meta" [ _property "media:duration" ] [ rawText (smilTimeStamp mo.TotalDuration) ]

                        match manifest.TitlePage.Smil with
                        | Some s -> tag "meta" [ _property "media:duration"; _refines "#title_smil" ] [ rawText (smilTimeStamp s.Duration) ]
                        | _ -> ()

                        for index, x in contentFiles do
                            match x.Smil with
                            | Some s -> tag "meta" [ _property "media:duration"; _refines $"#item{index + 1}_smil" ] [ rawText (smilTimeStamp s.Duration) ]
                            | _ -> ()

                        match mo.ActiveClass with
                        | Some cls -> tag "meta" [ _property "media:active-class" ] [ str cls ]
                        | _ -> ()

                        match mo.PlaybackActiveClass with
                        | Some cls -> tag "meta" [ _property "media:playback-active-class" ] [ str cls ]
                        | _ -> ()

                        for narrator in mo.Narrators do
                            tag "meta" [ _property "media:narrator" ] [ str narrator ]
                    | _ -> ()
                ]
                tag "manifest" [] [
                    item manifest.TitlePage.FileName "title" "application/xhtml+xml" manifest.TitlePage.Smil.IsSome
                    itemNav "_nav.xhtml" "application/xhtml+xml"

                    match manifest.CoverImage with
                    | Some x -> itemCoverImage x.Extension x.MediaType
                    | _ -> ()

                    for index, x in contentFiles do
                        item x.FileName $"item{index + 1}" "application/xhtml+xml" x.Smil.IsSome

                    if manifest.TitlePage.Smil.IsSome then
                        item $"{manifest.TitlePage.FileName}.smil" "title_smil" "application/smil+xml" false

                    for index, x in contentFiles do
                        if x.Smil.IsSome then
                            item $"{x.FileName}.smil" $"item{index + 1}_smil" "application/smil+xml" false

                    for index, x in Seq.indexed manifest.OtherFiles do
                        item x.FileName $"other{index + 1}" x.MediaType false

                    for index, css in Seq.indexed manifest.CssFiles do
                        item css.FileName $"css{index + 1}" "text/css" false
                ]
                tag "spine" [] [
                    itemRef "title" true
                    itemRef "nav" true

                    for index, x in contentFiles do
                        itemRef $"item{index + 1}" (x.Navigation.IsSome && x.Navigation.Value = Linear)
                ]
            ]
        ]

    let writeEntryStream (archive: ZipArchive) level name (inStream: Stream) = backgroundTask {
        let e = archive.CreateEntry (name, level)
        use stream = e.Open ()
        do! inStream.CopyToAsync stream
    }

    let writeEntryBytes archive level name (bytes: byte[]) =
        new MemoryStream (bytes)
        |> writeEntryStream archive level name

    let generateNav manifest =
        let firstContentFile =
            manifest.ContentFiles
            |> Seq.tryPick (fun x -> if x.Navigation.IsSome then Some x.FileName else None)
            |> Option.defaultValue manifest.TitlePage.FileName

        [
            xhtmlDoctype
            html [ attr "xmlns" "http://www.w3.org/1999/xhtml"; attr "xmlns:epub" "http://www.idpf.org/2007/ops" ] [
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
                    nav [ attr "epub:type" "landmarks"; attr "hidden" "" ] [
                        h2 [] [ str "Landmarks" ]
                        ol [] [
                            li [] [ a [ attr "epub:type" "toc"; _href "_nav.xhtml#toc" ] [ str "Table of Contents" ] ]
                            li [] [ a [ attr "epub:type" "bodymatter"; _href firstContentFile ] [ str "Start of Content" ] ]
                        ]
                    ]
                ]
            ]
        ]
        |> RenderView.AsBytes.xmlNodes

    let renderXhtmlFromBody cssFiles title' body' =
        [
            xhtmlDoctype
            html [ attr "xmlns" "http://www.w3.org/1999/xhtml"; attr "xmlns:epub" "http://www.idpf.org/2007/ops" ] [
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
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile media-type=\"application/oebps-package+xml\" full-path=\"EPUB/package.opf\" /></rootfiles></container>"B
        |> writeEntryBytes archive CompressionLevel.NoCompression "META-INF/container.xml"

    let writePackageEntry archive metadata manifest =
        package metadata manifest
        |> RenderView.AsBytes.xmlNodes
        |> writeEntryBytes archive CompressionLevel.Optimal "EPUB/package.opf"

module Write =
    let write (stream: Stream) metadata manifest: System.Threading.Tasks.Task = backgroundTask {
        use archive = new ZipArchive (stream, ZipArchiveMode.Create, true)
        do! writeEntryBytes archive CompressionLevel.NoCompression "mimetype" "application/epub+zip"B
        do! writeContainerEntry archive
        do! writePackageEntry archive metadata manifest

        match manifest.NavigationFile with
        | NavInput.Raw s -> do! writeEntryStream archive CompressionLevel.Optimal "EPUB/_nav.xhtml" s
        | Autogenerated ->
            do!
                generateNav manifest
                |> writeEntryBytes archive CompressionLevel.Optimal "EPUB/_nav.xhtml"

        match manifest.CoverImage with
        | Some { Input = s; Extension = ext } -> do! writeEntryStream archive CompressionLevel.NoCompression ("EPUB/cover" + ext) s
        | _ -> ()

        for x in seq { manifest.TitlePage; yield! manifest.ContentFiles } do
            let name = "EPUB/" + x.FileName

            do!
                match x.Input with
                | ContentInput.Raw s -> writeEntryStream archive CompressionLevel.Optimal name s
                | ContentInput.Structured body ->
                    renderXhtmlFromBody manifest.CssFiles x.Title (Seq.toList body)
                    |> writeEntryBytes archive CompressionLevel.Optimal name

            match x.Smil with
            | Some smil ->
                let smilName = "EPUB/" + x.FileName + ".smil"
            
                do!
                    match smil.Input with
                    | SmilInput.Raw s -> writeEntryStream archive CompressionLevel.Optimal smilName s
                    | SmilInput.Structured (head, body) ->
                        [
                            xmlDecl
                            tag "smil" [ attr "xmlns" "http://www.w3.org/ns/SMIL"; attr "xmlns:epub" "http://www.idpf.org/2007/ops"; attr "version" "3.0" ] [
                                tag "head" [] (Seq.toList head)
                                tag "body" [ attr "epub:textref" x.FileName ] (Seq.toList body)
                            ]
                        ]
                        |> RenderView.AsBytes.xmlNodes
                        |> writeEntryBytes archive CompressionLevel.Optimal smilName
                    | SmilInput.ParNodes (audioRef, parNodes) ->
                        [
                            xmlDecl
                            tag "smil" [ attr "xmlns" "http://www.w3.org/ns/SMIL"; attr "xmlns:epub" "http://www.idpf.org/2007/ops"; attr "version" "3.0" ] [
                                tag "body" [ attr "epub:textref" x.FileName ] [
                                    for p in parNodes do
                                        tag "par" [] [
                                            voidTag "text" [ attr "src" (x.FileName + p.TextFragmentReference) ]
                                            voidTag "audio" [ attr "src" audioRef; attr "clipBegin" (smilTimeStamp p.ClipBegin); attr "clipEnd" (smilTimeStamp p.ClipEnd) ]
                                        ]
                                ]
                            ]
                        ]
                        |> RenderView.AsBytes.xmlNodes
                        |> writeEntryBytes archive CompressionLevel.Optimal smilName
            | _ -> ()

        for x in manifest.OtherFiles do
            do! writeEntryStream archive (if x.Compress then CompressionLevel.Optimal else CompressionLevel.NoCompression) ("EPUB/" + x.FileName) x.Input

        for css in manifest.CssFiles do
            do! writeEntryStream archive CompressionLevel.Optimal ("EPUB/" + css.FileName) css.Input
    }