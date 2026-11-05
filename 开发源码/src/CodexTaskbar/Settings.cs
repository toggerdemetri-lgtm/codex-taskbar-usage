using System.Text.Json;
using Microsoft.Win32;

namespace CodexTaskbar;

internal static class AppPaths
{
    internal static string DataDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexTaskbar");
    internal static string CodexHome=>Environment.GetEnvironmentVariable("CODEX_HOME") is {Length:>0} home?home:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
    internal static string SettingsFile=>Path.Combine(DataDirectory,"settings.json");
}
internal sealed class Settings
{
    public string? LockedSessionId {get;set;}
    public static Settings Load()
    {try{return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile))??new();}catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return new();}}
    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var temp=AppPaths.SettingsFile+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,AppPaths.SettingsFile,true);
    }
    private const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private static string StartupCommand=>"\""+Path.Combine(AppContext.BaseDirectory,"CodexTaskbar.exe")+"\" --start";
    public static bool AutoStartEnabled {get{using var key=Registry.CurrentUser.OpenSubKey(RunKey);return string.Equals(key?.GetValue("CodexTaskbar") as string,StartupCommand,StringComparison.OrdinalIgnoreCase);}}
    public static void SetAutoStart(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(RunKey);
        if(enabled)key.SetValue("CodexTaskbar",StartupCommand,RegistryValueKind.String);
        else key.DeleteValue("CodexTaskbar",false);
    }
}
