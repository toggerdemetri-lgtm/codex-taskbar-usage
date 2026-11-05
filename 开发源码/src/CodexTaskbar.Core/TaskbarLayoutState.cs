namespace CodexTaskbar.Core;

public sealed record TaskbarGeometry(nint Handle,PixelRect Bar,int TrayLeft,uint Dpi,IReadOnlyList<PixelRect> Occupied,string? Error,bool Transient=false,string BoundarySource="uia");

public sealed class TaskbarLayoutState
{
    private TaskbarGeometry? lastGood;
    private PixelRect? lastSlot;
    private DateTimeOffset lastSuccess;
    private int lastWidth;
    public bool UsingPrevious {get;private set;}
    public PixelRect? Resolve(TaskbarGeometry geometry,DateTimeOffset now,int widthDip=240)
    {
        UsingPrevious=false;
        if(geometry.Error is not null)
        {
            if(geometry.Transient && lastGood is not null && lastSlot is not null &&
               geometry.Handle==lastGood.Handle && geometry.Bar==lastGood.Bar && geometry.Dpi==lastGood.Dpi && geometry.TrayLeft==lastGood.TrayLeft &&
               widthDip==lastWidth && now>=lastSuccess && now-lastSuccess<=TimeSpan.FromSeconds(3))
            {UsingPrevious=true;return lastSlot;}
            lastGood=null;lastSlot=null;return null;
        }
        var scale=geometry.Dpi/96d;
        var slot=SlotLayout.Find(geometry.Bar,geometry.TrayLeft,(int)Math.Round(widthDip*scale),Math.Min(geometry.Bar.Height-4,(int)Math.Round(36*scale)),geometry.Occupied);
        lastGood=slot.HasValue?geometry:null;lastSlot=slot;lastSuccess=now;lastWidth=widthDip;
        return slot;
    }
}
