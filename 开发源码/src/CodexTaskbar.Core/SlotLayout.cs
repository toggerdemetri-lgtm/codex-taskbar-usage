namespace CodexTaskbar.Core;

public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Intersects(PixelRect other) => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
}

public static class SlotLayout
{
    public static PixelRect? Find(PixelRect bar, int trayLeft, int width, int height, IReadOnlyList<PixelRect> occupied)
    {
        if(width<=0 || height<=0 || height>bar.Height)return null;
        var right=Math.Min(trayLeft-8,bar.Right-8);
        var left=bar.Left+8;
        var obstacles=occupied.Where(r=>r.Intersects(bar)).OrderByDescending(r=>r.Right);
        foreach(var rect in obstacles)
        {
            if(rect.Left>=right || rect.Right<=left)continue;
            if(right-Math.Max(rect.Right+8,left)>=width)
                return new(right-width,bar.Top+(bar.Height-height)/2,width,height);
            right=Math.Min(right,rect.Left-8);
        }
        return right-left>=width?new(right-width,bar.Top+(bar.Height-height)/2,width,height):null;
    }
}
