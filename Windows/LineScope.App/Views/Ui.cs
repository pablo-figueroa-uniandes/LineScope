using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LineScope.App.Views;

/// <summary>An <see cref="ICommand"/> whose enabled state is re-queried by WPF's command manager.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

/// <summary>Small helpers for building the views in code.</summary>
public static class Ui
{
    public static Brush SecondaryText => SystemColors.GrayTextBrush;

    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = SecondaryText,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// A segmented control (like SwiftUI's segmented picker): mutually exclusive toggle buttons.
    /// </summary>
    public static StackPanel Segmented(IReadOnlyList<string> labels, int selected, Action<int> onSelect)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var buttons = new List<ToggleButton>();
        for (int i = 0; i < labels.Count; i++)
        {
            int index = i;
            var b = new ToggleButton
            {
                Content = labels[i],
                IsChecked = i == selected,
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(i == 0 ? 0 : -1, 0, 0, 0),
                MinWidth = 36,
            };
            // Checked/Unchecked rather than Click, so keyboard and UI Automation toggles work too.
            b.Checked += (_, _) =>
            {
                foreach (var other in buttons.Where(o => o != b)) other.IsChecked = false;
                onSelect(index);
            };
            b.Unchecked += (_, _) =>
            {
                // Clicking the selected segment keeps it selected.
                if (buttons.All(o => o.IsChecked != true)) b.IsChecked = true;
            };
            buttons.Add(b);
            panel.Children.Add(b);
        }
        return panel;
    }

    public static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>A button that opens a drop-down menu of presets.</summary>
    public static Button PresetsButton(IEnumerable<(string Label, Action Action)> items)
    {
        var menu = new ContextMenu();
        foreach (var (label, action) in items)
        {
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        var button = new Button { Content = "Presets ▾", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
        button.Click += (_, _) =>
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return button;
    }
}
