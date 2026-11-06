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
        internal static int ArchiveBuild(string gameDirectory)
        {
            try
            {
                string source=String.IsNullOrEmpty(gameDirectory)?GameLocator.FindExecutable():GameLocator.InDirectory(gameDirectory);
                if(source==null)throw new Exception("Game not found. Pass its executable or installation folder after --archive-build.");
                string gameHash=GameImage.Digest(File.ReadAllBytes(source));string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"trainer","reference-builds",gameHash);Directory.CreateDirectory(folder);
                string executable=Path.Combine(folder,"AzerothSurvivors.exe");
                if(!File.Exists(executable))File.Copy(source,executable,false);
                if(GameImage.Digest(File.ReadAllBytes(executable))!=gameHash)throw new Exception("The archive copy does not match the source; the game may have updated during copying.");
                string trainer=System.Reflection.Assembly.GetExecutingAssembly().Location,trainerHash=GameImage.Digest(File.ReadAllBytes(trainer));
                string result;int code=0;try{var layout=GameLayout.Load(executable);result=(layout.Complete?"PASS":"PARTIAL")+Environment.NewLine+layout.Details;}catch(Exception ex){result="FAIL"+Environment.NewLine+Failure(ex);code=1;}
                string report=Path.Combine(folder,trainerHash+".txt");
                if(!File.Exists(report))File.WriteAllText(report,"Archived UTC: "+DateTime.UtcNow.ToString("u")+Environment.NewLine+"Game SHA-256: "+gameHash+Environment.NewLine+"Trainer SHA-256: "+trainerHash+Environment.NewLine+result+Environment.NewLine+"Static recognition only. No game launched, hooks installed or saves changed.");
                return Report("Archived executable: "+executable+Environment.NewLine+"Baseline recognition report: "+report+Environment.NewLine+result,code);
            }
            catch(Exception ex){return Report("Archive failed: "+Failure(ex),1);}
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
