using System.Drawing;
using CodexTaskbar;
using CodexTaskbar.Core;

namespace CodexTaskbar.WindowsTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="app-server")return QuotaClientRecoveryTests.RunFakeServer();
        var failures=QuotaClientRecoveryTests.RunAsync().GetAwaiter().GetResult();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        foreach(var context in new nint[]{-2,-4})
        {
            var previous=Native.SetThreadDpiAwarenessContext(context);
            using var parent=new Form{ShowInTaskbar=false};
            _=parent.Handle;
            Native.SetThreadDpiAwarenessContext(previous);
            using var child=TaskbarSurface.CreateForTaskbar(parent.Handle);
            _=child.Handle;
            if(!child.MatchesDpiContext(parent.Handle)){failures++;Console.WriteLine("FAIL matching parent DPI context "+context);}
            else Console.WriteLine("PASS matching parent DPI context "+context);
            var style=Native.GetWindowLong(child.Handle,Native.GwlStyle);
            Native.SetWindowLong(child.Handle,Native.GwlStyle,(style&~Native.WsPopup)|Native.WsChild);
            Native.SetParent(child.Handle,parent.Handle);
            if(Native.GetParent(child.Handle)!=parent.Handle || !child.MatchesDpiContext(parent.Handle))
            {failures++;Console.WriteLine("FAIL native parenting without DPI reset "+context);}
            else Console.WriteLine("PASS native parenting without DPI reset "+context);
            var restored=Native.SetThreadDpiAwarenessContext(-4);
            if(!Native.AreDpiAwarenessContextsEqual(restored,previous)){failures++;Console.WriteLine("FAIL thread DPI context restored");}
            else Console.WriteLine("PASS thread DPI context restored");
            Native.SetThreadDpiAwarenessContext(restored);
        }
        using var fixture=new Form{Text="Codex compatibility test",ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=new(40,40,320,160),Opacity=0.01};
        var tabs=new TabControl{Bounds=new(0,0,300,120)};tabs.TabPages.Add("one");tabs.TabPages.Add("two");fixture.Controls.Add(tabs);
        fixture.Shown+=async(_,_)=>
        {
            try
            {
                var handle=tabs.Handle;
                var batch=await Task.Run(()=>LegacyTaskbarAccessibility.Read(handle));
                if(!batch.Complete || !batch.Controls.Any(c=>c.Role==TaskbarControlRole.TabItem))
                {failures++;Console.WriteLine("FAIL native classic MSAA tab child traversal");}
                else Console.WriteLine("PASS native classic MSAA tab child traversal");
            }
            catch(Exception e){failures++;Console.WriteLine("FAIL MSAA fixture: "+e.GetType().Name);}
            finally{fixture.Close();}
        };
        Application.Run(fixture);
        Console.WriteLine(failures==0?"Windows integration passed":"Windows integration failed");
        return failures==0?0:1;
    }
}
