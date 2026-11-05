using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Iced.Intel;
namespace AzerothTrainer
{
    internal sealed class ImageFunction
    {
        internal readonly int Start,Length,Unwind;
        internal ImageFunction(int start,int end,int unwind=0){Start=start;Length=end-start;Unwind=unwind;}
    }
    internal sealed class GameImage
    {
        sealed class Section
        {
            internal int Rva,Raw,Size,VirtualSize,Flags;
        }
        readonly byte[] bytes;
        readonly List<Section> sections=new List<Section>();
        sealed class Transfer {internal int Source,Target;}
        List<Transfer> transfers;
        internal readonly List<ImageFunction> Functions=new List<ImageFunction>();
        internal readonly List<int> Relocations=new List<int>();
        internal readonly Dictionary<int,string> Imports=new Dictionary<int,string>();
        internal readonly string Path,Hash;
        internal readonly long PreferredBase;
        internal string VersionToken
        {
            get
            {
                byte[] marker=Encoding.ASCII.GetBytes("AzerothSurvivors/");
                foreach(int at in Find(marker,false))
                {
                    try
                    {
                        byte[] data=Read(at,80);int end=marker.Length;while(end<data.Length&&((data[end]>='0'&&data[end]<='9')||data[end]=='.'||data[end]=='-'||data[end]=='+'))end++;
                        if(end>marker.Length)return Encoding.ASCII.GetString(data,0,end);
                    }catch{}
                }
                return "not present (identity is checked by code recognition)";
            }
        }
        internal GameImage(string path)
        {
            Path=System.IO.Path.GetFullPath(path);bytes=File.ReadAllBytes(Path);Hash=Digest(bytes);
            if(bytes.Length<64||U16(0)!=0x5a4d)throw new Exception("The game is not a Windows executable.");
            int pe=I32(60);if(I32(pe)!=0x4550||U16(pe+4)!=0x8664||U16(pe+24)!=0x20b)throw new Exception("The game must be a Windows x64 executable.");
            int optional=U16(pe+20),count=U16(pe+6);PreferredBase=I64(pe+48);
            for(int i=0;i<count;i++)
            {
                int at=checked(pe+24+optional+40*i);
                var section=new Section{Rva=I32(at+12),Raw=I32(at+20),Size=I32(at+16),VirtualSize=I32(at+8),Flags=I32(at+36)};
                if(section.Rva<0||section.Raw<0||section.Size<0||(long)section.Raw+section.Size>bytes.Length)throw new Exception("Invalid game image section.");
                sections.Add(section);
            }
            int table=I32(pe+24+112+3*8),length=I32(pe+24+116+3*8);
            if(length<=0||length%12!=0)throw new Exception("Missing game runtime-function table.");
            byte[] entries=Read(table,length);int previous=-1;
            for(int i=0;i<length;i+=12)
            {
                int start=BitConverter.ToInt32(entries,i),end=BitConverter.ToInt32(entries,i+4);
                if(start<=previous||end<=start||!Executable(start,end-start))throw new Exception("Invalid game runtime-function range.");
                Functions.Add(new ImageFunction(start,end,BitConverter.ToInt32(entries,i+8)));previous=start;
            }
            int imports=I32(pe+24+112+8),importSize=I32(pe+24+116+8);
            for(int at=0;imports!=0&&at+20<=importSize;at+=20)
            {
                byte[] descriptor=Read(checked(imports+at),20);int names=BitConverter.ToInt32(descriptor,0),library=BitConverter.ToInt32(descriptor,12),tableRva=BitConverter.ToInt32(descriptor,16);if(library==0)break;
                string dll=Ascii(library);if(names==0)names=tableRva;
                for(int index=0;index<8192;index++)
                {
                    ulong value=BitConverter.ToUInt64(Read(checked(names+index*8),8),0);if(value==0)break;if((value>>63)!=0)continue;
                    if(value>Int32.MaxValue-2)throw new Exception("Invalid import name.");Imports[checked(tableRva+index*8)]=dll+"!"+Ascii((int)value+2);
                }
            }
            int reloc=I32(pe+24+112+5*8),relocSize=I32(pe+24+116+5*8);
            if(reloc!=0&&relocSize>0)
            {
                byte[] data=Read(reloc,relocSize);
                for(int at=0;at<data.Length;)
                {
                    if(at+8>data.Length)throw new Exception("Incomplete image relocation block.");
                    int page=BitConverter.ToInt32(data,at),size=BitConverter.ToInt32(data,at+4);
                    if(size<8||(size&1)!=0||(long)at+size>data.Length)throw new Exception("Invalid image relocation block.");
                    for(int i=at+8;i<at+size;i+=2)
                    {
                        int entry=BitConverter.ToUInt16(data,i),kind=entry>>12;
                        if(kind==10)Relocations.Add(checked(page+(entry&4095)));
                        else if(kind!=0)throw new Exception("Unsupported x64 image relocation.");
                    }
                    at+=size;
                }
            }
        }
        void Bounds(int offset,int count){if(offset<0||count<0||(long)offset+count>bytes.Length)throw new Exception("Incomplete game executable.");}
        int I32(int at){Bounds(at,4);return BitConverter.ToInt32(bytes,at);}
        int U16(int at){Bounds(at,2);return BitConverter.ToUInt16(bytes,at);}
        long I64(int at){Bounds(at,8);return BitConverter.ToInt64(bytes,at);}
        string Ascii(int rva)
        {
            var text=new StringBuilder();for(int i=0;i<512;i++){byte value=Read(checked(rva+i),1)[0];if(value==0)return text.ToString();if(value>127)throw new Exception("Invalid import text.");text.Append((char)value);}throw new Exception("Unterminated import text.");
        }
        internal byte[] Read(int rva,int count)
        {
            foreach(var section in sections)
                if(rva>=section.Rva&&count>=0&&(long)rva+count<=(long)section.Rva+section.Size)
                {var result=new byte[count];Array.Copy(bytes,section.Raw+rva-section.Rva,result,0,count);return result;}
            throw new Exception("A compatibility reference points outside the game image.");
        }
        internal bool Executable(int rva,int count)
        {
            foreach(var section in sections)if((section.Flags&0x20000000)!=0&&rva>=section.Rva&&count>=0&&(long)rva+count<=(long)section.Rva+section.Size)return true;
            return false;
        }
        internal bool Mapped(int rva)
        {
            foreach(var section in sections)if(rva>=section.Rva&&(long)rva<(long)section.Rva+Math.Max(section.Size,section.VirtualSize))return true;
            return false;
        }
        internal void CheckIncoming(int begin,int end,ImageFunction owner,Dictionary<int,int> dependencies)
        {
            if(transfers==null)
            {
                transfers=new List<Transfer>();
                foreach(var section in sections)
                {
                    if((section.Flags&0x20000000)==0)continue;
                    for(int at=section.Raw;at<section.Raw+section.Size;at++)
                    {
                        byte op=bytes[at];int size=0,delta=0,remaining=section.Raw+section.Size-at;
                        if((op==0xe8||op==0xe9)&&remaining>=5){size=5;delta=BitConverter.ToInt32(bytes,at+1);}
                        else if((op==0xeb||op>=0x70&&op<=0x7f)&&remaining>=2){size=2;delta=unchecked((sbyte)bytes[at+1]);}
                        else if(op==0x0f&&remaining>=6&&bytes[at+1]>=0x80&&bytes[at+1]<=0x8f){size=6;delta=BitConverter.ToInt32(bytes,at+2);}
                        if(size==0)continue;long source=(long)section.Rva+at-section.Raw,target=source+size+delta;
                        if(target<0||target>Int32.MaxValue)continue;
                        var destination=FunctionAt((int)target);
                        if(destination==null||target==destination.Start||source>=destination.Start&&source<destination.Start+destination.Length)continue;
                        transfers.Add(new Transfer{Source=(int)source,Target=(int)target});
                    }
                }
                transfers.Sort((a,b)=>a.Target.CompareTo(b.Target));
            }
            int low=0,high=transfers.Count;while(low<high){int middle=low+(high-low)/2;if(transfers[middle].Target<begin)low=middle+1;else high=middle;}
            for(int at=low;at<transfers.Count&&transfers[at].Target<end;at++)
            {
                var branch=transfers[at];if(branch.Source>=owner.Start&&branch.Source<owner.Start+owner.Length)continue;
                var function=FunctionAt(branch.Source);if(function==null)continue; // Raw bytes outside function metadata do not establish an instruction.
                var decoder=Iced.Intel.Decoder.Create(64,new ByteArrayCodeReader(Read(function.Start,function.Length)));decoder.IP=(ulong)function.Start;
                while(decoder.IP<=(ulong)branch.Source)
                {
                    Instruction i;decoder.Decode(out i);if(i.IsInvalid)throw new Exception("Cannot establish the boundary of a possible incoming branch from a split function.");
                    if(i.IP!=(ulong)branch.Source)continue;
                    if(i.Op0Kind!=OpKind.NearBranch64||i.NearBranchTarget!=(ulong)branch.Target)break;
                    if(branch.Target!=begin)throw new Exception("A decoded branch from another function fragment enters patched bytes at RVA 0x"+branch.Target.ToString("x")+".");
                    dependencies[branch.Source]=i.Length;break;
                }
            }
        }
        internal ImageFunction FunctionAt(int rva)
        {
            int lo=0,hi=Functions.Count-1;
            while(lo<=hi){int mid=lo+(hi-lo)/2;var f=Functions[mid];if(rva<f.Start)hi=mid-1;else if(rva>=f.Start+f.Length)lo=mid+1;else return f;}
            return null;
        }
        internal IEnumerable<int> Find(byte[] needle,bool executable)
        {
            if(needle.Length==0)yield break;
            foreach(var section in sections)
            {
                if(executable&&(section.Flags&0x20000000)==0)continue;
                int end=section.Raw+section.Size-needle.Length+1;
                for(int at=section.Raw;at<end;)
                {
                    at=Array.IndexOf(bytes,needle[0],at,end-at);if(at<0)break;
                    bool match=true;for(int i=1;i<needle.Length;i++)if(bytes[at+i]!=needle[i]){match=false;break;}
                    if(match)yield return section.Rva+at-section.Raw;at++;
                }
            }
        }
        internal byte[] UnwindInfo(ImageFunction function)
        {
            byte[] head=Read(function.Unwind,4);return Read(function.Unwind,4+((head[2]+1)&~1)*2);
        }
        internal static string Digest(byte[] value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
    }
}
