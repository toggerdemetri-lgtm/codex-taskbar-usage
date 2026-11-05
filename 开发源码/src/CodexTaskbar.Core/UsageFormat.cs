namespace CodexTaskbar.Core;

public static class UsageFormat
{
    public static string Compact(long? tokens)=>tokens switch
    {
        null=>"--",>=1_000_000_000=>(tokens.Value/1_000_000_000m).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"B",
        >=1_000_000=>(tokens.Value/1_000_000m).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"M",
        >=1_000=>(tokens.Value/1_000m).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"K",
        _=>tokens.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
    public static string ResetTime(long? seconds,TimeZoneInfo? zone=null)
    {
        if(!seconds.HasValue)return "--";
        try{return TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(seconds.Value),zone??TimeZoneInfo.Local).ToString("MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture);}
        catch(ArgumentOutOfRangeException){return "--";}
    }
}
