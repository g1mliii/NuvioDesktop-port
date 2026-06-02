using Avalonia;
using Avalonia.Controls.Primitives;
using System.Windows.Input;

namespace Nuvio.Desktop.Controls;

/// <summary>
/// Shared loading / empty / error block used by every browse page. Replaces the copy-pasted inline
/// TextBlock triplets and is the single home for actionable failure copy (Phase 7.11) — the
/// <see cref="Hint"/> tells the user how to recover (install mpv, enable an addon, check the network)
/// and an optional <see cref="RetryCommand"/> surfaces a retry affordance.
/// </summary>
public sealed class StateOverlay : TemplatedControl
{
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<StateOverlay, bool>(nameof(IsLoading));

    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<StateOverlay, bool>(nameof(IsEmpty));

    public static readonly StyledProperty<bool> IsErrorProperty =
        AvaloniaProperty.Register<StateOverlay, bool>(nameof(IsError));

    public static readonly StyledProperty<string?> LoadingMessageProperty =
        AvaloniaProperty.Register<StateOverlay, string?>(nameof(LoadingMessage), "Loading…");

    public static readonly StyledProperty<string?> EmptyMessageProperty =
        AvaloniaProperty.Register<StateOverlay, string?>(nameof(EmptyMessage), "Nothing to show yet.");

    public static readonly StyledProperty<string?> ErrorMessageProperty =
        AvaloniaProperty.Register<StateOverlay, string?>(nameof(ErrorMessage));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<StateOverlay, string?>(nameof(Hint));

    public static readonly StyledProperty<ICommand?> RetryCommandProperty =
        AvaloniaProperty.Register<StateOverlay, ICommand?>(nameof(RetryCommand));

    public static readonly StyledProperty<string> RetryLabelProperty =
        AvaloniaProperty.Register<StateOverlay, string>(nameof(RetryLabel), "Retry");

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public bool IsEmpty
    {
        get => GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    public bool IsError
    {
        get => GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    public string? LoadingMessage
    {
        get => GetValue(LoadingMessageProperty);
        set => SetValue(LoadingMessageProperty, value);
    }

    public string? EmptyMessage
    {
        get => GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    /// <summary>Actionable recovery copy shown under an error (e.g. how to install mpv or enable an addon).</summary>
    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public string RetryLabel
    {
        get => GetValue(RetryLabelProperty);
        set => SetValue(RetryLabelProperty, value);
    }
}
