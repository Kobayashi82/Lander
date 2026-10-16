using System.Windows;
using System.Windows.Controls;

namespace Lander.App.UI;

/// <summary>Fila de ajuste al estilo de la Configuración de Windows 11.</summary>
public sealed class SettingCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingCard));

    /// <summary>Con <c>true</c>, el contenido ocupa todo el ancho bajo el título.</summary>
    public static readonly DependencyProperty WideProperty =
        DependencyProperty.Register(nameof(Wide), typeof(bool), typeof(SettingCard));

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public bool Wide
    {
        get => (bool)GetValue(WideProperty);
        set => SetValue(WideProperty, value);
    }
}
