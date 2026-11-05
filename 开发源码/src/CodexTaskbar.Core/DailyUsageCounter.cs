namespace CodexTaskbar.Core;

public sealed class DailyUsageCounter(DateOnly day,TimeZoneInfo zone)
{
    public DateOnly Day {get;}=day;
    public TimeZoneInfo Zone {get;}=zone;
    public long Total {get;private set;}
    private readonly SessionAccumulator accumulator=new("");
    private long previous;
    public void Observe(string line)
    {
        if(!accumulator.ApplyLine(line) || accumulator.Snapshot is not {} snapshot || snapshot.ActivityAt is not {} at || snapshot.TotalTokens is not {} total)return;
        var delta=total>=previous?total-previous:total;
        previous=total;
        if(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at,Zone).DateTime)==Day)Total=checked(Total+delta);
    }
}
