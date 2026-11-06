using System;
using System.Collections.Generic;
using System.Linq;
using Iced.Intel;
namespace AzerothTrainer
{
    // Must-analysis: a value survives a merge only when every incoming path
    // agrees. Registers, frame spills and pointer arithmetic retain provenance.
    internal sealed class RegisterFlow
    {
        internal sealed class Value
        {
            // Roots 1..4 are entry RCX/RDX/R8/R9, 5 is the frame, 6 an
            // image literal, 7 an integer constant, and 11..14 an object field
            // read through the corresponding entry argument. Root 20 is a sum;
            // root 22 identifies a scalar value in a local expression walk.
            // Offset is bytes except for scalar arguments and value identities.
            internal readonly int Root,Transform;internal readonly long Offset;internal readonly bool Floating;
            internal readonly Value Left,Right;internal readonly bool Current=true;internal readonly int Depth;
            internal Value(int root,long offset=0,bool floating=false,int transform=0){Root=root;Offset=offset;Floating=floating;Transform=transform;}
            internal Value Stale(){return new Value(this);}
            Value(Value source){Root=source.Root;Offset=source.Offset;Transform=source.Transform;Floating=source.Floating;Left=source.Left;Right=source.Right;Depth=source.Depth;Current=false;}
            internal Value(Value left,Value right,bool floating){Root=20;Left=left;Right=right;Floating=floating;Depth=Math.Max(left.Depth,right.Depth)+1;}
            internal static Value Sum(Value left,Value right,bool floating){return left==null||right==null||Math.Max(left.Depth,right.Depth)>=16?null:new Value(left,right,floating);}
            internal bool Snapshot {get{return Current&&(Root!=20||Left.Snapshot&&Right.Snapshot);}}
            internal bool ReadsField {get{return Root>=11&&Root<=14||Root==20&&(Left.ReadsField||Right.ReadsField);}}
            internal bool Aliases(Value address,int width)
            {
                if(!ReadsField)return false;if(address==null||address.Root<1||address.Root>4)return true;
                return Root==20?Left.Aliases(address,width)||Right.Aliases(address,width):Root!=address.Root+10||Offset<address.Offset+width&&address.Offset<Offset+4;
            }
            internal bool Clock(long offset){return Snapshot&&Root==20&&Floating&&(FieldTime(Left,Right,offset)||FieldTime(Right,Left,offset));}
            static bool FieldTime(Value field,Value time,long offset){return field.Current&&field.Root==11&&field.Floating&&field.Transform==0&&field.Offset==offset&&time.Root==8&&time.Offset==1;}
            internal Value Add(long offset){return new Value(Root,checked(Offset+offset),Floating,Transform);}
            internal bool Same(Value other){return other!=null&&Root==other.Root&&Offset==other.Offset&&Floating==other.Floating&&Transform==other.Transform&&(Root!=20||Left.Same(other.Left)&&Right.Same(other.Right));}
        }
        sealed class State
        {
            internal Value[] Registers=new Value[16],Scalars=new Value[16];internal Dictionary<long,Value> Stack=new Dictionary<long,Value>();internal Dictionary<long,int> Sizes=new Dictionary<long,int>();
            internal State Copy(){return new State{Registers=(Value[])Registers.Clone(),Scalars=(Value[])Scalars.Clone(),Stack=new Dictionary<long,Value>(Stack),Sizes=new Dictionary<long,int>(Sizes)};}
            internal bool Merge(State other)
            {
                bool changed=false;
                for(int i=0;i<16;i++)if(Registers[i]!=null&&!Registers[i].Same(other.Registers[i])){Registers[i]=null;changed=true;}
                for(int i=0;i<16;i++)if(Scalars[i]!=null&&!Scalars[i].Same(other.Scalars[i])){Scalars[i]=null;changed=true;}
                for(int i=0;i<16;i++)
                {
                    if(Registers[i]!=null&&Registers[i].Snapshot&&!other.Registers[i].Snapshot){Registers[i]=Registers[i].Stale();changed=true;}
                    if(Scalars[i]!=null&&Scalars[i].Snapshot&&!other.Scalars[i].Snapshot){Scalars[i]=Scalars[i].Stale();changed=true;}
                }
                foreach(long key in Stack.Keys.ToArray()){Value value;if(!other.Stack.TryGetValue(key,out value)||!Stack[key].Same(value)||Sizes[key]!=other.Sizes[key]){Stack.Remove(key);Sizes.Remove(key);changed=true;}else if(Stack[key].Snapshot&&!value.Snapshot){Stack[key]=Stack[key].Stale();changed=true;}}
                return changed;
            }
        }
        readonly State[] before;
        static int Vector(Register register){int index=(int)DecodedCode.Full(register)-(int)DecodedCode.Full(Register.XMM0);return index>=0&&index<16?index:-1;}
        static Value Get(State state,Register register){int index=DecodedCode.Gpr(register);if(index>=0)return state.Registers[index];index=Vector(register);return index>=0?state.Scalars[index]:null;}
        static Value Load(State state,Instruction i,bool scalar)
        {
            Value address=Address(state,i),result;
            if(address==null)return null;
            if(address.Root==5)return state.Stack.TryGetValue(address.Offset,out result)&&state.Sizes[address.Offset]>=i.MemorySize.GetSize()?result:null;
            return address.Root>=1&&address.Root<=4&&i.MemorySize.GetSize()==4?new Value(10+address.Root,address.Offset,scalar):null;
        }
        static Value Address(State state,Instruction i)
        {
            if(i.IsIPRelativeMemoryOperand)return new Value(6,(long)i.IPRelativeMemoryAddress);
            Value value=Get(state,i.MemoryBase);return value!=null&&value.Root<11&&i.MemoryIndex==Register.None?value.Add(unchecked((long)i.MemoryDisplacement64)):null;
        }
        internal RegisterFlow(DecodedCode code)
        {
            before=new State[code.Instructions.Length];var entry=new State();
            entry.Registers[DecodedCode.Gpr(Register.RCX)]=new Value(1);entry.Registers[DecodedCode.Gpr(Register.RDX)]=new Value(2);
            entry.Registers[DecodedCode.Gpr(Register.R8)]=new Value(3);entry.Registers[DecodedCode.Gpr(Register.R9)]=new Value(4);entry.Registers[DecodedCode.Gpr(Register.RSP)]=new Value(5);
            for(int n=0;n<4;n++)entry.Scalars[n]=new Value(8,n,true);
            before[0]=entry;var pending=new Queue<int>();var queued=new HashSet<int>();pending.Enqueue(0);queued.Add(0);var factory=new InstructionInfoFactory();int budget=0;
            while(pending.Count>0)
            {
                if(++budget>code.Instructions.Length*100)throw new Exception("Pointer provenance did not converge.");
                int at=pending.Dequeue();queued.Remove(at);var state=before[at].Copy();var i=code.Instructions[at];var info=DecodedCode.Info(factory,ref i);Value result=null;
                bool writesFirst=i.Op0Kind==OpKind.Register&&info.GetUsedRegisters().Any(r=>DecodedCode.Full(r.Register)==DecodedCode.Full(i.Op0Register)&&(DecodedCode.Write(r.Access)||r.Access==OpAccess.CondWrite||r.Access==OpAccess.ReadCondWrite));
                int destination=writesFirst?DecodedCode.Gpr(i.Op0Register):-1,vector=writesFirst?Vector(i.Op0Register):-1;
                if(destination>=0)
                {
                    if(i.Mnemonic==Mnemonic.Mov&&(i.Op0Register.GetSize()==8||i.Op0Register.GetSize()==4))
                    {
                        if(i.Op1Kind==OpKind.Register){result=Get(state,i.Op1Register);if(result!=null&&i.Op0Register.GetSize()==4&&result.Root<7)result=result.Root>=1&&result.Root<=4?new Value(9,result.Root-1):null;}
                        else if(i.Op1Kind==OpKind.Memory)
                        {
                            result=Load(state,i,false);
                        }
                        else if(i.Op1Kind==OpKind.Immediate64)result=new Value(7,unchecked((long)i.Immediate64));
                    }
                    if(i.Mnemonic==Mnemonic.Lea)result=Address(state,i);
                    if((i.Mnemonic==Mnemonic.Add||i.Mnemonic==Mnemonic.Sub)&&i.Op1Kind!=OpKind.Register&&i.Op1Kind!=OpKind.Memory)
                    {Value old=Get(state,i.Op0Register);if(old!=null&&old.Root<11)result=old.Add((i.Mnemonic==Mnemonic.Sub?-1:1)*unchecked((long)i.GetImmediate(1)));}
                    if(i.Mnemonic==Mnemonic.Mov&&i.Op0Register.GetSize()==4&&i.Op1Kind==OpKind.Immediate32)result=new Value(7,i.Immediate32);
                    if(i.Mnemonic==Mnemonic.Cmovb&&at>=2&&i.Op1Kind==OpKind.Register)
                    {
                        var limit=Get(state,i.Op0Register);var source=Get(state,i.Op1Register);var compare=code.Instructions[at-2];
                        if(limit!=null&&limit.Root==7&&limit.Offset==3&&source!=null&&source.Root>=11&&source.Root<=14&&source.Transform==0&&compare.Mnemonic==Mnemonic.Cmp&&compare.Op0Register==i.Op1Register&&DecodedCode.Immediate(compare,1,3)&&code.Instructions[at-1].Mnemonic==Mnemonic.Mov&&code.Instructions[at-1].Op0Register==i.Op0Register&&DecodedCode.Immediate(code.Instructions[at-1],1,3))result=new Value(source.Root,source.Offset,false,1);
                    }
                    if(i.Mnemonic==Mnemonic.Inc){var source=Get(state,i.Op0Register);if(source!=null&&source.Transform==1)result=new Value(source.Root,source.Offset,false,2);}
                }
                if(destination>=0&&i.Mnemonic==Mnemonic.Add&&i.Op0Register.GetSize()==4&&(i.Op1Kind==OpKind.Register||i.Op1Kind==OpKind.Memory))
                    result=Value.Sum(Get(state,i.Op0Register),i.Op1Kind==OpKind.Memory?Load(state,i,false):i.Op1Kind==OpKind.Register?Get(state,i.Op1Register):null,false);
                Value scalarResult=null;
                if(vector>=0&&(i.Mnemonic==Mnemonic.Movss||i.Mnemonic==Mnemonic.Movaps||i.Mnemonic==Mnemonic.Movups))
                {
                    if(i.Op1Kind==OpKind.Register)scalarResult=Get(state,i.Op1Register);
                    else if(i.Mnemonic==Mnemonic.Movss&&i.Op1Kind==OpKind.Memory)scalarResult=Load(state,i,true);
                }
                if(vector>=0&&i.Mnemonic==Mnemonic.Addss)
                    scalarResult=Value.Sum(Get(state,i.Op0Register),i.Op1Kind==OpKind.Memory?Load(state,i,true):i.Op1Kind==OpKind.Register?Get(state,i.Op1Register):null,true);
                if(i.Op0Kind==OpKind.Memory)
                {
                    Value address=Address(state,i);
                    if(address!=null&&address.Root==5)
                    {
                        int width=i.MemorySize.GetSize();
                        if(info.GetUsedMemory().Any(m=>DecodedCode.Write(m.Access)||m.Access==OpAccess.CondWrite||m.Access==OpAccess.ReadCondWrite))
                        {
                            foreach(long key in state.Stack.Keys.Where(k=>k<address.Offset+width&&address.Offset<k+state.Sizes[k]).ToArray()){state.Stack.Remove(key);state.Sizes.Remove(key);}
                            if((i.Mnemonic==Mnemonic.Mov||i.Mnemonic==Mnemonic.Movss)&&(width==8||width==4)&&i.Op1Kind==OpKind.Register){Value value=Get(state,i.Op1Register);if(value!=null){state.Stack[address.Offset]=value;state.Sizes[address.Offset]=width;}}
                        }
                    }
                }
                Value writtenAddress=i.Op0Kind==OpKind.Memory?Address(state,i):null;
                bool call=i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.IndirectCall;
                if(call||i.Op0Kind==OpKind.Memory&&(writtenAddress==null||writtenAddress.Root!=5)&&info.GetUsedMemory().Any(m=>DecodedCode.Write(m.Access)||m.Access==OpAccess.CondWrite||m.Access==OpAccess.ReadCondWrite))
                {
                    Value alias=call?null:writtenAddress;int width=i.MemorySize.GetSize();
                    for(int r=0;r<16;r++){if(state.Registers[r]!=null&&state.Registers[r].Aliases(alias,width))state.Registers[r]=state.Registers[r].Stale();if(state.Scalars[r]!=null&&state.Scalars[r].Aliases(alias,width))state.Scalars[r]=state.Scalars[r].Stale();}
                    foreach(long key in new List<long>(state.Stack.Keys))if(state.Stack[key].Aliases(alias,width))state.Stack[key]=state.Stack[key].Stale();
                }
                Value sp=Get(state,Register.RSP);
                if(sp!=null&&i.Mnemonic==Mnemonic.Sub&&i.Op0Register==Register.RSP&&i.Op1Register==Register.RAX)
                {int allocation=code.ProbedAllocation(at);if(allocation>0)result=sp.Add(-allocation);}
                foreach(var use in info.GetUsedRegisters())if(DecodedCode.Write(use.Access)||use.Access==OpAccess.CondWrite||use.Access==OpAccess.ReadCondWrite){int reg=DecodedCode.Gpr(use.Register);if(reg>=0)state.Registers[reg]=null;reg=Vector(use.Register);if(reg>=0)state.Scalars[reg]=null;}
                if(destination>=0)state.Registers[destination]=result;
                if(vector>=0)state.Scalars[vector]=scalarResult;
                if(sp!=null&&(i.Mnemonic==Mnemonic.Push||i.Mnemonic==Mnemonic.Pop))state.Registers[DecodedCode.Gpr(Register.RSP)]=sp.Add(i.Mnemonic==Mnemonic.Push?-8:8);
                if(i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.IndirectCall)
                {
                    state.Registers[DecodedCode.Gpr(Register.RSP)]=sp; // CALL returns with the caller's stack pointer.
                    bool probe=code.PreservesProbeArguments(at);
                    foreach(Register reg in probe?new[]{Register.R10,Register.R11}:new[]{Register.RAX,Register.RCX,Register.RDX,Register.R8,Register.R9,Register.R10,Register.R11})state.Registers[DecodedCode.Gpr(reg)]=null;
                    if(!probe)for(int reg=0;reg<6;reg++)state.Scalars[reg]=null;
                }
                foreach(int next in code.Next(at))
                {
                    bool changed;if(before[next]==null){before[next]=state.Copy();changed=true;}else changed=before[next].Merge(state);
                    if(changed&&queued.Add(next))pending.Enqueue(next);
                }
            }
        }
        // A local expression walk gives each scalar write a distinct identity.
        // Matching the total against the registers at the RNG call therefore
        // cannot confuse a field reload with the value used by the earlier sum.
        internal static int[] SumRegisters(Instruction[] instructions,int first,int end,int total)
        {
            var values=new Value[16];for(int r=0;r<16;r++)values[r]=new Value(22,r,true);
            var factory=new InstructionInfoFactory();int identity=16;
            for(int at=first;at<end;at++)
            {
                var i=instructions[at];if(i.FlowControl!=FlowControl.Next)return null;
                var info=DecodedCode.Info(factory,ref i);int destination=Vector(i.Op0Register);
                Value result=null;
                if(destination>=0)
                {
                    int source=i.Op1Kind==OpKind.Register?Vector(i.Op1Register):-1;
                    if(source>=0&&(i.Mnemonic==Mnemonic.Movaps||i.Mnemonic==Mnemonic.Movups||i.Mnemonic==Mnemonic.Movss))result=values[source];
                    else if(i.Mnemonic==Mnemonic.Addss&&source>=0)result=Value.Sum(values[destination],values[source],true);
                    else result=new Value(22,identity++,true);
                }
                foreach(var use in info.GetUsedRegisters())if(DecodedCode.Write(use.Access)||use.Access==OpAccess.CondWrite||use.Access==OpAccess.ReadCondWrite)
                {int r=Vector(use.Register);if(r>=0)values[r]=new Value(22,identity++,true);}
                if(destination>=0&&info.GetUsedRegisters().Any(r=>Vector(r.Register)==destination&&DecodedCode.Write(r.Access)))values[destination]=result;
            }
            var terms=new List<Value>();Terms(values[total],terms);if(terms.Count!=5)return null;var registers=new int[5];
            for(int n=0;n<5;n++)
            {
                var matches=Enumerable.Range(6,10).Where(r=>Object.ReferenceEquals(terms[n],values[r])).ToArray();if(matches.Length!=1)return null;registers[n]=matches[0];
            }
            return registers.Distinct().Count()==5?registers:null;
        }
        static void Terms(Value value,List<Value> terms)
        {if(value==null)return;if(value.Root==20&&value.Floating){Terms(value.Left,terms);Terms(value.Right,terms);}else terms.Add(value);}
        internal Value At(int index,Register register){return index>=0&&index<before.Length&&before[index]!=null?Get(before[index],register):null;}
        internal Value FrameAt(int index,long offset)
        {Value value;return index>=0&&index<before.Length&&before[index]!=null&&before[index].Stack.TryGetValue(offset,out value)?value:null;}
        internal bool Argument(int index,Register register,int argument,long offset=0){Value value=At(index,register);return value!=null&&value.Root==argument+1&&value.Offset==offset;}
        internal Value StackAt(int index,long offset)
        {
            if(index<0||index>=before.Length||before[index]==null)return null;Value sp=Get(before[index],Register.RSP),result;
            return sp!=null&&sp.Root==5&&before[index].Stack.TryGetValue(sp.Offset+offset,out result)?result:null;
        }
    }
}
