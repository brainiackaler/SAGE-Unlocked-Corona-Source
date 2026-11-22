using System;
using System.IO;
using System.Linq;
using System.Reflection;

public static class InspectExtension
{
    public static int Main(string[] args)
    {
        if (args.Length < 2 || args.Length > 3)
        {
            Console.Error.WriteLine("Usage: InspectExtension <output-directory> <core-dll> [game-file]");
            return 1;
        }
        try
        {
            string output = args[0];
            File.WriteAllBytes(Path.Combine(output, "reset-stub.bin"),
                AutoZoom.MakeStub(0x900000, 0xA00000));
            File.WriteAllBytes(Path.Combine(output, "startup-stub.bin"),
                StartupZoom.MakeFrameStub(0x100000, 0x100800, 0xA00000, 0x900000, 0xE00000, 0x200000));
            File.WriteAllBytes(Path.Combine(output, "frame-stub.bin"),
                TraceHooks.FrameStub(0x100000, 0x100800, 0xA00000));
            File.WriteAllBytes(Path.Combine(output, "setter-stub.bin"),
                TraceHooks.SetterStub(0x100000, 0x100800, 0xA00000, new byte[] { 0xA1, 0, 0, 0x90, 0 }));

            Assembly core = Assembly.LoadFile(Path.GetFullPath(args[1]));
            var entry = core.GetType("SAGEUnlocked.DllEntry", true).GetMethod("Run", new Type[] { typeof(string) });
            if (entry == null || !entry.IsStatic || entry.ReturnType != typeof(int))
                throw new MissingMethodException("Expected static int SAGEUnlocked.DllEntry.Run(string)");
            Console.WriteLine("Core entry verified: " + core.GetName().Name + " " + entry);
            VerifyParticleShader(core);
            var expiryStub = core.GetType("Program", true).GetMethod("MakeParticleExpiryStub", StaticMethods);
            File.WriteAllBytes(Path.Combine(output, "particle-expiry-stub.bin"),
                (byte[])expiryStub.Invoke(null, new object[] { 0x100000u, 0xE00000u, 29u }));

            if (args.Length == 3)
            {
                byte[] image = AutoZoom.MapImage(File.ReadAllBytes(args[2]));
                int reset = AutoZoom.FindUnique(image, AutoZoom.ResetPattern);
                int network = AutoZoom.FindUnique(image, AutoZoom.NetworkPattern);
                int frame = AutoZoom.FindUnique(image, TraceHooks.FramePattern);
                int setter = AutoZoom.FindUnique(image, TraceHooks.SetterPattern);
                int clock = AutoZoom.FindUnique(image, StartupZoom.ClockPattern);
                Console.WriteLine("Reset RVA: 0x{0:X}, network RVA: 0x{1:X}, frame RVA: 0x{2:X}, setter RVA: 0x{3:X}",
                    reset, network, frame, setter);
                Console.WriteLine("Game-system global: 0x{0:X}", BitConverter.ToUInt32(image, clock + 2));
                VerifyParticleSites(core, image);
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    const BindingFlags StaticMethods = BindingFlags.NonPublic | BindingFlags.Static;

    static void VerifyParticleShader(Assembly core)
    {
        MethodInfo edit = core.GetType("ParticleShader", true).GetMethod("Edit", StaticMethods);
        uint[] shader = {
            0xFFFE0300,
            0x03000002, 0x80010000, 0xA0000026, 0x91FF0001, // add r0.x, c38.x, -v1.w
            0x0300000C, 0x80020001, 0x90FF0000, 0x80000000, // slt r1.y, v0.w, r0.x
            0x02000006, 0x80040001, 0x90FF0000,             // rcp r1.z, v0.w
            0x03000005, 0x80080001, 0x80000000, 0x80AA0001, // mul r1.w, r0.x, r1.z
            0x0000FFFF
        };
        uint[] edited = (uint[])edit.Invoke(null, new object[] { shader, 0.875f });
        uint[] expected = {
            0xFFFE0300,
            0x05000051, 0xA00F00FF, 0x3F600000, 0x3F800000, 0, 0, // def c255, 0.875, 1, 0, 0
            0x03000002, 0x80010000, 0xA0000026, 0x91FF0001,
            0x03000002, 0x80020001, 0x80000000, 0xA10000FF,       // add r1.y, r0.x, -c255.x
            0x0300000C, 0x80020001, 0x90FF0000, 0x80550001,       // slt r1.y, v0.w, r1.y
            0x02000006, 0x80040001, 0x90FF0000,
            0x03000005, 0x80080001, 0x80000000, 0x80AA0001,
            0x0300000A, 0x80080001, 0x80FF0001, 0xA05500FF,       // min r1.w, r1.w, c255.y
            0x0000FFFF
        };
        if (edited == null || !edited.SequenceEqual(expected))
            throw new InvalidDataException("Particle shader expiry or colour clamp is incorrect");
        shader[4] = 0x91FF0002; // A different vertex input is outside the particle signature.
        if (edit.Invoke(null, new object[] { shader, 0.875f }) != null)
            throw new InvalidDataException("An unrelated shader matched the particle signature");
        Console.WriteLine("PASS: particle expiry, colour clamp and shader signature");
    }

    static void VerifyParticleSites(Assembly core, byte[] image)
    {
        Type patcher = core.GetType("Program", true);
        uint render = (uint)patcher.GetMethod("FindRenderFps", StaticMethods).Invoke(null, new object[] { image });
        object[] expiryArgs = { image, render, 0u };
        uint expiry = (uint)patcher.GetMethod("FindParticleExpiry", StaticMethods).Invoke(null, expiryArgs);
        object clock = patcher.GetMethod("FindEffectClock", StaticMethods).Invoke(null, new object[] { image, render });
        if (expiry == 0 || clock == null)
            throw new InvalidDataException("RA3 particle expiry or clock signature is missing");
        Console.WriteLine("Particle expiry call: 0x{0:X}, W3D time: 0x{1:X}", expiry, expiryArgs[2]);
    }
}
