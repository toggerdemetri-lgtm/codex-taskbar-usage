using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace CodexTaskbar;

internal sealed class StartupRegistration(string executable,string startupFolder,string registryPath)
{
    private string Command=>"\""+executable+"\" --start";
    private string ShortcutPath=>Path.Combine(startupFolder,AppBrand.Name+".lnk");
    private string LegacyExecutable=>Path.Combine(Path.GetDirectoryName(executable)!,"CodexTaskbar.exe");
    internal bool Enabled {get{using var key=Registry.CurrentUser.OpenSubKey(registryPath);return string.Equals(key?.GetValue("CodexTaskbar") as string,Command,StringComparison.OrdinalIgnoreCase);}}
    internal bool MigrateLegacy()
    {
        if(string.Equals(executable,LegacyExecutable,StringComparison.OrdinalIgnoreCase))return false;
        using(var key=Registry.CurrentUser.OpenSubKey(registryPath))
            if(!string.Equals(key?.GetValue("CodexTaskbar") as string,"\""+LegacyExecutable+"\" --start",StringComparison.OrdinalIgnoreCase))return false;
        SetEnabled(true);return true;
    }
    internal void SetEnabled(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(registryPath);
        if(!enabled){File.Delete(ShortcutPath);RemoveOwnedLegacyShortcut();key.DeleteValue("CodexTaskbar",false);return;}
        Directory.CreateDirectory(startupFolder);
        var previous=File.Exists(ShortcutPath)?File.ReadAllBytes(ShortcutPath):null;
        var temporary=Path.Combine(startupFolder,"CodexTaskbar-"+Guid.NewGuid().ToString("N")+".lnk");
        try
        {
            WriteShortcut(temporary);
            File.Move(temporary,ShortcutPath,true);
            try{key.SetValue("CodexTaskbar",Command,RegistryValueKind.String);}
            catch
            {
                if(previous is null)File.Delete(ShortcutPath);else File.WriteAllBytes(ShortcutPath,previous);
                throw;
            }
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        RemoveOwnedLegacyShortcut();
    }
    private void RemoveOwnedLegacyShortcut()
    {
        var path=Path.Combine(startupFolder,"CodexTaskbar.lnk");
        if(!File.Exists(path))return;
        var type=Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("Windows快捷方式服务不可用");
        dynamic shell=Activator.CreateInstance(type)!;
        dynamic? link=null;
        try
        {
            link=shell.CreateShortcut(path);var target=(string)link.TargetPath;
            if(string.Equals(target,LegacyExecutable,StringComparison.OrdinalIgnoreCase)||string.Equals(target,executable,StringComparison.OrdinalIgnoreCase))File.Delete(path);
        }
        finally{if(link is not null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
    }
    private void WriteShortcut(string path)
    {
        var type=Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("Windows快捷方式服务不可用");
        dynamic shell=Activator.CreateInstance(type)!;
        dynamic? link=null;
        try
        {
            link=shell.CreateShortcut(path);link.TargetPath=executable;link.Arguments="--autostart";
            link.WorkingDirectory=Path.GetDirectoryName(executable);link.IconLocation=executable+",0";
            link.Description=AppBrand.Name+"：登录后启动，跟随组件开机启动开关";link.Save();
        }
        finally{if(link is not null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
    }
}
