using Avalonia.Controls;
using Avalonia;
using System.Globalization;
using Avalonia.Markup.Xaml;
using Ranvier;

namespace Ranvier.Examples.CSharp.Desktop;

public sealed partial class OrderWindow : Window
{
    private readonly Graph graph;
    private readonly OrderModel order;
    private readonly OrderViewModel viewModel;

    public OrderWindow()
    {
        AvaloniaXamlLoader.Load(this);
        graph = new Graph();
        using var active = graph.Activate();
        order = new OrderModel(graph);
        viewModel = new OrderViewModel(graph, order);
        DataContext = viewModel;
        var numericEditors = new[] { this.FindControl<NumericUpDown>("QuantityInput")!, this.FindControl<NumericUpDown>("PriceInput")!, this.FindControl<NumericUpDown>("DiscountInput")! };
        EventHandler<AvaloniaPropertyChangedEventArgs> textChanged = (sender, change) =>
        {
            if (change.Property == NumericUpDown.TextProperty && sender is NumericUpDown editor
                && !decimal.TryParse(editor.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out _))
                editor.Value = null;
        };
        foreach (var editor in numericEditors) editor.PropertyChanged += textChanged;
        Closed += (_, _) =>
        {
            foreach (var editor in numericEditors) editor.PropertyChanged -= textChanged;
            DataContext = null;
            viewModel.Dispose();
            order.Dispose();
            graph.Dispose();
        };
    }
}
