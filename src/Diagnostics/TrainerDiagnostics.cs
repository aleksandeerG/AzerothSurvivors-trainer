using System;
using System.IO;
using System.Security.Cryptography;
namespace AzerothTrainer
{
    internal static class TrainerDiagnostics
    {
        static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        static string ReportPath()
        {
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"trainer");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder,"game-check.txt");
        }
        static void CheckFiles(string path)
        {
            using(var file=File.OpenRead(path))
            {
                using(var sha=SHA256.Create())Require(BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant()==RarityCode.SupportedHash,"Unsupported executable hash");
                int[] sites={RarityCode.HookRva,0x5f74c3e,0x5f74cff,RunCode.HookRva,RunCode.HudRva,RunCode.CoopRva,XpCode.HookRva,ProfileCode.HookRva,0x5eca83f,0x5ef6bbd,0x5f6e9e9,0x5f6fc3c,0x5f7a4b3,0x5eca8bd,0x5ef6c3b,0x5f6ea67,0x5f6fcba,0x5f7a535};
                byte[][] expected={RarityCode.Original,RarityCode.Hex("44 0F 2E F0 0F 86 07 03 00 00 31 F6 48 83 3F FF"),RarityCode.Hex("44 0F 2E F0 0F 86 12 05 00 00 45 31 C9 E9 5F 05 00 00"),RunCode.Original,RunCode.HudOriginal,RunCode.CoopOriginal,XpCode.Original,ProfileCode.Original,
                    RarityCode.Hex("45 85 C0 74 79"),RarityCode.Hex("45 85 C0 74 79"),RarityCode.Hex("45 85 C0 74 79"),RarityCode.Hex("45 85 C0 74 79"),RarityCode.Hex("45 85 C9 74 7D"),
                    RarityCode.Hex("B9 64 00 00 00"),RarityCode.Hex("B9 64 00 00 00"),RarityCode.Hex("B9 64 00 00 00"),RarityCode.Hex("B9 64 00 00 00"),RarityCode.Hex("BA 64 00 00 00")};
                for(int i=0;i<sites.Length;i++)
                {file.Position=sites[i]-0xc00;byte[] actual=new byte[expected[i].Length];Require(file.Read(actual,0,actual.Length)==actual.Length&&Native.Equal(actual,expected[i]),"Disk instruction mismatch at "+sites[i].ToString("X"));}
            }
            foreach(int site in new int[]{RarityCode.HookRva,0x5f74c3e,0x5f74cff,RunCode.HookRva,XpCode.HookRva,ProfileCode.HookRva})UnwindSupport.FrameFor(site,path);
        }
        internal static int VerifyFiles(string gameDirectory)
        {
            string report=ReportPath();
            try
            {
                string path=String.IsNullOrEmpty(gameDirectory)?GameLocator.FindExecutable():Path.Combine(gameDirectory,GameLocator.ExecutableName);
                if(path==null)throw new Exception("Game not found. Use Launch game to choose its folder, or pass the game directory after --verify-files.");
                CheckFiles(path);File.WriteAllText(report,"Checked UTC: "+DateTime.UtcNow.ToString("u")+Environment.NewLine+
                    "PASS (disk): supported executable SHA-256 "+RarityCode.SupportedHash+Environment.NewLine+
                    "PASS (disk): all 16 patch sites, unchanged HUD/co-op guards (18 compatibility sites), and six original unwind frames."+Environment.NewLine+
                    "LIVE HOOK CHECK: NOT RUN. File verification checks on-disk bytes only. Use --verify-game to inspect a running game's hook sites."+Environment.NewLine+
                    "No game process was launched and no game/profile files or memory were changed.");return 0;
            }
            catch(Exception ex){File.WriteAllText(report,"FAIL (disk): "+ex.Message);return 1;}
        }
        internal static int VerifyGame()
        {
            string report=ReportPath();
            try
            {
                using(var s=PatchSession.Attach())
                    File.WriteAllText(report,"PASS (live): game hash, process access, ASLR base, all six gameplay hooks, unchanged HUD/co-op guards and all five pricing paths verified. No memory changed.");
                return 0;
            }
            catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex.Message);return 1;}
        }
    }
}
