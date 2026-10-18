#r "nuget: Partas.Build, 0.4.0-alpha.3"
#r "nuget: Partas.TypeProvider.BuildHelper, 0.2.5"
#r "nuget: Fake.IO.FileSystem"
#r "nuget: Str"

open Partas.Build
open Partas.TypeProvider.BuildHelper
open Fake.IO.Globbing.Operators
open Fake.IO
open Str

[<Literal>]
let __REPOSITORY_DIRECTORY__ =
    __SOURCE_DIRECTORY__

type Repo = BuildHelperProvider<
    __REPOSITORY_DIRECTORY__,
    "bin/",
    capabilityFullOverride = true
>

let inline funApply value fn = fn value

module Spec =
    let formattingSourceFiles =
        !! "src/**/*.fs"
        ++ "tests/**/*.fs"
        ++ "build.fsx"
        -- "**/obj/**/*.*"
        -- "**/AssemblyInfo.fs"
        -- "**/fable-modules/**/*.*"

    let projects = Repo.Project.AllProjects()
    let sourceProjects = projects |> List.filter _.RelativePath.StartsWith("src")
    let testProjects = projects |> List.filter _.RelativePath.StartsWith("tests")
    let sourceProjectsMap =
        sourceProjects
        |> List.map (
            _.Name
            >> Str.replaceChar '.' '-'
            >> Str.toLower
            )
        |> List.zip
        |> funApply sourceProjects
        |> Map.ofList

module Options =
    let config =
        Baked.Input.DotNet.configString
        |> InputSpec.ofInput
        |> InputSpec.map (Option.defaultValue "Release")
    let quick =
        Input.option<bool> "--quick"
        |> Input.alias "-q"
        |> Input.desc "Skips installs and restores"

    let format =
        Input.option<bool> "--format"
        |> Input.alias "-f"
        |> Input.desc "Formats the source files prior to building/testing/publishing"

    let dryFormat =
        Input.option<bool> "--dry-format"
        |> Input.desc "Checks if the source files require formatting"

    let skipTests =
        Input.option<bool> "--skip-tests"
        |> Input.desc "Skips running the test suite"

    let watch =
        Input.option<bool> "--watch"
        |> Input.desc "Runs the operation in watch mode"




module Stage =
    let restore = input {
        let! quick = Options.quick
        return stage "restore" {
            quiet
            workingDir Repo.FileSystem.``.``
            when' (not quick)
            parallel'
            stage "restore solution" {
                run $"dotnet restore {Repo.Project.SolutionFile} -v q"
            }
            stage "restore tools" {
                run "dotnet tool restore -v q"
            }
        }
    }

    let clean = input {
        let! quick = Options.quick
        return stage "clean" {
            when' (not quick)
            run (async {
                !! "src/**/bin"
                ++ "tests/**/bin"
                |> Shell.cleanDirs
            })
        }
    }


    let format (formatInput: InputSpec<bool>) (dryRun: InputSpec<bool>) = input {
        let! format = formatInput
        and! dryRun = dryRun
        let commandString =
            Spec.formattingSourceFiles
            |> Seq.map (sprintf "\"%s\"")
            |> String.concat " "
            |> if dryRun
                then sprintf "dotnet fantomas %s --check"
                else sprintf "dotnet fantomas %s"
        return stage "format" {
            when' (format || dryRun)
            quiet
            run commandString
        }
    }

    let build = input {
        let! config = Options.config
        return stage "build" {
            quiet
            run (cmd $"dotnet build Ranvier.Examples.slnx -c {config} -v q")
        }
    }



    let runTests = input {
        let! config = Options.config
        and! skipTests = Options.skipTests
        return stage "run tests" {
            quiet
            when' (not skipTests)
            for { Name = name; Path = path } in Spec.testProjects do
            stage $"run {name}" {
                run (cmd $"dotnet test {path} -c {config} -v q")
            }
        }
    }

exit <| rootCommandOfScript {
    name "build.fsx"
    description "Build CLI"
    workingDir __REPOSITORY_DIRECTORY__
    command "build" {
        description "Builds the solution"
        Stage.restore
        Stage.build
    }
    command "test" {
        alias "tests"
        description "Runs the test suite"
        Stage.restore
        Stage.format (InputSpec.ofInput Options.format) (InputSpec.ofInput Options.dryFormat)
        Stage.runTests
    }
    command "format" {
        alias "apply-style"
        description "Formats the source files"
        Stage.restore
        Stage.format (InputSpec.ret true) (InputSpec.ofInput Options.dryFormat)
    }
}
