using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Iced.Intel;
namespace AzerothTrainer
{
    internal static class UnwindSupport
    {
        static readonly object gate=new object();
        static int relativeEntry;
        internal static bool RelativeEntrySafe()
        {
            lock(gate)
            {
                if(relativeEntry!=0)return relativeEntry>0;
                IntPtr memory=IntPtr.Zero,buffer=IntPtr.Zero,stack=IntPtr.Zero;
                try
                {
                    // Model an interrupted hook entry inside a 40-byte native
                    // frame. RtlVirtualUnwind reads these buffers; no code runs.
                    memory=Marshal.AllocHGlobal(512);buffer=Marshal.AllocHGlobal(1248);stack=Marshal.AllocHGlobal(256);
                    IntPtr context=new IntPtr((buffer.ToInt64()+15)&~15L);
                    for(int p=0;p<512;p++)Marshal.WriteByte(memory,p,0x90);
                    for(int p=0;p<1232;p++)Marshal.WriteByte(context,p,0);
                    byte[] jump={0x2e,0xe9,0xf6,0,0,0};Marshal.Copy(jump,0,Native.Add(memory,4),jump.Length);
                    var function=Native.Add(memory,64);Marshal.WriteInt32(function,0,0);Marshal.WriteInt32(function,4,64);Marshal.WriteInt32(function,8,128);
                    byte[] unwind={1,4,1,0,4,0x42,0,0};Marshal.Copy(unwind,0,Native.Add(memory,128),unwind.Length);
                    IntPtr sp=Native.Add(stack,64);Marshal.WriteInt64(sp,0,0xbad);Marshal.WriteInt64(sp,40,0x12345678);
                    Marshal.WriteInt32(context,48,0x100001);Marshal.WriteInt64(context,152,sp.ToInt64());Marshal.WriteInt64(context,248,memory.ToInt64()+4);
                    IntPtr data;ulong frame;Native.RtlVirtualUnwind(0,(ulong)memory.ToInt64(),(ulong)memory.ToInt64()+4,function,context,out data,out frame,IntPtr.Zero);
                    relativeEntry=Marshal.ReadInt64(context,248)==0x12345678&&Marshal.ReadInt64(context,152)==sp.ToInt64()+48?1:-1;
                }
                catch{relativeEntry=-1;}
                finally
                {
                    if(stack!=IntPtr.Zero)Marshal.FreeHGlobal(stack);if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(memory!=IntPtr.Zero)Marshal.FreeHGlobal(memory);
                }
                return relativeEntry>0;
            }
        }
        static byte[] ReadAt(FileStream file,long offset,int count)
        {
            byte[] bytes=new byte[count];file.Position=offset;int done=0;
            while(done<count){int n=file.Read(bytes,done,count-done);if(n==0)throw new EndOfStreamException("Incomplete unwind metadata.");done+=n;}return bytes;
        }
        internal static byte[] FrameFor(int sourceRva,string gamePath)
        {
            lock(gate)
            {
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
                return info;
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
        internal static byte[] Normalize(byte[] raw)
        {
            var info=(byte[])raw.Clone();if(info.Length<4||(info[0]&7)!=1||(info[0]&0x20)!=0)throw new Exception("Unsupported unwind frame.");info[0]=1;info[1]=0;
            for(int at=0;at<info[2];)
            {
                int op=info[5+at*2]&15,extra=info[5+at*2]>>4,count=op==1?(extra==0?2:3):(op==4||op==8?2:(op==5||op==9?3:1));
                if(op==6||op==7||op>10||at+count>info[2]||4+(at+count)*2>info.Length)throw new Exception("Incomplete unwind frame.");info[4+at*2]=0;at+=count;
            }
            return info;
        }
        internal static void Register(IntPtr handle,int pid,IntPtr cave,int codeLength,int sourceRva,byte[] validatedFrame=null)
        {
            if(codeLength>=3500)throw new Exception("Generated hook is too large for its metadata page.");
            byte[] frame=validatedFrame;if(frame==null)using(Process process=Process.GetProcessById(pid))frame=FrameFor(sourceRva,process.MainModule.FileName);
            if(frame.Length>96)throw new Exception("Original unwind frame exceeds the hook metadata capacity.");
            Native.Write(handle,Native.Add(cave,3648),frame);
            var decoder=Decoder.Create(64,new ByteArrayCodeReader(Native.Read(handle,cave,codeLength)));decoder.IP=0;
            int entered=0,left=0;
            while(decoder.IP<(ulong)codeLength)
            {
                Instruction instruction;decoder.Decode(out instruction);if(instruction.IsInvalid)throw new Exception("Generated hook contains an invalid instruction.");
                if(instruction.Op0Register==Iced.Intel.Register.RSP&&DecodedCode.Immediate(instruction,1,FrameCode.StackSize))
                {if(instruction.Mnemonic==Mnemonic.Sub){if(entered!=0)throw new Exception("Multiple private hook frames.");entered=checked((int)instruction.NextIP);}if(instruction.Mnemonic==Mnemonic.Add){if(left!=0)throw new Exception("Multiple hook-frame exits.");left=checked((int)instruction.NextIP);}}
            }
            var entries=new List<int>();
            if(entered!=0)
            {
                if(left<=entered||entered>255)throw new Exception("Incomplete private hook frame.");
                byte[] own=PrivateFrame(frame,entered);if(own.Length>96)throw new Exception("Combined hook unwind frame exceeds capacity.");Native.Write(handle,Native.Add(cave,3744),own);
                entries.AddRange(new[]{0,left,3744,left,codeLength,3648});
            }
            else entries.AddRange(new[]{0,codeLength,3648});
            entries.AddRange(new[]{3900,4000,3840});byte[] table=new byte[entries.Count*4];for(int i=0;i<entries.Count;i++)Array.Copy(BitConverter.GetBytes(entries[i]),0,table,i*4,4);
            Native.Write(handle,Native.Add(cave,3584),table);
            Native.Write(handle,Native.Add(cave,3840),RarityCode.Hex("01 04 01 00 04 42 00 00"));
            ulong result=Invoke(handle,pid,cave,RemoteApi(pid,"RtlAddFunctionTable"),(ulong)Native.Add(cave,3584).ToInt64(),(ulong)(entries.Count/3),(ulong)cave.ToInt64());
            if((result&255)==0)throw new Exception("Windows refused the generated hook's unwind table.");
        }
        internal static byte[] PrivateFrame(byte[] original,int allocationEnd)
        {
            // Unwind the private allocation first. The original save offsets then
            // refer to the restored game RSP. RBP still belongs to the game frame,
            // so omit SET_FPREG and use RSP for this combined description.
            var codes=new List<byte>{(byte)allocationEnd,1,(byte)(FrameCode.StackSize/8),0};
            for(int at=0;at<original[2];)
            {
                int op=original[5+at*2]&15,extra=original[5+at*2]>>4,count=op==1?(extra==0?2:3):(op==4||op==8?2:(op==5||op==9?3:1));
                if(at+count>original[2])throw new Exception("Truncated original unwind frame.");
                if(op!=3)for(int b=0;b<count*2;b++)codes.Add(b==0?(byte)0:original[4+at*2+b]);at+=count;
            }
            int slots=codes.Count/2;var result=new byte[4+((slots+1)&~1)*2];result[0]=1;result[1]=(byte)allocationEnd;result[2]=(byte)slots;codes.CopyTo(result,4);return result;
        }
    }
}
