using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ranvier;
using Ranvier.Examples.CSharp.Jobs;
using Ranvier.Examples.CSharp.Support;
using Ranvier.Examples.FSharp;

namespace Ranvier.Examples.Workflows.Desktop;

/// <summary>Real workflow cores hosted on one UI-thread graph with explicit shutdown.</summary>
public sealed class WorkflowWindow : Window
{
    private readonly Graph graph;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DataQualityModel dataModel;
    private readonly FileJobMonitor jobModel;
    private readonly ExpenseReview expenseModel;
    private SupportApi? api;
    private TicketHttpClient? http;
    private SupportDesk? supportModel;
    private bool closed;
    private readonly CancellationTokenSource lifetime = new();
    private Task initialization = Task.CompletedTask;
    public string DataDirectory { get; }
    public TabControl Tabs { get; }
    public SupportView Support { get; private set; } = null!;
    public JobsView Jobs { get; }
    public DataQualityView Data { get; }
    public ExpenseReviewView Expenses { get; }
    public Task Ready => ready.Task;
    public Task Stopped => stopped.Task;

    public WorkflowWindow(string? directory = null)
    {
        DataDirectory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ranvier.Examples", "workflows");
        Directory.CreateDirectory(DataDirectory);
        Title = "Ranvier · reactive workbench";
        Width = 1240; Height = 900; MinWidth = 980; MinHeight = 700;
        Background = Brush.Parse("#F3F5F7");
        graph = new Graph();
        using var active = graph.Activate();
        jobModel = new FileJobMonitor(graph, 2);
        dataModel = new DataQualityModel(graph, ReadCalibrationAsync);
        string expenseInput = Path.Combine(DataDirectory, "expense-claims.json");
        string expenseOutput = Path.Combine(DataDirectory, "expense-decisions.json");
        if (!File.Exists(expenseInput)) File.Copy(Path.Combine(AppContext.BaseDirectory, "samples", "expenses.json"), expenseInput);
        expenseModel = new ExpenseReview(graph, token => ExpenseFiles.LoadAsync(expenseInput, token),
            (rows, token) => ExpenseFiles.SaveAsync(expenseOutput, rows, token));
        Expenses = new ExpenseReviewView(graph, expenseModel, expenseInput, expenseOutput);
        string sample = Path.Combine(AppContext.BaseDirectory, "samples", "measurements.csv");
        string local = Path.Combine(DataDirectory, "measurements.csv");
        if (!File.Exists(local)) File.Copy(sample, local);
        Jobs = new JobsView(graph, jobModel);
        Data = new DataQualityView(graph, dataModel, local, Path.Combine(DataDirectory, "repaired.csv"));
        Tabs = new TabControl
        {
            ItemsSource = new[] {
                new TabItem { Header = "Support workbench", Content = Ui.Text("Starting the local ticket service...") },
                new TabItem { Header = "File processing", Content = Jobs },
                new TabItem { Header = "Data quality", Content = Data },
                new TabItem { Header = "Expense review / Elmish", Content = Expenses }
            }
        };
        var header = Ui.Stack(
            Ui.Text("RANVIER / REAL WORKFLOWS", 12, "#536274"),
            Ui.Text("Work in motion.", 32),
            Ui.Text("Keep drafts across refreshes. Follow concurrent workers. Repair data without recalculating every row.", 14, "#536274"));
        header.Margin = new Thickness(0, 0, 0, 16);
        DockPanel.SetDock(header, Dock.Top);
        var content = new DockPanel();
        content.Children.Add(header);
        content.Children.Add(Tabs);
        content.Margin = new Thickness(28);
        Content = content;
        Opened += (_, _) => { initialization = InitializeAsync(local); };
        Closed += async (_, _) => await StopAsync();
    }

    private static async Task<string> ReadCalibrationAsync(string station, CancellationToken token)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "samples", "calibrations.json");
        var records = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(file, token))
            ?? throw new InvalidDataException("Calibration file contains no records.");
        return records.TryGetValue(station, out string? record) ? record : throw new InvalidDataException($"No calibration certificate for {station}.");
    }

    private async Task InitializeAsync(string local)
    {
        try
        {
            using var active = graph.Activate();
            var dataLoad = ObserveStartupAsync(dataModel.LoadAsync(local));
            expenseModel.Load();
            try
            {
                var server = await SupportApi.StartAsync(Path.Combine(DataDirectory, "support"), lifetime.Token);
                if (closed)
                {
                    await server.DisposeAsync();
                    await Task.WhenAll(dataLoad, expenseModel.Completion);
                    return;
                }
                api = server;
                http = new TicketHttpClient(server.BaseAddress);
                supportModel = new SupportDesk(graph, http);
                Support = new SupportView(graph, supportModel, api, ApplyExternalUpdateAsync);
                ((TabItem[])Tabs.ItemsSource!)[0].Content = Support;
                await ObserveStartupAsync(supportModel.SearchAsync(""));
            }
            catch (Exception error)
            {
                if (!closed) ((TabItem[])Tabs.ItemsSource!)[0].Content = Ui.Text("Ticket service unavailable: " + error.Message, 16, "#B42318");
            }
            await Task.WhenAll(dataLoad, expenseModel.Completion);
            if (!closed) ready.TrySetResult();
        }
        catch (Exception error)
        {
            if (!closed) ready.TrySetException(error);
        }
        finally { if (closed) ready.TrySetCanceled(); }
    }

    private static async Task ObserveStartupAsync(Task task)
    {
        try { await task; }
        catch (Exception) { }
    }

    /// <summary>Writes a new server version through HTTP, leaving the independent editor draft intact.</summary>
    public async Task ApplyExternalUpdateAsync()
    {
        if (supportModel is null || http is null || supportModel.SelectedId == 0) return;
        int id = supportModel.SelectedId;
        var current = await http.GetAsync(id, CancellationToken.None);
        await http.SaveAsync(new(id, current.Notes + "\nColleague added a diagnostic note.", current.State, current.Version), CancellationToken.None);
        await supportModel.RefreshAsync();
    }

    private async Task StopAsync()
    {
        if (closed) return;
        closed = true;
        try
        {
            lifetime.Cancel();
            Support?.Dispose(); Jobs.Dispose(); Data.Dispose(); Expenses.Dispose();
            supportModel?.Dispose(); dataModel.Dispose(); jobModel.Dispose(); expenseModel.Dispose();
            await initialization;
            await Task.WhenAll(jobModel.Completion, expenseModel.Completion);
            http?.Dispose();
            if (api is not null) await api.DisposeAsync();
            graph.Dispose();
            lifetime.Dispose();
            ready.TrySetCanceled();
            stopped.TrySetResult();
        }
        catch (Exception error) { stopped.TrySetException(error); }
    }
}
