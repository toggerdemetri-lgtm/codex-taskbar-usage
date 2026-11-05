namespace CodexTaskbar.Core;

public sealed class QuotaRecoveryPolicy
{
    public int ConsecutiveFailures {get;private set;}
    public int ReconnectCount {get;private set;}
    public DateTimeOffset? LastReconnectAt {get;private set;}
    public void RecordSuccess()=>ConsecutiveFailures=0;
    public bool RecordFailure(bool reconnectable,DateTimeOffset now)
    {
        ConsecutiveFailures=reconnectable?Math.Min(3,ConsecutiveFailures+1):0;
        if(ConsecutiveFailures<3 || LastReconnectAt is {} last && now-last<TimeSpan.FromMinutes(2))return false;
        ConsecutiveFailures=0;ReconnectCount++;LastReconnectAt=now;return true;
    }
}
