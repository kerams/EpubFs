[<AutoOpen>]
module EpubFs.Types

open Giraffe.ViewEngine
open System
open System.IO

type RawInput =
    | Stream of Stream

type ContentInput =
    | Raw of RawInput
    | Structured of XmlNode list

type NavInput =
    | Autogenerated
    | Raw of RawInput

type CoverImage = {
    Input: RawInput
    /// Media type of the image, such as "image/jpeg".
    MediaType: string }

type Metadata = {
    /// https://www.w3.org/publishing/epub3/epub-packages.html#sec-opf-dcidentifier
    Id: string 
    /// https://www.w3.org/publishing/epub3/epub-packages.html#sec-opf-dctitle
    Title: string
    /// At least one required, with the first one considered primary.
    /// https://www.w3.org/publishing/epub3/epub-packages.html#sec-opf-dclanguage
    Languages: string list
    /// `None` defaults to DateTimeOffset.UtcNow
    /// https://www.w3.org/publishing/epub3/epub-packages.html#last-modified-date
    ModifiedAt: DateTimeOffset option
    /// Optional.
    /// https://www.w3.org/publishing/epub3/epub-packages.html#sec-opf-dccreator
    Creators: string list
    /// https://www.w3.org/publishing/epub3/epub-packages.html#sec-opf-dcmes-optional-def
    Source: string option }

type CssFile = {
    FileName: string
    Input: RawInput }

/// https://www.w3.org/publishing/epub3/epub-packages.html#attrdef-itemref-linear
type Navigation =
    /// Content that contributes to the primary reading order and has to be read sequentially.
    | Linear
    /// Auxiliary content that enhances or augments the primary content and can be accessed out of sequence.
    | NonLinear

type Xhtml = {
    Title: string
    FileName: string
    /// Indicates whether this file content that contributes to the primary reading order and has to be read sequentially.
    /// Also, if the value is not `None` and the table of contents is to be autogenerated (`NavXhtml = Autogenerate`), this file will have an entry in the navigation document.
    Navigation: Navigation option
    Input: ContentInput }

type Manifest = {
    CoverImage: CoverImage option
    NavXhtml: NavInput
    TitlePage: Xhtml
    ContentFiles: Xhtml list
    CssFiles: CssFile list }
