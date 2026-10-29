using System.Windows;
using System.Windows.Automation;

namespace VideoCapture.Controls;

/// <summary>Shared, non-interactive presentation of a button's actual feature state.</summary>
public static class ButtonState
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(ButtonState), new FrameworkPropertyMetadata(false,
            (owner, args) => AutomationProperties.SetItemStatus(owner, (bool)args.NewValue ? "On" : "Off")));
    public static bool GetIsActive(DependencyObject owner) => (bool)owner.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject owner, bool value)
    {
        string state = value ? "On" : "Off";
        if (GetIsActive(owner) != value) owner.SetValue(IsActiveProperty, value);
        if (AutomationProperties.GetItemStatus(owner) != state) AutomationProperties.SetItemStatus(owner, state);
    }
}
