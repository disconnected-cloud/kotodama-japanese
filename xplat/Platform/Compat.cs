using Avalonia.Interactivity;

namespace Matsubakira.Compat;

/// <summary>
/// Переключатели с WPF-событиями Checked/Unchecked (в Avalonia есть только IsCheckedChanged).
/// StyleKeyOverride — чтобы применялись обычные стили RadioButton/CheckBox/ToggleButton.
/// </summary>
public class RadioButton : Avalonia.Controls.RadioButton
{
    protected override Type StyleKeyOverride => typeof(Avalonia.Controls.RadioButton);
    public new event EventHandler<RoutedEventArgs>? Checked;
    public new event EventHandler<RoutedEventArgs>? Unchecked;
    public RadioButton() => IsCheckedChanged += (_, e) => (IsChecked == true ? Checked : Unchecked)?.Invoke(this, e);
}

public class CheckBox : Avalonia.Controls.CheckBox
{
    protected override Type StyleKeyOverride => typeof(Avalonia.Controls.CheckBox);
    public new event EventHandler<RoutedEventArgs>? Checked;
    public new event EventHandler<RoutedEventArgs>? Unchecked;
    public CheckBox() => IsCheckedChanged += (_, e) => (IsChecked == true ? Checked : Unchecked)?.Invoke(this, e);
}

public class ToggleButton : Avalonia.Controls.Primitives.ToggleButton
{
    protected override Type StyleKeyOverride => typeof(Avalonia.Controls.Primitives.ToggleButton);
    public new event EventHandler<RoutedEventArgs>? Checked;
    public new event EventHandler<RoutedEventArgs>? Unchecked;
    public ToggleButton() => IsCheckedChanged += (_, e) => (IsChecked == true ? Checked : Unchecked)?.Invoke(this, e);
}
