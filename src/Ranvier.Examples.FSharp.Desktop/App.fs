namespace Ranvier.Examples.FSharp.Desktop

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.Styling

/// <summary>Initializes the desktop theme and creates the order editor on the UI thread.</summary>
type App() =
    inherit Application()

    override this.Initialize() =
        this.RequestedThemeVariant <- ThemeVariant.Light
        this.Styles.Add (FluentTheme ())

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            if
                System.Environment.GetCommandLineArgs ()
                |> Array.contains "--intro"
            then
                desktop.MainWindow <- OrderWindow ()
            else
                desktop.MainWindow <- new Ranvier.Examples.Workflows.Desktop.WorkflowWindow ()
        | _ -> ()

        base.OnFrameworkInitializationCompleted ()
