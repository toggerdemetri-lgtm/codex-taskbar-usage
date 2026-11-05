using System.Text.Json;

namespace CodexTaskbar.Core;

public sealed record QuotaWindow(double UsedPercent,long? DurationMinutes,long? ResetsAt)
{
    public double Remaining=>Math.Clamp(100-UsedPercent,0,100);
    public string Label=>DurationMinutes switch {10080=>"周",300=>"5h",>=1440=> $"{DurationMinutes/1440}天",>0=> $"{DurationMinutes}分",_=>"额度"};
}
public sealed record QuotaSnapshot(string? AccountId,string? LimitId,IReadOnlyList<QuotaWindow> Windows,DateTimeOffset UpdatedAt)
{
    public long IdentityVersion {get;init;}
    public bool IsCurrentIdentity(long currentVersion)=>IdentityVersion==currentVersion;
    public QuotaWindow? Selected=>Windows.OrderBy(w=>w.Remaining).ThenBy(w=>w.DurationMinutes??long.MaxValue).FirstOrDefault();
}
public static class QuotaParser
{
    public static QuotaSnapshot Parse(JsonElement result,string? requestedLimitId,DateTimeOffset now)
    {
        var bucket=JsonValue.Child(result,"rateLimits");
        var limitId=JsonValue.String(bucket,"limitId");
        var buckets=JsonValue.Child(result,"rateLimitsByLimitId");
        if(!string.IsNullOrWhiteSpace(requestedLimitId) && buckets.ValueKind==JsonValueKind.Object && buckets.TryGetProperty(requestedLimitId,out var match))
        {bucket=match;limitId=requestedLimitId;}
        else if(!string.IsNullOrWhiteSpace(requestedLimitId) && requestedLimitId!="codex" && limitId!=requestedLimitId)
        {return new(JsonValue.String(result,"accountId"),requestedLimitId,[],now);}
        else if(buckets.ValueKind==JsonValueKind.Object && buckets.TryGetProperty("codex",out var common))
        {if(string.IsNullOrWhiteSpace(requestedLimitId)||requestedLimitId=="codex"){bucket=common;limitId="codex";}}
        var windows=new List<QuotaWindow>();
        foreach(var key in new[]{"primary","secondary"})
        {
            var window=JsonValue.Child(bucket,key);
            var used=JsonValue.Child(window,"usedPercent");
            if(used.ValueKind!=JsonValueKind.Number || !used.TryGetDouble(out var percent) || !double.IsFinite(percent))continue;
            var duration=JsonValue.Long(window,"windowDurationMins");
            var reset=JsonValue.Long(window,"resetsAt");
            if(reset is < -62135596800 or > 253402300799)reset=null;
            windows.Add(new(percent,duration is >0?duration:null,reset));
        }
        return new(JsonValue.String(result,"accountId"),limitId,windows,now);
    }
}
public sealed record SessionSnapshot(string Id,string Workdir,string LogPath,long? TotalTokens,long? InputTokens,long? CachedInputTokens,long? OutputTokens,DateTimeOffset? ActivityAt,string? Model,string? LimitId)
{
    public string ShortLabel=> $"{Path.GetFileName(Workdir.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))} · {Id[..Math.Min(8,Id.Length)]}";
}
public sealed class SessionAccumulator(string logPath)
{
    public string LogPath {get;}=logPath;
    public bool IsDesktopRoot {get;private set;}
    public bool HasMetadata {get;private set;}
    public SessionSnapshot? Snapshot {get;private set;}
    public bool ApplyLine(string line)
    {
        if(string.IsNullOrWhiteSpace(line))return false;
        try
        {
            using var doc=JsonDocument.Parse(line);
            var root=doc.RootElement;
            var type=JsonValue.String(root,"type");
            var payload=JsonValue.Child(root,"payload");
            if(type=="session_meta")
            {
                var id=JsonValue.String(payload,"id");
                var source=JsonValue.String(payload,"source");
                var origin=JsonValue.String(payload,"originator");
                HasMetadata=!string.IsNullOrWhiteSpace(id);
                IsDesktopRoot=!string.IsNullOrWhiteSpace(id) && source is "vscode" or "desktop" && string.Equals(origin,"Codex Desktop",StringComparison.OrdinalIgnoreCase);
                Snapshot=IsDesktopRoot?new(id!,JsonValue.String(payload,"cwd")??"",LogPath,null,null,null,null,null,null,null):null;
                return false;
            }
            if(!IsDesktopRoot || Snapshot is null)return false;
            if(type=="turn_context")
            {Snapshot=Snapshot with{Model=JsonValue.String(payload,"model")??Snapshot.Model};return false;}
            if(type!="event_msg" || JsonValue.String(payload,"type")!="token_count")return false;
            var usage=JsonValue.Child(JsonValue.Child(payload,"info"),"total_token_usage");
            var total=JsonValue.Long(usage,"total_tokens");
            if(total is null or <0)return false;
            DateTimeOffset? at=DateTimeOffset.TryParse(JsonValue.String(root,"timestamp"),out var parsed)?parsed:null;
            if(Snapshot.ActivityAt.HasValue && (!at.HasValue || at<Snapshot.ActivityAt))return false;
            Snapshot=Snapshot with{
                TotalTokens=total,InputTokens=JsonValue.Long(usage,"input_tokens"),
                CachedInputTokens=JsonValue.Long(usage,"cached_input_tokens"),OutputTokens=JsonValue.Long(usage,"output_tokens"),ActivityAt=at,
                LimitId=JsonValue.String(JsonValue.Child(payload,"rate_limits"),"limit_id")??Snapshot.LimitId
            };
            return true;
        }
        catch(JsonException){return false;}
    }
}
public static class SessionSelection
{
    public static SessionSnapshot? Choose(IEnumerable<SessionSnapshot> sessions,string? lockedId)=>
        lockedId is not null?sessions.FirstOrDefault(s=>s.Id==lockedId):sessions.Where(s=>s.ActivityAt.HasValue && s.TotalTokens.HasValue).OrderByDescending(s=>s.ActivityAt).ThenBy(s=>s.Id,StringComparer.Ordinal).FirstOrDefault();
}

public static class JsonValue
{
    public static JsonElement Child(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object && value.TryGetProperty(key,out var child)?child:default;
    public static string? String(JsonElement value,string key){var v=Child(value,key);return v.ValueKind==JsonValueKind.String?v.GetString():null;}
    public static long? Long(JsonElement value,string key){var v=Child(value,key);return v.ValueKind==JsonValueKind.Number&&v.TryGetInt64(out var n)?n:null;}
}
