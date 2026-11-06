using System.Text.Json;

namespace CodexTaskbar;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output=ValueAfter(args,"--output");
        if(args.Contains("--status")||args.Contains("--stop")||args.Contains("--capture")||args.Contains("--menu"))
        {
            var command=args.Contains("--stop")?"stop":args.Contains("--capture")?"capture":args.Contains("--menu")?"menu":"status";
            var response=ControlServer.SendAsync(command,command=="capture"?output:null).GetAwaiter().GetResult()??"{\"running\":false}";
            if(command!="capture" && output is not null)File.WriteAllText(Path.GetFullPath(output),response);
            if(command=="capture" && output is not null)File.WriteAllText(Path.GetFullPath(output)+".json",response);
            Console.WriteLine(response);return 0;
        }
        try
        {
            var autostart=args.Contains("--autostart");
            if(autostart)
            {
                if(!Settings.AutoStartEnabled)return 0;
                Thread.Sleep(TimeSpan.FromSeconds(15));
                if(!Settings.AutoStartEnabled)return 0;
            }
            using var mutex=new Mutex(true,"Local\\"+ControlServer.InstanceName,out var first);
            if(!first)
            {var response=ControlServer.SendAsync(args.Contains("--start")||autostart?"refresh":"menu").GetAwaiter().GetResult();Console.WriteLine(response??"{\"running\":true}");return response is null?2:0;}
            try
            {
            if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,19041))
            {MessageBox.Show("此版本需要Windows 10 2004及以上或Windows 11（x64）。","Codex 用量",MessageBoxButtons.OK,MessageBoxIcon.Information);return 1;}
            ApplicationConfiguration.Initialize();
            if(args.Contains("--diagnose"))
            {
                var g=TaskbarHost.Measure();
                File.WriteAllText(output??Path.Combine(AppContext.BaseDirectory,"diagnostics.json"),JsonSerializer.Serialize(new {handle=g.Handle.ToInt64(),g.Bar,g.TrayLeft,g.Dpi,g.Occupied,g.Error},new JsonSerializerOptions{WriteIndented=true}));
                return g.Error is null?0:1;
            }
            using var context=new TaskbarApplication();Application.Run(context);return 0;
            }
            finally{mutex.ReleaseMutex();}
        }
        catch(Exception e)
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(Path.Combine(AppPaths.DataDirectory,"errors.log"),$"{DateTimeOffset.Now:O} {e.GetType().Name}\n");
            return 1;
        }
    }
    internal static string? ValueAfter(string[] args,string name){var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
}
