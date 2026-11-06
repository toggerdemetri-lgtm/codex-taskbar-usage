using System.Runtime.InteropServices;
using CodexTaskbar;
using Microsoft.Win32;

namespace CodexTaskbar.WindowsTests;

internal static class StartupRegistrationTests
{
    internal static int Run()
    {
        var failures=0;
        void Test(string name,Action<Fixture> test)
        {
            using var fixture=new Fixture();
            try{test(fixture);Console.WriteLine("PASS "+name);}
            catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}
        }
        Test("enable installs direct executable shortcut alongside Run entry",f=>
        {
            f.Registration.SetEnabled(true);
            Check(f.Registration.Enabled,"Run switch must be enabled");
            Check(File.Exists(f.LinkPath),"native startup shortcut is missing");
            var link=ReadLink(f.LinkPath);
            Check(link.Target==f.Executable && link.Arguments=="--autostart","shortcut must launch the component directly");
        });
        Test("disable removes both startup routes and preserves unrelated entries",f=>
        {
            f.Registration.SetEnabled(true);
            WriteLink(f.LinkPath,f.Executable);
            File.WriteAllText(Path.Combine(f.Folder,"unrelated.txt"),"keep");
            using(var key=Registry.CurrentUser.CreateSubKey(f.KeyPath))key.SetValue("unrelated","keep");
            f.Registration.SetEnabled(false);
            Check(!f.Registration.Enabled && !File.Exists(f.LinkPath),"disabled component must have no launch route");
            using var read=Registry.CurrentUser.OpenSubKey(f.KeyPath);
            Check((string?)read?.GetValue("unrelated")=="keep" && File.Exists(Path.Combine(f.Folder,"unrelated.txt")),"unrelated startup data changed");
        });
        Test("failed shortcut creation does not enable a partial startup entry",f=>
        {
            Directory.Delete(f.Folder);File.WriteAllText(f.Folder,"blocked");
            try{f.Registration.SetEnabled(true);throw new Exception("expected shortcut creation failure");}
            catch(IOException){ }
            Check(!f.Registration.Enabled,"failed registration must not set the Run switch");
        });
        Test("re-enable refreshes the native shortcut after executable moves",f=>
        {
            f.Registration.SetEnabled(true);
            var moved=Path.Combine(f.Root,"new location","Codex用量任务栏.exe");
            var registration=new StartupRegistration(moved,f.Folder,f.KeyPath);
            registration.SetEnabled(true);
            var link=ReadLink(f.LinkPath);
            Check(registration.Enabled && link.Target==moved && link.Arguments=="--autostart","shortcut retained the previous location");
        });
        Test("rename migrates enabled legacy startup and removes the owned old shortcut",f=>
        {
            f.InstallLegacy();
            Check(f.Registration.MigrateLegacy(),"enabled legacy startup was not migrated");
            var link=ReadLink(f.LinkPath);
            Check(f.Registration.Enabled && link.Target==f.Executable && link.Arguments=="--autostart","renamed startup target is incorrect");
            Check(!File.Exists(f.LegacyLinkPath),"owned legacy shortcut remains");
        });
        Test("rename never enables a disabled startup preference",f=>
        {
            WriteLink(f.LegacyLinkPath,f.LegacyExecutable);
            Check(!f.Registration.MigrateLegacy() && !f.Registration.Enabled && !File.Exists(f.LinkPath),"rename enabled startup without consent");
        });
        Test("rename does not take over a different installation's startup",f=>
        {
            var other=Path.Combine(f.Root,"other installation","CodexTaskbar.exe");
            var command="\""+other+"\" --start";
            using(var key=Registry.CurrentUser.CreateSubKey(f.KeyPath))key.SetValue("CodexTaskbar",command);
            WriteLink(f.LegacyLinkPath,other);
            Check(!f.Registration.MigrateLegacy() && !File.Exists(f.LinkPath),"another installation was migrated");
            using var read=Registry.CurrentUser.OpenSubKey(f.KeyPath);
            Check((string?)read?.GetValue("CodexTaskbar")==command && ReadLink(f.LegacyLinkPath).Target==other,"unrelated registration changed");
        });
        Test("failed rename migration retains the previous startup command",f=>
        {
            f.InstallLegacy();Directory.Delete(f.Folder,true);File.WriteAllText(f.Folder,"blocked");
            try{f.Registration.MigrateLegacy();throw new Exception("expected migration failure");}catch(IOException){ }
            using var read=Registry.CurrentUser.OpenSubKey(f.KeyPath);
            Check((string?)read?.GetValue("CodexTaskbar")=="\""+f.LegacyExecutable+"\" --start","failed migration lost the legacy command");
        });
        return failures;
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static (string Target,string Arguments) ReadLink(string path)
    {
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic? link=null;
        try{link=shell.CreateShortcut(path);return ((string)link.TargetPath,(string)link.Arguments);}
        finally{if(link is not null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
    }
    private static void WriteLink(string path,string executable)
    {
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic? link=null;
        try{link=shell.CreateShortcut(path);link.TargetPath=executable;link.Arguments="--autostart";link.Save();}
        finally{if(link is not null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
    }
    private sealed class Fixture:IDisposable
    {
        internal string Root {get;}=Path.Combine(Path.GetTempPath(),"CodexTaskbar-startup-test-"+Guid.NewGuid().ToString("N"));
        internal string KeyPath {get;}="Software\\CodexTaskbar.Tests\\"+Guid.NewGuid().ToString("N");
        internal string Folder=>Path.Combine(Root,"Startup");
        internal string Executable=>Path.Combine(Root,"program with spaces","Codex用量任务栏.exe");
        internal string LegacyExecutable=>Path.Combine(Root,"program with spaces","CodexTaskbar.exe");
        internal string LinkPath=>Path.Combine(Folder,"Codex用量任务栏.lnk");
        internal string LegacyLinkPath=>Path.Combine(Folder,"CodexTaskbar.lnk");
        internal StartupRegistration Registration {get;}
        internal Fixture(){Directory.CreateDirectory(Folder);Registration=new(Executable,Folder,KeyPath);}
        internal void InstallLegacy()
        {
            using(var key=Registry.CurrentUser.CreateSubKey(KeyPath))key.SetValue("CodexTaskbar","\""+LegacyExecutable+"\" --start");
            WriteLink(LegacyLinkPath,LegacyExecutable);
        }
        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath,false);
            if(File.Exists(Folder))File.Delete(Folder);
            Directory.Delete(Root,true);
        }
    }
}
