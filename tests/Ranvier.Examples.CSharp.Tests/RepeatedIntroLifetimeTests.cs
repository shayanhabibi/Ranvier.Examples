using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Ranvier.Examples.Workflows.Desktop;
using Xunit;

namespace Ranvier.Examples.CSharp.Tests;

public sealed class RepeatedIntroLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderControlsRecoverAcrossRepeatedSubmitEditMalformedInputAndReset(bool fsharp)
    {
        Window window = fsharp ? new global::Ranvier.Examples.FSharp.Desktop.OrderWindow()
            : new Desktop.OrderWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var quantity = Find<NumericUpDown>(window, "QuantityInput");
            var price = Find<NumericUpDown>(window, "PriceInput");
            var discount = Find<NumericUpDown>(window, "DiscountInput");
            var delivery = Find<CheckBox>(window, "DeliveryInput");
            var address = Find<TextBox>(window, "AddressInput");
            var total = Find<TextBlock>(window, "TotalText");
            var confirmation = Find<TextBlock>(window, "ConfirmationText");
            var submit = Find<Button>(window, "SubmitButton");

            for (int round = 0; round < 5; round++)
            {
                decimal count = 3 + round, unitPrice = 20 + round;
                quantity.Value = count;
                price.Value = unitPrice;
                discount.Value = 10;
                delivery.IsChecked = true;
                address.Text = "";
                Dispatcher.UIThread.RunJobs();
                Assert.False(submit.IsEffectivelyEnabled);

                address.Text = $"{round + 1} Example Street";
                Dispatcher.UIThread.RunJobs();
                decimal accepted = Payable(count, unitPrice);
                Assert.Equal(Money(accepted), total.Text);
                Press(window, "SubmitButton");
                string? firstConfirmation = confirmation.Text;
                Assert.Contains(Money(accepted), firstConfirmation);

                quantity.Value = count + 1;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Money(Payable(count + 1, unitPrice)), total.Text);
                Assert.Equal(firstConfirmation, confirmation.Text);
                Press(window, "SubmitButton");
                Assert.Contains(Money(Payable(count + 1, unitPrice)), confirmation.Text);

                price.Text = "not a number";
                Dispatcher.UIThread.RunJobs();
                Assert.False(submit.IsEffectivelyEnabled);
                Assert.Equal("Unavailable", total.Text);
                Press(window, "ResetButton");
                Assert.Equal(2m, quantity.Value);
                Assert.Equal(25m, price.Value);
                Assert.Equal(0m, discount.Value);
                Assert.False(delivery.IsChecked.GetValueOrDefault());
                Assert.Equal("", address.Text);
                Assert.Equal("50.00", total.Text);
                Assert.True(submit.IsEffectivelyEnabled);
                Assert.Equal("No order submitted", confirmation.Text);
            }
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaFact]
    public async Task MalformedPersistedCsvCanRecoverWithoutLosingSupportTab()
    {
        string directory = NewDirectory();
        string source = Path.Combine(directory, "measurements.csv");
        await File.WriteAllTextAsync(source, "incorrect,header\n");
        var window = new WorkflowWindow(directory);
        window.Show();
        try
        {
            try { await window.Ready.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (InvalidDataException) { }

            await File.WriteAllTextAsync(source,
                "id,station,value,unit,included\n1,Station A,20,C,true\n");
            window.Tabs.SelectedIndex = 2;
            Press(window, "CsvLoad");
            await Until(() => !window.Data.Load.IsRunning && window.Data.Model.ValidCount == 1);
            Assert.Empty(window.Data.LastError);
            Assert.Equal(20m, window.Data.Model.MeanCelsius);
            var supportTab = Assert.IsType<TabItem>(((TabItem[])window.Tabs.ItemsSource!)[0]);
            Assert.Same(window.Support, Assert.IsType<SupportView>(supportTab.Content));
            window.Tabs.SelectedIndex = 0;
            await Until(() => window.Support.Model.Queue.Length > 0);
            Find<ListBox>(window, "SupportQueue").SelectedIndex = 0;
            await Until(() => window.Support.Model.SelectedId != 0);
            Assert.True(Find<TextBox>(window, "SupportDraft").IsEffectivelyEnabled);
        }
        finally { window.Close(); await window.Stopped.WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    [AvaloniaFact]
    public async Task EarlyCloseReleasesPersistedStoreBeforeStoppedCompletes()
    {
        string directory = NewDirectory();
        for (int round = 0; round < 5; round++)
        {
            var closing = new WorkflowWindow(directory);
            closing.Show();
            closing.Close();
            await closing.Stopped.WaitAsync(TimeSpan.FromSeconds(15));

            var reopened = new WorkflowWindow(directory);
            reopened.Show();
            try
            {
                await reopened.Ready.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.NotEmpty(reopened.Support.Model.Queue);
            }
            finally { reopened.Close(); await reopened.Stopped.WaitAsync(TimeSpan.FromSeconds(15)); }
        }
    }

    private static decimal Payable(decimal quantity, decimal price)
    {
        decimal subtotal = quantity * price;
        decimal discounted = subtotal - decimal.Round(subtotal * 0.1m, 2, MidpointRounding.AwayFromZero);
        return discounted + (discounted < 100m ? 7.50m : 0m);
    }

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string NewDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "ui-repeated-lifetime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Distinct().Single(control => control.Name == name);

    private static void Press(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var button = Find<Button>(window, name);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(), "The workflow did not reach its expected state.");
    }
}
