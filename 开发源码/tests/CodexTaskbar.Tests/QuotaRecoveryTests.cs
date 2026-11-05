using CodexTaskbar.Core;

internal static class QuotaRecoveryTests
{
    public static IEnumerable<(string,Action)> All()
    {
        var at=DateTimeOffset.Parse("2026-11-05T13:00:00Z");
        yield return ("three consecutive retryable failures request one reconnect",()=>{
            var policy=new QuotaRecoveryPolicy();
            Check(!policy.RecordFailure(true,at));Check(!policy.RecordFailure(true,at.AddSeconds(1)));
            Check(policy.RecordFailure(true,at.AddSeconds(2)) && policy.ReconnectCount==1 && policy.ConsecutiveFailures==0);
        });
        yield return ("successful quota refresh breaks failure streak",()=>{
            var policy=new QuotaRecoveryPolicy();policy.RecordFailure(true,at);policy.RecordFailure(true,at);
            policy.RecordSuccess();Check(policy.ConsecutiveFailures==0);
            Check(!policy.RecordFailure(true,at) && !policy.RecordFailure(true,at));
        });
        yield return ("authentication or protocol failures do not trigger reconnect",()=>{
            var policy=new QuotaRecoveryPolicy();policy.RecordFailure(true,at);policy.RecordFailure(true,at);
            Check(!policy.RecordFailure(false,at) && policy.ConsecutiveFailures==0);
            for(var i=0;i<10;i++)Check(!policy.RecordFailure(false,at));
            Check(policy.ReconnectCount==0 && !policy.RecordFailure(true,at));
        });
        yield return ("reconnect cooldown blocks manual refresh restart storms",()=>{
            var policy=new QuotaRecoveryPolicy();for(var i=0;i<3;i++)policy.RecordFailure(true,at);
            for(var i=1;i<120;i++)Check(!policy.RecordFailure(true,at.AddSeconds(i)));
            Check(policy.ReconnectCount==1 && policy.ConsecutiveFailures==3);
        });
        yield return ("reconnect resumes at two minute cooldown boundary",()=>{
            var policy=new QuotaRecoveryPolicy();for(var i=0;i<3;i++)policy.RecordFailure(true,at);
            policy.RecordFailure(true,at.AddSeconds(10));policy.RecordFailure(true,at.AddSeconds(20));
            Check(!policy.RecordFailure(true,at.AddSeconds(119)));
            Check(policy.RecordFailure(true,at.AddMinutes(2)) && policy.ReconnectCount==2);
        });
        yield return ("success clears streak without erasing reconnect diagnostics",()=>{
            var policy=new QuotaRecoveryPolicy();for(var i=0;i<3;i++)policy.RecordFailure(true,at);
            policy.RecordFailure(true,at.AddSeconds(1));policy.RecordSuccess();
            Check(policy.ConsecutiveFailures==0 && policy.ReconnectCount==1 && policy.LastReconnectAt==at);
        });
    }
    private static void Check(bool value){if(!value)throw new Exception("Assertion failed");}
}
