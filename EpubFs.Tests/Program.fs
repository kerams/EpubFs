﻿module Tests

open EpubFs
open EpubFs.Write
open Expecto
open Giraffe.ViewEngine
open System
open System.IO
open System.IO.Compression

let commonModifiedAt = DateTimeOffset (2010, 10, 10, 0, 0, 0, TimeSpan.Zero) |> Some

let compareEpubs (generated: Stream) (sample: Stream) =
    use generated = new ZipArchive (generated)
    use sample = new ZipArchive (sample)

    let generatedEntries = generated.Entries |> Seq.sortBy (fun x -> x.FullName) |> Seq.toArray
    let sampleEntries = sample.Entries |> Seq.sortBy (fun x -> x.FullName) |> Seq.toArray

    for generated, sample in Array.zip generatedEntries sampleEntries do
        Expect.equal generated.FullName sample.FullName "Epubs contain files with different names"

        use generatedStream = generated.Open ()
        use sampleStream = sample.Open ()

        Expect.streamsEqual generatedStream sampleStream $"Generated and sample {sample.FullName} files are not equal"

let writeTests = testList "WriteTests" [
    test "No cover, auto nav, no css" {
        use generated = new MemoryStream ()
        let title = { FileName = "title.xhtml"; Title = "Title page"; Input = Structured [ h1 [] [ str "Odyssey" ] ]; Navigation = None }
        let p1 = { FileName = "p1.xhtml"; Title = "Page one"; Input = Structured [ div [] [ str "Ἄνδρα μοι ἔννεπε, Μοῦσα, πολύτροπον, ὃς μάλα πολλὰ" ]; div [] [ str "πλάγχθη, ἐπεὶ Τροίης ἱερὸν πτολίεθρον ἔπερσε·" ] ]; Navigation = Some Linear }
        let p2 = { FileName = "p2.xhtml"; Title = "Page two"; Input = Structured [ h2 [] [ str "yup again" ] ]; Navigation = Some Linear }

        let metadata = { Id = "54645-3231-54"; Title = "Ὀδύσσεια"; Languages = [ "en"; "grc" ]; ModifiedAt = commonModifiedAt; Source = None; Creators = [ "Ὅμηρος" ] }
        write generated metadata { CoverImage = None; TitlePage = title; NavXhtml = Autogenerated; ContentFiles = [ p1; p2 ]; CssFiles = [] }
        generated.Position <- 0L
        
        use sample = File.OpenRead "samples/No cover, auto nav, no css.epub"
        
        compareEpubs generated sample
    }
]

[<EntryPoint>]
let main args =
    runTestsWithCLIArgs [] args writeTests