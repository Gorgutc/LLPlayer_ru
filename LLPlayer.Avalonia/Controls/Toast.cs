using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Controls;

/// <summary>
/// Small notification card (shadcn "sonner"-style toast): title, optional message, variant accent and a close button.
/// Its look lives in Themes/Controls/Toast.axaml; the pseudo-classes :success / :destructive select the variant.
/// </summary>
public sealed class Toast : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Toast, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<Toast, string?>(nameof(Message));

    public static readonly StyledProperty<ToastVariant> VariantProperty =
        AvaloniaProperty.Register<Toast, ToastVariant>(nameof(Variant));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<Toast, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<object?> CloseCommandParameterProperty =
        AvaloniaProperty.Register<Toast, object?>(nameof(CloseCommandParameter));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ToastVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public object? CloseCommandParameter
    {
        get => GetValue(CloseCommandParameterProperty);
        set => SetValue(CloseCommandParameterProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == VariantProperty)
            UpdatePseudoClasses();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdatePseudoClasses();
    }

    void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":success", Variant == ToastVariant.Success);
        PseudoClasses.Set(":destructive", Variant == ToastVariant.Destructive);
    }
}
