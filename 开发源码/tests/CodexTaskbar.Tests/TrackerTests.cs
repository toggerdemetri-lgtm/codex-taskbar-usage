using System.Text;
using CodexTaskbar.Core;

internal static class TrackerTests
{
    internal static IEnumerable<(string,Action)> All()
    {
        yield return ("daily bootstrap ignores valid JSON until newline",()=>WithLog((file,tracker)=>{var token=UsageTests.Token(77,DateTimeOffset.UtcNow.ToString("O"));File.AppendAllText(file,token);tracker.Scan();Check(tracker.DailyTokens==0);File.AppendAllText(file,"\n");tracker.Scan();Check(tracker.DailyTokens==77);}));
        yield return ("archived usage remains daily but is not selected",()=>WithLog((file,tracker)=>{var archive=Path.Combine(tracker.DirectoryPath,"archive");Directory.CreateDirectory(archive);using var combined=new SessionTracker(tracker.DirectoryPath,archive);File.AppendAllText(file,UsageTests.Token(55,DateTimeOffset.UtcNow.ToString("O"))+"\n");combined.Scan();Check(combined.DailyTokens==55);File.Move(file,Path.Combine(archive,"moved.jsonl"));combined.Scan();Check(combined.DailyTokens==55&&combined.Sessions.Count==0);}));
        yield return ("tracker bootstraps latest absolute usage",()=>WithLog((file,tracker)=>{File.AppendAllText(file,UsageTests.Token(10)+"\n"+UsageTests.Token(20)+"\n");tracker.Scan();Check(tracker.Sessions.Single().TotalTokens==20);}));
        yield return ("tracker waits for a complete appended line",()=>WithLog((file,tracker)=>{tracker.Scan();var token=UsageTests.Token(50);File.AppendAllText(file,token[..30]);tracker.Scan();Check(tracker.Sessions.Single().TotalTokens is null);File.AppendAllText(file,token[30..]+"\n");tracker.Scan();Check(tracker.Sessions.Single().TotalTokens==50);}));
        yield return ("tracker handles truncated logs",()=>WithLog((file,tracker)=>{File.AppendAllText(file,UsageTests.Token(900)+"\n");tracker.Scan();File.WriteAllText(file,Meta+"\n"+UsageTests.Token(3)+"\n");tracker.Scan();Check(tracker.Sessions.Single().TotalTokens==3);}));
        yield return ("tracker skips huge nonusage records",()=>WithLog((file,tracker)=>{File.AppendAllText(file,UsageTests.Token(123)+"\n"+new string('x',3_000_000)+"\n");tracker.Scan();Check(tracker.Sessions.Single().TotalTokens==123);File.AppendAllText(file,UsageTests.Token(456)+"\n");tracker.Scan();Check(tracker.Sessions.Single().TotalTokens==456);}));
        yield return ("tracker ignores removed sessions",()=>WithLog((file,tracker)=>{tracker.Scan();File.Delete(file);tracker.Scan();Check(tracker.Sessions.Count==0);}));
        yield return ("bootstrap preserves an incomplete tail",()=>WithLog((file,tracker)=>{var token=UsageTests.Token(234);File.AppendAllText(file,UsageTests.Token(123)+"\n"+token[..40]);tracker.Scan();Check(tracker.Sessions.Single().TotalTokens==123);File.AppendAllText(file,token[40..]+"\n");tracker.Scan();Check(tracker.Sessions.Single().TotalTokens==234);}));
        yield return ("new file with partial metadata is discovered after completion",()=>WithLog((file,tracker)=>{File.WriteAllText(file,Meta[..25]);tracker.Scan();Check(tracker.Sessions.Count==0);File.AppendAllText(file,Meta[25..]+"\n"+UsageTests.Token(77)+"\n");tracker.Scan();Check(tracker.Sessions.Single().TotalTokens==77);}));
        yield return ("truncate and regrow beyond prior size detects replacement usage",()=>WithLog((file,tracker)=>{File.AppendAllText(file,UsageTests.Token(900)+"\n");tracker.Scan();File.WriteAllText(file,Meta+"\n"+UsageTests.Token(3)+"\n"+new string('x',4000)+"\n");tracker.Scan();Check(tracker.Sessions.Single().TotalTokens==3);}));
    }
    private const string Meta="""{"type":"session_meta","payload":{"id":"root-0001","cwd":"F:/project","source":"vscode","originator":"Codex Desktop"}}""";
    private static void WithLog(Action<string,SessionTracker> action)
    {
        var dir=Path.Combine(Path.GetTempPath(),"codex-taskbar-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try{var file=Path.Combine(dir,"rollout.jsonl");File.WriteAllText(file,Meta+"\n",new UTF8Encoding(false));using var tracker=new SessionTracker(dir);action(file,tracker);}
        finally{Directory.Delete(dir,true);}
    }
    private static void Check(bool b){if(!b)throw new Exception("Assertion failed");}
}
