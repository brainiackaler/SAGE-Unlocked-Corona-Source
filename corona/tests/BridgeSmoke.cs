using System;
using System.IO;
using System.Reflection;
public static class BridgeSmoke {
    public static int Main(string[] args) {
        Assembly bridge=Assembly.LoadFile(Path.GetFullPath(Path.Combine(args[0],"CnCFpsUnlocker.dll")));
        int code=(int)bridge.GetType("SAGEUnlocked.DllEntry",true).GetMethod("Run").Invoke(null,new object[]{args[0]});
        if(code!=1 || File.ReadAllLines(Path.Combine(args[0],"delegated.txt")).Length!=1) throw new Exception("Original plugin delegation failed");
        if(!File.ReadAllText(Path.Combine(args[0],"SAGEAutoZoom.log")).Contains("autozoom=0")) throw new Exception("Disabled extension did not preserve original behavior");
        Console.WriteLine("PASS: bridge filename, shared namespace, original assembly delegation and opt-out flag");
        return 0;
    }
}
