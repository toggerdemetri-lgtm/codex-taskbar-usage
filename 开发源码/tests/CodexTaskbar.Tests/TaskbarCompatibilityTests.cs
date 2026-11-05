using CodexTaskbar.Core;

internal static class TaskbarCompatibilityTests
{
    public static IEnumerable<(string,Action)> All()
    {
        var bar=new PixelRect(0,1032,1920,40);
        yield return ("Windows 10 tabs and list items cannot be covered",()=>{
            var occupied=TaskbarControls.Occupied(bar,new TaskbarControlBatch([
                new(TaskbarControlRole.TabItem,new(0,1032,1300,40)),
                new(TaskbarControlRole.ListItem,new(1300,1032,200,40))],true));
            Check(occupied is {Count:2});
            Check(SlotLayout.Find(bar,1600,240,36,occupied!) is null);
        });
        yield return ("classic MSAA buttons merge with modern UIA bounds",()=>{
            var occupied=TaskbarControls.Occupied(bar,
                new TaskbarControlBatch([new(TaskbarControlRole.Button,new(0,1032,50,40))],true),
                new TaskbarControlBatch([new(TaskbarControlRole.TabItem,new(100,1032,900,40))],true));
            Check(occupied is {Count:2});
            var slot=SlotLayout.Find(bar,1600,240,36,occupied!);
            Check(slot.HasValue && !occupied!.Any(slot.Value.Intersects));
        });
        yield return ("partial accessibility reads never establish safe empty space",()=>{
            Check(TaskbarControls.Occupied(bar,new TaskbarControlBatch([new(TaskbarControlRole.Button,new(0,1032,50,40))],false)) is null);
            Check(TaskbarControls.Occupied(bar,new TaskbarControlBatch([],true),new TaskbarControlBatch([new(TaskbarControlRole.Button,new(0,1032,50,40))],false)) is null);
        });
        yield return ("complete provider survives another provider failing",()=>{
            var occupied=TaskbarControls.Occupied(bar,
                new TaskbarControlBatch([new(TaskbarControlRole.Button,new(0,1032,50,40))],true),
                new TaskbarControlBatch([new(TaskbarControlRole.Button,new(50,1032,1870,40))],false));
            Check(occupied is {Count:1} && SlotLayout.Find(bar,1600,240,36,occupied).HasValue);
        });
        yield return ("task-list-only MSAA cannot replace failed whole-taskbar UIA",()=>{
            var taskList=new TaskbarControlBatch([new(TaskbarControlRole.TabItem,new(500,1032,1000,40))],true);
            Check(TaskbarControls.Occupied(bar,new TaskbarControlBatch([],false),taskList) is null);
            Check(TaskbarControls.Occupied(bar,new TaskbarControlBatch([],true),taskList) is null);
        });
        yield return ("containers hidden controls and the widget are excluded",()=>{
            var occupied=TaskbarControls.Occupied(bar,new TaskbarControlBatch([
                new(TaskbarControlRole.Container,bar),
                new(TaskbarControlRole.TabItem,bar,false),
                new(TaskbarControlRole.Button,bar,true,true),
                new(TaskbarControlRole.Button,new(0,0,100,100)),
                new(TaskbarControlRole.Button,new(0,1032,40,40)),
                new(TaskbarControlRole.Button,new(0,1032,40,40))],true));
            Check(occupied is {Count:1});
        });
        yield return ("Windows 10 small taskbar retains two complete text rows",()=>{
            foreach(var dpi in new[]{1d,1.25,1.5,2}){
                var height=(int)Math.Round(30*dpi)-4;
                var scale=UsageLayout.FontScale(dpi,height);
                Check(scale>0 && scale<=dpi && 15*scale<=height/2d+0.001);
            }
        });
        yield return ("normal Windows 10 and 11 retain existing text size",()=>{
            foreach(var dpi in new[]{1d,1.25,1.5,2})
                Check(UsageLayout.FontScale(dpi,(int)Math.Round(36*dpi))==dpi);
        });
        yield return ("small taskbar accounts for actual Chinese font line spacing",()=>{
            foreach(var lineHeight in new[]{15.240234375,16d,18d}){
                var scale=UsageLayout.FontScale(1,26,lineHeight);
                Check(lineHeight*scale<=13.001);
            }
        });
    }
    private static void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}
}
