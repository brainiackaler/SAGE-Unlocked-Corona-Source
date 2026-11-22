// RA3 AutoZoom bridge. Original SAGE Unlocked binaries remain unchanged.
// Local extension by Codex, 2026. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

[assembly: AssemblyTitle("RA3 default camera distance extension")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace SAGEUnlocked
{
    // The original d3d9.dll calls this exact CLR entry point.
    public static class DllEntry
    {
        public static int Run(string dir)
        {
            int result;
            try
            {
                Assembly core = Assembly.LoadFile(Path.Combine(dir, "CnCFpsUnlocker.original.dll"));
                Type entry = core.GetType("SAGEUnlocked.DllEntry", true);
                result = (int)entry.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { dir });
            }
            catch (Exception error)
            {
                AutoZoom.Log(dir, "Original plugin failed to load: " + error);
                return 0;
            }
            if (result == 1)
            {
                try { AutoZoom.Install(dir); }
                catch (Exception error) { AutoZoom.Log(dir, "AutoZoom disabled: " + error.Message); }
            }
            return result;
        }
    }
}

public static class AutoZoom
{
    public const string ResetPattern = "8B 07 8B 50 04 8B CF F3 0F 11 46 40 FF D2 D9 5E 44 5F 88 9E 18 04 00 00 88 9E 40 04 00 00";
    public const string NetworkPattern = "8B 35 ?? ?? ?? ?? 3B F5 0F 84 ?? ?? ?? ?? 80 3D ?? ?? ?? ?? 00 0F 85 ?? ?? ?? ?? 80 3D ?? ?? ?? ?? 00 0F 85";
    static readonly byte[] OriginalCall = { 0xFF, 0xD2, 0xD9, 0x5E, 0x44 };
    static IntPtr allocation;

    public static void Log(string dir, string message)
    {
        try { File.AppendAllText(Path.Combine(dir, "SAGEAutoZoom.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine); }
        catch { }
    }

    static string ReadIni(string path, string key)
    {
        foreach (string line in File.ReadAllLines(path))
        {
            string text = line.Trim();
            if (text.Length == 0 || text[0] == ';' || text[0] == '#') continue;
            int equals = text.IndexOf('=');
            if (equals > 0 && text.Substring(0, equals).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return text.Substring(equals + 1).Split(';')[0].Trim();
        }
        return "";
    }

    // Mapping uses the file's PE section headers, not assumptions about file offsets.
    public static byte[] MapImage(byte[] file)
    {
        int pe = BitConverter.ToInt32(file, 0x3C), optional = pe + 24;
        if (BitConverter.ToUInt16(file, 0) != 0x5A4D || BitConverter.ToUInt32(file, pe) != 0x4550 ||
            BitConverter.ToUInt16(file, optional) != 0x10B)
            throw new InvalidDataException("Only a 32-bit PE game is supported");
        byte[] image = new byte[BitConverter.ToInt32(file, optional + 56)];
        Buffer.BlockCopy(file, 0, image, 0, BitConverter.ToInt32(file, optional + 60));
        int section = optional + BitConverter.ToUInt16(file, pe + 20);
        for (int i = 0; i < BitConverter.ToUInt16(file, pe + 6); i++, section += 40)
        {
            int size = BitConverter.ToInt32(file, section + 16), raw = BitConverter.ToInt32(file, section + 20);
            if (size > 0) Buffer.BlockCopy(file, raw, image, BitConverter.ToInt32(file, section + 12), size);
        }
        return image;
    }

    public static int FindUnique(byte[] image, string pattern)
    {
        string[] parts = pattern.Split(' ');
        int result = -1;
        for (int i = 0; i <= image.Length - parts.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < parts.Length; j++)
                if (parts[j] != "??" && image[i + j] != byte.Parse(parts[j], NumberStyles.HexNumber)) { match = false; break; }
            if (!match) continue;
            if (result >= 0) throw new InvalidDataException("Camera pattern was ambiguous");
            result = i;
        }
        if (result < 0) throw new InvalidDataException("Camera pattern was not found");
        return result;
    }

    static void D(List<byte> code, uint value) { code.AddRange(BitConverter.GetBytes(value)); }

    public static byte[] MakeStub(uint network, uint data)
    {
        // Observe the native default, then defer changing the camera until game logic has started.
        var code = new List<byte>(new byte[] { 0xFF, 0xD2 });
        code.Add(0x9C);                                      // pushfd
        code.AddRange(new byte[] { 0x83, 0x3D }); D(code, network); code.Add(0);
        code.AddRange(new byte[] { 0x75, 0x34 });             // jne unchanged (52 bytes)
        code.AddRange(new byte[] { 0x89, 0x35 }); D(code, data + 8); // camera=this/esi
        code.AddRange(new byte[] { 0xD9, 0x15 }); D(code, data + 12); // fst [native height]
        code.AddRange(new byte[] { 0xD9, 0xC0 });             // fld st0; keep native value for caller
        code.AddRange(new byte[] { 0xD8, 0x0D }); D(code, data + 4);
        code.AddRange(new byte[] { 0xD9, 0x1D }); D(code, data + 16); // fstp [target height]
        code.AddRange(new byte[] { 0xC7, 0x05 }); D(code,data+0x50); D(code,1); // pending
        code.AddRange(new byte[] { 0xC7, 0x05 }); D(code,data+0x70); D(code,0); // attempts
        code.AddRange(new byte[] { 0xFF, 0x05 }); D(code,data); // publish last
        code.AddRange(new byte[] { 0x9D, 0xD9, 0x5E, 0x44, 0xC3 }); // popfd; original fstp [esi+44]; ret
        return code.ToArray();
    }

    public static void Install(string dir)
    {
        string ini = Path.Combine(dir, "SAGEUnlocked.ini");
        if (ReadIni(ini, "autozoom") != "1") { Log(dir, "autozoom=0, original behavior"); return; }
        float factor;
        if (!float.TryParse(ReadIni(ini, "zoom"), NumberStyles.Float, CultureInfo.InvariantCulture, out factor) || factor <= 1 || factor > 1.5f)
            throw new InvalidDataException("autozoom requires zoom between 1 and 1.5");
        ProcessModule module = Process.GetCurrentProcess().MainModule;
        string name = Path.GetFileName(module.FileName);
        if (!name.Equals("ra3_1.12.game", StringComparison.OrdinalIgnoreCase) && !name.Equals("ra3_1.13.game", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only Red Alert 3 1.12/1.13 is supported");
        byte[] file = File.ReadAllBytes(module.FileName);
        uint imageBase = BitConverter.ToUInt32(file, BitConverter.ToInt32(file, 0x3C) + 52);
        if (IntPtr.Size != 4 || module.BaseAddress.ToInt64() != imageBase)
            throw new InvalidDataException("Unexpected process architecture or relocation");
        byte[] image = MapImage(file);
        uint site = imageBase + (uint)FindUnique(image, ResetPattern) + 12;
        uint network = BitConverter.ToUInt32(image, FindUnique(image, NetworkPattern) + 2);
        byte[] live = new byte[OriginalCall.Length];
        Marshal.Copy((IntPtr)site, live, 0, live.Length);
        for (int i = 0; i < live.Length; i++) if (live[i] != OriginalCall[i])
            throw new InvalidDataException("Another mod already changed the default-camera call; left unchanged");
        allocation = VirtualAlloc(IntPtr.Zero, (UIntPtr)4096, 0x3000, 0x04);
        if (allocation == IntPtr.Zero) throw new InvalidOperationException("VirtualAlloc failed");
        uint stub = (uint)allocation.ToInt64(), data = stub + 0x200;
        byte[] code = MakeStub(network, data);
        Marshal.Copy(code, 0, allocation, code.Length);
        Marshal.Copy(BitConverter.GetBytes(factor), 0, (IntPtr)(data + 4), 4);
        uint old;
        if (!VirtualProtect(allocation, (UIntPtr)0x200, 0x40, out old)) throw new InvalidOperationException("Stub protection failed");
        byte[] patch = new byte[5]; patch[0] = 0xE8;
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(stub - site - 5)), 0, patch, 1, 4);
        if (!VirtualProtect((IntPtr)site, (UIntPtr)5, 0x40, out old)) throw new InvalidOperationException("Camera protection failed");
        Marshal.Copy(patch, 0, (IntPtr)site, 5);
        uint ignored; VirtualProtect((IntPtr)site, (UIntPtr)5, old, out ignored);
        StartupZoom.Install(dir,image,imageBase,stub,data,network);
        if (ReadIni(ini, "autozoomtrace") == "1") TraceHooks.Install(dir, image, imageBase, stub, data);
        FlushInstructionCache(GetCurrentProcess(), IntPtr.Zero, UIntPtr.Zero);
        Log(dir, "AutoZoom 1.1 installed, zoom=" + factor.ToString(CultureInfo.InvariantCulture) +
            ", site=0x" + site.ToString("X") + ", data=0x" + data.ToString("X") + ", original plugin preserved");
        var thread = new Thread(delegate() { Observe(dir, data); });
        thread.IsBackground = true;
        thread.Start();
    }

    static float ReadFloat(uint address)
    {
        byte[] value = new byte[4]; Marshal.Copy((IntPtr)address, value, 0, 4);
        return BitConverter.ToSingle(value, 0);
    }

    static void Observe(string dir, uint data)
    {
        try
        {
            int seen = 0;
            int applied = 0;
            while (true)
            {
                Thread.Sleep(500);
                TraceHooks.Report(dir, data);
                int appliedNow=Marshal.ReadInt32((IntPtr)(data+0x60));
                if(appliedNow!=applied) {
                    applied=appliedNow;
                    Log(dir,"startup camera applied, count="+applied+", logicFrame="+Marshal.ReadInt32((IntPtr)(data+0x5C))+
                        ", actual="+ReadFloat(data+0x68).ToString(CultureInfo.InvariantCulture));
                }
                int hits = Marshal.ReadInt32((IntPtr)data);
                if (hits == seen) continue;
                seen = hits;
                Log(dir, "camera reset observed, pending startup, resets=" + hits +
                    ", camera=0x" + Marshal.ReadInt32((IntPtr)(data + 8)).ToString("X") +
                    ", native=" + ReadFloat(data + 12).ToString(CultureInfo.InvariantCulture) +
                    ", target=" + ReadFloat(data + 16).ToString(CultureInfo.InvariantCulture));
            }
        }
        catch { }
    }

    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protect);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint oldProtect);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
}
