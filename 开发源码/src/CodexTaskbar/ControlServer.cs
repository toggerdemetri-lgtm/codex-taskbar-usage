using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace CodexTaskbar;

internal sealed class ControlServer:IDisposable
{
    internal static string InstanceName
    {
        get
        {
            var sid=WindowsIdentity.GetCurrent().User?.Value??Environment.UserName;
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
            return $"CodexTaskbar-{hash}-{Process.GetCurrentProcess().SessionId}";
        }
    }
    private readonly CancellationTokenSource shutdown=new();
    private readonly Func<ControlRequest,Task<string>> dispatch;
    public ControlServer(Func<ControlRequest,Task<string>> dispatch){this.dispatch=dispatch;_=ListenAsync();}
    private async Task ListenAsync()
    {
        while(!shutdown.IsCancellationRequested)
        {
            try
            {
                using var server=new NamedPipeServerStream(InstanceName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(shutdown.Token);
                using var reader=new StreamReader(server,Encoding.UTF8,false,4096,true);
                using var writer=new StreamWriter(server,new UTF8Encoding(false),4096,true){AutoFlush=true};
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var line=await reader.ReadLineAsync(timeout.Token);
                if(line is null || line.Length>8192)continue;
                var request=JsonSerializer.Deserialize<ControlRequest>(line);
                var response=request is null?"{\"error\":\"invalid request\"}":await dispatch(request).WaitAsync(timeout.Token);
                await writer.WriteLineAsync(response.AsMemory(),timeout.Token);
            }
            catch(Exception e) when(e is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException){ }
        }
    }
    internal static async Task<string?> SendAsync(string command,string? path=null)
    {
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var pipe=new NamedPipeClientStream(".",InstanceName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
            using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new ControlRequest(command,path)).AsMemory(),timeout.Token);
            return await reader.ReadLineAsync(timeout.Token);
        }
        catch(Exception e) when(e is IOException or OperationCanceledException or UnauthorizedAccessException){return null;}
    }
    public void Dispose(){shutdown.Cancel();shutdown.Dispose();}
}
internal sealed record ControlRequest(string Command,string? Path=null);
