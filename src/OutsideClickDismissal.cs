using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsageTaskbar
{
    // Listen only while the panel is visible; never consume or record mouse input.
    internal sealed class OutsideClickDismissal : IDisposable
    {
        delegate IntPtr MouseHook(int code,IntPtr message,IntPtr data);
        [StructLayout(LayoutKind.Sequential)] struct MouseData {public Point Position;public uint Data,Flags,Time;public UIntPtr ExtraInfo;}
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int type,MouseHook callback,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        readonly Control owner;readonly Func<Point,bool> contains;readonly Action dismiss;readonly MouseHook callback;
        IntPtr hook;bool queued;
        public OutsideClickDismissal(Control control,Func<Point,bool> inside,Action close){owner=control;contains=inside;dismiss=close;callback=OnMouse;}
        public void Start(){queued=false;if(hook==IntPtr.Zero)hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);}
        public void Stop(){if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}queued=false;}
        IntPtr OnMouse(int code,IntPtr message,IntPtr data)
        {
            int kind=message.ToInt32();
            if(code>=0&&!queued&&owner.Visible&&(kind==0x201||kind==0x204||kind==0x207||kind==0x20b)) {
                var mouse=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));
                if(!contains(mouse.Position)) {
                    queued=true;
                    try {owner.BeginInvoke(new Action(delegate {queued=false;if(owner.Visible)dismiss();}));}
                    catch(InvalidOperationException){queued=false;}
                }
            }
            return CallNextHookEx(hook,code,message,data);
        }
        public void Dispose(){Stop();}
    }
}
