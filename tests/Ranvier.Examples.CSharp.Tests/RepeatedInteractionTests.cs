using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using Ranvier.Examples.Workflows.Desktop;

namespace Ranvier.Examples.CSharp.Tests;

public sealed class RepeatedInteractionTests
{
    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Distinct().Single(control => control.Name == name);
    private static async Task Until(Func<bool> condition, string label)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(), label);
    }
    private static void Press(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var button = Find<Button>(window, name);
        Assert.True(button.IsEffectivelyEnabled, name + " should be enabled");
        Assert.True(button.Focus(), name + " should accept focus");
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }
    private static WorkflowWindow Create() => new(Path.Combine(AppContext.BaseDirectory, "repeat-ui", Guid.NewGuid().ToString("N")));

    [AvaloniaFact]
    public async Task SupportErrorBannerClearsAfterConnectionRecovery()
    {
        var window = Create(); window.Show();
        try
        {
            await window.Ready;
            Find<ListBox>(window, "SupportQueue").SelectedIndex = 0;
            await Until(() => window.Support.Model.SelectedId == 1, "Ticket opens");
            Press(window, "SupportConnection");
            await Until(() => window.Support.LastError.Length != 0, "Disconnect displays its error");
            Press(window, "SupportConnection");
            await Until(() => !window.Support.Model.QueueStatus.Contains("Loading") && !window.Support.Model.QueueStatus.Contains("Retaining"), "Queue recovers");
            Assert.Empty(window.Support.LastError);
            Press(window, "SupportConnection");
            await Until(() => window.Support.LastError.Length != 0, "Second disconnect displays its error");
            Press(window, "SupportExternal");
            await Until(() => !window.Support.ExternalUpdate.IsRunning && window.Support.ExternalUpdate.Error is not null, "Offline command fails visibly");
            Press(window, "SupportConnection");
            await Until(() => !window.Support.Model.QueueStatus.Contains("Loading") && !window.Support.Model.QueueStatus.Contains("Retaining"), "Queue recovers after command failure");
            Assert.Empty(window.Support.LastError);
        }
        finally { window.Close(); await window.Stopped; }
    }

    [AvaloniaFact]
    public async Task SupportCanSaveRefreshBackAndReopenForThreeRounds()
    {
        var window = Create(); window.Show();
        try
        {
            await window.Ready;
            for (int round = 1; round <= 3; round++)
            {
                Find<ListBox>(window, "SupportQueue").SelectedIndex = 0;
                await Until(() => window.Support.Model.SelectedId == 1, "Ticket should open");
                Find<TextBox>(window, "SupportDraft").Text = "Investigation round " + round;
                Press(window, "SupportSave");
                await Until(() => !window.Support.Save.IsRunning, "Save command should settle");
                Assert.False(window.Support.Model.IsDirty, window.Support.Model.Message);
                Assert.Contains(window.Support.Model.Queue, row => row.Id == 1 && row.Notes == "Investigation round " + round);
                Press(window, "SupportRefresh");
                await Until(() => !window.Support.Refresh.IsRunning, "Refresh should settle");
                Assert.Equal(1, window.Support.Model.SelectedId);
                Assert.Equal(0, Find<ListBox>(window, "SupportQueue").SelectedIndex);
                Press(window, "SupportBack");
                Assert.Equal(0, window.Support.Model.SelectedId);
                Assert.Equal(-1, Find<ListBox>(window, "SupportQueue").SelectedIndex);
            }
        }
        finally { window.Close(); await window.Stopped; }
    }

    [AvaloniaFact]
    public async Task JobSelectionSurvivesProgressCompletionAndRetry()
    {
        var window = Create(); window.Show();
        try
        {
            await window.Ready; window.Tabs.SelectedIndex = 1;
            string input = Path.Combine(window.DataDirectory, "large.bin");
            await File.WriteAllBytesAsync(input, new byte[8 * 1024 * 1024], TestContext.Current.CancellationToken);
            Find<TextBox>(window, "JobPaths").Text = input;
            Press(window, "QueueFiles");
            Find<ListBox>(window, "FileJobs").SelectedIndex = 0;
            int id = window.Jobs.Model.SelectedId;
            Assert.NotEqual(0, id);
            await window.Jobs.Model.Completion;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(id, window.Jobs.Model.SelectedId);
            Assert.Equal(0, Find<ListBox>(window, "FileJobs").SelectedIndex);
            for (int round = 0; round < 3; round++)
            {
                Press(window, "QueueFiles");
                await window.Jobs.Model.Completion;
                Assert.Equal(id, window.Jobs.Model.SelectedId);
            }
            string missing = Path.Combine(window.DataDirectory, "retry.txt");
            Find<TextBox>(window, "JobPaths").Text = missing;
            Press(window, "QueueFiles");
            await window.Jobs.Model.Completion;
            Find<ComboBox>(window, "JobFilter").SelectedItem = "Failed";
            Find<ListBox>(window, "FileJobs").SelectedIndex = 0;
            int retryId = window.Jobs.Model.SelectedId;
            Assert.NotEqual(id, retryId);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Press(window, "RetryJob");
                await window.Jobs.Model.Completion;
                Assert.Equal(retryId, window.Jobs.Model.SelectedId);
                Assert.Equal(0, Find<ListBox>(window, "FileJobs").SelectedIndex);
            }
            await File.WriteAllTextAsync(missing, "Repaired input", TestContext.Current.CancellationToken);
            Press(window, "RetryJob");
            await window.Jobs.Model.Completion;
            Assert.Equal(retryId, window.Jobs.Model.SelectedId);
            Assert.Equal(-1, Find<ListBox>(window, "FileJobs").SelectedIndex);
            Find<ComboBox>(window, "JobFilter").SelectedItem = "All";
            Assert.Equal(4, Find<ListBox>(window, "FileJobs").SelectedIndex);
            Assert.Equal(5, window.Jobs.Model.Succeeded);
        }
        finally { window.Close(); await window.Stopped; }
    }

    [AvaloniaFact]
    public async Task DataSelectionSurvivesRepeatedEditsAndFilteredRepairs()
    {
        var window = Create(); window.Show();
        try
        {
            await window.Ready; window.Tabs.SelectedIndex = 2;
            var list = Find<ListBox>(window, "MeasurementRows");
            list.SelectedIndex = 0;
            for (int round = 0; round < 3; round++)
            {
                Find<TextBox>(window, "MeasurementValue").Text = (22 + round).ToString();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, window.Data.Model.SelectedId);
                Assert.Equal(0, list.SelectedIndex);
                Press(window, "ToFahrenheit");
                Assert.Equal(1, window.Data.Model.SelectedId);
                Assert.Equal(0, list.SelectedIndex);
                Press(window, "ToCelsius");
                Assert.Equal(0, list.SelectedIndex);
                window.Tabs.SelectedIndex = 0;
                window.Tabs.SelectedIndex = 2;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Equal(0, list.SelectedIndex);
                Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == window.Data.Model.GetRow(1).Display);
            }
            Find<ComboBox>(window, "CsvFilter").SelectedItem = "Invalid";
            list.SelectedIndex = 0;
            Assert.Equal(4, window.Data.Model.SelectedId);
            Find<TextBox>(window, "MeasurementValue").Text = "19";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, window.Data.Model.SelectedId);
            Assert.Equal(-1, list.SelectedIndex);
            list.SelectedIndex = 0;
            Assert.Equal(5, window.Data.Model.SelectedId);
            Find<TextBox>(window, "MeasurementUnit").Text = "C";
            Assert.Equal(0, window.Data.Model.InvalidCount);
        }
        finally { window.Close(); await window.Stopped; }
    }

    [AvaloniaFact]
    public async Task ElmishCanRepeatReviewUndoRedoSaveAndReload()
    {
        var window = Create(); window.Show();
        try
        {
            await window.Ready; window.Tabs.SelectedIndex = 3;
            for (int round = 1; round <= 3; round++)
            {
                Find<ListBox>(window, "ExpenseRows").SelectedIndex = 1;
                Find<TextBox>(window, "ExpenseReason").Text = "Receipt missing " + round;
                Press(window, "ExpenseReject");
                Assert.Equal("Receipt missing " + round, window.Expenses.Model.Snapshot.Single(row => row.Claim.Id == 2).Reason);
                Assert.Equal(1, Find<ListBox>(window, "ExpenseRows").SelectedIndex);
                window.Tabs.SelectedIndex = 0;
                window.Tabs.SelectedIndex = 3;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Equal(1, Find<ListBox>(window, "ExpenseRows").SelectedIndex);
                Assert.Contains(Find<ListBox>(window, "ExpenseRows").GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("Grace") == true && text.Text.Contains("Rejected"));
                Press(window, "ExpenseUndo"); Press(window, "ExpenseRedo");
                Press(window, "ExpenseSave");
                await Until(() => !window.Expenses.Model.IsSaving && !window.Expenses.Model.IsDirty, "Expense save should settle");
                Press(window, "ExpenseLoad");
                await Until(() => !window.Expenses.Model.IsLoading, "Expense reload should settle");
                Assert.All(window.Expenses.Model.Snapshot, row => Assert.Equal("Pending", row.Decision));
                Assert.False(window.Expenses.Model.CanUndo);
                Assert.Equal(-1, Find<ListBox>(window, "ExpenseRows").SelectedIndex);
            }
        }
        finally { window.Close(); await window.Stopped; }
    }
}
