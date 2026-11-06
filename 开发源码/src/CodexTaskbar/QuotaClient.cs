using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexTaskbar.Core;

namespace CodexTaskbar;

internal sealed class QuotaClient:IDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly ConcurrentDictionary<long,TaskCompletionSource<JsonElement>> pending=new();
    private readonly CancellationTokenSource shutdown=new();
    private readonly QuotaRecoveryPolicy recovery=new();
    private readonly Func<string?> findExecutable;
    private readonly Func<ProcessStartInfo,Process?> startProcess;
    private readonly TimeSpan requestTimeout;
    private Process? process;
    private long requestId,accountVersion;
    private bool initialized;
    public long AccountVersion=>Interlocked.Read(ref accountVersion);
    public string? Executable {get;private set;}
    public event Action<bool>? Changed;
    public int ConsecutiveRecoverableFailures=>recovery.ConsecutiveFailures;
    public int AutomaticReconnectCount=>recovery.ReconnectCount;
    public DateTimeOffset? LastAutomaticReconnect=>recovery.LastReconnectAt;
    public int? QueryProcessId=>process is {HasExited:false} current?current.Id:null;

    internal QuotaClient(Func<string?>? findExecutable=null,Func<ProcessStartInfo,Process?>? startProcess=null,TimeSpan? requestTimeout=null)
    {this.findExecutable=findExecutable??FindCodex;this.startProcess=startProcess??Process.Start;this.requestTimeout=requestTimeout??TimeSpan.FromSeconds(20);}

    public async Task<QuotaSnapshot> RefreshAsync(string? limitId)
    {
        await gate.WaitAsync(shutdown.Token);
        try
        {
            await EnsureStartedAsync();
            var account=await RequestAsync("account/read",new {refreshToken=false});
            var identity=JsonValue.Child(account,"account");
            var authType=JsonValue.String(identity,"type");
            if(identity.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                throw new QuotaException("请先在 Codex 登录",clear:true);
            if(authType=="apiKey")throw new QuotaException("当前为 API Key 登录，无法查询订阅额度",clear:true);
            var version=AccountVersion;
            var result=await RequestAsync("account/rateLimits/read",null);
            if(version!=AccountVersion)throw new QuotaException("账号状态变化，正在重新查询",clear:true,retrySoon:true);
            var snapshot=QuotaParser.Parse(result,limitId,DateTimeOffset.UtcNow) with{IdentityVersion=version};
            if(snapshot.Windows.Count==0)throw new QuotaException("当前会话的额度类别暂不可用",clear:true);
            recovery.RecordSuccess();
            return snapshot;
        }
        catch(QuotaException e)
        {
            if(recovery.RecordFailure(e.Reconnectable,DateTimeOffset.UtcNow))
            {
                Reset();
                throw new QuotaException("额度查询连续失败，正在自动重连",inner:e,retrySoon:true);
            }
            throw;
        }
        catch(Exception e) when(e is IOException or InvalidOperationException or TimeoutException)
        {recovery.RecordFailure(false,DateTimeOffset.UtcNow);Reset();throw new QuotaException("Codex 连接中断，稍后重试",inner:e);}
        finally{gate.Release();}
    }

    private async Task EnsureStartedAsync()
    {
        if(process is {HasExited:false} && initialized)return;
        Reset();
        Executable=findExecutable();
        if(Executable is null)throw new QuotaException("未找到 codex.exe，请启动或安装 Codex 桌面版");
        var start=new ProcessStartInfo(Executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=AppContext.BaseDirectory};
        start.ArgumentList.Add("app-server");start.ArgumentList.Add("--listen");start.ArgumentList.Add("stdio://");
        start.ArgumentList.Add("-c");start.ArgumentList.Add("analytics.enabled=false");
        process=startProcess(start)??throw new IOException("Cannot launch Codex");
        var current=process;
        _=ReadOutputAsync(current);
        // Drain stderr without logging it: it can contain private environment details.
        _=DrainErrorsAsync(current);
        await RequestAsync("initialize",new {clientInfo=new{name="codex_taskbar",title=AppBrand.Name,version=typeof(QuotaClient).Assembly.GetName().Version?.ToString(3)??"1.3.0"}});
        await current.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
        await current.StandardInput.FlushAsync();initialized=true;
    }
    private async Task<JsonElement> RequestAsync(string method,object? parameters)
    {
        var id=Interlocked.Increment(ref requestId);
        var completion=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id]=completion;
        try
        {
            var current=process??throw new IOException("Codex stopped");
            await current.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{id,method,@params=parameters}));
            await current.StandardInput.FlushAsync();
            return await completion.Task.WaitAsync(requestTimeout,shutdown.Token);
        }
        finally{pending.TryRemove(id,out _);}
    }
    private async Task ReadOutputAsync(Process current)
    {
        try
        {
            while(!shutdown.IsCancellationRequested && await current.StandardOutput.ReadLineAsync(shutdown.Token) is { } line)
            {
                if(!ReferenceEquals(Volatile.Read(ref process),current))break;
                try
                {
                    using var doc=JsonDocument.Parse(line);var root=doc.RootElement;
                    var id=JsonValue.Long(root,"id");
                    if(id.HasValue && pending.TryRemove(id.Value,out var completion))
                    {
                        var error=JsonValue.Child(root,"error");
                        if(error.ValueKind==JsonValueKind.Object)
                        {
                            var message=JsonValue.String(error,"message")??"";
                            var auth=message.Contains("401",StringComparison.OrdinalIgnoreCase)||message.Contains("unauthorized",StringComparison.OrdinalIgnoreCase)||message.Contains("not logged",StringComparison.OrdinalIgnoreCase);
                            var code=JsonValue.Long(error,"code");
                            var reconnectable=!auth && code is -32603 or >=-32099 and <=-32000;
                            completion.TrySetException(new QuotaException(auth?"Codex 登录已失效，请重新登录":$"额度查询失败（RPC {code}）",auth,reconnectable:reconnectable));
                        }
                        else completion.TrySetResult(JsonValue.Child(root,"result").Clone());
                    }
                    else
                    {
                        var method=JsonValue.String(root,"method");
                        if(method=="account/updated"){Interlocked.Increment(ref accountVersion);Changed?.Invoke(true);}
                        else if(method=="account/rateLimits/updated")Changed?.Invoke(false);
                        // Never satisfy auth-refresh requests by reading credentials ourselves.
                        else if(method=="account/chatgptAuthTokens/refresh" && JsonValue.Child(root,"id").ValueKind!=JsonValueKind.Undefined)
                        {await current.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{id=JsonValue.Child(root,"id"),error=new{code=-32000,message="Please sign in through Codex."}}));await current.StandardInput.FlushAsync();}
                    }
                }
                catch(JsonException){ /* Non-protocol output is never displayed or persisted. */ }
            }
        }
        catch(Exception e) when(e is OperationCanceledException or IOException or InvalidOperationException or ObjectDisposedException){ }
        finally{if(ReferenceEquals(process,current))foreach(var request in pending.Values)request.TrySetException(new IOException("Codex connection closed"));}
    }
    private static async Task DrainErrorsAsync(Process current)
    {try{while(await current.StandardError.ReadLineAsync() is not null){ }}catch(Exception e) when(e is IOException or InvalidOperationException or ObjectDisposedException){ }}
    private void Reset()
    {
        initialized=false;var old=process;process=null;
        if(old is not null){try{if(!old.HasExited)old.Kill(entireProcessTree:true);}catch(InvalidOperationException){ }old.Dispose();}
        foreach(var request in pending.Values)request.TrySetException(new IOException("Codex connection reset"));
        pending.Clear();
    }
    internal static string? FindCodex()
    {
        foreach(var part in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {try{var candidate=Path.Combine(part.Trim('"'),"codex.exe");if(File.Exists(candidate))return candidate;}catch(ArgumentException){ }}
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(folder))return Directory.EnumerateFiles(folder,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return null;
    }
    public void Dispose(){shutdown.Cancel();Reset();shutdown.Dispose();}
}

internal sealed class QuotaException(string message,bool clear=false,Exception? inner=null,bool retrySoon=false,bool reconnectable=false):Exception(message,inner)
{public bool ClearSnapshot {get;}=clear;public bool RetrySoon {get;}=retrySoon;public bool Reconnectable {get;}=reconnectable;}
