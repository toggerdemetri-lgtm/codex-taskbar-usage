using System.Drawing;

namespace CodexTaskbar;

internal static class AppBrand
{
    internal const string Name="Codex用量任务栏";
    internal const string ExecutableName=Name+".exe";
    internal static Icon WindowIcon {get;}=LoadIcon();
    private static Icon LoadIcon()
    {
        using var stream=typeof(AppBrand).Assembly.GetManifestResourceStream("CodexTaskbar.AppIcon")??throw new IOException("程序图标资源缺失");
        return new Icon(stream,32,32);
    }
}
