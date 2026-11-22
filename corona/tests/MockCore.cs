using System.IO;
namespace SAGEUnlocked {
    public static class DllEntry {
        public static int Run(string dir) {
            File.AppendAllText(Path.Combine(dir,"delegated.txt"),"original called\n");
            return 1;
        }
    }
}
