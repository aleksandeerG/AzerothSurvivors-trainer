using System;
using System.Diagnostics;
using System.IO;
namespace AzerothTrainer
{
    internal static class UnwindSupport
    {
        static readonly object gate=new object();
        static readonly System.Collections.Generic.Dictionary<string,byte[]> frames=new System.Collections.Generic.Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        static byte[] ReadAt(FileStream file,long offset,int count)
        {
            byte[] bytes=new byte[count];file.Position=offset;int done=0;
            while(done<count){int n=file.Read(bytes,done,count-done);if(n==0)throw new EndOfStreamException("Incomplete unwind metadata.");done+=n;}return bytes;
        }
        internal static byte[] FrameFor(int sourceRva,string gamePath)
        {
            lock(gate)
            {
                var fileInfo=new FileInfo(gamePath);
                string key=Path.GetFullPath(gamePath)+"|"+fileInfo.Length+"|"+fileInfo.LastWriteTimeUtc.Ticks+"|"+sourceRva;
                byte[] cached;if(frames.TryGetValue(key,out cached))return (byte[])cached.Clone();
                byte[] info;
                using(var file=File.OpenRead(gamePath))
                {
                    int pe=BitConverter.ToInt32(ReadAt(file,60,4),0);byte[] coff=ReadAt(file,pe,24),optional=ReadAt(file,pe+24,BitConverter.ToUInt16(coff,20));
                    byte[] sections=ReadAt(file,pe+24+optional.Length,40*BitConverter.ToUInt16(coff,6));
                    Func<int,long> offset=delegate(int rva)
                    {
                        for(int i=0;i<sections.Length;i+=40)
                        {
                            int va=BitConverter.ToInt32(sections,i+12),length=Math.Max(BitConverter.ToInt32(sections,i+8),BitConverter.ToInt32(sections,i+16));
                            if(rva>=va&&rva<va+length)return (long)BitConverter.ToInt32(sections,i+20)+rva-va;
                        }
                        throw new Exception("Unwind data points outside the supported image.");
                    };
                    long table=offset(BitConverter.ToInt32(optional,112+3*8));int low=0,high=BitConverter.ToInt32(optional,116+3*8)/12-1,unwind=0;
                    while(low<=high)
                    {
                        int middle=low+(high-low)/2;byte[] entry=ReadAt(file,table+12L*middle,12);
                        if(sourceRva<BitConverter.ToInt32(entry,0))high=middle-1;
                        else if(sourceRva>=BitConverter.ToInt32(entry,4))low=middle+1;
                        else{unwind=BitConverter.ToInt32(entry,8);break;}
                    }
                    if(unwind==0)throw new Exception("No original unwind frame found for hook.");
                    long position=offset(unwind);byte[] head=ReadAt(file,position,4);int count=head[2];
                    if((head[0]&7)!=1||(head[0]&0x20)!=0)throw new Exception("Unsupported chained unwind frame.");
                    info=ReadAt(file,position,4+((count+1)&~1)*2);info[0]=1;info[1]=0;
                    for(int i=0;i<count;)
                    {
                        int op=info[5+2*i]&15,extra=info[5+2*i]>>4;info[4+2*i]=0;
                        int slots=op==1?(extra==0?2:3):(op==4||op==8?2:(op==5||op==9?3:1));
                        if(op==6||op==7||op>10)throw new Exception("Unknown unwind operation.");i+=slots;
                    }
                }
                frames.Add(key,info);return (byte[])info.Clone();
            }
        }
        internal static ulong Invoke(IntPtr handle,int pid,IntPtr cave,IntPtr function,ulong a,ulong b,ulong c)
        {
            var code=new Assembler();code.Emit("48 83 EC 28 48 B9");code.Bytes.AddRange(BitConverter.GetBytes(a));
            code.Emit("48 BA");code.Bytes.AddRange(BitConverter.GetBytes(b));code.Emit("49 B8");code.Bytes.AddRange(BitConverter.GetBytes(c));
            code.Emit("48 B8");code.Bytes.AddRange(BitConverter.GetBytes(function.ToInt64()));code.Emit("FF D0 49 BB");
            code.Bytes.AddRange(BitConverter.GetBytes(Native.Add(cave,4096+256).ToInt64()));code.Emit("49 89 03 31 C0 48 83 C4 28 C3");
            IntPtr bootstrap=Native.Add(cave,3900);uint old;
            if(!Native.VirtualProtectEx(handle,bootstrap,(UIntPtr)code.Bytes.Count,0x40,out old))throw Native.Error("Cannot prepare unwind registration");
            IntPtr thread=IntPtr.Zero;bool finished=false;
            try
            {
                Native.Write(handle,bootstrap,code.Finish());Native.FlushInstructionCache(handle,bootstrap,(UIntPtr)code.Bytes.Count);
                thread=Native.CreateRemoteThread(handle,IntPtr.Zero,UIntPtr.Zero,bootstrap,IntPtr.Zero,0,IntPtr.Zero);
                if(thread==IntPtr.Zero)throw Native.Error("Cannot register Windows unwind information");
                // Registration precedes hook installation and all thread suspension.
                // Do not free bootstrap storage while its registration thread is alive.
                uint wait=Native.WaitForSingleObject(thread,10000);
                if(wait!=0)throw new Exception("Windows unwind registration did not finish; retry or close the game.");
                finished=true;
                return BitConverter.ToUInt64(Native.Read(handle,Native.Add(cave,4352),8),0);
            }
            finally
            {
                if(thread!=IntPtr.Zero)Native.CloseHandle(thread);
                // On timeout this retained page stays executable until the process exits.
                if(finished||thread==IntPtr.Zero){uint ignored;Native.VirtualProtectEx(handle,bootstrap,(UIntPtr)code.Bytes.Count,old,out ignored);}
            }
        }
        internal static IntPtr RemoteApi(int pid,string name)
        {
            IntPtr local=Native.GetModuleHandle("ntdll.dll"),function=Native.GetProcAddress(local,name);
            using(Process process=Process.GetProcessById(pid))
                foreach(ProcessModule module in process.Modules)
                    if(String.Equals(module.ModuleName,"ntdll.dll",StringComparison.OrdinalIgnoreCase))return Native.Add(module.BaseAddress,function.ToInt64()-local.ToInt64());
            throw new Exception("Cannot locate Windows unwind services in the game.");
        }
        internal static void Register(IntPtr handle,int pid,IntPtr cave,int codeLength,int sourceRva)
        {
            if(codeLength>=3500)throw new Exception("Generated hook is too large for its metadata page.");
            byte[] frame;using(Process process=Process.GetProcessById(pid))frame=FrameFor(sourceRva,process.MainModule.FileName);
            Native.Write(handle,Native.Add(cave,3680),frame);
            byte[] table=new byte[24];Array.Copy(BitConverter.GetBytes(codeLength),0,table,4,4);Array.Copy(BitConverter.GetBytes(3680),0,table,8,4);
            Array.Copy(BitConverter.GetBytes(3900),0,table,12,4);Array.Copy(BitConverter.GetBytes(4000),0,table,16,4);Array.Copy(BitConverter.GetBytes(3840),0,table,20,4);
            Native.Write(handle,Native.Add(cave,3584),table);
            Native.Write(handle,Native.Add(cave,3840),RarityCode.Hex("01 04 01 00 04 42 00 00"));
            ulong result=Invoke(handle,pid,cave,RemoteApi(pid,"RtlAddFunctionTable"),(ulong)Native.Add(cave,3584).ToInt64(),2,(ulong)cave.ToInt64());
            if((result&255)==0)throw new Exception("Windows refused the generated hook's unwind table.");
        }
    }
}
