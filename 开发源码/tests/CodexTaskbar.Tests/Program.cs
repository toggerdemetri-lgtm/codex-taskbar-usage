using CodexTaskbar.Core;

var tests = new List<(string Name, Action Run)>
{
    ("slot stays left of notification area", () => {
        var slot = SlotLayout.Find(new(0, 1032, 1920, 48), 1600, 240, 36, [new(400,1032,700,48)]);
        Check(slot.HasValue && slot.Value.Right <= 1592 && slot.Value.Left >= 1100);
    }),
    ("slot never covers a taskbar button", () => {
        var buttons = new[] { new PixelRect(400,1032,1080,48) };
        var slot = SlotLayout.Find(new(0,1032,1920,48), 1600,240,36,buttons);
        Check(slot.HasValue && !buttons.Any(slot.Value.Intersects));
    }),
    ("full taskbar returns unavailable", () => Check(SlotLayout.Find(new(0,0,500,48),450,240,36,[new(0,0,450,48)]) is null)),
    ("slot supports nonzero monitor origin", () => {
        var slot=SlotLayout.Find(new(-1920,1000,1920,48),-300,240,36,[new(-1800,1000,600,48)]);
        Check(slot.HasValue && slot.Value.Left >= -1920 && slot.Value.Right <= -308);
    })
};
tests.AddRange(UsageTests.All());
tests.AddRange(TrackerTests.All());
tests.AddRange(FormatTests.All());
tests.AddRange(DailyTests.All());
tests.AddRange(LayoutStabilityTests.All());
tests.AddRange(TaskbarCompatibilityTests.All());
var failures=0;
foreach (var test in tests) { try { test.Run(); Console.WriteLine("PASS " + test.Name); } catch(Exception e) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + e.Message); } }
Console.WriteLine($"{tests.Count-failures}/{tests.Count} passed");
return failures == 0 ? 0 : 1;

static void Check(bool condition) { if(!condition) throw new Exception("Assertion failed"); }
