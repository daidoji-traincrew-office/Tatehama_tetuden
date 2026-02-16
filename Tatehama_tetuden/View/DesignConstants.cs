using System.Windows.Media;

namespace Tatehama_tetuden.View;

public static class DesignConstants
{
    public static readonly Brush PrimaryColor = new SolidColorBrush(Color.FromRgb(0, 120, 215));
    public static readonly Brush DangerColor = new SolidColorBrush(Color.FromRgb(232, 17, 35));
    public static readonly Brush AcceptColor = new SolidColorBrush(Color.FromRgb(30, 180, 50));
    public static readonly Brush HoldColor = new SolidColorBrush(Color.FromRgb(255, 140, 0));
    public static readonly Brush BgColor = new SolidColorBrush(Color.FromRgb(240, 244, 248));
    public static readonly Brush OfflineBgColor = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    public static readonly Brush WarningBgColor = new SolidColorBrush(Color.FromRgb(255, 240, 240));

    static DesignConstants()
    {
        PrimaryColor.Freeze();
        DangerColor.Freeze();
        AcceptColor.Freeze();
        HoldColor.Freeze();
        BgColor.Freeze();
        OfflineBgColor.Freeze();
        WarningBgColor.Freeze();
    }
}
