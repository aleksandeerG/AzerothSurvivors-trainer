using System;
using System.Collections.Generic;
using System.Linq;
using Iced.Intel;
namespace AzerothTrainer
{
    internal sealed class DecodedCode
    {
        internal readonly GameImage Image;
        internal readonly ImageFunction Function;
        internal readonly Instruction[] Instructions;
        internal readonly Dictionary<int,int> Dependencies=new Dictionary<int,int>();
        readonly Dictionary<ulong,int> indices=new Dictionary<ulong,int>();
        readonly Dictionary<int,int[]> jumpTables=new Dictionary<int,int[]>();
        RegisterFlow flow;
        internal RegisterFlow Flow {get{return flow??(flow=new RegisterFlow(this));}}
        delegate InstructionInfo ReadInfo(InstructionInfoFactory factory,ref Instruction instruction);
        static readonly ReadInfo readInfo=CreateInfoReader();
        static ReadInfo CreateInfoReader()
        {
            // The Windows Framework compiler predates C# ref returns. Copy the
            // decoder's readonly ref result without requiring a compiler SDK.
            var method=new System.Reflection.Emit.DynamicMethod("InstructionInfoCopy",typeof(InstructionInfo),new[]{typeof(InstructionInfoFactory),typeof(Instruction).MakeByRefType()},typeof(DecodedCode),true);
            var il=method.GetILGenerator();il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            il.Emit(System.Reflection.Emit.OpCodes.Call,typeof(InstructionInfoFactory).GetMethod("GetInfo",new[]{typeof(Instruction).MakeByRefType()}));
            il.Emit(System.Reflection.Emit.OpCodes.Ldobj,typeof(InstructionInfo));il.Emit(System.Reflection.Emit.OpCodes.Ret);return (ReadInfo)method.CreateDelegate(typeof(ReadInfo));
        }
        internal static InstructionInfo Info(InstructionInfoFactory factory,ref Instruction instruction){return readInfo(factory,ref instruction);}
        internal DecodedCode(GameImage image,ImageFunction function)
        {
            Image=image;Function=function;
            var decoder=Decoder.Create(64,new ByteArrayCodeReader(image.Read(function.Start,function.Length)));decoder.IP=(ulong)function.Start;
            var result=new List<Instruction>();ulong end=(ulong)(function.Start+function.Length);
            while(decoder.IP<end){Instruction instruction;decoder.Decode(out instruction);if(instruction.IsInvalid||instruction.NextIP>end)throw new Exception("Invalid or truncated x64 instruction.");indices.Add(instruction.IP,result.Count);result.Add(instruction);}
            Instructions=result.ToArray();
            ResolveTables();
            if(Instructions.Count(i=>i.FlowControl==FlowControl.IndirectBranch)>jumpTables.Count){flow=new RegisterFlow(this);ResolveTables();flow=null;}
        }
        void ResolveTables()
        {
            var factory=new InstructionInfoFactory();
            for(int at=0;at<Instructions.Length;at++)
            {
                var jump=Instructions[at];if(jump.FlowControl!=FlowControl.IndirectBranch||jump.Op0Kind!=OpKind.Register)continue;
                Register target=jump.Op0Register;int load=-1;Register table=Register.None;
                for(int p=at-1;p>=0&&p>=at-24;p--)
                {
                    var i=Instructions[p];if(i.Mnemonic==Mnemonic.Movsxd&&i.Op0Register==target&&i.Op1Kind==OpKind.Memory&&i.MemoryIndexScale==4&&i.MemoryBase!=Register.None){load=p;table=i.MemoryBase;break;}
                    if(i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.IndirectCall)break;
                }
                if(load<0||!Instructions.Skip(load+1).Take(at-load-1).Any(i=>i.Mnemonic==Mnemonic.Add&&i.Op0Register==target&&i.Op1Register==table))continue;
                int address=-1;
                for(int p=load-1;p>=0;p--)
                {
                    var i=Instructions[p];if(i.Mnemonic==Mnemonic.Lea&&i.Op0Register==table&&i.IsIPRelativeMemoryOperand){address=checked((int)i.IPRelativeMemoryAddress);break;}
                    if(Info(factory,ref i).GetUsedRegisters().Any(r=>Full(r.Register)==Full(table)&&Write(r.Access)))break;
                }
                if(address<0&&flow!=null){var value=flow.At(load,table);if(value!=null&&value.Root==6&&value.Offset<=Int32.MaxValue)address=(int)value.Offset;}
                if(address<0)continue;var targets=new List<int>();
                ulong boundary=Instructions.Where(i=>i.Mnemonic==Mnemonic.Lea&&i.IsIPRelativeMemoryOperand&&i.IPRelativeMemoryAddress>(ulong)address).Select(i=>i.IPRelativeMemoryAddress).DefaultIfEmpty(UInt64.MaxValue).Min();
                // LLVM emits contiguous signed offsets relative to the table.
                // Include non-boundary targets here so CheckSite rejects them.
                for(int entry=0;entry<1024;entry++)
                {
                    if((ulong)address+(uint)entry*4>=boundary)break;
                    int destination;try{destination=checked(address+BitConverter.ToInt32(Image.Read(checked(address+entry*4),4),0));}catch{break;}
                    if(destination<Function.Start||destination>=Function.Start+Function.Length)break;
                    targets.Add(destination);
                }
                if(targets.Count==0||targets.Count==1024)continue;
                Dependencies[address]=checked(targets.Count*4);
                jumpTables[at]=targets.Distinct().ToArray();
            }
        }
        internal int Index(ulong ip){int index;return indices.TryGetValue(ip,out index)?index:-1;}
        internal bool Calls(int target){return Instructions.Any(i=>(i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.UnconditionalBranch)&&i.Op0Kind==OpKind.NearBranch64&&i.NearBranchTarget==(ulong)target);}
        internal static Register Full(Register register){return register.GetFullRegister();}
        internal static int Xmm(Register register){return register>=Register.XMM0&&register<=Register.XMM15?(int)register-(int)Register.XMM0:-1;}
        internal static int Gpr(Register register){register=Full(register);return register>=Register.RAX&&register<=Register.R15?(int)register-(int)Register.RAX:-1;}
        internal static bool Reg(Instruction i,int operand){return operand<i.OpCount&&i.GetOpKind(operand)==OpKind.Register;}
        internal static bool Memory(Instruction i,Register register,int width=0){return i.OpCount>0&&i.GetOpKind(0)==OpKind.Memory||i.OpCount>1&&i.GetOpKind(1)==OpKind.Memory ? i.MemoryBase==register&&i.MemoryIndex==Register.None&&(width==0||i.MemorySize.GetSize()==width):false;}
        internal static int Disp(Instruction i){return unchecked((int)i.MemoryDisplacement64);}
        internal static bool Immediate(Instruction i,int operand,long value)
        {return operand<i.OpCount&&i.GetOpKind(operand)>=OpKind.Immediate8&&i.GetOpKind(operand)<=OpKind.Immediate32to64&&unchecked((long)i.GetImmediate(operand))==value;}
        internal static bool Read(OpAccess access){return access==OpAccess.Read||access==OpAccess.CondRead||access==OpAccess.ReadWrite||access==OpAccess.ReadCondWrite;}
        internal static bool Write(OpAccess access){return access==OpAccess.Write||access==OpAccess.ReadWrite;}
        internal int ProbedAllocation(int index)
        {
            var i=Instructions[index];if(i.Mnemonic!=Mnemonic.Sub||i.Op0Register!=Register.RSP||i.Op1Register!=Register.RAX)return 0;
            byte[] frame=Image.UnwindInfo(Function);ValidateUnwind(frame);
            for(int slot=0;slot<frame[2];)
            {
                int op=frame[5+slot*2]&15,extra=frame[5+slot*2]>>4,used=op==1?(extra==0?2:3):op==4||op==8?2:op==5||op==9?3:1;
                if(op==1&&frame[4+slot*2]==(int)i.NextIP-Function.Start)return extra==0?BitConverter.ToUInt16(frame,6+slot*2)*8:BitConverter.ToInt32(frame,6+slot*2);
                slot+=used;
            }
            return 0;
        }
        internal bool PreservesProbeArguments(int index)
        {
            var call=Instructions[index];if(index+1>=Instructions.Length||call.FlowControl!=FlowControl.Call||call.Op0Kind!=OpKind.NearBranch64||ProbedAllocation(index+1)==0)return false;
            var function=Image.FunctionAt(checked((int)call.NearBranchTarget));if(function==null||function.Start!=(int)call.NearBranchTarget||function.Length>512)return false;
            var decoder=Decoder.Create(64,new ByteArrayCodeReader(Image.Read(function.Start,function.Length)));decoder.IP=(ulong)function.Start;var factory=new InstructionInfoFactory();
            while(decoder.IP<(ulong)(function.Start+function.Length))
            {
                Instruction i;decoder.Decode(out i);if(i.IsInvalid||i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.IndirectCall||i.FlowControl==FlowControl.IndirectBranch)return false;
                if((i.FlowControl==FlowControl.ConditionalBranch||i.FlowControl==FlowControl.UnconditionalBranch)&&(i.NearBranchTarget<(ulong)function.Start||i.NearBranchTarget>=(ulong)(function.Start+function.Length)))return false;
                if(Info(factory,ref i).GetUsedRegisters().Any(r=>(Write(r.Access)||r.Access==OpAccess.CondWrite||r.Access==OpAccess.ReadCondWrite)&&new[]{Register.RAX,Register.RCX,Register.RDX,Register.R8,Register.R9,Full(Register.XMM0),Full(Register.XMM1),Full(Register.XMM2),Full(Register.XMM3)}.Contains(Full(r.Register))))return false;
            }
            Dependencies[function.Start]=function.Length;return true;
        }
        internal IEnumerable<int> Next(int index)
        {
            var i=Instructions[index];
            int[] targets;if(i.FlowControl==FlowControl.IndirectBranch&&jumpTables.TryGetValue(index,out targets))
            {
                foreach(int target in targets){int next=Index((ulong)target);if(next<0)throw new Exception("A relative jump-table entry is not an instruction boundary.");yield return next;}
                yield break;
            }
            if(i.FlowControl==FlowControl.ConditionalBranch||i.FlowControl==FlowControl.UnconditionalBranch)
            {int target=Index(i.NearBranchTarget);if(target>=0)yield return target;if(i.FlowControl==FlowControl.UnconditionalBranch)yield break;}
            if(i.FlowControl==FlowControl.Return||i.FlowControl==FlowControl.Exception||i.FlowControl==FlowControl.IndirectBranch)yield break;
            if(index+1<Instructions.Length)yield return index+1;
        }
        internal void DeadAfter(int index,params Register[] registers)
        {DeadAfter(index,2,registers);}
        internal void RequireSingleIntegerArgument(){DeadAfter(0,16,new[]{Register.RDX,Register.R8,Register.R9});}
        internal void RequireTwoIntegerArguments()
        {
            DeadAfter(0,16,new[]{Register.R8,Register.R9,Register.XMM0,Register.XMM1,Register.XMM2,Register.XMM3});
            var factory=new InstructionInfoFactory();
            for(int at=0;at<Instructions.Length;at++)
            {
                var i=Instructions[at];if(i.MemoryBase==Register.None)continue;
                var address=Flow.At(at,i.MemoryBase);
                if(address!=null&&address.Root==5&&address.Offset+unchecked((long)i.MemoryDisplacement64)+(i.MemoryIndex==Register.None?Math.Max(1,i.MemorySize.GetSize()):1)>40&&(i.Mnemonic==Mnemonic.Lea||Info(factory,ref i).GetUsedMemory().Any(m=>Read(m.Access))))
                    throw new Exception("Native action reads an incoming stack argument at RVA 0x"+i.IP.ToString("x")+".");
            }
        }
        void DeadAfter(int index,int depth,Register[] registers)
        {
            var factory=new InstructionInfoFactory();
            foreach(Register register in registers)
            {
                Register full=Full(register);var pending=new Stack<Tuple<int,ulong>>();var seen=new Dictionary<int,ulong>();pending.Push(Tuple.Create(index,RegisterBits(register,false)));
                while(pending.Count>0)
                {
                    var state=pending.Pop();int at=state.Item1;ulong bits=state.Item2,old;
                    if(seen.TryGetValue(at,out old)&&(bits&~old)==0)continue;seen[at]=old|bits;
                    var instruction=Instructions[at];var info=Info(factory,ref instruction);
                    foreach(var use in info.GetUsedRegisters())if(Full(use.Register)==full&&Read(use.Access)&&(RegisterBits(use.Register,false)&bits)!=0)throw new Exception(register+" is live at RVA 0x"+instruction.IP.ToString("x")+".");
                    foreach(var use in info.GetUsedRegisters())if(Full(use.Register)==full&&Write(use.Access))bits&=~RegisterBits(use.Register,true);
                    // R10/R11 are volatile and are not argument or return registers
                    // in the verified Windows x64 call convention.
                    if((instruction.FlowControl==FlowControl.Call||instruction.FlowControl==FlowControl.IndirectCall)&&!PreservesProbeArguments(at))
                    {
                        if(full==Register.RAX||register==Register.XMM0||full==Register.R10||full==Register.R11)bits=0;
                        else if(bits!=0)
                        {
                            if(UnusedImportArgument(instruction,full)){bits=0;continue;}
                            if(instruction.Op0Kind!=OpKind.NearBranch64)throw new Exception("Cannot prove "+register+" dead across an indirect call.");
                            CallDoesNotRead(instruction.NearBranchTarget,full,bits,depth);bits=0;
                        }
                    }
                    if(bits==0)continue;
                    if(instruction.FlowControl==FlowControl.Return&&full==Register.RAX)throw new Exception("RAX reaches a return without being overwritten.");
                    if((instruction.FlowControl==FlowControl.UnconditionalBranch||instruction.FlowControl==FlowControl.ConditionalBranch)&&Index(instruction.NearBranchTarget)<0)
                    {if(full!=Register.R10&&full!=Register.R11)CallDoesNotRead(instruction.NearBranchTarget,full,bits,2);if(instruction.FlowControl==FlowControl.UnconditionalBranch)continue;}
                    if(instruction.FlowControl==FlowControl.IndirectBranch&&!jumpTables.ContainsKey(at))throw new Exception("Register liveness crosses an unresolved indirect branch.");
                    foreach(int next in Next(at))pending.Push(Tuple.Create(next,bits));
                }
            }
        }
        static ulong RegisterBits(Register register,bool write)
        {
            if(Gpr(register)<0)return UInt64.MaxValue;int size=register.GetSize();
            if(size==8||write&&size==4)return UInt64.MaxValue; // 32-bit writes clear the upper half.
            if(register>=Register.AH&&register<=Register.BH)return 0xff00;
            return (1UL<<(size*8))-1;
        }
        void CallDoesNotRead(ulong target,Register register,ulong initial,int depth)
        {
            if(depth<0||target>Int32.MaxValue||!Image.Executable((int)target,1))throw new Exception("Callee argument liveness cannot be resolved at RVA 0x"+target.ToString("x")+" (remaining call depth "+depth+").");
            var function=Image.FunctionAt((int)target);if(function!=null&&function.Start!=(int)target)throw new Exception("Callee does not start at a function boundary.");
            // Small leaf math helpers legitimately have no .pdata entry. Decode
            // only reachable instructions, with bounded byte and call budgets.
            ulong end=function==null?target+1024:target+(uint)function.Length;
            var pending=new Stack<Tuple<ulong,ulong>>();var seen=new Dictionary<ulong,ulong>();var factory=new InstructionInfoFactory();pending.Push(Tuple.Create(target,initial));
            while(pending.Count>0)
            {
                var state=pending.Pop();ulong at=state.Item1,bits=state.Item2,old;
                if(seen.TryGetValue(at,out old)&&(bits&~old)==0)continue;seen[at]=old|bits;
                if(seen.Count>4096||at<target||at>=end||at>Int32.MaxValue||!Image.Executable((int)at,15))throw new Exception("Callee argument analysis exceeded its code bounds.");
                var decoder=Decoder.Create(64,new ByteArrayCodeReader(Image.Read((int)at,15)));decoder.IP=at;Instruction i;decoder.Decode(out i);if(i.IsInvalid)throw new Exception("Invalid instruction in a register-liveness dependency.");
                Dependencies[(int)at]=i.Length;var info=Info(factory,ref i);
                foreach(var use in info.GetUsedRegisters())if(Full(use.Register)==register&&Read(use.Access)&&(RegisterBits(use.Register,false)&bits)!=0)throw new Exception(register+" is an incoming callee argument at RVA 0x"+at.ToString("x")+".");
                foreach(var use in info.GetUsedRegisters())if(Full(use.Register)==register&&Write(use.Access))bits&=~RegisterBits(use.Register,true);
                if(i.FlowControl==FlowControl.Return&&bits!=0&&register==Register.RAX)throw new Exception("A tail callee returns the incoming RAX value.");
                if(bits==0||i.FlowControl==FlowControl.Return)continue;
                if(i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.IndirectCall)
                {
                    if(UnusedImportArgument(i,register))continue;
                    // A call followed by UD2 has no returning continuation.
                    // Explicit register reads above are still checked, but an
                    // incoming argument cannot survive this abort-only path.
                    if(i.NextIP+2<=end){byte[] next=Image.Read((int)i.NextIP,2);if(next[0]==0x0f&&next[1]==0x0b){Dependencies[(int)i.NextIP]=2;continue;}}
                    if(i.Op0Kind!=OpKind.NearBranch64){string import;Image.Imports.TryGetValue(unchecked((int)i.IPRelativeMemoryAddress),out import);throw new Exception("An indirect callee consumes an unresolved argument at RVA 0x"+i.IP.ToString("x")+" "+import+".");}
                    CallDoesNotRead(i.NearBranchTarget,register,bits,depth-1);continue;
                }
                if(i.FlowControl==FlowControl.IndirectBranch&&UnusedImportArgument(i,register))continue;
                if(i.FlowControl==FlowControl.IndirectBranch||i.FlowControl==FlowControl.Exception){string import;Image.Imports.TryGetValue(unchecked((int)i.IPRelativeMemoryAddress),out import);throw new Exception("Callee argument analysis encountered unresolved control flow at RVA 0x"+i.IP.ToString("x")+" "+import+".");}
                if(i.FlowControl==FlowControl.ConditionalBranch||i.FlowControl==FlowControl.UnconditionalBranch)
                {
                    ulong next=i.NearBranchTarget;
                    if(next>=target&&next<end)pending.Push(Tuple.Create(next,bits));else CallDoesNotRead(next,register,bits,depth-1);
                    if(i.FlowControl==FlowControl.UnconditionalBranch)continue;
                }
                pending.Push(Tuple.Create(i.NextIP,bits));
            }
        }
        bool UnusedImportArgument(Instruction instruction,Register register)
        {
            if(FloatingMathImport(instruction,register))return true;
            int position=register==Register.RCX?0:register==Register.RDX?1:register==Register.R8?2:register==Register.R9?3:-1;
            if(position<0||!instruction.IsIPRelativeMemoryOperand||instruction.IPRelativeMemoryAddress>Int32.MaxValue)return false;
            string import;if(!Image.Imports.TryGetValue((int)instruction.IPRelativeMemoryAddress,out import))return false;
            int split=import.IndexOf('!');string library=import.Substring(0,split),name=import.Substring(split+1);
            if(position==3&&(String.Equals(library,"VCRUNTIME140.dll",StringComparison.OrdinalIgnoreCase)||String.Equals(library,"ucrtbase.dll",StringComparison.OrdinalIgnoreCase)||String.Equals(library,"msvcrt.dll",StringComparison.OrdinalIgnoreCase))&&new[]{"memcpy","memmove","memcmp"}.Contains(name))return true; // Three integer arguments.
            if(!String.Equals(library,"kernel32.dll",StringComparison.OrdinalIgnoreCase)&&!String.Equals(library,"kernelbase.dll",StringComparison.OrdinalIgnoreCase))return false;
            // Published heapapi.h signatures: GetProcessHeap(void), HeapAlloc(heap, flags, bytes).
            // https://learn.microsoft.com/windows/win32/api/heapapi/nf-heapapi-getprocessheap
            // https://learn.microsoft.com/windows/win32/api/heapapi/nf-heapapi-heapalloc
            return name=="GetProcessHeap"||name=="HeapAlloc"&&position>=3;
        }
        bool FloatingMathImport(Instruction instruction,Register register)
        {
            if(register!=Register.RCX&&register!=Register.RDX&&register!=Register.R8&&register!=Register.R9&&register!=Full(Register.XMM1)&&register!=Full(Register.XMM2)&&register!=Full(Register.XMM3)||!instruction.IsIPRelativeMemoryOperand||instruction.IPRelativeMemoryAddress>Int32.MaxValue)return false;
            string name;if(!Image.Imports.TryGetValue((int)instruction.IPRelativeMemoryAddress,out name))return false;int split=name.IndexOf('!');string dll=name.Substring(0,split).ToLowerInvariant(),function=name.Substring(split+1);
            if(dll!="ucrtbase.dll"&&dll!="msvcrt.dll"&&!dll.StartsWith("api-ms-win-crt-math-l",StringComparison.Ordinal))return false;
            // These published CRT signatures take only floating arguments in
            // XMM registers. They neither consume nor preserve integer argument
            // registers, even when reached through a leaf import thunk.
            return new[]{"round","roundf","ceil","ceilf","floor","floorf","trunc","truncf"}.Contains(function);
        }
        internal void FlagsDead(int index)
        {
            var pending=new Stack<Tuple<int,uint>>();var seen=new Dictionary<int,uint>();pending.Push(Tuple.Create(index,(uint)(RflagsBits.CF|RflagsBits.PF|RflagsBits.AF|RflagsBits.ZF|RflagsBits.SF|RflagsBits.OF)));
            while(pending.Count>0)
            {
                var state=pending.Pop();var i=Instructions[state.Item1];uint flags=state.Item2,old;
                if(seen.TryGetValue(state.Item1,out old)&&(flags&~old)==0)continue;seen[state.Item1]=old|flags;
                if(((uint)i.RflagsRead&flags)!=0)throw new Exception("Arithmetic flags are live at RVA 0x"+i.IP.ToString("x")+".");
                flags&=~(uint)i.RflagsModified;if(flags==0)continue;
                if(i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.IndirectCall)continue;
                if(i.FlowControl==FlowControl.IndirectBranch&&!jumpTables.ContainsKey(state.Item1))throw new Exception("Flag liveness crosses an unresolved branch.");
                foreach(int next in Next(state.Item1))pending.Push(Tuple.Create(next,flags));
            }
        }
        internal void CheckSite(int first,int last,int continuation,int bypassEnd=0)
        {
            if(first<0||last<=first||last>Instructions.Length||continuation<0||continuation>=Instructions.Length)throw new Exception("Invalid decoded patch boundaries.");
            ulong begin=Instructions[first].IP,end=Instructions[last-1].NextIP;
            Image.CheckIncoming(checked((int)begin),checked((int)end),Function,Dependencies);
            // Replacing a decision tree makes its internal branches unreachable.
            // Confirm this from the decoded CFG, rather than accepting raw E8 bytes.
            var pending=new Stack<int>();var seen=new HashSet<int>();pending.Push(0);
            while(pending.Count>0)
            {
                int at=pending.Pop();if(!seen.Add(at))continue;
                if(at==first){pending.Push(continuation);continue;}
                var i=Instructions[at];
                if(i.IP>begin&&i.IP<end)throw new Exception("A reachable branch enters the middle of the patch.");
                foreach(int next in Next(at))pending.Push(next);
            }
            foreach(var i in Instructions)
                if((i.FlowControl==FlowControl.ConditionalBranch||i.FlowControl==FlowControl.UnconditionalBranch)&&i.NearBranchTarget>begin&&i.NearBranchTarget<end&&
                    !(bypassEnd>0&&i.IP>=begin&&i.IP<Instructions[bypassEnd].IP))throw new Exception("A branch outside the replaced decision tree enters patched bytes.");
            foreach(var jump in jumpTables)foreach(int target in jump.Value)
                if((ulong)target>begin&&(ulong)target<end)throw new Exception("A decoded jump-table target enters patched bytes.");
            foreach(var i in Instructions.Where(i=>i.FlowControl==FlowControl.IndirectBranch))
                if(!jumpTables.ContainsKey(Index(i.IP)))throw new Exception("Indirect jump at RVA 0x"+i.IP.ToString("x")+" has no verified relative jump table; incoming patch edges are unproven.");
            byte[] unwind=Image.UnwindInfo(Function);int prologue=unwind[1];
            if(begin<(ulong)(Function.Start+prologue))throw new Exception("The hook starts before its stack frame is complete.");
            if((unwind[0]&7)!=1||(unwind[0]&0x20)!=0)throw new Exception("Unsupported chained or versioned unwind information.");
            ValidateUnwind(unwind);
            // The frame is decoded from this image, never inferred from a build flag.
            for(int slot=0;slot<unwind[2];)
            {
                int op=unwind[5+slot*2]&15,extra=unwind[5+slot*2]>>4,used=op==1?(extra==0?2:3):(op==4||op==8?2:(op==5||op==9?3:1));
                if(op==6||op==7||op>10||slot+used>unwind[2])throw new Exception("Unsupported or incomplete unwind operation.");
                int code=unwind[4+slot*2];if(code>prologue||Index((ulong)(Function.Start+code))<0)throw new Exception("Unwind operation does not end on an instruction boundary.");
                slot+=used;
            }
        }
        void ValidateUnwind(byte[] unwind)
        {
            int allocation=0;var adjustments=new Dictionary<ulong,int>();
            for(int slot=0;slot<unwind[2];)
            {
                int op=unwind[5+slot*2]&15,extra=unwind[5+slot*2]>>4,count=op==1?(extra==0?2:3):(op==4||op==8?2:(op==5||op==9?3:1));
                if(slot+count>unwind[2]||op==6||op==7||op>9)throw new Exception("Unsupported unwind operation.");
                ulong end=(ulong)(Function.Start+unwind[4+slot*2]);int index=Index(end);if(index<=0)throw new Exception("Unwind operation is not an instruction boundary.");var i=Instructions[index-1];
                if(op==0)
                {
                    if(i.Mnemonic!=Mnemonic.Push||Gpr(i.Op0Register)!=extra)throw new Exception("Unwind push register disagrees with the prologue.");adjustments[i.IP]=8;allocation+=8;
                }
                if(op==1||op==2)
                {
                    int size=op==2?extra*8+8:extra==0?BitConverter.ToUInt16(unwind,6+slot*2)*8:BitConverter.ToInt32(unwind,6+slot*2);
                    if(size<=0||i.Mnemonic!=Mnemonic.Sub||i.Op0Register!=Register.RSP)throw new Exception("Unwind allocation disagrees with the prologue.");
                    if(!Immediate(i,1,size))
                    {
                        if(i.Op1Register!=Register.RAX)throw new Exception("Unrecognized variable stack allocation.");
                        bool probed=false;for(int p=Math.Max(0,index-5);p<index-1;p++)if(Instructions[p].Mnemonic==Mnemonic.Mov&&Instructions[p].Op0Register==Register.EAX&&Immediate(Instructions[p],1,size))probed=true;
                        if(!probed)throw new Exception("Stack-probe allocation size cannot be verified.");
                    }
                    adjustments[i.IP]=size;allocation+=size;
                }
                if(op==3)
                {
                    int frame=unwind[3]&15,offset=(unwind[3]>>4)*16;
                    if(Gpr(i.Op0Register)!=frame||!(i.Mnemonic==Mnemonic.Lea&&i.MemoryBase==Register.RSP&&i.MemoryIndex==Register.None&&Disp(i)==offset)&&!RRFrame(i,frame,offset))throw new Exception("Unwind frame register disagrees with the prologue.");
                }
                slot+=count;
            }
            if(allocation<32)throw new Exception("The original frame has no verified outgoing shadow space.");
            for(int slot=0;slot<unwind[2];)
            {
                int op=unwind[5+slot*2]&15,extra=unwind[5+slot*2]>>4,count=op==1?(extra==0?2:3):(op==4||op==8?2:(op==5||op==9?3:1));
                if(op==4||op==5||op==8||op==9)
                {
                    int index=Index((ulong)(Function.Start+unwind[4+slot*2]));var i=Instructions[index-1];
                    int offset=op==4||op==8?BitConverter.ToUInt16(unwind,6+slot*2)*(op==8?16:8):BitConverter.ToInt32(unwind,6+slot*2);
                    int actual=Disp(i);
                    if(i.MemoryBase==Register.RBP&&(unwind[3]&15)==5)actual+=(unwind[3]>>4)*16;
                    else if(i.MemoryBase==Register.RSP)actual+=allocation-adjustments.Where(a=>a.Key<i.IP).Sum(a=>a.Value);
                    else throw new Exception("Unrecognized nonvolatile save address.");
                    bool xmm=op==8||op==9;
                    if(i.Op0Kind!=OpKind.Memory||i.Op1Kind!=OpKind.Register||i.MemoryIndex!=Register.None||actual!=offset||(xmm?Xmm(i.Op1Register):Gpr(i.Op1Register))!=extra||i.MemorySize.GetSize()!=(xmm?16:8))throw new Exception("Nonvolatile register save disagrees with unwind metadata.");
                }
                slot+=count;
            }
        }
        static bool RRFrame(Instruction i,int frame,int offset){return offset==0&&i.Mnemonic==Mnemonic.Mov&&Gpr(i.Op0Register)==frame&&i.Op1Register==Register.RSP;}
    }
}
