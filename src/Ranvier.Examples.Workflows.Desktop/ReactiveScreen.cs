using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Ranvier;
using Ranvier.CSharp;

namespace Ranvier.Examples.Workflows.Desktop;

public abstract class ReactiveScreen : UserControl, INotifyPropertyChanged, IDisposable
{
    protected readonly ReactiveBindings Nodes;
    private readonly Signal<string> error;
    private readonly BoundValue<string> errorText;
    private bool disposed;
    private long operationVersion;
    private readonly List<IDisposable> owned = [];

    protected ReactiveScreen(Graph graph)
    {
        Nodes = new ReactiveBindings(this, graph);
        error = Nodes.Run(() => Reactive.Signal(""));
        errorText = Nodes.Computed(nameof(LastError), () => error.Value);
        DataContext = this;
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => Nodes.PropertyChanged += value;
        remove => Nodes.PropertyChanged -= value;
    }

    public string LastError => errorText.Value;

    protected T Own<T>(T resource) where T : IDisposable { owned.Add(resource); return resource; }

    private long BeginAction()
    {
        long version = ++operationVersion;
        if (!disposed) Nodes.Run(() => error.Value = "");
        return version;
    }

    protected ReactiveCommand Command(Action<object?> action, Func<bool>? canExecute = null) =>
        Nodes.Command(parameter =>
        {
            long version = BeginAction();
            try { action(parameter); }
            catch (Exception failure)
            {
                if (!disposed && version == operationVersion) Nodes.Run(() => error.Value = failure.Message);
                throw;
            }
        }, canExecute);

    protected ReactiveCommand Command(Func<object?, CancellationToken, Task> action, Func<bool>? canExecute = null) =>
        Nodes.Command((parameter, token) => ObserveAsync(() => action(parameter, token), true), canExecute);

    protected void Fire(Func<Task> action) => _ = ObserveAsync(action, false);
    private async Task ObserveAsync(Func<Task> action, bool propagate)
    {
        long version = BeginAction();
        try
        {
            await action();
        }
        catch (OperationCanceledException) { if (propagate) throw; }
        catch (Exception failure)
        {
            if (!disposed && version == operationVersion) Nodes.Run(() => error.Value = failure.Message);
            if (propagate) throw;
        }
    }
    public virtual void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var resource in owned) resource.Dispose();
        Nodes.Dispose();
    }
}

internal static class Ui
{
    internal static TextBlock Text(string text, double size = 14, string color = "#263B42") =>
        new() { Text = text, FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };
    internal static TextBlock Read(string property, double size = 14, string color = "#263B42") =>
        new() { FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap, [!TextBlock.TextProperty] = new Binding(property) };
    internal static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    internal static StackPanel Actions(params Control[] children)
    {
        var panel = Stack(children);
        panel.Orientation = Orientation.Horizontal;
        return panel;
    }
    internal static Border Card(Control child) => new() { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Child = child };
    internal static TextBox Edit(string name, string property, bool multiline = false) =>
        new() { Name = name, AcceptsReturn = multiline, MinHeight = multiline ? 100 : 36, [!TextBox.TextProperty] = new Binding(property) { Mode = BindingMode.TwoWay } };
    internal static Button Button(string name, string text, string command) =>
        new() { Name = name, Content = text, Padding = new Thickness(14, 9), [!Avalonia.Controls.Button.CommandProperty] = new Binding(command) };
    internal static ComboBox Choice(string name, string property, string[] options) =>
        new() { Name = name, ItemsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch, [!ComboBox.SelectedItemProperty] = new Binding(property) { Mode = BindingMode.TwoWay } };
    internal static Grid Columns(Control left, Control right, string widths = "360,*")
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(widths), ColumnSpacing = 20 };
        Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right); return grid;
    }
}
