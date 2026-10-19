using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Ranvier;
using Ranvier.CSharp;

namespace Ranvier.Examples.Workflows.Desktop;

internal sealed class StableKeyList : ListBox, IDisposable
{
    protected override Type StyleKeyOverride => typeof(ListBox);
    private readonly ReactiveBindings nodes;
    private bool synchronizing;

    internal StableKeyList(Graph graph, Func<int[]> keys, Func<int> selected, Action<int> select, Func<int, string> caption)
    {
        var items = new ObservableCollection<int>();
        ItemsSource = items;
        ItemTemplate = new FuncDataTemplate<int>((id, _) => new ReactiveCaption(graph, () => caption(id)));
        SelectionChanged += (_, _) =>
        {
            if (!synchronizing && SelectedItem is int id && id != selected()) select(id);
        };
        nodes = new ReactiveBindings(this, graph);
        nodes.Run(() => Reactive.Effect(() =>
        {
            int[] desired = keys();
            int key = selected();
            synchronizing = true;
            try
            {
                for (int index = 0; index < desired.Length; index++)
                {
                    if (index < items.Count && items[index] == desired[index]) continue;
                    int previous = items.IndexOf(desired[index]);
                    if (previous >= 0) items.Move(previous, index);
                    else items.Insert(index, desired[index]);
                }
                while (items.Count > desired.Length) items.RemoveAt(items.Count - 1);
                SelectedItem = desired.Contains(key) ? key : null;
            }
            finally { synchronizing = false; }
        }));
    }

    public void Dispose() => nodes.Dispose();

    private sealed class ReactiveCaption : TextBlock
    {
        protected override Type StyleKeyOverride => typeof(TextBlock);
        private ReactiveBindings? binding;
        internal ReactiveCaption(Graph graph, Func<string> caption)
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            AttachedToVisualTree += (_, _) =>
            {
                binding = new ReactiveBindings(this, graph);
                binding.Run(() => Reactive.Effect(() => { Text = caption(); }));
            };
            DetachedFromVisualTree += (_, _) => { binding?.Dispose(); binding = null; };
        }
    }
}
