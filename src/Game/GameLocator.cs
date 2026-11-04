using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
namespace AzerothTrainer
{
    internal static class GameLocator
    {
        internal const string ExecutableName="AzerothSurvivors.exe";
        static readonly string SettingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AzerothSurvivorsTrainer","game-path.txt");

        internal static Process FindRunning()
        {
            Process[] processes=Process.GetProcessesByName("AzerothSurvivors");
            Process selected=null;bool complete=false,denied=false;
            try
            {
                foreach(Process process in processes)
                {
                    try
                    {
                        if(process.MainModule==null)continue;
                        if(selected!=null)throw new Exception("More than one game copy is running. Close the extra copy first.");
                        selected=process;
                    }
                    catch(Win32Exception ex){if(ex.NativeErrorCode==5)denied=true;else throw;}
                    catch(InvalidOperationException){/* A candidate exited during enumeration. */}
                }
                if(denied)throw new Exception(PatchSession.PrivilegeMessage);
                complete=true;return selected;
            }
            finally{foreach(Process process in processes)if(!complete||process!=selected)process.Dispose();}
        }

        internal static string FindInstalled()
        {
            string adjacent=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,ExecutableName);
            if(File.Exists(adjacent))return adjacent;
            try
            {
                string saved=File.ReadAllText(SettingsPath).Trim();
                if(Path.IsPathRooted(saved)&&String.Equals(Path.GetFileName(saved),ExecutableName,StringComparison.OrdinalIgnoreCase)&&File.Exists(saved))return saved;
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
            catch(ArgumentException){}
            catch(NotSupportedException){}
            return null;
        }

        internal static string FindExecutable()
        {
            using(Process process=FindRunning())if(process!=null)return process.MainModule.FileName;
            return FindInstalled();
        }

        internal static void Remember(string path)
        {
            try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));File.WriteAllText(SettingsPath,Path.GetFullPath(path));}
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
    }
}
