using System.Windows;
using System.Windows.Media;

namespace BGInfoPlus;

internal static class ApplicationIcon
{
    public static ImageSource Create()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(37, 99, 235)), null,
            new RectangleGeometry(new Rect(0, 0, 64, 64), 12, 12)));

        var whitePen = new Pen(Brushes.White, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawing.Children.Add(new GeometryDrawing(null, whitePen, new RectangleGeometry(new Rect(11, 13, 42, 30), 4, 4)));
        drawing.Children.Add(new GeometryDrawing(null, whitePen, Geometry.Parse("M 32,43 L 32,51 M 22,51 L 42,51")));
        drawing.Children.Add(new GeometryDrawing(null, whitePen, Geometry.Parse("M 32,20 L 32,36 M 24,28 L 40,28")));
        drawing.Freeze();
        return new DrawingImage(drawing);
    }
}
