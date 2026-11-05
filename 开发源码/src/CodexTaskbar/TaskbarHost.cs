using System.Windows.Automation;
using CodexTaskbar.Core;

namespace CodexTaskbar;

internal sealed class TaskbarHost(Form form)
{
    private nint parent;
    private PixelRect? lastSlot;
    private readonly TaskbarLayoutState layout=new();
    public bool Attached => parent!=0 && Native.IsWindow(parent) && Native.GetParent(form.Handle)==parent && lastSlot.HasValue;
    public string? Error {get;private set;}
    public PixelRect? Bounds => lastSlot;
    public IReadOnlyList<PixelRect> Occupied {get;private set;}=[];
    public bool KeepingLastLayout=>layout.UsingPrevious;
    public nint Parent => parent;
    public string BoundarySource {get;private set;}="uia";
    public bool NeedsDpiRecreate {get;private set;}

    // Executed on a dedicated worker: UI Automation can stall when Explorer is restarting.
    public static TaskbarGeometry Measure()
    {
        var taskbar=Native.FindWindow("Shell_TrayWnd",null);
        if(taskbar==0) return new(0,default,0,96,[],"等待 Windows 任务栏");
        var bar=Native.Bounds(taskbar);
        if(bar.Width < bar.Height || bar.Height < 10) return new(taskbar,bar,0,96,[],"首版仅支持水平任务栏");
        var children=Native.Children(taskbar);
        var tray=children.FirstOrDefault(h=>Native.Class(h)=="TrayNotifyWnd");
        if(tray==0) return new(taskbar,bar,0,96,[],"无法识别系统通知区域，已停止嵌入");
        var dpi=Native.GetDpiForWindow(taskbar);if(dpi==0)dpi=96;
        var trayLeft=Native.Bounds(tray).Left;
        var controls=new List<TaskbarControlSnapshot>();
        var uiaComplete=true;
        try
        {
            var root=AutomationElement.FromHandle(taskbar);
            var elements=root.FindAll(TreeScope.Descendants,new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.CheckBox),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.SplitButton),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Hyperlink)));
            foreach(AutomationElement element in elements)
            {
                var current=element.Current;
                if(current.IsOffscreen) continue;
                var r=current.BoundingRectangle;
                if(r.IsEmpty || r.Width<=0 || r.Height<=0)continue;
                var rect=new PixelRect((int)r.Left,(int)r.Top,(int)Math.Ceiling(r.Width),(int)Math.Ceiling(r.Height));
                controls.Add(new(TaskbarControlRole.Button,rect));
            }
        }
        catch(Exception ex) when(ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {uiaComplete=false;}
        var batches=new List<TaskbarControlBatch>{new(controls,uiaComplete)};
        // Classic Windows 10 task buttons also expose MSAA child IDs, even when UIA
        // does not expose them as buttons. Read their bounds without changing Explorer.
        var classic=children.Where(h=>Native.Class(h)=="MSTaskListWClass").Select(LegacyTaskbarAccessibility.Read).ToArray();
        batches.AddRange(classic);
        var occupied=TaskbarControls.Occupied(bar,batches.ToArray());
        if(occupied is null)return new(taskbar,bar,trayLeft,dpi,[],"任务栏控件边界暂不可用",true);
        var source=classic.Any(b=>b.Complete && b.Controls.Count>0)?"uia+msaa":"uia";
        return new(taskbar,bar,trayLeft,dpi,occupied,null,false,source);
    }

    public void Apply(TaskbarGeometry geometry,int widthDip=240)
    {
        if(form.IsDisposed)return;
        NeedsDpiRecreate=false;
        BoundarySource=geometry.BoundarySource;
        if(geometry.Handle!=0 && (!Native.IsWindow(geometry.Handle)||Native.Class(geometry.Handle)!="Shell_TrayWnd"))
        {Native.ShowWindow(form.Handle,0);lastSlot=null;Error="等待任务栏重新创建";return;}
        var slot=layout.Resolve(geometry,DateTimeOffset.UtcNow,widthDip);
        if(!layout.UsingPrevious)Occupied=geometry.Occupied;
        Error=slot is null?geometry.Error??"任务栏空白空间不足，请减少任务栏按钮后重试":null;
        if(slot is null){Native.ShowWindow(form.Handle,0);lastSlot=null;return;}
        if(form is TaskbarSurface dpiSurface && !dpiSurface.MatchesDpiContext(geometry.Handle))
        {
            NeedsDpiRecreate=true;Error="正在匹配任务栏DPI模式";Native.ShowWindow(form.Handle,0);lastSlot=null;return;
        }
        if(Native.GetParent(form.Handle)!=geometry.Handle)
        {
            var style=Native.GetWindowLong(form.Handle,Native.GwlStyle);
            Native.SetWindowLong(form.Handle,Native.GwlStyle,(style&~Native.WsPopup)|Native.WsChild);
            Native.SetParent(form.Handle,geometry.Handle);
            if(Native.GetParent(form.Handle)!=geometry.Handle){Error="任务栏嵌入失败";Native.ShowWindow(form.Handle,0);lastSlot=null;return;}
        }
        parent=geometry.Handle;
        var s=slot.Value;
        if(!Native.SetWindowPos(form.Handle,0,s.Left-geometry.Bar.Left,s.Top-geometry.Bar.Top,s.Width,s.Height,Native.SwpNoActivate|Native.SwpShowWindow))
        {Error="任务栏窗口定位失败";lastSlot=null;return;}
        lastSlot=s;
        if(form is TaskbarSurface surface && !surface.RenderLayered()){lastSlot=null;Error="透明窗口绘制失败";Native.ShowWindow(form.Handle,0);return;}
        form.Invalidate();
    }
    public void Detach(){if(!form.IsDisposed && form.IsHandleCreated){Native.ShowWindow(form.Handle,0);Native.SetParent(form.Handle,0);}parent=0;lastSlot=null;}
}

