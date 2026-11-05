using CodexTaskbar.Core;

internal static class LayoutStabilityTests
{
    public static IEnumerable<(string,Action)> All()
    {
        var at=DateTimeOffset.Parse("2026-11-05T06:00:00Z");
        TaskbarGeometry Good()=>new(1,new(0,1032,1920,48),1600,96,[new(400,1032,700,48)],null);
        TaskbarGeometry Unavailable()=>Good() with{Occupied=[],Error="任务栏控件边界暂不可用",Transient=true};
        yield return ("layout survives temporary empty shell tree",()=>{
            var state=new TaskbarLayoutState();var expected=state.Resolve(Good(),at);
            Check(expected is not null && state.Resolve(Unavailable(),at.AddSeconds(2))==expected && state.UsingPrevious);
        });
        yield return ("layout grace expires without renewing on failures",()=>{
            var state=new TaskbarLayoutState();state.Resolve(Good(),at);
            state.Resolve(Unavailable(),at.AddSeconds(2));
            Check(state.Resolve(Unavailable(),at.AddSeconds(4)) is null);
        });
        yield return ("layout cannot reuse another Explorer or changed monitor geometry",()=>{
            foreach(var failure in new[]{Unavailable() with{Handle=2},Unavailable() with{Bar=new(0,1000,1920,48)},Unavailable() with{Dpi=144},Unavailable() with{TrayLeft=1500},Unavailable() with{Transient=false}}){
                var state=new TaskbarLayoutState();state.Resolve(Good(),at);
                Check(state.Resolve(failure,at.AddSeconds(1)) is null);
            }
        });
        yield return ("confirmed occupied space hides immediately and clears fallback",()=>{
            var state=new TaskbarLayoutState();state.Resolve(Good(),at);
            Check(state.Resolve(Good() with{Occupied=[new(0,1032,1920,48)]},at.AddSeconds(1)) is null);
            Check(state.Resolve(Unavailable(),at.AddSeconds(2)) is null);
        });
        yield return ("valid shell measurement recovers after grace expires",()=>{
            var state=new TaskbarLayoutState();var expected=state.Resolve(Good(),at);
            state.Resolve(Unavailable(),at.AddSeconds(4));
            Check(state.Resolve(Good(),at.AddSeconds(5))==expected && !state.UsingPrevious);
        });
    }
    private static void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}
}
