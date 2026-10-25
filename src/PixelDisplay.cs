using System.Windows;
using System.Windows.Media;
namespace IndependentDev;

internal static class PixelDisplay
{
    static readonly DrawingBrush Checks=CreateChecks();
    static readonly Pen GridPen=CreatePen();
    static Pen CreatePen(){var pen=new Pen(new SolidColorBrush(Color.FromArgb(72,0,0,0)),1);pen.Freeze();return pen;}
    static DrawingBrush CreateChecks()
    {
        var tiles=new DrawingGroup();using(var drawing=tiles.Open()){drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(64,67,71)),null,new Rect(0,0,16,16));var light=new SolidColorBrush(Color.FromRgb(80,83,87));drawing.DrawRectangle(light,null,new Rect(0,0,8,8));drawing.DrawRectangle(light,null,new Rect(8,8,8,8));}
        var brush=new DrawingBrush(tiles){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,16,16)};brush.Freeze();return brush;
    }
    internal static void Image(DrawingContext drawing,ImageSource source,Rect rectangle,BitmapScalingMode mode)
    {
        // Set the option on the drawing itself: WPF may resample nested images despite the visual's option.
        var pixels=new DrawingGroup();RenderOptions.SetBitmapScalingMode(pixels,mode);
        using(var context=pixels.Open())context.DrawImage(source,rectangle);drawing.DrawDrawing(pixels);
    }
    internal static void Background(DrawingContext drawing,Rect bounds,Point origin,double scale)
    {
        if(bounds.IsEmpty)return;
        var brush=Checks.Clone();brush.Viewport=new Rect(origin.X,origin.Y,16*scale,16*scale);
        drawing.DrawRectangle(brush,null,bounds);
    }
    internal static void Grid(DrawingContext drawing,Rect bounds,Point origin,double step)
    {
        if(bounds.IsEmpty||step<8||!double.IsFinite(step))return;
        var x=bounds.Left+(origin.X%step-bounds.Left%step+step)%step;var y=bounds.Top+(origin.Y%step-bounds.Top%step+step)%step;
        for(;x<=bounds.Right;x+=step)drawing.DrawLine(GridPen,new(x,bounds.Top),new(x,bounds.Bottom));
        for(;y<=bounds.Bottom;y+=step)drawing.DrawLine(GridPen,new(bounds.Left,y),new(bounds.Right,y));
    }
}
