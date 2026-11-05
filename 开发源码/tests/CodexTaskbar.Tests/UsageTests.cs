using System.Text.Json;
using CodexTaskbar.Core;

internal static class UsageTests
{
    internal static IEnumerable<(string,Action)> All()
    {
        yield return ("inflight quota from prior identity is rejected at UI commit",()=>{
            var oldRead=Parse("""{"rateLimits":{"primary":{"usedPercent":20}}}""") with{IdentityVersion=4};
            Check(oldRead.IsCurrentIdentity(4)&&!oldRead.IsCurrentIdentity(5));
        });
        yield return ("primary weekly window is labelled weekly",()=> {
            var q=Parse("""{"rateLimits":{"primary":{"usedPercent":78,"windowDurationMins":10080,"resetsAt":1794024498},"secondary":null}}""");
            Check(q.Selected?.Label=="周" && q.Selected.Remaining==22);
        });
        yield return ("tightest quota retains its own reset",()=> {
            var q=Parse("""{"rateLimits":{"primary":{"usedPercent":10,"windowDurationMins":300,"resetsAt":100},"secondary":{"usedPercent":80,"windowDurationMins":10080,"resetsAt":200}}}""");
            Check(q.Selected?.ResetsAt==200 && q.Selected.Remaining==20);
        });
        yield return ("equal quota prefers shorter window",()=> {
            var q=Parse("""{"rateLimits":{"primary":{"usedPercent":20,"windowDurationMins":10080},"secondary":{"usedPercent":20,"windowDurationMins":300}}}""");
            Check(q.Selected?.DurationMinutes==300);
        });
        yield return ("unknown quota remains unknown",()=>Check(Parse("""{"rateLimits":{"primary":null,"secondary":null}}""").Selected is null));
        yield return ("zero percent remaining is a real value",()=>Check(Parse("""{"rateLimits":{"primary":{"usedPercent":100}}}""").Selected?.Remaining==0));
        yield return ("invalid and missing percentages are ignored",()=>Check(Parse("""{"rateLimits":{"primary":{"resetsAt":12},"secondary":{"usedPercent":"bad"}}}""").Selected is null));
        yield return ("requested quota bucket excludes unrelated models",()=> {
            var q=Parse("""{"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":20}},"special":{"primary":{"usedPercent":40}}}}""","special");
            Check(q.LimitId=="special" && q.Selected?.Remaining==60);
        });
        yield return ("absent category uses the general codex bucket",()=>Check(Parse("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":20}},"special":{"primary":{"usedPercent":99}}},"rateLimits":{}}""").Selected?.Remaining==80));
        yield return ("missing explicit category never borrows general quota",()=>Check(Parse("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":20}}},"rateLimits":{"primary":{"usedPercent":99}}}""","missing").Selected is null));
        yield return ("matching explicit legacy category is usable",()=>Check(Parse("""{"rateLimits":{"limitId":"special","primary":{"usedPercent":40}}}""","special").Selected?.Remaining==60));
        yield return ("percentage is bounded",()=>Check(Parse("""{"rateLimits":{"primary":{"usedPercent":150}}}""").Selected?.Remaining==0));
        yield return ("desktop metadata creates root session",()=>{var a=Root();Check(a.IsDesktopRoot && a.Snapshot?.Id=="root-0001");});
        yield return ("subagent cannot become current desktop session",()=> {
            var a=new SessionAccumulator("x");a.ApplyLine("""{"type":"session_meta","payload":{"id":"child","source":{"subagent":{"spawn":{"parent_thread_id":"root"}}},"originator":"Codex Desktop"}}""");
            Check(!a.IsDesktopRoot);
        });
        yield return ("CLI session is excluded",()=>{var a=new SessionAccumulator("x");a.ApplyLine("""{"type":"session_meta","payload":{"id":"cli","source":"cli","originator":"codex_cli_rs"}}""");Check(!a.IsDesktopRoot);});
        yield return ("token snapshot uses authoritative total",()=>{var a=Root();a.ApplyLine(Token(1234));Check(a.Snapshot?.TotalTokens==1234);});
        yield return ("duplicate snapshots do not accumulate cache or reasoning",()=>{var a=Root();a.ApplyLine(Token(1234));a.ApplyLine(Token(1234));Check(a.Snapshot?.TotalTokens==1234 && a.Snapshot.CachedInputTokens==800);});
        yield return ("partial and unrelated lines preserve counters",()=>{var a=Root();a.ApplyLine(Token(1234));a.ApplyLine("{\"type\":");a.ApplyLine("""{"type":"event_msg","payload":{"type":"agent_message","text":"token_count"}}""");Check(a.Snapshot?.TotalTokens==1234);});
        yield return ("late older token event cannot roll counters back",()=>{var a=Root();a.ApplyLine(Token(2000,"2026-11-05T08:00:00Z"));a.ApplyLine(Token(1000,"2026-11-05T07:00:00Z"));Check(a.Snapshot?.TotalTokens==2000);});
        yield return ("empty token info does not turn unknown into zero",()=>{var a=Root();a.ApplyLine("""{"type":"event_msg","payload":{"type":"token_count","info":null}}""");Check(a.Snapshot?.TotalTokens is null);});
        yield return ("current selection uses token activity not list order",()=>{var a=Root();a.ApplyLine(Token(1));var b=a.Snapshot! with{Id="other",ActivityAt=a.Snapshot!.ActivityAt!.Value.AddMinutes(1)};Check(SessionSelection.Choose([b,a.Snapshot!],null)?.Id=="other");});
        yield return ("session lock wins over recent activity",()=>{var a=Root();a.ApplyLine(Token(1));var b=a.Snapshot! with{Id="other",ActivityAt=a.Snapshot!.ActivityAt!.Value.AddMinutes(1)};Check(SessionSelection.Choose([a.Snapshot!,b],"root-0001")?.Id=="root-0001");});
        yield return ("missing lock does not silently switch sessions",()=>{var a=Root();a.ApplyLine(Token(1));Check(SessionSelection.Choose([a.Snapshot!],"missing") is null);});
    }
    internal static SessionAccumulator Root(){var a=new SessionAccumulator("test.jsonl");a.ApplyLine("""{"type":"session_meta","payload":{"id":"root-0001","cwd":"F:/project","source":"vscode","originator":"Codex Desktop"}}""");return a;}
    internal static string Token(long tokens,string at="2026-11-05T08:00:00Z")=>JsonSerializer.Serialize(new {timestamp=at,type="event_msg",payload=new {type="token_count",info=new {total_token_usage=new {input_tokens=1000,cached_input_tokens=800,output_tokens=234,reasoning_output_tokens=50,total_tokens=tokens}},rate_limits=new {limit_id="codex"}}});
    private static QuotaSnapshot Parse(string json,string? bucket=null){using var d=JsonDocument.Parse(json);return QuotaParser.Parse(d.RootElement,bucket,DateTimeOffset.UtcNow);}
    private static void Check(bool b){if(!b)throw new Exception("Assertion failed");}
}
