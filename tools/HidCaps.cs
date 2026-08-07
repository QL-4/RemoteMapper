// HidCaps.cs - open the Xiaomi keyboard HID with access=0 and inspect preparsed metadata.
// Safe diagnostic: it does not read/write live reports. HidP_SetUsages output is parser-synthesized
// canonical data and MUST NOT be treated as the device's on-wire byte layout.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

class HidCaps {
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA {
        public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS caps);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int reportType, IntPtr buttonCaps, ref ushort buttonCapsLength, IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_InitializeReportForID(int reportType, byte reportId, IntPtr p, byte[] report, uint reportLength);
    [DllImport("hid.dll")] static extern int HidP_SetUsages(int reportType, ushort usagePage, ushort linkCollection,
        [In] ushort[] usageList, ref uint usageLength, IntPtr p, byte[] report, uint reportLength);

    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr h, uint f);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr di, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA did);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr h, ref SP_DEVICE_INTERFACE_DATA did, IntPtr dd, uint size, ref uint req, IntPtr devInfo);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr CreateFileW(string n, uint access, uint share, IntPtr sa, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr h);

    const uint DIGCF_PRESENT=2, DIGCF_DEVICEINTERFACE=0x10;
    const uint FILE_SHARE_READ=1, FILE_SHARE_WRITE=2, OPEN_EXISTING=3;
    const int HIDP_INPUT=0;
    const int HIDP_STATUS_SUCCESS=0x00110000;

    static void Main() {
        Guid hid; HidD_GetHidGuid(out hid);
        IntPtr set=SetupDiGetClassDevs(ref hid,IntPtr.Zero,IntPtr.Zero,DIGCF_PRESENT|DIGCF_DEVICEINTERFACE);
        var paths=new List<string>();
        for(uint i=0;;i++) {
            var did=new SP_DEVICE_INTERFACE_DATA{cbSize=(uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA))};
            if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref hid,i,ref did)) break;
            string p=GetPath(set,ref did); if(p!=null && p.IndexOf("c05d39",StringComparison.OrdinalIgnoreCase)>=0) paths.Add(p);
        }
        SetupDiDestroyDeviceInfoList(set);
        Console.WriteLine("Xiaomi HID nodes: "+paths.Count);
        foreach(string path in paths) Dump(path);
    }

    static void Dump(string path) {
        Console.WriteLine("\nNODE: "+path);
        // Keyboard/mouse collections reject GENERIC_READ, but access=0 allows metadata/preparsed data.
        IntPtr h=CreateFileW(path,0,FILE_SHARE_READ|FILE_SHARE_WRITE,IntPtr.Zero,OPEN_EXISTING,0,IntPtr.Zero);
        if(h==new IntPtr(-1)) { Console.WriteLine("CreateFile(access=0) failed err="+Marshal.GetLastWin32Error()); return; }
        IntPtr pp;
        if(!HidD_GetPreparsedData(h,out pp)) { Console.WriteLine("HidD_GetPreparsedData failed err="+Marshal.GetLastWin32Error()); CloseHandle(h); return; }
        try {
            var caps=new HIDP_CAPS{Reserved=new ushort[17]};
            int st=HidP_GetCaps(pp,ref caps);
            Console.WriteLine(string.Format("HidP_GetCaps=0x{0:X8} topUsage=0x{1:X4}/0x{2:X4} inputLen={3} outputLen={4} featureLen={5} inputButtons={6} inputValues={7}",
                st,caps.UsagePage,caps.Usage,caps.InputReportByteLength,caps.OutputReportByteLength,caps.FeatureReportByteLength,caps.NumberInputButtonCaps,caps.NumberInputValueCaps));
            if(st!=HIDP_STATUS_SUCCESS || caps.InputReportByteLength==0) return;
            DumpButtonCaps(pp,caps.NumberInputButtonCaps);
            ushort[] usages={0x0080,0x0081,0x00F1,0x0068,0x0069,0x006A,0x0052};
            foreach(ushort usage in usages) Synthesize(pp,caps.InputReportByteLength,usage);
        } finally { HidD_FreePreparsedData(pp); CloseHandle(h); }
    }

    static void DumpButtonCaps(IntPtr pp, ushort count) {
        if(count==0)return;
        // HIDP_BUTTON_CAPS is 72 bytes on Windows; decode the stable leading fields and range union.
        const int size=72; IntPtr b=Marshal.AllocHGlobal(size*count);
        try {
            ushort n=count; int st=HidP_GetButtonCaps(HIDP_INPUT,b,ref n,pp);
            Console.WriteLine(string.Format("HidP_GetButtonCaps=0x{0:X8} count={1}",st,n));
            if(st!=HIDP_STATUS_SUCCESS)return;
            for(int i=0;i<n;i++) {
                IntPtr p=new IntPtr(b.ToInt64()+i*size);
                ushort page=(ushort)Marshal.ReadInt16(p,0); byte rid=Marshal.ReadByte(p,2);
                ushort bitField=(ushort)Marshal.ReadInt16(p,4); ushort link=(ushort)Marshal.ReadInt16(p,6);
                bool isRange=Marshal.ReadByte(p,12)!=0; ushort reportCount=(ushort)Marshal.ReadInt16(p,16);
                ushort usageA=(ushort)Marshal.ReadInt16(p,56); ushort usageB=(ushort)Marshal.ReadInt16(p,58);
                Console.WriteLine(string.Format("  cap[{0}] page=0x{1:X4} reportId=0x{2:X2} bitField={3} link={4} reportCount={5} isRange={6} usage={7}",
                    i,page,rid,bitField,link,reportCount,isRange,
                    isRange ? string.Format("0x{0:X4}..0x{1:X4}",usageA,usageB) : string.Format("0x{0:X4}",usageA)));
            }
        } finally { Marshal.FreeHGlobal(b); }
    }

    static void Synthesize(IntPtr pp, ushort len, ushort usage) {
        bool any=false;
        for(int rid=0;rid<256;rid++) {
            byte[] report=new byte[len];
            int init=HidP_InitializeReportForID(HIDP_INPUT,(byte)rid,pp,report,(uint)report.Length);
            if(init!=HIDP_STATUS_SUCCESS) continue;
            ushort[] list={usage}; uint n=1;
            int st=HidP_SetUsages(HIDP_INPUT,0x0007,0,list,ref n,pp,report,(uint)report.Length);
            if(st==HIDP_STATUS_SUCCESS) {
                Console.WriteLine(string.Format("usage 0x{0:X2}: reportId=0x{1:X2} parserSynth={2}",usage,rid,BitConverter.ToString(report)));
                any=true;
            }
        }
        if(!any) Console.WriteLine(string.Format("usage 0x{0:X2}: not in this collection",usage));
    }

    static string GetPath(IntPtr h, ref SP_DEVICE_INTERFACE_DATA did) {
        uint req=0; SetupDiGetDeviceInterfaceDetailW(h,ref did,IntPtr.Zero,0,ref req,IntPtr.Zero);
        if(req==0)return null; IntPtr b=Marshal.AllocHGlobal((int)req);
        try { Marshal.WriteInt32(b,8); if(!SetupDiGetDeviceInterfaceDetailW(h,ref did,b,req,ref req,IntPtr.Zero))return null; return Marshal.PtrToStringUni(new IntPtr(b.ToInt64()+4)); }
        finally { Marshal.FreeHGlobal(b); }
    }
}
