using System;
using System.IO;
namespace AzerothTrainer
{
    internal static class TrainerDiagnostics
    {
        static string Failure(Exception ex){return ex.Message+(ex.Data.Contains("CompatibilityDetails")?Environment.NewLine+ex.Data["CompatibilityDetails"]:"");}
        static string ReportPath()
        {
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"trainer");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder,"game-check.txt");
        }
        internal static int VerifyFiles(string gameDirectory)
        {
            string report=ReportPath();
            try
            {
                string path=String.IsNullOrEmpty(gameDirectory)?GameLocator.FindExecutable():Path.Combine(gameDirectory,GameLocator.ExecutableName);
                if(path==null)throw new Exception("Game not found. Use Launch game to choose its folder, or pass the game directory after --verify-files.");
                GameLayout layout=GameLayout.Load(path);File.WriteAllText(report,"Checked UTC: "+DateTime.UtcNow.ToString("u")+Environment.NewLine+
                    (layout.Complete?"PASS":"PARTIAL")+" (disk): "+layout.Details+Environment.NewLine+
                    "LIVE HOOK CHECK: NOT RUN. File verification checks on-disk bytes only. Use --verify-game to inspect a running game's hook sites."+Environment.NewLine+
                    "No game process was launched and no game/profile files or memory were changed.");return 0;
            }
            catch(Exception ex){File.WriteAllText(report,"FAIL (disk): "+Failure(ex));return 1;}
        }
        internal static int VerifyGame()
        {
            string report=ReportPath();
            try
            {
                using(var s=PatchSession.Attach())
                    File.WriteAllText(report,(s.UnavailableFeatures.Length==0?"PASS":"PARTIAL")+" (live): "+s.CompatibilityDetails+Environment.NewLine+"Matched live function bodies agree with the validated image, including ASLR relocations. No game memory changed.");
                return 0;
            }
            catch(Exception ex){File.WriteAllText(report,"FAIL: "+Failure(ex));return 1;}
        }
    }
}
