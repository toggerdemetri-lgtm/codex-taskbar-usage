using CodexTaskbar.Core;

internal static class FormatTests
{
    internal static IEnumerable<(string,Action)> All()
    {
        yield return ("token display distinguishes zero from unknown",()=>Check(UsageFormat.Compact(null)=="--"&&UsageFormat.Compact(0)=="0"));
        yield return ("compact token formatting uses K and M",()=>Check(UsageFormat.Compact(696654)=="696.7K"&&UsageFormat.Compact(1234567)=="1.23M"));
        yield return ("reset timestamp crosses date in local timezone",()=> {
            var utc=DateTimeOffset.Parse("2026-11-05T20:30:00Z").ToUnixTimeSeconds();
            Check(UsageFormat.ResetTime(utc,TimeZoneInfo.CreateCustomTimeZone("test+8",TimeSpan.FromHours(8),"UTC+8","UTC+8"))=="11-06 04:30");
        });
        yield return ("missing reset timestamp remains unknown",()=>Check(UsageFormat.ResetTime(null)=="--"));
    }
    private static void Check(bool b){if(!b)throw new Exception("Assertion failed");}
}
