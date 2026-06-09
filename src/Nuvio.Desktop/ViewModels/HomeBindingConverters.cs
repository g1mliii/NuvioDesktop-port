using System.Globalization;
using Avalonia.Data.Converters;

namespace Nuvio.Desktop.ViewModels;

/// <summary>Small value converters for the Home view (kept out of XAML for compiled-binding friendliness).</summary>
public static class HomeBindingConverters
{
    /// <summary>Maps the hero dot's selected state to an opacity (selected = solid, others = dim).</summary>
    public static readonly IValueConverter SelectedDotOpacity =
        new FuncValueConverter<bool, double>(selected => selected ? 1d : 0.35d);
}
