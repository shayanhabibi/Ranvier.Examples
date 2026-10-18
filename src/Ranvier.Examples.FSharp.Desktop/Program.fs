module Ranvier.Examples.FSharp.Desktop.Program

open System
open Avalonia

[<EntryPoint; STAThread>]
let main args =
    AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime (args)
