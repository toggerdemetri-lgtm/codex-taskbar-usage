using System.Runtime.InteropServices;
using System.Text;
using CodexTaskbar.Core;

namespace CodexTaskbar;

internal static class Native
{
    internal const int GwlStyle=-16, WsChild=0x40000000, WsPopup=unchecked((int)0x80000000);
    internal const uint SwpNoActivate=0x10, SwpNoZOrder=4, SwpShowWindow=0x40;
    internal delegate bool EnumProc(nint hwnd,nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; public readonly PixelRect Pixels => new(Left,Top,Right-Left,Bottom-Top); }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern nint FindWindow(string? cls,string? title);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent,EnumProc callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd,StringBuilder value,int length);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll",SetLastError=true)] internal static extern nint SetParent(nint child,nint parent);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] internal static extern int SetWindowLong(nint hwnd,int index,int value);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll",SetLastError=true)] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(nint first,nint second);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd,int command);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetLayeredWindowAttributes(nint hwnd,uint colorKey,byte alpha,uint flags);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("gdi32.dll",SetLastError=true)] internal static extern bool BitBlt(nint destination,int x,int y,int width,int height,nint source,int sourceX,int sourceY,uint operation);
    [StructLayout(LayoutKind.Sequential)] internal struct Point {public int X,Y;public Point(int x,int y){X=x;Y=y;}}
    [StructLayout(LayoutKind.Sequential)] internal struct Size {public int Width,Height;public Size(int width,int height){Width=width;Height=height;}}
    [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Blend {public byte Operation,Flags,Alpha,Format;}
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool UpdateLayeredWindow(nint hwnd,nint destination,ref Point location,ref Size size,nint source,ref Point origin,uint key,ref Blend blend,uint flags);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    internal static string Class(nint hwnd) {var text=new StringBuilder(256);GetClassName(hwnd,text,text.Capacity);return text.ToString();}
    internal static PixelRect Bounds(nint hwnd) => GetWindowRect(hwnd,out var rect)?rect.Pixels:default;
    internal static List<nint> Children(nint hwnd) {var result=new List<nint>();EnumChildWindows(hwnd,(child,_)=>{result.Add(child);return true;},0);return result;}
}
