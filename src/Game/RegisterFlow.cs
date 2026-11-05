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
            // read through the corresponding entry argument. Offset is bytes.
            internal readonly int Root;internal readonly long Offset;internal readonly bool Floating;
            internal Value(int root,long offset=0,bool floating=false){Root=root;Offset=offset;Floating=floating;}
            internal Value Add(long offset){return new Value(Root,checked(Offset+offset),Floating);}
            internal bool Same(Value other){return other!=null&&Root==other.Root&&Offset==other.Offset&&Floating==other.Floating;}
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
                foreach(long key in Stack.Keys.ToArray()){Value value;if(!other.Stack.TryGetValue(key,out value)||!Stack[key].Same(value)||Sizes[key]!=other.Sizes[key]){Stack.Remove(key);Sizes.Remove(key);changed=true;}}
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
                        if(i.Op1Kind==OpKind.Register){result=Get(state,i.Op1Register);if(result!=null&&i.Op0Register.GetSize()==4&&result.Root<7)result=null;}
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
                }
                Value scalarResult=null;
                if(vector>=0&&(i.Mnemonic==Mnemonic.Movss||i.Mnemonic==Mnemonic.Movaps||i.Mnemonic==Mnemonic.Movups))
                {
                    if(i.Op1Kind==OpKind.Register)scalarResult=Get(state,i.Op1Register);
                    else if(i.Mnemonic==Mnemonic.Movss&&i.Op1Kind==OpKind.Memory)scalarResult=Load(state,i,true);
                }
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
        internal Value At(int index,Register register){return index>=0&&index<before.Length&&before[index]!=null?Get(before[index],register):null;}
        internal bool Argument(int index,Register register,int argument,long offset=0){Value value=At(index,register);return value!=null&&value.Root==argument+1&&value.Offset==offset;}
        internal Value StackAt(int index,long offset)
        {
            if(index<0||index>=before.Length||before[index]==null)return null;Value sp=Get(before[index],Register.RSP),result;
            return sp!=null&&sp.Root==5&&before[index].Stack.TryGetValue(sp.Offset+offset,out result)?result:null;
        }
    }
}
