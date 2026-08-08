using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class KeyComboSenderSmoke {
    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLL { public uint vk, scan, flags, time; public IntPtr extra; }

    const int WH_KEYBOARD_LL=13, WM_KEYDOWN=0x100, WM_KEYUP=0x101, WM_SYSKEYDOWN=0x104, WM_SYSKEYUP=0x105;
    const uint LLKHF_INJECTED=0x10;
    static readonly List<string> events = new List<string>();
    static HookProc callback = Hook;
    static IntPtr hook;

    static IntPtr Hook(int code, IntPtr message, IntPtr data) {
        if(code>=0) {
            KBDLL k=(KBDLL)Marshal.PtrToStructure(data,typeof(KBDLL));
            bool down=message==(IntPtr)WM_KEYDOWN || message==(IntPtr)WM_SYSKEYDOWN;
            bool up=message==(IntPtr)WM_KEYUP || message==(IntPtr)WM_SYSKEYUP;
            if((down||up) && (k.flags&LLKHF_INJECTED)!=0 && (k.vk==0xA2 || k.vk==0x5A))
                events.Add(k.vk.ToString("X2")+":"+(down?"D":"U"));
        }
        return CallNextHookEx(hook,code,message,data);
    }

    [STAThread]
    static void Main() {
        hook=SetWindowsHookEx(WH_KEYBOARD_LL,callback,GetModuleHandle(null),0);
        if(hook==IntPtr.Zero) { Console.WriteLine("FAIL hook install"); Environment.Exit(1); }
        var timer=new System.Windows.Forms.Timer();
        timer.Interval=200;
        timer.Tick+=(s,e)=>{
            timer.Stop();
            ushort[] combo={0xA2,0x5A};
            bool sent=KeyComboSender.Tap(combo);
            var done=new System.Windows.Forms.Timer();
            done.Interval=250;
            done.Tick+=(s2,e2)=>{
                done.Stop();
                UnhookWindowsHookEx(hook);
                string actual=String.Join(",",events.ToArray());
                string expected="A2:D,5A:D,5A:U,A2:U";
                Console.WriteLine("actual="+actual);
                if(!sent||actual!=expected) Environment.Exit(1);
                Console.WriteLine("PASS KeyComboSender Ctrl+Z order");
                Application.Exit();
            };
            done.Start();
        };
        timer.Start();
        Application.Run();
    }
}
