using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Soot;

internal sealed class PetPanelContext
{
    internal required PetSettings Settings { get; init; }
    internal required Action SaveSettings { get; init; }
    internal required Action ResetPosition { get; init; }
    internal required Action HidePet { get; init; }
    internal required Func<bool, bool> SetStartup { get; init; }
    internal required Action OpenCodex { get; init; }
}

internal interface IPetPanelModule
{
    string Title { get; }
    FrameworkElement CreateView(PetPanelContext context);
    void SetPanelVisible(bool visible);
}

internal sealed class PetPanel : IDisposable
{
    internal const double Width = 286;

    private readonly Popup popup;
    private readonly PetPanelContext context;
    private readonly IReadOnlyList<IPetPanelModule> modules;

    internal PetPanel(UIElement anchor, PetPanelContext context)
    {
        this.context = context;
        modules = new IPetPanelModule[] { new PetControlsModule(), new CodexModule(), new ResourceUsageModule() };
        popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Right,
            HorizontalOffset = 12,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            StaysOpen = false,
            Child = BuildShell()
        };
        popup.Opened += (_, _) => SetModulesVisible(true);
        popup.Closed += (_, _) => SetModulesVisible(false);
    }

    internal bool IsOpen => popup.IsOpen;

    internal void Open(bool placeLeft)
    {
        popup.Placement = placeLeft ? PlacementMode.Left : PlacementMode.Right;
        popup.HorizontalOffset = placeLeft ? -12 : 12;
        popup.IsOpen = true;
    }

    internal void Close() => popup.IsOpen = false;

    public void Dispose() => Close();

    private FrameworkElement BuildShell()
    {
        var tabs = new TabControl
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White
        };
        foreach (var module in modules)
        {
            tabs.Items.Add(new TabItem
            {
                Header = module.Title,
                Content = module.CreateView(context),
                Background = new SolidColorBrush(Color.FromArgb(248, 28, 30, 36)),
                Foreground = Brushes.White,
                Padding = new Thickness(7, 4, 7, 4)
            });
        }

        var content = new StackPanel();
        content.Children.Add(tabs);
        var hide = new Button
        {
            Content = "Hide Soot",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(10, 7, 10, 7)
        };
        hide.Click += (_, _) => context.HidePet();
        content.Children.Add(hide);

        return new Border
        {
            Width = Width,
            Padding = new Thickness(17),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(248, 28, 30, 36)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 93, 97, 108)),
            BorderThickness = new Thickness(1),
            Child = content
        };
    }

    private void SetModulesVisible(bool visible)
    {
        foreach (var module in modules) module.SetPanelVisible(visible);
    }
}

internal sealed class PetControlsModule : IPetPanelModule
{
    public string Title => "Pet settings";

    public FrameworkElement CreateView(PetPanelContext context)
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateCheckBox("Animate Soot", context.Settings.AnimationsEnabled, enabled =>
        {
            context.Settings.AnimationsEnabled = enabled;
            context.SaveSettings();
            return true;
        }));
        panel.Children.Add(CreateCheckBox("Follow cursor", context.Settings.Following, enabled =>
        {
            context.Settings.Following = enabled;
            context.SaveSettings();
            return true;
        }));
        panel.Children.Add(CreateCheckBox("Start with Windows", context.Settings.StartWithWindows, enabled =>
        {
            if (!context.SetStartup(enabled)) return false;
            return true;
        }));

        var reset = new Button
        {
            Content = "Reset position",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(10, 7, 10, 7)
        };
        reset.Click += (_, _) => context.ResetPosition();
        panel.Children.Add(reset);
        return panel;
    }

    public void SetPanelVisible(bool visible) { }

    private static CheckBox CreateCheckBox(string label, bool initialValue, Func<bool, bool> onChanged)
    {
        var checkBox = new CheckBox
        {
            Content = label,
            IsChecked = initialValue,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 5, 0, 5),
            Padding = new Thickness(2)
        };
        checkBox.Checked += (_, _) => Apply(true);
        checkBox.Unchecked += (_, _) => Apply(false);
        return checkBox;

        void Apply(bool value)
        {
            if (!onChanged(value)) checkBox.IsChecked = !value;
        }
    }
}
