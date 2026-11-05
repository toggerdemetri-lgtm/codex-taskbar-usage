using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using CodexTaskbar.Core;

namespace CodexTaskbar;

internal sealed class TaskbarApplication:ApplicationContext
{
    private TaskbarSurface surface=null!;
    private TaskbarHost host=null!;
    private readonly ToolTip tooltip=new(){InitialDelay=300,ReshowDelay=100,AutoPopDelay=30000};
    private readonly System.Windows.Forms.Timer timer=new(){Interval=1000};
    private readonly SessionTracker tracker=new(Path.Combine(AppPaths.CodexHome,"sessions"),Path.Combine(AppPaths.CodexHome,"archived_sessions"));
    private readonly QuotaClient quotaClient=new();
    private readonly ControlServer control;
    private IReadOnlyList<SessionSnapshot> sessions=[];
    private SessionSnapshot? session;
    private long? dailyTokens;
    private QuotaSnapshot? quota;
    private string? quotaError,sessionError,quotaRequestBucket;
    private bool scanning,refreshing,closing,disposed;
    private Task<TaskbarGeometry>? measurement;
    private DateTimeOffset measurementStarted,lastScan=DateTimeOffset.MinValue,nextQuota=DateTimeOffset.MinValue;
    private DateTimeOffset? usageUpdated;
    private long? expiredRefresh;
    private int failedRefreshes;

    internal TaskbarApplication()
    {
        CreateSurface();
        quotaClient.Changed+=identityChanged=>Post(()=>{if(identityChanged){quota=null;quotaError="账号状态变化，正在刷新";}if(!refreshing)nextQuota=DateTimeOffset.MinValue;});
        timer.Tick+=(_,_)=>Tick();
        control=new(DispatchAsync);surface.Show();timer.Start();
    }
    private void CreateSurface()
    {
        surface=new TaskbarSurface();host=new(surface);
        var menu=new ContextMenuStrip();
        // WinForms initially cancels an empty menu; populate it before clearing cancellation.
        menu.Opening+=(_,e)=>{BuildMenu(menu);e.Cancel=false;};surface.ContextMenuStrip=menu;
    }
    private void Tick()
    {
        if(closing)return;
        if(surface.IsDisposed){CreateSurface();surface.Show();measurement=null;}
        var now=DateTimeOffset.UtcNow;
        if(measurement is null){measurementStarted=now;measurement=Task.Run(TaskbarHost.Measure);}
        else if(measurement.IsCompleted)
        {
            if(measurement.IsCompletedSuccessfully){surface.DpiScale=measurement.Result.Dpi/96d;host.Apply(measurement.Result);}
            else host.Apply(new(0,default,0,96,[],"任务栏边界读取失败，稍后重试"));
            measurement=null;
        }
        else if(now-measurementStarted>TimeSpan.FromSeconds(5))host.Apply(new(0,default,0,96,[],"Windows 任务栏暂时没有响应"));
        if(!scanning && now-lastScan>=TimeSpan.FromSeconds(2)){lastScan=now;_=ScanAsync();}
        var reset=quota?.Selected?.ResetsAt;
        if(reset.HasValue && now.ToUnixTimeSeconds()>=reset && expiredRefresh!=reset){expiredRefresh=reset;nextQuota=DateTimeOffset.MinValue;}
        if(!refreshing && now>=nextQuota)_=RefreshQuotaAsync();
        UpdateDisplay();
    }
    private async Task ScanAsync()
    {
        scanning=true;
        try
        {
            var found=await Task.Run(()=>{tracker.Scan(false);return (Sessions:tracker.Sessions,Daily:tracker.DailyTokens);});if(closing)return;
            sessions=found.Sessions;dailyTokens=found.Daily;session=SessionSelection.Choose(sessions,null);sessionError=null;usageUpdated=DateTimeOffset.UtcNow;
            var bucket=session?.LimitId??"codex";
            if(quotaRequestBucket is not null && quotaRequestBucket!=bucket){quota=null;nextQuota=DateTimeOffset.MinValue;}
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){sessionError="无法读取会话日志，稍后重试";}
        finally{scanning=false;if(!closing)UpdateDisplay();}
    }
    private async Task RefreshQuotaAsync()
    {
        refreshing=true;var requested=session?.LimitId??"codex";var retrySoon=false;
        try
        {
            var result=await quotaClient.RefreshAsync(requested);if(closing)return;
            if(requested==(session?.LimitId??"codex") && result.IsCurrentIdentity(quotaClient.AccountVersion))
            {quota=result;quotaRequestBucket=requested;quotaError=null;failedRefreshes=0;}
            else {quota=null;retrySoon=true;}
        }
        catch(QuotaException e){if(e.ClearSnapshot)quota=null;quotaError=e.Message;failedRefreshes++;retrySoon=e.RetrySoon;}
        catch(OperationCanceledException){ }
        finally
        {
            refreshing=false;nextQuota=DateTimeOffset.UtcNow.AddSeconds(Math.Min(300,60*Math.Max(1,failedRefreshes)));
            if(retrySoon || requested!=(session?.LimitId??"codex"))nextQuota=DateTimeOffset.MinValue;
            if(!closing)UpdateDisplay();
        }
    }
    private bool IsStale=>quota is not null && (quotaError is not null || DateTimeOffset.UtcNow-quota.UpdatedAt>TimeSpan.FromSeconds(120) || quota.Selected?.ResetsAt is {} reset && reset<=DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    private void UpdateDisplay()
    {
        var window=quota?.Selected;var stale=IsStale;
        surface.QuotaText=window is null?"额度 --":$"{window.Label}余 {window.Remaining:0}%"+(stale?"*":"");
        surface.TokenText=$"今日 {UsageFormat.Compact(dailyTokens)}";
        surface.ResetLine="重置 "+UsageFormat.ResetTime(window?.ResetsAt)+(stale?" · 数据过期":"");
        surface.Remaining=window?.Remaining;surface.Stale=stale;
        surface.AccessibleDescription=surface.QuotaText+"；"+surface.TokenText+"；"+surface.ResetLine;
        tooltip.SetToolTip(surface,TooltipText());surface.RenderLayered();
    }
    private string TooltipText()
    {
        var lines=new List<string>{"Codex 用量 · "+DateTime.Now.ToString("yyyy-MM-dd"),
            "今日累计："+(dailyTokens?.ToString("N0",CultureInfo.CurrentCulture)??"--")+" Token",
            "范围：本机全部桌面主会话，按本地日期统计增量（含归档）",
            "缓存与推理子项不重复相加；午夜自动开始新一天",
            "用量每2秒检查，额度通常每60秒刷新",
            "统计更新："+(usageUpdated?.ToLocalTime().ToString("MM-dd HH:mm:ss")??"--")};
        if(quota is not null)
        {
            foreach(var w in quota.Windows)
            {
                var countdown=w.ResetsAt is {} at?TimeSpan.FromSeconds(Math.Max(0,at-DateTimeOffset.UtcNow.ToUnixTimeSeconds())):(TimeSpan?)null;
                var remaining=countdown is {} t?$"（约 {(int)t.TotalHours}小时{t.Minutes}分后）":"";
                lines.Add($"{w.Label}额度剩余 {w.Remaining:0}% · 重置 {UsageFormat.ResetTime(w.ResetsAt)} {remaining}");
            }
            lines.Add("额度更新："+quota.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm:ss")+(IsStale?" · 数据已过期":""));
        }
        if(quotaError is not null)lines.Add(quotaError);if(sessionError is not null)lines.Add(sessionError);if(host.Error is not null)lines.Add(host.Error);
        return string.Join("\n",lines);
    }
    private void BuildMenu(ContextMenuStrip menu)
    {
        foreach(ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray())item.Dispose();menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem(host.Attached?"Codex 用量 · 已嵌入任务栏":"Codex 用量 · "+(host.Error??"初始化中")){Enabled=false});
        menu.Items.Add("立即刷新",null,(_,_)=>{nextQuota=DateTimeOffset.MinValue;lastScan=DateTimeOffset.MinValue;});
        menu.Items.Add("查看用量详情",null,(_,_)=>MessageBox.Show(TooltipText(),"Codex 用量详情",MessageBoxButtons.OK,MessageBoxIcon.Information));
        menu.Items.Add(new ToolStripSeparator());
        var startup=new ToolStripMenuItem("开机启动"){Checked=Settings.AutoStartEnabled};startup.Click+=(_,_)=>{try{Settings.SetAutoStart(!startup.Checked);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){MessageBox.Show("无法更新开机启动设置。","Codex 用量");}};menu.Items.Add(startup);
        menu.Items.Add("退出",null,(_,_)=>ExitThread());
    }
    private void Post(Action action)
    {if(closing||surface.IsDisposed)return;try{surface.BeginInvoke(()=>{if(!closing)action();});}catch(InvalidOperationException){ }}
    private Task<string> DispatchAsync(ControlRequest request)
    {
        var response=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(()=>
        {
            try
            {
                if(request.Command=="stop"){response.SetResult("{\"running\":false,\"stopping\":true}");_=Task.Delay(200).ContinueWith(_=>Post(ExitThread));return;}
                if(request.Command=="refresh"){nextQuota=DateTimeOffset.MinValue;lastScan=DateTimeOffset.MinValue;}
                if(request.Command=="menu" && surface.ContextMenuStrip is {} menu)
                {
                    var anchor=host.Bounds is {} slot?new Point(slot.Left,slot.Top):new Point(Screen.PrimaryScreen!.WorkingArea.Right-250,Screen.PrimaryScreen.WorkingArea.Bottom-40);
                    menu.Show(anchor,ToolStripDropDownDirection.AboveRight);
                }
                if(request.Command=="capture")
                {
                    if(!host.Attached)throw new InvalidOperationException("Taskbar not attached");
                    var path=Path.GetFullPath(request.Path??Path.Combine(AppPaths.DataDirectory,"taskbar.png"));
                    var bounds=Native.Bounds(host.Parent);
                    if(surface.ContextMenuStrip is {Visible:true} openMenu){var combined=Rectangle.Union(new Rectangle(bounds.Left,bounds.Top,bounds.Width,bounds.Height),openMenu.Bounds);bounds=new(combined.Left,combined.Top,combined.Width,combined.Height);}
                    using var bitmap=new Bitmap(bounds.Width,bounds.Height);using var graphics=Graphics.FromImage(bitmap);
                    var source=Native.GetDC(0);var destination=graphics.GetHdc();
                    try{if(!Native.BitBlt(destination,0,0,bounds.Width,bounds.Height,source,bounds.Left,bounds.Top,0x40CC0020))throw new InvalidOperationException("Capture failed");}
                    finally{graphics.ReleaseHdc(destination);Native.ReleaseDC(0,source);}
                    bitmap.Save(path,ImageFormat.Png);
                    response.SetResult(JsonSerializer.Serialize(new{running=true,capture=path}));return;
                }
                response.SetResult(StatusJson());
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {response.SetResult(JsonSerializer.Serialize(new{running=true,error=e.GetType().Name}));}
        });return response.Task;
    }
    private string StatusJson()=>JsonSerializer.Serialize(new{
        running=true,pid=Environment.ProcessId,attached=host.Attached,embeddingError=host.Error,
        parentClass=host.Parent==0?null:Native.Class(host.Parent),window=surface.Handle.ToInt64(),parent=host.Parent.ToInt64(),bounds=host.Bounds,keepingLastLayout=host.KeepingLastLayout,
        visible=Native.IsWindowVisible(surface.Handle),actualBounds=Native.Bounds(surface.Handle),style=Native.GetWindowLong(surface.Handle,Native.GwlStyle),
        exStyle=Native.GetWindowLong(surface.Handle,-20),paintCount=surface.PaintCount,
        trayIcon=false,menuVisible=surface.ContextMenuStrip?.Visible??false,menuItems=surface.ContextMenuStrip?.Items.Cast<ToolStripItem>().Select(i=>i.Text).ToArray(),
        rightClickAreaReady=host.Attached && new[]{new Point(2,2),new Point(surface.Width/2,surface.Height/2),new Point(surface.Width-3,surface.Height-3)}.All(p=>Native.WindowFromPoint(new Native.Point(Native.Bounds(surface.Handle).Left+p.X,Native.Bounds(surface.Handle).Top+p.Y))==surface.Handle),
        textFits=surface.TextFits(surface.DpiScale),quota=surface.QuotaText,tokens=surface.TokenText,reset=surface.ResetLine,stale=IsStale,
        sessionId=session?.Id,lockedSessionId=(string?)null,sessionCount=sessions.Count,sessionError,quotaError,
        totalTokens=dailyTokens,sessionTotalTokens=session?.TotalTokens,usageScope="localDay",lastQuotaUpdate=quota?.UpdatedAt,scales=new[]{1d,1.25,1.5,2}.Select(s=>new{scale=s,textFits=surface.TextFits(s)}).ToArray()
    });
    protected override void ExitThreadCore()
    {
        if(closing)return;closing=true;timer.Stop();host.Detach();surface.Close();base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing&&!disposed){disposed=true;closing=true;timer.Dispose();control.Dispose();quotaClient.Dispose();tracker.Dispose();tooltip.Dispose();surface.ContextMenuStrip?.Dispose();surface.Dispose();}
        base.Dispose(disposing);
    }
}
