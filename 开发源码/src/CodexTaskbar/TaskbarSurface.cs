using Microsoft.Win32;
using CodexTaskbar.Core;

namespace CodexTaskbar;

internal sealed class TaskbarSurface:Form
{
    internal string QuotaText="额度 --";
    internal string TokenText="今日 --";
    internal string ResetLine="重置 --";
    internal double? Remaining;
    internal double DpiScale=1;
    internal bool Stale;
    internal int PaintCount;
    private static double LineHeightAtScaleOne
    {
        get{var family=SystemFonts.MessageBoxFont!.FontFamily;return 12d*family.GetLineSpacing(FontStyle.Regular)/family.GetEmHeight(FontStyle.Regular);}
    }
    internal double TextScale=>UsageLayout.FontScale(DpiScale,Height,LineHeightAtScaleOne);
    private TaskbarSurface()
    {
        Text=AppBrand.Name;Icon=AppBrand.WindowIcon;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
        Bounds=new(-10000,-10000,240,36);AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;AccessibleName=AppBrand.Name;AccessibleRole=AccessibleRole.Pane;
    }
    protected override bool ShowWithoutActivation=>true;
    internal static TaskbarSurface CreateForTaskbar(nint dpiParent=0)
    {
        var target=dpiParent!=0?dpiParent:Native.FindWindow("Shell_TrayWnd",null);
        var context=target!=0?Native.GetWindowDpiAwarenessContext(target):0;
        var previous=context!=0?Native.SetThreadDpiAwarenessContext(context):0;
        // WinForms caches DPI awareness in Control's constructor, before CreateHandle.
        try{var surface=new TaskbarSurface();_=surface.Handle;return surface;}
        finally{if(previous!=0)Native.SetThreadDpiAwarenessContext(previous);}
    }
    internal bool MatchesDpiContext(nint parent)=>Native.AreDpiAwarenessContextsEqual(Native.GetWindowDpiAwarenessContext(Handle),Native.GetWindowDpiAwarenessContext(parent));
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80|0x08000000|0x00080000;return p;}}
    protected override void OnPaint(PaintEventArgs e){RenderLayered();}
    internal bool RenderLayered()
    {
        if(IsDisposed||!IsHandleCreated||!Native.IsWindowVisible(Handle)||Native.GetParent(Handle)==0||Width<=0||Height<=0)return false;
        using var bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(bitmap)){PaintUsage(graphics,new Rectangle(0,0,Width,Height),DpiScale);}
        var screen=Native.GetDC(0);var memory=Native.CreateCompatibleDC(screen);var image=bitmap.GetHbitmap(Color.FromArgb(0));var old=Native.SelectObject(memory,image);
        try
        {
            var bounds=Native.Bounds(Handle);var parentBounds=Native.Bounds(Native.GetParent(Handle));var location=new Native.Point(bounds.Left-parentBounds.Left,bounds.Top-parentBounds.Top);var size=new Native.Size(Width,Height);var origin=new Native.Point(0,0);
            var blend=new Native.Blend{Alpha=255,Format=1};
            var rendered=Native.UpdateLayeredWindow(Handle,screen,ref location,ref size,memory,ref origin,0,ref blend,2);if(rendered)PaintCount++;return rendered;
        }
        finally{Native.SelectObject(memory,old);Native.DeleteObject(image);Native.DeleteDC(memory);Native.ReleaseDC(0,screen);}
    }
    internal void PaintUsage(Graphics graphics,Rectangle rectangle,double scale)
    {
        var light=IsLightTheme();var foreground=light?Color.FromArgb(30,30,30):Color.FromArgb(240,240,240);
        // Alpha 1 is visually transparent but keeps blank gaps inside the right-click hit area.
        graphics.Clear(Color.FromArgb(1,0,0,0));
        graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var textScale=UsageLayout.FontScale(scale,rectangle.Height,LineHeightAtScaleOne);
        using var font=new Font(SystemFonts.MessageBoxFont!.FontFamily,(float)(12*textScale),FontStyle.Regular,GraphicsUnit.Pixel);
        using var small=new Font(SystemFonts.MessageBoxFont!.FontFamily,(float)(11*textScale),FontStyle.Regular,GraphicsUnit.Pixel);
        var padding=(int)Math.Round(5*scale);var leftWidth=(int)Math.Round(78*scale);var half=rectangle.Height/2;
        var color=Stale?(light?Color.FromArgb(120,90,30):Color.FromArgb(235,189,110)):Remaining switch{<=10=>light?Color.FromArgb(180,35,35):Color.FromArgb(255,130,130),<=20=>light?Color.FromArgb(150,85,0):Color.FromArgb(240,190,100),_=>foreground};
        using var format=new StringFormat{LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter};
        using var quotaBrush=new SolidBrush(color);using var textBrush=new SolidBrush(foreground);using var resetBrush=new SolidBrush(light?Color.FromArgb(85,85,85):Color.FromArgb(190,190,190));
        graphics.DrawString(QuotaText,font,quotaBrush,new Rectangle(rectangle.Left+padding,rectangle.Top,leftWidth-padding,half),format);
        graphics.DrawString(TokenText,font,textBrush,new Rectangle(rectangle.Left+leftWidth,rectangle.Top,rectangle.Width-leftWidth-padding,half),format);
        graphics.DrawString(ResetLine,small,resetBrush,new Rectangle(rectangle.Left+padding,rectangle.Top+half,rectangle.Width-2*padding,rectangle.Height-half),format);
    }
    internal bool TextFits(double scale)
    {
        using var font=new Font(SystemFonts.MessageBoxFont!.FontFamily,(float)(12*scale),FontStyle.Regular,GraphicsUnit.Pixel);
        return TextRenderer.MeasureText(QuotaText,font,Size.Empty,TextFormatFlags.NoPadding).Width<=73*scale && TextRenderer.MeasureText(TokenText,font,Size.Empty,TextFormatFlags.NoPadding).Width<=157*scale;
    }
    private static bool IsLightTheme(){using var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");return key?.GetValue("SystemUsesLightTheme") is not int value||value!=0;}
}
