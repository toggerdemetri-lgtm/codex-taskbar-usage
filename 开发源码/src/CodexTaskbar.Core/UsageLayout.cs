namespace CodexTaskbar.Core;

public static class UsageLayout
{
    // Use real font line spacing: Chinese system fonts can exceed 15px at 12px em.
    public static double FontScale(double dpiScale,int heightPixels,double lineHeightAtScaleOne=15)=>
        lineHeightAtScaleOne>0?Math.Max(0,Math.Min(dpiScale,heightPixels/(2*lineHeightAtScaleOne))):0;
}
