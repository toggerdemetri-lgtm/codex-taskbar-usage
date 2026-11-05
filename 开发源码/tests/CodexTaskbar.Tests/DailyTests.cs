using CodexTaskbar.Core;

internal static class DailyTests
{
    internal static IEnumerable<(string,Action)> All()
    {
        yield return ("daily subtracts prior day baseline and ignores duplicate totals",()=>{
            var c=Counter(); c.Observe(UsageTests.Token(1000,"2026-11-04T15:59:00Z"));c.Observe(UsageTests.Token(1100,"2026-11-04T16:01:00Z"));c.Observe(UsageTests.Token(1100,"2026-11-04T16:01:00Z"));c.Observe(UsageTests.Token(1300));Check(c.Total==300);
        });
        yield return ("daily excludes next local day",()=>{var c=Counter();c.Observe(UsageTests.Token(200));c.Observe(UsageTests.Token(500,"2026-11-05T16:01:00Z"));Check(c.Total==200);});
        yield return ("daily ignores partial records",()=>{var c=Counter();c.Observe("{\"type\":");c.Observe(UsageTests.Token(20));Check(c.Total==20);});
    }
    private static DailyUsageCounter Counter(){var c=new DailyUsageCounter(new(2026,11,5),TimeZoneInfo.CreateCustomTimeZone("test",TimeSpan.FromHours(8),"+8","+8"));c.Observe("""{"type":"session_meta","payload":{"id":"root","cwd":"F:/test","source":"vscode","originator":"Codex Desktop"}}""");return c;}
    private static void Check(bool ok){if(!ok)throw new Exception("Assertion failed");}
}
