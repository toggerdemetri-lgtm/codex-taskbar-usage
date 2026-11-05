namespace CodexTaskbar.Core;

public enum TaskbarControlRole { Container,Button,CheckBox,SplitButton,TabItem,ListItem,MenuItem,Link }
public sealed record TaskbarControlSnapshot(TaskbarControlRole Role,PixelRect Bounds,bool Visible=true,bool IsOwnWindow=false);
public sealed record TaskbarControlBatch(IReadOnlyList<TaskbarControlSnapshot> Controls,bool Complete);

public static class TaskbarControls
{
    public static IReadOnlyList<PixelRect>? Occupied(PixelRect bar,params TaskbarControlBatch[] batches)
    {
        // The first provider covers the entire taskbar. Later providers may only
        // cover the task list, so they cannot establish safety on their own.
        if(batches.Length==0 || !batches[0].Complete || !batches[0].Controls.Any(c=>Eligible(c,bar)))return null;
        var rectangles=batches.Where(b=>b.Complete).SelectMany(b=>b.Controls)
            .Where(c=>Eligible(c,bar))
            .Select(c=>c.Bounds).Distinct().ToArray();
        return rectangles.Length==0?null:rectangles;
    }
    private static bool Eligible(TaskbarControlSnapshot control,PixelRect bar)=>control.Visible && !control.IsOwnWindow
        && control.Bounds.Width>0 && control.Bounds.Height>0 && control.Bounds.Intersects(bar)
        && control.Role is TaskbarControlRole.Button or TaskbarControlRole.CheckBox or TaskbarControlRole.SplitButton
            or TaskbarControlRole.TabItem or TaskbarControlRole.ListItem or TaskbarControlRole.MenuItem or TaskbarControlRole.Link;
}
