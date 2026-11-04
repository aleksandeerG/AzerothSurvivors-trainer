using System;
using System.IO;
namespace AzerothTrainer
{
    internal static class TrainerDiagnostics
    {
        static string Failure(Exception ex){return ex.Message+(ex.Data.Contains("CompatibilityDetails")?Environment.NewLine+ex.Data["CompatibilityDetails"]:"");}
        static int Report(string text,int exitCode)
        {
            Exception failure=null;
            foreach(var root in new Func<string>[]{()=>Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"trainer"),()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AzerothSurvivorsTrainer","reports"),()=>Path.Combine(Path.GetTempPath(),"AzerothSurvivorsTrainer")})
            {
                try{string folder=root();Directory.CreateDirectory(folder);string path=Path.Combine(folder,"game-check.txt");File.WriteAllText(path,text);Console.WriteLine("Compatibility report: "+path);return exitCode;}
                catch(Exception ex){failure=ex;}
            }
            Console.Error.WriteLine(text);Console.Error.WriteLine("Cannot write a compatibility report: "+(failure==null?"No writable report directory.":failure.Message));return 2;
        }
        internal static int VerifyFiles(string gameDirectory)
        {
            try
            {
                string path=String.IsNullOrEmpty(gameDirectory)?GameLocator.FindExecutable():GameLocator.InDirectory(gameDirectory);
                if(path==null)throw new Exception("Game not found. Use Launch game to choose its folder, or pass the game directory after --verify-files.");
                GameLayout layout=GameLayout.Load(path);return Report("Checked UTC: "+DateTime.UtcNow.ToString("u")+Environment.NewLine+
                    (layout.Complete?"PASS":"PARTIAL")+" (disk): "+layout.Details+Environment.NewLine+
                    "LIVE HOOK CHECK: NOT RUN. File verification checks on-disk bytes only. Use --verify-game to inspect a running game's hook sites."+Environment.NewLine+
                    "No game process was launched and no game/profile files or memory were changed.",0);
            }
            catch(Exception ex){return Report("FAIL (disk): "+Failure(ex),1);}
        }
        internal static int VerifyGame()
        {
            try
            {
                using(var s=PatchSession.Attach())
                    return Report((s.UnavailableFeatures.Length==0?"PASS":"PARTIAL")+" (live): "+s.CompatibilityDetails+Environment.NewLine+"Available capabilities agree with the running executable, including ASLR relocations. No game memory changed.",0);
            }
            catch(Exception ex){return Report("FAIL: "+Failure(ex),1);}
        }
    }
}
