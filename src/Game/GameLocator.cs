using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Linq;
namespace AzerothTrainer
{
    internal static class GameLocator
    {
        internal const string ExecutableName="AzerothSurvivors.exe";
        static readonly string SettingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AzerothSurvivorsTrainer","game-path.txt");

        internal static Process FindRunning()
        {
            Process[] processes=Process.GetProcesses();string remembered=Remembered();string expected=remembered==null?"AzerothSurvivors":Path.GetFileNameWithoutExtension(remembered);
            Process selected=null;bool complete=false,denied=false;
            try
            {
                foreach(Process process in processes)
                {
                    try
                    {
                        bool named=String.Equals(process.ProcessName,"AzerothSurvivors",StringComparison.OrdinalIgnoreCase)||String.Equals(process.ProcessName,expected,StringComparison.OrdinalIgnoreCase);
                        if(!named&&process.MainWindowTitle.IndexOf("Azeroth",StringComparison.OrdinalIgnoreCase)<0)continue;
                        if(process.MainModule==null)continue;
                        bool canonical=String.Equals(process.ProcessName,"AzerothSurvivors",StringComparison.OrdinalIgnoreCase);
                        bool selectedPath=remembered!=null&&String.Equals(process.MainModule.FileName,remembered,StringComparison.OrdinalIgnoreCase);
                        if(!canonical&&!selectedPath&&!IsGameExecutable(process.MainModule.FileName))continue;
                        if(selected!=null)throw new Exception("More than one game copy is running. Close the extra copy first.");
                        selected=process;
                    }
                    catch(Win32Exception ex){if(ex.NativeErrorCode==5&&(process.ProcessName==expected||process.ProcessName=="AzerothSurvivors"))denied=true;}
                    catch(InvalidOperationException){/* A candidate exited during enumeration. */}
                }
                if(denied)throw new Exception(PatchSession.PrivilegeMessage);
                complete=true;return selected;
            }
            finally{foreach(Process process in processes)if(!complete||process!=selected)process.Dispose();}
        }

        internal static string FindInstalled()
        {
            string saved=Remembered();if(saved!=null)return saved;return InDirectory(AppDomain.CurrentDomain.BaseDirectory);
        }
        static string Remembered()
        {
            try
            {
                string saved=File.ReadAllText(SettingsPath).Trim();
                if(Path.IsPathRooted(saved)&&String.Equals(Path.GetExtension(saved),".exe",StringComparison.OrdinalIgnoreCase)&&File.Exists(saved))return saved;
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
            catch(ArgumentException){}
            catch(NotSupportedException){}
            return null;
        }
        internal static string InDirectory(string path)
        {
            if(File.Exists(path))return Path.GetFullPath(path);
            if(!Directory.Exists(path))return null;
            string standard=Path.Combine(path,ExecutableName);if(File.Exists(standard))return standard;
            string[] games=Directory.EnumerateFiles(path,"*.exe").Where(IsGameExecutable).ToArray();
            if(games.Length>1)throw new Exception("More than one game executable is in that folder. Pass the executable path explicitly.");
            return games.Length==1?games[0]:null;
        }
        internal static bool IsGameExecutable(string path)
        {
            try
            {
                byte[] marker=Encoding.ASCII.GetBytes("AzerothSurvivors/");
                using(var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                {
                    if(stream.Length<64||stream.Length>512L*1024*1024)return false;byte[] buffer=new byte[65536+marker.Length];int carry=0,count;
                    while((count=stream.Read(buffer,carry,65536))>0)
                    {
                        int end=carry+count;for(int at=0;at+marker.Length<=end;at++){if(buffer[at]!=marker[0])continue;int i=1;while(i<marker.Length&&buffer[at+i]==marker[i])i++;if(i==marker.Length)return true;}
                        carry=Math.Min(marker.Length-1,end);Array.Copy(buffer,end-carry,buffer,0,carry);
                    }
                }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}return false;
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
