using System;
using System.Collections.Generic;
using System.Text;
using Iced.Intel;
namespace AzerothTrainer
{
    // Supported Rust format bytecode: sequential arguments, literal spans and
    // fixed float precision. Indexed arguments and unknown tags use HUD fallback.
    internal static class NativeText
    {
        internal const string ReferenceKey="\0native-format";
        internal static bool StatLine(string[] keys){int count=0;foreach(string key in keys)foreach(var row in RunStat.All)if(row.TextKey==key){count++;break;}return count>=6;}
        internal static bool RunLine(string[] keys){int count=0;foreach(string key in keys)if(Array.IndexOf(new[]{"t","lvl","xp","hpm","sh","gold","lives","rr"},key)>=0)count++;return count>=4;}
        internal static bool LooksLikeTemplate(byte[] bytes,int at,int available)
        {
            int length=bytes[at];if(length<2||length>64||length+1>available||bytes[at+length]!='=')return false;
            for(int p=1;p<length;p++){byte c=bytes[at+p];if(c!=';'&&c!=' '&&c!='_'&&!(c>='a'&&c<='z')&&!(c>='A'&&c<='Z')&&!(c>='0'&&c<='9'))return false;}
            return true;
        }
        internal static bool Consumer(GameImage image,int target,DecodedCode witness)
        {
            var function=image.FunctionAt(target);if(function==null||function.Start!=target)return false;var builder=new DecodedCode(image,function);
            foreach(var call in builder.Instructions)
            {
                if(call.FlowControl!=FlowControl.Call||call.Op0Kind!=OpKind.NearBranch64)continue;int at=builder.Index(call.IP);
                if(!builder.Flow.Argument(at,Register.R8,1)||!builder.Flow.Argument(at,Register.R9,2))continue;
                var body=image.FunctionAt((int)call.NearBranchTarget);if(body==null||body.Start!=(int)call.NearBranchTarget)continue;var writer=new DecodedCode(image,body);var ins=writer.Instructions;
                for(int p=3;p+3<ins.Length;p++)
                {
                    var dispatch=ins[p];if(dispatch.FlowControl!=FlowControl.IndirectCall||dispatch.Op0Kind!=OpKind.Memory||dispatch.MemoryIndexScale!=1||dispatch.MemoryIndex==Register.None||DecodedCode.Disp(dispatch)!=8||!writer.Flow.Argument(p,dispatch.MemoryBase,3))continue;
                    Register index=dispatch.MemoryIndex;int scale=-1;
                    for(int q=Math.Max(1,p-16);q<p;q++)if(ins[q].Mnemonic==Mnemonic.Shl&&ins[q].Op0Register==index&&DecodedCode.Immediate(ins[q],1,4)&&ins[q-1].Mnemonic==Mnemonic.Mov&&ins[q-1].Op0Register==index)scale=q;
                    if(scale<0)continue;Register counter=ins[scale-1].Op1Register;
                    bool consistent=true;int strides=0;
                    for(int q=0;q+1<ins.Length;q++)
                        if(ins[q].Mnemonic==Mnemonic.Mov&&ins[q].Op0Register==index&&ins[q].Op1Register==counter)
                        {strides++;if(ins[q+1].Mnemonic!=Mnemonic.Shl||ins[q+1].Op0Register!=index||!DecodedCode.Immediate(ins[q+1],1,4))consistent=false;}
                    if(!consistent||strides!=2)continue;
                    bool load=false;for(int q=p-3;q<p;q++)if(ins[q].Mnemonic==Mnemonic.Mov&&ins[q].Op0Register==Register.RCX&&ins[q].MemoryBase==dispatch.MemoryBase&&ins[q].MemoryIndex==index&&DecodedCode.Disp(ins[q])==0)load=true;
                    bool zero=false;foreach(var i in ins)if(DecodedCode.Zero(i)&&DecodedCode.Full(i.Op0Register)==DecodedCode.Full(counter))zero=true;
                    bool increment=false;for(int q=p+1;q<=p+3;q++)if(ins[q].Mnemonic==Mnemonic.Inc&&DecodedCode.Full(ins[q].Op0Register)==DecodedCode.Full(counter))increment=true;
                    if(!load||!zero||!increment)continue;
                    witness.Dependencies[function.Start]=function.Length;witness.Dependencies[body.Start]=body.Length;return true;
                }
            }
            return false;
        }
        internal static string[] Keys(GameImage image,int address,out int length)
        {
            var keys=new List<string>();string literal="";int at=address;
            for(int budget=0;budget<256;budget++)
            {
                byte tag=image.Read(at++,1)[0];
                if(tag==0){length=at-address;return keys.ToArray();}
                if(tag<128)
                {literal+=Encoding.ASCII.GetString(image.Read(at,tag));at+=tag;continue;}
                if(tag!=0xc0&&tag!=0xc1&&tag!=0xc5)throw new Exception("Unsupported native format tag; sequential argument order is unproven.");
                if(tag!=0xc0)
                {
                    byte[] flags=image.Read(at,4);if(BitConverter.ToUInt32(flags,0)!=0x70000020)throw new Exception("Unsupported native format flags.");at+=4;
                    if(tag==0xc5){if(BitConverter.ToUInt16(image.Read(at,2),0)>9)throw new Exception("Unsupported format precision.");at+=2;}
                }
                string key=literal.Trim().TrimStart(';').Trim();
                if(!key.EndsWith("=",StringComparison.Ordinal))throw new Exception("Native format argument has no key.");key=key.Substring(0,key.Length-1);
                foreach(char c in key)if(c!='_'&&!Char.IsLetterOrDigit(c))throw new Exception("Native format key is ambiguous.");
                if(key.Length==0||keys.Contains(key))throw new Exception("Duplicate or empty native format key.");keys.Add(key);literal="";
            }
            throw new Exception("Native format exceeds its bounded parser.");
        }
        internal static bool Formatter(GameImage image,RegisterFlow.Value formatter,bool floating,DecodedCode witness,int width=4)
        {
            if(formatter==null||formatter.Root!=6||formatter.Offset<0||formatter.Offset>Int32.MaxValue||!image.Executable((int)formatter.Offset,96))return false;
            var decoder=Iced.Intel.Decoder.Create(64,new ByteArrayCodeReader(image.Read((int)formatter.Offset,96)));decoder.IP=(ulong)formatter.Offset;
            for(int n=0;n<12;n++)
            {
                Instruction i;decoder.Decode(out i);if(i.IsInvalid||i.FlowControl!=FlowControl.Next)return false;
                if(i.Op1Kind==OpKind.Memory&&DecodedCode.Memory(i,Register.RCX,width)&&DecodedCode.Disp(i)==0)
                {if(i.Mnemonic!=(floating?Mnemonic.Movss:Mnemonic.Mov))return false;witness.Dependencies[(int)formatter.Offset]=(int)(i.NextIP-(ulong)formatter.Offset);return true;}
                if(i.Op0Kind==OpKind.Register&&DecodedCode.Full(i.Op0Register)==Register.RCX)return false;
            }
            return false;
        }
    }
}
