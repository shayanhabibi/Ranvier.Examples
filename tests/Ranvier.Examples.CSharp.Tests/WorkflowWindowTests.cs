using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Ranvier.Examples.Workflows.Desktop;
using Xunit;

namespace Ranvier.Examples.CSharp.Tests;

public sealed class WorkflowWindowTests
{
    private static void Press(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var button = Find<Button>(window, name);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus(), $"Could not focus {name}; visible={button.IsEffectivelyVisible}, bounds={button.Bounds}");
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }
    [AvaloniaFact]
    public async Task ElmishTabReviewsUndoesAndSavesThroughControls()
    {
        var window = new WorkflowWindow(Path.Combine(AppContext.BaseDirectory, "ui-workflows", Guid.NewGuid().ToString("N")));
        window.Show();
        try
        {
            await window.Ready;
            window.Tabs.SelectedIndex = 3;
            await Until(() => window.Expenses.Model.Snapshot.Length == 3);
            Find<ListBox>(window, "ExpenseRows").SelectedIndex = 0;
            Press(window, "ExpenseApprove");
            await Until(() => window.Expenses.Model.ApprovedTotal == 86m);
            Press(window, "ExpenseUndo");
            await Until(() => window.Expenses.Model.ApprovedTotal == 0m);
            Press(window, "ExpenseRedo");
            Find<ListBox>(window, "ExpenseRows").SelectedIndex = 1;
            Find<TextBox>(window, "ExpenseReason").Text = "Please supply a receipt";
            Press(window, "ExpenseReject");
            await Until(() => window.Expenses.Model.SelectedDecision == "Rejected");
            Press(window, "ExpenseSave");
            await Until(() => !window.Expenses.Model.IsDirty && !window.Expenses.Model.IsSaving);
            Assert.True(File.Exists(Path.Combine(window.DataDirectory, "expense-decisions.json")));
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(AppContext.BaseDirectory, "workflows-elmish.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); await window.Stopped; }
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Distinct().Single(control => control.Name == name);
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(), "The workflow did not reach its expected state.");
    }

    [AvaloniaFact]
    public async Task SupportRefreshKeepsDraftAndSaveAfterBackUpdatesQueue()
    {
        var window = new WorkflowWindow(Path.Combine(AppContext.BaseDirectory, "ui-workflows", Guid.NewGuid().ToString("N")));
        window.Show();
        try
        {
            await window.Ready;
            Assert.True(Find<Button>(window, "CsvReload").IsEffectivelyEnabled);
            Find<ListBox>(window, "SupportQueue").SelectedIndex = 0;
            await Until(() => window.Support.Model.SelectedId == 1);
            Find<TextBox>(window, "SupportDraft").Text = "Reviewed from the UI";
            await Until(() => window.Support.Model.DraftNotes == "Reviewed from the UI");
            await window.ApplyExternalUpdateAsync();
            await window.Support.Model.RefreshAsync();
            Assert.Equal("Reviewed from the UI", window.Support.Model.DraftNotes);
            Assert.True(window.Support.Model.HasConflict);
            window.Support.Model.Rebase();
            var save = window.Support.Model.SaveAsync();
            window.Support.Model.Back();
            Assert.True(await save);
            Assert.Contains(window.Support.Model.Queue, ticket => ticket.Id == 1 && ticket.Notes == "Reviewed from the UI");
            Assert.Equal(0, window.Support.Model.SelectedId);
        }
        finally { window.Close(); await window.Stopped; }
    }

    [AvaloniaFact]
    public async Task FileViewProcessesSamplesAndDataViewRepairsAndExports()
    {
        var window = new WorkflowWindow(Path.Combine(AppContext.BaseDirectory, "ui-workflows", Guid.NewGuid().ToString("N")));
        window.Show();
        try
        {
            await window.Ready;
            window.Jobs.QueueSamples();
            await window.Jobs.Model.Completion;
            Assert.Equal(3, window.Jobs.Model.Succeeded);
            Assert.Equal(2, window.Data.Model.InvalidCount);
            window.Tabs.SelectedIndex = 2;
            Find<ListBox>(window, "MeasurementRows").SelectedIndex = 3;
            Find<TextBox>(window, "MeasurementValue").Text = "19";
            await Until(() => window.Data.Model.GetRow(4).RawText == "19");
            Find<ListBox>(window, "MeasurementRows").SelectedIndex = 4;
            Find<TextBox>(window, "MeasurementUnit").Text = "C";
            await Until(() => window.Data.Model.GetRow(5).Unit == "C");
            Assert.Equal(0, window.Data.Model.InvalidCount);
            Assert.Equal(20.26m, window.Data.Model.MeanCelsius);
            await window.Data.Model.ExportAsync(Path.Combine(window.DataDirectory, "repaired.csv"));
            Assert.True(File.Exists(Path.Combine(window.DataDirectory, "repaired.csv")));
            window.Data.Destination = Path.Combine(window.DataDirectory, "missing", "cannot-write.csv");
            window.Data.Export.Execute(null);
            await Until(() => window.Data.Export.Error is not null);
            Assert.NotEmpty(window.Data.LastError);
            window.Tabs.SelectedIndex = 2;
            await Until(() => window.Data.Model.ValidCount == 5);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(AppContext.BaseDirectory, "workflows-data.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); await window.Stopped; }
    }
}
