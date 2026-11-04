using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace AzerothTrainer
{
    internal static class ProfileBackup
    {
        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process,int informationClass,byte[] information,int length,out int returned);
        static IntPtr Pointer(IntPtr process,IntPtr address){return new IntPtr(BitConverter.ToInt64(Native.Read(process,address,8),0));}
        internal static Dictionary<string,string> EnvironmentFor(IntPtr process,out string directory)
        {
            byte[] basic=new byte[48];int returned;
            if(NtQueryInformationProcess(process,0,basic,basic.Length,out returned)!=0)throw new Exception("Cannot read the game's environment; the profile edit was not queued.");
            IntPtr peb=new IntPtr(BitConverter.ToInt64(basic,8)),parameters=Pointer(process,Native.Add(peb,0x20)),environment=Pointer(process,Native.Add(parameters,0x80));
            byte[] current=Native.Read(process,Native.Add(parameters,0x38),16);int length=BitConverter.ToUInt16(current,0);Need(length<=32766&&(length&1)==0,"Invalid game working-directory length.");
            directory=Encoding.Unicode.GetString(Native.Read(process,new IntPtr(BitConverter.ToInt64(current,8)),length));
            var text=new StringBuilder();long address=environment.ToInt64();Need(address!=0,"The game's environment is unavailable.");
            for(int total=0;total<1024*1024;)
            {
                int count=4096-(int)(address&4095);byte[] page=Native.Read(process,new IntPtr(address),count);text.Append(Encoding.Unicode.GetString(page));total+=count;address+=count;
                string block=text.ToString();int end=block.IndexOf("\0\0",StringComparison.Ordinal);
                if(end<0)continue;
                Need(Pointer(process,Native.Add(parameters,0x80))==environment,"The game's environment changed while resolving its save path; retry the edit.");
                var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach(string entry in block.Substring(0,end).Split('\0')){int equals=entry.IndexOf('=');if(equals>0)result[entry.Substring(0,equals)]=entry.Substring(equals+1);}
                return result;
            }
            throw new Exception("The game's environment exceeds the supported size; the profile edit was not queued.");
        }
        static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
        internal static string SavePath(IntPtr process)
        {
            string directory;var environment=EnvironmentFor(process,out directory);string root;
            if(!environment.TryGetValue("WOW_ROGUE_SAVE",out root))
            {
                string appData;Need(environment.TryGetValue("APPDATA",out appData)&&!String.IsNullOrWhiteSpace(appData),"Cannot resolve the game's AppData save directory; the profile edit was not queued.");
                root=Path.Combine(appData,"wenilla-rogue");
            }
            if(!Path.IsPathRooted(root)){Need(Path.IsPathRooted(directory),"Cannot resolve the game's relative save directory.");root=Path.Combine(directory,root);}
            return Path.GetFullPath(Path.Combine(root,"characters.txt"));
        }
        internal static string Create(IntPtr process)
        {
            string source=SavePath(process);Need(File.Exists(source),"Profile save not found at "+source+". Create a game profile first. The edit was not queued.");
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"trainer","backups");Directory.CreateDirectory(folder);
            string target=Path.Combine(folder,"characters-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".txt");
            try
            {
                using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                {
                    long length=input.Length;
                    using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}
                    input.Position=0;using(var sha=SHA256.Create())using(var copied=File.OpenRead(target))
                        Need(input.Length==length&&copied.Length==length&&Native.Equal(sha.ComputeHash(input),sha.ComputeHash(copied)),"The save changed during backup. Retry the edit.");
                }
                return target;
            }
            catch{if(File.Exists(target))File.Delete(target);throw;}
        }
    }
}
