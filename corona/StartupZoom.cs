using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class StartupZoom
{
    // The engine itself checks the same per-match logic-frame field before using live world objects.
    public const string ClockPattern="8B 0D ?? ?? ?? ?? 85 C9 74 ?? 83 79 50 03 76 ?? 85 FF 75 ??";
    sealed class Code
    {
        public readonly List<byte> Bytes=new List<byte>();
        readonly Dictionary<string,int> labels=new Dictionary<string,int>();
        readonly List<KeyValuePair<int,string>> fixups=new List<KeyValuePair<int,string>>();
        public void E(params byte[] b){Bytes.AddRange(b);}
        public void U(uint value){Bytes.AddRange(BitConverter.GetBytes(value));}
        public void Mark(string name){labels.Add(name,Bytes.Count);}
        public void Branch(byte condition,string label){E(0x0F,condition);fixups.Add(new KeyValuePair<int,string>(Bytes.Count,label));U(0);}
        public void Store(uint address){E(0xA3);U(address);}
        public byte[] Finish(uint address,uint resume)
        {
            E(0xE9);U(unchecked(resume-address-(uint)Bytes.Count-4));
            foreach(var f in fixups){byte[] offset=BitConverter.GetBytes(labels[f.Value]-f.Key-4);for(int i=0;i<4;i++)Bytes[f.Key+i]=offset[i];}
            return Bytes.ToArray();
        }
    }

    public static byte[] MakeFrameStub(uint at,uint resume,uint data,uint network,uint gameSystem,uint setter)
    {
        var c=new Code();
        c.E(0x51,0x53,0x56,0x8B,0xF1); // original prologue; esi is the live view, not a saved pointer
        c.E(0x9C,0x60); // pushfd, pushad
        c.E(0xFF,0x05);c.U(data+0x20);
        c.E(0x89,0x35);c.U(data+0x24);
        c.E(0x8B,0x46,0x44);c.Store(data+0x28);
        c.E(0x8B,0x46,0x40);c.Store(data+0x2C);
        c.E(0x0F,0xB6,0x46,0x48);c.Store(data+0x30);
        c.E(0x8B,0x86,0x94,0x25,0,0);c.Store(data+0x34);
        c.E(0x83,0x3D);c.U(data+0x50);c.E(1);c.Branch(0x85,"done");
        c.E(0x3B,0x35);c.U(data+8);c.Branch(0x85,"done");
        c.E(0x83,0x3D);c.U(network);c.E(0);c.Branch(0x85,"done");
        c.E(0xA1);c.U(gameSystem);c.E(0x85,0xC0);c.Branch(0x84,"done");
        c.E(0x8B,0x40,0x50);c.E(0x83,0xF8,3);c.Branch(0x82,"done");
        c.Store(data+0x5C);
        c.E(0x83,0xBE,0x94,0x25,0,0,0);c.Branch(0x85,"done");
        c.E(0x80,0x7E,0x48,0);c.Branch(0x84,"done");
        foreach(uint field in new uint[]{0x418,0x440,0x464,0x4B8,0x4B9,0x2737}) {
            c.E(0x80,0xBE);c.U(field);c.E(0);c.Branch(0x85,"done");
        }
        c.E(0x83,0x3D);c.U(data+0x70);c.E(30);c.Branch(0x83,"giveup");
        c.E(0xFF,0x05);c.U(data+0x70);
        // Preserve all FP/SIMD state around the normal engine setter. Its stdcall argument is cleaned by the callee.
        c.E(0x8B,0xEC,0x81,0xEC,0x10,2,0,0,0x83,0xE4,0xF0,0x0F,0xAE,0x04,0x24);
        c.E(0xFF,0x35);c.U(data+16);c.E(0x8B,0xCE,0xB8);c.U(setter);c.E(0xFF,0xD0);
        c.E(0x0F,0xAE,0x0C,0x24,0x8B,0xE5);
        c.E(0x8B,0x46,0x44);c.Store(data+0x68);
        c.E(0x3B,0x05);c.U(data+16);c.Branch(0x85,"done");
        c.E(0xFF,0x05);c.U(data+0x60);
        c.Mark("giveup");c.E(0xC7,0x05);c.U(data+0x50);c.U(0);
        c.Mark("done");c.E(0x61,0x9D);
        return c.Finish(at,resume);
    }

    public static void Install(string dir,byte[] image,uint imageBase,uint block,uint data,uint network)
    {
        uint frame=imageBase+(uint)AutoZoom.FindUnique(image,TraceHooks.FramePattern);
        uint setter=imageBase+(uint)AutoZoom.FindUnique(image,TraceHooks.SetterPattern);
        uint clock=BitConverter.ToUInt32(image,AutoZoom.FindUnique(image,ClockPattern)+2);
        byte[] want={0x51,0x53,0x56,0x8B,0xF1},live=new byte[5];
        Marshal.Copy((IntPtr)frame,live,0,5);
        for(int i=0;i<5;i++)if(live[i]!=want[i])throw new Exception("Live camera update already changed; startup extension skipped");
        uint stub=block+0x400;
        byte[] body=MakeFrameStub(stub,frame+5,data,network,clock,setter);
        if(body.Length>0x600)throw new Exception("Startup stub too large");
        Marshal.Copy(body,0,(IntPtr)stub,body.Length);
        byte[] patch=new byte[5];patch[0]=0xE9;
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(stub-frame-5)),0,patch,1,4);
        uint old,ignored;
        if(!VirtualProtect((IntPtr)frame,(UIntPtr)5,0x40,out old))throw new Exception("Startup hook protection failed");
        Marshal.Copy(patch,0,(IntPtr)frame,5);VirtualProtect((IntPtr)frame,(UIntPtr)5,old,out ignored);
        AutoZoom.Log(dir,"Startup hook installed, frame=0x"+frame.ToString("X")+", gameSystem=0x"+clock.ToString("X")+", setter=0x"+setter.ToString("X"));
    }
    [DllImport("kernel32.dll")]static extern bool VirtualProtect(IntPtr a,UIntPtr n,uint p,out uint old);
}
