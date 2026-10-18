using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Ranvier.Examples.CSharp.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Ranvier.Examples.CSharp.Tests;

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class DesktopTests
{
    [AvaloniaTheory]
    [InlineData(false, "QuantityInput")]
    [InlineData(true, "QuantityInput")]
    [InlineData(false, "PriceInput")]
    [InlineData(true, "PriceInput")]
    [InlineData(false, "DiscountInput")]
    [InlineData(true, "DiscountInput")]
    public void MalformedTextCannotSubmitStaleValue(bool fsharp, string field)
    {
        Window window = fsharp ? new global::Ranvier.Examples.FSharp.Desktop.OrderWindow()
            : new Desktop.OrderWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Control<NumericUpDown>(window, field).Text = "not a number";
            Dispatcher.UIThread.RunJobs();
            Assert.False(Control<Button>(window, "SubmitButton").IsEffectivelyEnabled);
            Assert.Equal("Unavailable", Control<TextBlock>(window, "TotalText").Text);
            Click(window, Control<Button>(window, "ResetButton"));
            Assert.Equal("50.00", Control<TextBlock>(window, "TotalText").Text);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    private static T Control<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Click(Window window, Button button)
    {
        button.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualWindowReactsToEditsSubmissionAndReset(bool fsharp)
    {
        Window window = fsharp ? new global::Ranvier.Examples.FSharp.Desktop.OrderWindow()
            : new Desktop.OrderWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var quantity = Control<NumericUpDown>(window, "QuantityInput");
            var discount = Control<NumericUpDown>(window, "DiscountInput");
            var delivery = Control<CheckBox>(window, "DeliveryInput");
            var address = Control<TextBox>(window, "AddressInput");
            var total = Control<TextBlock>(window, "TotalText");
            var submit = Control<Button>(window, "SubmitButton");
            var reset = Control<Button>(window, "ResetButton");
            Assert.Equal("50.00", total.Text);
            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                Assert.True(frame.PixelSize.Width > 0);
                frame.Save(Path.Combine(AppContext.BaseDirectory, fsharp ? "order-fsharp.png" : "order-csharp.png"));
            }
            quantity.Value = 3; discount.Value = 10;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("67.50", total.Text);
            delivery.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(submit.IsEffectivelyEnabled);
            Assert.Equal("Unavailable", total.Text);
            address.Text = "42 Example Street";
            Dispatcher.UIThread.RunJobs();
            Assert.True(submit.IsEffectivelyEnabled);
            Assert.Equal("75.00", total.Text);
            Click(window, submit);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("75.00", Control<TextBlock>(window, "ConfirmationText").Text);
            Click(window, reset);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2m, quantity.Value);
            Assert.Equal("50.00", total.Text);
            Assert.Equal("No order submitted", Control<TextBlock>(window, "ConfirmationText").Text);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        Assert.False(window.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false, "QuantityInput")]
    [InlineData(true, "QuantityInput")]
    [InlineData(false, "PriceInput")]
    [InlineData(true, "PriceInput")]
    [InlineData(false, "DiscountInput")]
    [InlineData(true, "DiscountInput")]
    public void EmptyNumericEditorCannotSubmitStaleValue(bool fsharp, string field)
    {
        Window window = fsharp ? new global::Ranvier.Examples.FSharp.Desktop.OrderWindow()
            : new Desktop.OrderWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Control<NumericUpDown>(window, field).Value = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(Control<Button>(window, "SubmitButton").IsEffectivelyEnabled);
            Assert.Equal("Unavailable", Control<TextBlock>(window, "TotalText").Text);
            Click(window, Control<Button>(window, "ResetButton"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(Control<Button>(window, "SubmitButton").IsEffectivelyEnabled);
            Assert.Equal("50.00", Control<TextBlock>(window, "TotalText").Text);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
