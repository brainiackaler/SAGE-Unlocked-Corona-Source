using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

public static class TraceHooks
{
    public const string FramePattern = "51 53 56 8B F1 32 DB 38 9E 64 04 00 00 74 07 E8 ?? ?? ?? ?? B3 01 80 BE 90 04 00 00 00";
    public const string SetterPattern = "A1 ?? ?? ?? ?? 53 33 DB 38 98 BC 00 00 00 56 8B F1 74 09 38 5E 48";
    static bool enabled;
    static string previous = "";
    static void U(List<byte> code, uint n) { code.AddRange(BitConverter.GetBytes(n)); }
    static void Store(List<byte> code, uint destination) { code.Add(0xA3); U(code, destination); }
    static void Jump(List<byte> code, uint at, uint destination) { code.Add(0xE9); U(code, unchecked(destination-at-(uint)code.Count-4)); }

    public static byte[] FrameStub(uint at, uint next, uint data)
    {
        var code=new List<byte>(new byte[]{0x51,0x53,0x56,0x8B,0xF1,0x9C,0x50});
        code.AddRange(new byte[]{0xFF,0x05}); U(code,data+0x20);
        code.AddRange(new byte[]{0x89,0x35}); U(code,data+0x24);
        code.AddRange(new byte[]{0x8B,0x46,0x44}); Store(code,data+0x28);
        code.AddRange(new byte[]{0x8B,0x46,0x40}); Store(code,data+0x2C);
        code.AddRange(new byte[]{0x0F,0xB6,0x46,0x48}); Store(code,data+0x30);
        code.AddRange(new byte[]{0x8B,0x86,0x94,0x25,0,0}); Store(code,data+0x34);
        code.AddRange(new byte[]{0x58,0x9D}); Jump(code,at,next);
        return code.ToArray();
    }

    public static byte[] SetterStub(uint at,uint next,uint data,byte[] original)
    {
        var code=new List<byte>(original);
        code.AddRange(new byte[]{0x9C,0x50});
        code.AddRange(new byte[]{0xFF,0x05}); U(code,data+0x40);
        code.AddRange(new byte[]{0x89,0x0D}); U(code,data+0x44);
        code.AddRange(new byte[]{0x8B,0x44,0x24,0x0C}); Store(code,data+0x48);
        code.AddRange(new byte[]{0x8B,0x44,0x24,0x08}); Store(code,data+0x4C);
        code.AddRange(new byte[]{0x58,0x9D}); Jump(code,at,next);
        return code.ToArray();
    }

    static void Hook(uint site,uint stub,byte[] body,byte[] want)
    {
        byte[] actual=new byte[5]; Marshal.Copy((IntPtr)site,actual,0,5);
        for(int i=0;i<5;i++) if(actual[i]!=want[i]) throw new Exception("Trace site already changed: "+site.ToString("X"));
        Marshal.Copy(body,0,(IntPtr)stub,body.Length);
        byte[] jump=new byte[5];jump[0]=0xE9;
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(stub-site-5)),0,jump,1,4);
        uint old,ignored;
        if(!VirtualProtect((IntPtr)site,(UIntPtr)5,0x40,out old)) throw new Exception("Trace protection failed");
        Marshal.Copy(jump,0,(IntPtr)site,5);
        VirtualProtect((IntPtr)site,(UIntPtr)5,old,out ignored);
    }

    public static void Install(string dir,byte[] image,uint imageBase,uint block,uint data)
    {
        uint frame=imageBase+(uint)AutoZoom.FindUnique(image,FramePattern);
        uint setter=imageBase+(uint)AutoZoom.FindUnique(image,SetterPattern);
        byte[] original=new byte[5];Buffer.BlockCopy(image,(int)(setter-imageBase),original,0,5);
        Hook(setter,block+0x300,SetterStub(block+0x300,setter+5,data,original),original);
        enabled=true;
        AutoZoom.Log(dir,"Trace installed, frame=0x"+frame.ToString("X")+", setter=0x"+setter.ToString("X"));
    }

    static uint R(uint a){return unchecked((uint)Marshal.ReadInt32((IntPtr)a));}
    static string F(uint a){return BitConverter.ToSingle(BitConverter.GetBytes(R(a)),0).ToString(CultureInfo.InvariantCulture);}
    public static void Report(string dir,uint data)
    {
        if(!enabled) return;
        string snapshot="view=0x"+R(data+0x24).ToString("X")+", height="+F(data+0x28)+", pitch="+F(data+0x2C)+", lock="+R(data+0x30)+", mode="+R(data+0x34)+
            ", setters="+R(data+0x40)+", setView=0x"+R(data+0x44).ToString("X")+", setValue="+F(data+0x48)+", caller=0x"+R(data+0x4C).ToString("X");
        if(snapshot!=previous){previous=snapshot;AutoZoom.Log(dir,"trace frames="+R(data+0x20)+", "+snapshot);}
    }
    [DllImport("kernel32.dll")] static extern bool VirtualProtect(IntPtr a,UIntPtr n,uint p,out uint old);
}
