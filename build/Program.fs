open System.Collections.Generic
open Fake.Core

let run workingDir fileName args =
    printfn $"CWD: %s{workingDir}"

    let fileName, args =
        if Environment.isUnix then
            fileName, args
        else
            "cmd", ("/C " + fileName + " " + args)

    CreateProcess.fromRawCommandLine fileName args
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.ensureExitCodeWithMessage $"'%s{workingDir}> %s{fileName} %s{args}' task failed"
    |> Proc.run
    |> ignore

let dotnet = "dotnet"
let cwd = "."
let tests = "EpubFs.Tests"
let sln = "EpubFs.slnx"

let targets = Dictionary<string, TargetParameter -> unit>()

let createTarget name run = targets.Add (name, run)

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
    | [||] ->
        runTarget "Build"
        0
    | [| targetName |] ->
        runTarget targetName
        0
    | otherwise ->
        printfn $"Unknown args %A{otherwise}"
        1