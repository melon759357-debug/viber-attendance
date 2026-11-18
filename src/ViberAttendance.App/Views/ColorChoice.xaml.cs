using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ViberAttendance.App.Views;

public partial class ColorChoice : UserControl
{
    private static readonly string[] Palette =
    [
        "#E53935", "#F9A825", "#43A047", "#1E88E5", "#8E24AA", "#6D4C41", "#546E7A", "#212121",
        "#FFCDD2", "#FFF59D", "#C8E6C9", "#BBDEFB", "#E1BEE7", "#D7CCC8", "#CFD8DC", "#FFFFFF"
    ];

    private bool _updating;

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color),
        typeof(string),
        typeof(ColorChoice),
        new FrameworkPropertyMetadata("#E53935", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    public ColorChoice()
    {
        InitializeComponent();
        foreach (var hex in Palette)
        {
            var button = new Button
            {
                Height = 22,
                Margin = new Thickness(2),
                Background = Brush(hex),
                BorderThickness = new Thickness(1),
                Tag = hex
            };
            button.Click += (_, _) => ApplyChannels(Parse(hex));
            Swatches.Children.Add(button);
        }

        Apply(Color);
    }

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private static void OnColorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is ColorChoice choice)
        {
            choice.Apply(args.NewValue as string);
        }
    }

    private void OpenPicker(object sender, RoutedEventArgs args)
    {
        ApplyChannels(Parse(Color));
        Picker.IsOpen = true;
    }

    private void PreviewChannel(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (_updating || Swatch is null)
        {
            return;
        }

        Paint(CurrentChannels());
    }

    private void AcceptColor(object sender, RoutedEventArgs args)
    {
        var (red, green, blue) = CurrentChannels();
        Color = $"#{red:X2}{green:X2}{blue:X2}";
        Picker.IsOpen = false;
    }

    private void Apply(string? hex)
    {
        if (Swatch is null)
        {
            return;
        }

        ApplyChannels(Parse(hex));
        Paint(Parse(hex));
    }

    private void ApplyChannels((byte Red, byte Green, byte Blue) color)
    {
        if (RedSlider is null)
        {
            return;
        }

        _updating = true;
        RedSlider.Value = color.Red;
        GreenSlider.Value = color.Green;
        BlueSlider.Value = color.Blue;
        _updating = false;
        Paint(color);
    }

    private (byte Red, byte Green, byte Blue) CurrentChannels() =>
        ((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);

    private void Paint((byte Red, byte Green, byte Blue) color)
    {
        if (Swatch is null)
        {
            return;
        }

        Swatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
        var bright = color.Red * 0.299 + color.Green * 0.587 + color.Blue * 0.114;
        Swatch.Foreground = new SolidColorBrush(bright > 150 ? Colors.Black : Colors.White);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var color = Parse(hex);
        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
    }

    private static (byte Red, byte Green, byte Blue) Parse(string? hex)
    {
        var text = (hex ?? string.Empty).Trim().TrimStart('#');
        if (text.Length == 6 && int.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var packed))
        {
            return ((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
        }

        return (0xE5, 0x39, 0x35);
    }
}
