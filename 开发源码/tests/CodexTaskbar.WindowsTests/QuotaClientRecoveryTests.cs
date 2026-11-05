using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CodexTaskbar.Core;

namespace CodexTaskbar.WindowsTests;

internal static class QuotaClientRecoveryTests
{
    internal static async Task<int> RunAsync()
    {
        var failures=0;
        async Task Run(string name,Func<Task> test)
        {try{await test();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        await Run("RPC failures recreate only the query process and recover quota",async()=>
        {
            var starts=0;
            using var client=Client("recover",()=>++starts);
            for(var i=0;i<3;i++){var error=await Failure(client);Check(error.RetrySoon==(i==2),"third failure must request immediate retry");}
            var snapshot=await client.RefreshAsync("codex");
            Check(starts==2 && client.AutomaticReconnectCount==1 && client.ConsecutiveRecoverableFailures==0,"expected a new connection and reset streak");
            Check(snapshot.Selected?.Remaining==75 && snapshot.UpdatedAt>DateTimeOffset.UtcNow.AddSeconds(-5),"fresh successful quota is required");
        });
        await Run("successful quota interrupts real RPC failure streak",async()=>
        {
            var starts=0;using var client=Client("success-break",()=>++starts);
            await Failure(client);await Failure(client);await client.RefreshAsync("codex");await Failure(client);await Failure(client);
            Check(starts==1 && client.AutomaticReconnectCount==0 && client.ConsecutiveRecoverableFailures==2,"success must break the streak");
        });
        await Run("authentication failures request login without restart storms",async()=>
        {
            var starts=0;using var client=Client("auth",()=>++starts);
            for(var i=0;i<4;i++)Check((await Failure(client)).ClearSnapshot,"auth failures must clear identity-bound quota");
            Check(starts==1 && client.AutomaticReconnectCount==0,"authentication cannot be fixed by restarting");
        });
        await Run("protocol failures do not restart unsupported methods",async()=>
        {
            var starts=0;using var client=Client("protocol",()=>++starts);
            for(var i=0;i<4;i++)await Failure(client);
            Check(starts==1 && client.AutomaticReconnectCount==0,"permanent protocol errors must not restart");
        });
        await Run("query timeouts retain transport reset and recover next connection",async()=>
        {
            var starts=0;using var client=Client("timeout",()=>++starts,TimeSpan.FromMilliseconds(500));
            await Failure(client);var snapshot=await client.RefreshAsync("codex");
            Check(starts==2 && snapshot.Selected is not null,"broken transport must recover");
        });
        await Run("exit cancels a pending query and closes its child process",async()=>
        {
            var client=Client("timeout",()=>1);
            var query=client.RefreshAsync("codex");
            var deadline=DateTime.UtcNow.AddSeconds(5);
            while(client.QueryProcessId is null && DateTime.UtcNow<deadline)await Task.Delay(10);
            var processId=client.QueryProcessId??throw new Exception("query child did not start");
            using var child=Process.GetProcessById(processId);
            await Task.Delay(100);
            client.Dispose();
            try{await query.WaitAsync(TimeSpan.FromSeconds(3));throw new Exception("query unexpectedly succeeded");}
            catch(Exception e) when(e is OperationCanceledException or QuotaException){ }
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(child.HasExited,"query child survived exit");
        });
        return failures;
    }
    private static QuotaClient Client(string scenario,Func<int> nextConnection,TimeSpan? timeout=null)
    {
        var executable=Path.Combine(AppContext.BaseDirectory,"CodexTaskbar.WindowsTests.exe");
        return new(()=>executable,info=>
        {
            var runtime=new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
            var dotnetRoot=runtime.Parent!.Parent!.Parent!.FullName;
            info.Environment["DOTNET_ROOT"]=dotnetRoot;info.Environment["DOTNET_ROOT_X64"]=dotnetRoot;
            info.Environment["CODEX_TASKBAR_TEST_SCENARIO"]=scenario;
            info.Environment["CODEX_TASKBAR_TEST_CONNECTION"]=nextConnection().ToString();
            return Process.Start(info);
        },timeout);
    }
    private static async Task<QuotaException> Failure(QuotaClient client)
    {try{await client.RefreshAsync("codex");}catch(QuotaException e){return e;}throw new Exception("expected query failure");}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}

    internal static int RunFakeServer()
    {
        Console.OutputEncoding=new UTF8Encoding(false);
        var scenario=Environment.GetEnvironmentVariable("CODEX_TASKBAR_TEST_SCENARIO");
        var connection=int.Parse(Environment.GetEnvironmentVariable("CODEX_TASKBAR_TEST_CONNECTION")??"1");
        var requests=0;
        while(Console.ReadLine() is {} line)
        {
            using var document=JsonDocument.Parse(line);var request=document.RootElement;
            if(!request.TryGetProperty("id",out var id))continue;
            var method=request.GetProperty("method").GetString();
            object result=new{};object? error=null;
            if(method=="account/read")result=new{account=new{type="chatgpt",id="fixture-account"}};
            if(method=="account/rateLimits/read")
            {
                requests++;
                if(scenario=="timeout" && connection==1)continue;
                var fail=scenario is "auth" or "protocol" || scenario=="recover" && connection==1 || scenario=="success-break" && requests%3!=0;
                if(fail)error=new{code=scenario=="protocol"?-32601:-32603,message=scenario=="auth"?"401 unauthorized":"fixture failure"};
                else result=new{rateLimits=new{limitId="codex",primary=new{usedPercent=25,windowDurationMins=300,resetsAt=DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}};
            }
            Console.WriteLine(error is null?JsonSerializer.Serialize(new{id,result}):JsonSerializer.Serialize(new{id,error}));
        }
        return 0;
    }
}
