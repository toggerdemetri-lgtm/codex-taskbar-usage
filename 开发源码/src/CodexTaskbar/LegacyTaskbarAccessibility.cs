using System.Runtime.InteropServices;
using Accessibility;
using CodexTaskbar.Core;

namespace CodexTaskbar;

internal static class LegacyTaskbarAccessibility
{
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint window,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren(IAccessible accessible,int start,int count,[Out,MarshalAs(UnmanagedType.LPArray,SizeParamIndex=2)] object[] children,out int obtained);

    internal static TaskbarControlBatch Read(nint taskList)
    {
        var controls=new List<TaskbarControlSnapshot>();
        try
        {
            var iid=typeof(IAccessible).GUID;
            if(AccessibleObjectFromWindow(taskList,unchecked((uint)-4),ref iid,out var accessible)<0)return new([],false);
            var budget=2048;
            try{Visit(accessible,0,0,controls,ref budget);}
            finally{Release(accessible);}
            return new(controls,true);
        }
        catch(Exception e) when(e is COMException or InvalidCastException or ArgumentException or InvalidOperationException)
        {return new([],false);}
    }
    private static void Visit(IAccessible accessible,int childId,int depth,List<TaskbarControlSnapshot> controls,ref int budget)
    {
        if(--budget<0 || depth>8)throw new InvalidOperationException("Accessibility tree exceeds safe traversal limit");
        var state=(AccessibleStates)Convert.ToInt32(accessible.get_accState(childId));
        if((state&(AccessibleStates.Invisible|AccessibleStates.Offscreen))!=0)return;
        var role=(AccessibleRole)Convert.ToInt32(accessible.get_accRole(childId));
        var kind=role switch
        {
            AccessibleRole.PushButton or AccessibleRole.ButtonDropDown or AccessibleRole.ButtonMenu or AccessibleRole.ButtonDropDownGrid=>TaskbarControlRole.Button,
            AccessibleRole.CheckButton or AccessibleRole.RadioButton=>TaskbarControlRole.CheckBox,
            AccessibleRole.PageTab=>TaskbarControlRole.TabItem,
            AccessibleRole.ListItem=>TaskbarControlRole.ListItem,
            AccessibleRole.MenuItem=>TaskbarControlRole.MenuItem,
            AccessibleRole.Link=>TaskbarControlRole.Link,
            _=>TaskbarControlRole.Container
        };
        if(kind!=TaskbarControlRole.Container)
        {
            accessible.accLocation(out var left,out var top,out var width,out var height,childId);
            controls.Add(new(kind,new(left,top,width,height)));
        }
        if(childId!=0)return;
        var count=accessible.accChildCount;
        if(count<0 || count>budget)throw new InvalidOperationException("Accessibility child count is invalid");
        if(count==0)return;
        var children=new object[count];
        try
        {
            if(AccessibleChildren(accessible,0,count,children,out var obtained)!=0 || obtained!=count)
                throw new InvalidOperationException("Accessibility tree changed while reading");
            foreach(var child in children)
            {
                if(child is int id && id>0)Visit(accessible,id,depth+1,controls,ref budget);
                else if(child is IAccessible nested)Visit(nested,0,depth+1,controls,ref budget);
                else throw new InvalidOperationException("Unknown accessibility child");
            }
            if(accessible.accChildCount!=count)throw new InvalidOperationException("Accessibility tree changed while reading");
        }
        finally{foreach(var child in children)if(child is IAccessible nested)Release(nested);}
    }
    private static void Release(IAccessible accessible)
    {if(Marshal.IsComObject(accessible))Marshal.ReleaseComObject(accessible);}
}
