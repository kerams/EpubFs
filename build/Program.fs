open System
open System.Collections.Generic
open Fake.Core
open Fake.IO

let (</>) x y = System.IO.Path.Combine (x, y)

let run workingDir fileName args =
    printfn $"CWD: %s{workingDir}"

    let fileName, args =
        if Environment.isUnix then
            fileName, args
        else
            "cmd", ("/C " + fileName + " " + args)

    CreateProcess.fromRawCommandLine fileName args
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.withTimeout TimeSpan.MaxValue
    |> CreateProcess.ensureExitCodeWithMessage $"'%s{workingDir}> %s{fileName} %s{args}' task failed"
    |> Proc.run
    |> ignore

let dotnet = "dotnet"
let cwd = "."
let project = "EpubFs"
let tests = "EpubFs.Tests"
let sln = "EpubFs.sln"

let clean projectPath =
    Shell.cleanDirs [
      projectPath </> "bin"
      projectPath </> "obj"
    ]

let targets = Dictionary<string, TargetParameter -> unit>()

let createTarget name run = targets.Add (name, run)

createTarget "Publish" <| fun _ ->
    clean project
    run project dotnet "pack -c Release"

    let nugetKey =
        match Environment.environVarOrNone "NUGET_KEY" with
        | Some nugetKey -> nugetKey
        | None -> failwith "The Nuget API key must be set in a NUGET_KEY environmental variable"

    let nupkg = System.IO.Directory.GetFiles (project </> "bin" </> "Release") |> Seq.head
    let pushCmd = $"nuget push %s{nupkg} -s nuget.org -k %s{nugetKey}"
    run project dotnet pushCmd

createTarget "Build" <| fun _ ->
    run cwd "dotnet" $"build {sln}"

createTarget "Test" <| fun _ ->
    run tests "dotnet" "run"
    
let runTarget targetName =
    if targets.ContainsKey targetName then
        let input = Unchecked.defaultof<TargetParameter>
        targets[targetName] input
    else
        printfn $"Could not find build target {targetName}"

[<EntryPoint>]
let main (args: string[]) =
    match args with
    | [||] -> runTarget "Build"
    | [| targetName |] -> runTarget targetName
    | otherwise -> printfn $"Unknown args %A{otherwise}"
    0