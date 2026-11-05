using System.Text;

namespace CodexTaskbar.Core;

public sealed class SessionTracker(string directory,string? archivedDirectory=null):IDisposable
{
    public string? ArchivedDirectory {get;}=archivedDirectory;
    public string DirectoryPath {get;}=directory;
    private readonly Dictionary<string,Cursor> files=new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? watcher;
    private int dirty=1;
    private DateTimeOffset lastDiscovery=DateTimeOffset.MinValue;
    public long DailyTokens=>files.Values.Where(c=>c.Accumulator.IsDesktopRoot).GroupBy(c=>c.Accumulator.Snapshot!.Id).Sum(g=>g.OrderByDescending(c=>c.Accumulator.Snapshot!.ActivityAt).First().Daily.Total);
    private bool IsArchived(string path)=>ArchivedDirectory is not null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(ArchivedDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<SessionSnapshot> Sessions=>files.Values.Where(c=>!IsArchived(c.LogPath)).Select(c=>c.Accumulator.Snapshot).OfType<SessionSnapshot>()
        .GroupBy(s=>s.Id).Select(g=>g.OrderByDescending(s=>s.ActivityAt).First()).OrderByDescending(s=>s.ActivityAt).ToArray();
    public void Scan(bool forceDiscovery=true)
    {
        if(!Directory.Exists(DirectoryPath)&&!Directory.Exists(ArchivedDirectory)){files.Clear();return;}
        if(watcher is null && Directory.Exists(DirectoryPath))
        {
            try{watcher=new(DirectoryPath,"*.jsonl"){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.Size|NotifyFilters.LastWrite};
                watcher.Created+=Changed;watcher.Deleted+=Changed;watcher.Renamed+=Changed;watcher.Error+=(_,_)=>Interlocked.Exchange(ref dirty,1);watcher.EnableRaisingEvents=true;}
            catch(IOException){watcher?.Dispose();watcher=null;}
        }
        if(forceDiscovery || Interlocked.Exchange(ref dirty,0)!=0 || DateTimeOffset.UtcNow-lastDiscovery>TimeSpan.FromSeconds(30))
        {
            var paths=new[]{DirectoryPath,ArchivedDirectory}.Where(p=>p is not null&&Directory.Exists(p)).SelectMany(p=>Directory.EnumerateFiles(p!,"*.jsonl",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true})).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var removed in files.Keys.Except(paths,StringComparer.OrdinalIgnoreCase).ToArray())files.Remove(removed);
            foreach(var path in paths)files.TryAdd(path,new Cursor(path));
            lastDiscovery=DateTimeOffset.UtcNow;
        }
        foreach(var cursor in files.Values)
        {
            try{cursor.Scan();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){ /* Writers may be replacing a rollout. Try next scan. */ }
        }
    }
    private void Changed(object sender,FileSystemEventArgs args)=>Interlocked.Exchange(ref dirty,1);
    public void Dispose(){watcher?.Dispose();watcher=null;}

    private sealed class Cursor(string path)
    {
        private const int MaxRecord=2*1024*1024;
        public string LogPath=>path;
        private long offset,lastLength=-1;
        private DateTime lastWrite;
        private byte[] prefix=[];
        private (long Offset,byte[] Bytes)? usageProof;
        public SessionAccumulator Accumulator {get;private set;}=new(path);
        public DailyUsageCounter Daily {get;private set;}=new(DateOnly.FromDateTime(DateTime.Now),TimeZoneInfo.Local);
        public void Scan()
        {
            var info=new FileInfo(path);
            if(!info.Exists)return;
            var length=info.Length;
            var day=DateOnly.FromDateTime(DateTime.Now);
            if(length==lastLength && info.LastWriteTimeUtc==lastWrite && Daily.Day==day)return;
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,FileOptions.RandomAccess);
            if(lastLength<0 || Daily.Day!=day || !Accumulator.HasMetadata || length<lastLength || (length==lastLength && info.LastWriteTimeUtc!=lastWrite) || !Matches(stream,0,prefix) || usageProof is {} proof && !Matches(stream,proof.Offset,proof.Bytes))
            {
                Accumulator=new(path);usageProof=null;
                stream.Position=0;
                using(var reader=new StreamReader(stream,Encoding.UTF8,true,4096,true))
                {var meta=reader.ReadLine();if(meta is not null && meta.Length<=MaxRecord)Accumulator.ApplyLine(meta);}
                offset=FindCompleteEnd(stream,length);
                if(Accumulator.IsDesktopRoot)
                {
                    var tokenFound=false;var modelFound=false;
                    foreach(var record in Backwards(stream,offset))
                    {
                        if(!tokenFound && record.Line.Contains("token_count",StringComparison.Ordinal))
                        {tokenFound=Accumulator.ApplyLine(record.Line);if(tokenFound)usageProof=(record.Offset,Encoding.UTF8.GetBytes(record.Line));}
                        else if(!modelFound && record.Line.Contains("turn_context",StringComparison.Ordinal)){Accumulator.ApplyLine(record.Line);modelFound=true;}
                        if(tokenFound&&modelFound)break;
                    }
                }
                Daily=new(day,TimeZoneInfo.Local);
                if(Accumulator.IsDesktopRoot)
                {
                    stream.Position=0;
                    var dailyBuffer=new byte[65536];using var dailyLine=new MemoryStream();var skip=false;
                    while(stream.Position<offset)
                    {
                        var count=stream.Read(dailyBuffer,0,(int)Math.Min(dailyBuffer.Length,offset-stream.Position));if(count==0)break;
                        for(var i=0;i<count;i++)
                            if(dailyBuffer[i]==10){if(!skip)Daily.Observe(Encoding.UTF8.GetString(dailyLine.GetBuffer(),0,(int)dailyLine.Length));dailyLine.SetLength(0);skip=false;}
                            else if(!skip){if(dailyLine.Length>=MaxRecord){skip=true;dailyLine.SetLength(0);}else dailyLine.WriteByte(dailyBuffer[i]);}
                    }
                }
                prefix=new byte[(int)Math.Min(512,length)];stream.Position=0;stream.ReadExactly(prefix);
            }
            else if(Accumulator.IsDesktopRoot)
            {
                stream.Position=offset;
                var buffer=new byte[65536];using var line=new MemoryStream();var skipped=false;var lineStart=offset;
                while(stream.Position<length)
                {
                    var start=stream.Position;
                    var count=stream.Read(buffer,0,(int)Math.Min(buffer.Length,length-stream.Position));if(count==0)break;
                    for(var i=0;i<count;i++)
                    {
                        if(buffer[i]==10)
                        {
                            if(!skipped){var completeLine=Encoding.UTF8.GetString(line.GetBuffer(),0,(int)line.Length);Daily.Observe(completeLine);if(Accumulator.ApplyLine(completeLine))usageProof=(lineStart,line.ToArray());}
                            offset=start+i+1;lineStart=offset;line.SetLength(0);skipped=false;
                        }
                        else if(!skipped)
                        {if(line.Length>=MaxRecord){skipped=true;line.SetLength(0);}else line.WriteByte(buffer[i]);}
                    }
                }
            }
            lastLength=length;lastWrite=info.LastWriteTimeUtc;
        }
        private static long FindCompleteEnd(FileStream stream,long length)
        {
            var buffer=new byte[65536];var position=length;
            while(position>0)
            {
                var count=(int)Math.Min(buffer.Length,position);position-=count;stream.Position=position;stream.ReadExactly(buffer.AsSpan(0,count));
                for(var i=count-1;i>=0;i--)if(buffer[i]==10)return position+i+1;
            }
            return 0;
        }
        private static bool Matches(FileStream stream,long at,byte[] expected)
        {
            if(expected.Length==0)return true;if(at+expected.Length>stream.Length)return false;
            var actual=new byte[expected.Length];stream.Position=at;stream.ReadExactly(actual);return actual.AsSpan().SequenceEqual(expected);
        }
        private static IEnumerable<(string Line,long Offset)> Backwards(FileStream stream,long end)
        {
            var buffer=new byte[65536];var reversed=new List<byte>();var skipped=false;var position=end;
            while(position>0)
            {
                var count=(int)Math.Min(buffer.Length,position);position-=count;stream.Position=position;stream.ReadExactly(buffer.AsSpan(0,count));
                for(var i=count-1;i>=0;i--)
                {
                    if(buffer[i]==10)
                    {
                        if(!skipped&&reversed.Count>0){reversed.Reverse();yield return (Encoding.UTF8.GetString(reversed.ToArray()),position+i+1);}
                        reversed.Clear();skipped=false;
                    }
                    else if(!skipped)
                    {if(reversed.Count>=MaxRecord){skipped=true;reversed.Clear();}else reversed.Add(buffer[i]);}
                }
            }
            if(!skipped&&reversed.Count>0){reversed.Reverse();yield return (Encoding.UTF8.GetString(reversed.ToArray()),0);}
        }
    }
}
