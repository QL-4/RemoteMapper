// RemoteKeyTest.cs - compare Up, then verify every device-specific F13-F19 remap.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

class RemoteKeyTest {
    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLL { public uint vk, scan, flags, time; public IntPtr extra; }

    const int WH_KEYBOARD_LL=13, WM_KEYDOWN=0x100, WM_KEYUP=0x101, WM_SYSKEYDOWN=0x104, WM_SYSKEYUP=0x105;
    static readonly uint[] expected={0x26,0x7C,0x7D,0x7E,0x7F,0x80,0x81,0x82};
    static readonly string[] physical={"方向上（对照）", "音量加", "音量减", "返回键", "主页键", "菜单键", "直播键", "电源键"};
    static HookProc proc=Hook;
    static IntPtr hook;
    static volatile bool armed;
    static volatile bool got;
    static volatile bool released;
    static uint gotVk, gotScan, gotFlags;
    static StreamWriter log;

    static IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam) {
        if(nCode>=0 && armed) {
            KBDLL k=(KBDLL)Marshal.PtrToStructure(lParam,typeof(KBDLL));
            bool down=wParam==(IntPtr)WM_KEYDOWN || wParam==(IntPtr)WM_SYSKEYDOWN;
            bool up=wParam==(IntPtr)WM_KEYUP || wParam==(IntPtr)WM_SYSKEYUP;
            if(down && !got) {
                gotVk=k.vk; gotScan=k.scan; gotFlags=k.flags; got=true;
                // Swallow only remote-specific F13-F20. Physical keyboard keys remain usable.
                if(k.vk>=0x7C && k.vk<=0x83)return (IntPtr)1;
            }
            if(up && got && k.vk==gotVk) {
                released=true;
                if(k.vk>=0x7C && k.vk<=0x83)return (IntPtr)1;
            }
        }
        return CallNextHookEx(hook,nCode,wParam,lParam);
    }

    static string Name(uint vk) {
        if(vk>=0x70 && vk<=0x87)return "F"+(vk-0x6F);
        return "VK_0x"+vk.ToString("X2");
    }

    static void Print(string s) { Console.WriteLine(s); log.WriteLine(s); log.Flush(); }

    [STAThread]
    static void Main() {
        Console.OutputEncoding=new UTF8Encoding(false);
        log=new StreamWriter("remote-keys.log",false,new UTF8Encoding(false));
        hook=SetWindowsHookEx(WH_KEYBOARD_LL,proc,GetModuleHandle(null),0);
        if(hook==IntPtr.Zero) { Print("ERROR: SetWindowsHookEx failed."); log.Close(); return; }

        int passed=0;
        Print("=== 小米遥控器八键驱动测试 ===");
        Print("先测方向上作对照，再测驱动映射的七个键；每键等待 15 秒。\n");
        for(int i=0;i<expected.Length;i++) {
            got=false; released=false; gotVk=gotScan=gotFlags=0; armed=true;
            Console.Write("[{0}/{1}] 请按 [{2}] ... ",i+1,expected.Length,physical[i]);
            log.Write("[{0}/{1}] {2}: ",i+1,expected.Length,physical[i]); log.Flush();
            Stopwatch sw=Stopwatch.StartNew();
            while(!got && sw.ElapsedMilliseconds<15000) { Application.DoEvents(); Thread.Sleep(10); }
            if(!got) {
                armed=false; Print("TIMEOUT (无按键事件)");
                continue;
            }
            while(!released && sw.ElapsedMilliseconds<17000) { Application.DoEvents(); Thread.Sleep(10); }
            armed=false;
            bool ok=gotVk==expected[i];
            string result=string.Format("VK=0x{0:X2} ({1}) scan=0x{2:X2} flags=0x{3:X2}  {4}",gotVk,Name(gotVk),gotScan,gotFlags,ok?"PASS":"FAIL");
            Print(result);
            if(ok)passed++;
            Thread.Sleep(250);
        }

        Print(string.Format("\nRESULT: {0}/{1} PASS",passed,expected.Length));
        Print(passed==expected.Length ? "对照键与七个驱动映射键全部成功。" : "测试未全部通过，请保留窗口并告知结果。");
        UnhookWindowsHookEx(hook); log.Close();
        Console.WriteLine("\n窗口将在 5 秒后关闭...");
        Thread.Sleep(5000);
        Environment.ExitCode=passed==expected.Length?0:1;
    }
}
