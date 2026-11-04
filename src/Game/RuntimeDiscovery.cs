using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Iced.Intel;
namespace AzerothTrainer
{
    internal sealed class RarityRegisters
    {
        internal int[] Weights;internal int Result;
    }
    internal sealed class RuntimeDiscovery
    {
        internal readonly GameImage Image;
        internal readonly Dictionary<string,DecodedCode> Functions=new Dictionary<string,DecodedCode>();
        internal readonly Dictionary<string,HookSite> Sites=new Dictionary<string,HookSite>();
        internal readonly Dictionary<string,RarityRegisters> Rarity=new Dictionary<string,RarityRegisters>();
        internal readonly Dictionary<string,Dictionary<string,int>> Fields=new Dictionary<string,Dictionary<string,int>>();
        internal readonly Dictionary<string,string> Failures=new Dictionary<string,string>();
        internal int HealthRegister=-1,PriceCount,OfferStride,OfferRarity;
        readonly Dictionary<int,DecodedCode> cache=new Dictionary<int,DecodedCode>();
        internal RuntimeDiscovery(GameImage image)
        {
            Image=image;
            DiscoverRarity();DiscoverHealth();DiscoverRun();DiscoverCoop();DiscoverXp();DiscoverChoose();DiscoverPrices();DiscoverProfile();cache.Clear();
        }
        DecodedCode Decode(ImageFunction f)
        {DecodedCode code;if(!cache.TryGetValue(f.Start,out code)){code=new DecodedCode(Image,f);if(cache.Count>=64)cache.Clear();cache.Add(f.Start,code);}return code;}
        IEnumerable<DecodedCode> Anchored(byte[] bytes)
        {
            foreach(var f in Image.Find(bytes,true).Select(r=>Image.FunctionAt(r)).Where(f=>f!=null).GroupBy(f=>f.Start).Select(g=>g.First()))
            {DecodedCode code;try{code=Decode(f);}catch{continue;}yield return code;}
        }
        IEnumerable<DecodedCode> Referencing(string value)
        {
            var targets=new HashSet<int>(Image.Find(Encoding.UTF8.GetBytes(value),false));var found=new HashSet<int>();
            foreach(byte rex in new byte[]{0x48,0x4c})
                foreach(int at in Image.Find(new[]{rex,(byte)0x8d},true))
                {
                    byte[] bytes;try{bytes=Image.Read(at,7);}catch{continue;}
                    if((bytes[2]&0xc7)!=5||!targets.Contains(at+7+BitConverter.ToInt32(bytes,3)))continue;
                    var f=Image.FunctionAt(at);if(f==null||!found.Add(f.Start))continue;var code=Decode(f);int index=code.Index((ulong)at);
                    if(index>=0&&code.Instructions[index].IsIPRelativeMemoryOperand)yield return code;
                }
        }
        static bool RR(Instruction i,Mnemonic name,Register destination,Register source)
        {return i.Mnemonic==name&&i.Op0Kind==OpKind.Register&&i.Op1Kind==OpKind.Register&&i.Op0Register==destination&&i.Op1Register==source;}
        static bool Store(Instruction i,Mnemonic name,Register owner,int offset,Register source)
        {return i.Mnemonic==name&&i.Op0Kind==OpKind.Memory&&i.Op1Kind==OpKind.Register&&DecodedCode.Memory(i,owner)&&DecodedCode.Disp(i)==offset&&i.Op1Register==source;}
        static bool Nonvolatile(Register r){return DecodedCode.Xmm(r)>=6;}
        static int SkipNops(Instruction[] code,int at){while(at<code.Length&&code[at].Mnemonic==Mnemonic.Nop)at++;return at;}
        static void Need(bool condition,string reason){if(!condition)throw new Exception(reason);}
        void Field(string name,int offset,string witness)
        {
            Need(offset>=0x100&&offset<0x10000,"Field "+name+" lies outside the supported object range.");
            Dictionary<string,int> evidence;if(!Fields.TryGetValue(name,out evidence)){evidence=new Dictionary<string,int>();Fields.Add(name,evidence);}
            evidence[witness]=offset;
        }
        internal void Region(string group,int start,string source)
        {
            Need(start>=0x100&&start+0xd0<=0x10000&&(start&3)==0,"Invalid attributes region.");
            if(group!="Attributes")throw new Exception("Unknown region.");
            Field("hp",start+8,source);Field("maxHp",start+0x68,source);
            Field("attributes",start,source);
        }
        HookSite Site(string name,DecodedCode code,int first,int last,int continuation,int guardEnd=0)
        {
            code.CheckSite(first,last,continuation,guardEnd);
            int start=checked((int)code.Instructions[first].IP),end=checked((int)code.Instructions[last-1].NextIP),next=checked((int)code.Instructions[continuation].IP);
            var site=new HookSite(Image,code.Function,start,end-start,next,guardEnd==0?end-start:checked((int)code.Instructions[guardEnd].IP-start));
            if(name!="branch"&&name!="cost")
            {
                foreach(Register register in new[]{Register.R11,Register.R10})try{code.DeadAfter(continuation,register);site.TailRegister=DecodedCode.Gpr(register);break;}catch{}
                Need(site.TailRegister>=0,"No dead continuation register; refusing to borrow game stack space.");
            }
            return site;
        }
        int Cover(DecodedCode code,int first,int minimum)
        {int end=first;ulong at=code.Instructions[first].IP;while(end<code.Instructions.Length&&code.Instructions[end].NextIP-at<(ulong)minimum)end++;Need(end<code.Instructions.Length,"Truncated patch window.");return end+1;}
        void DiscoverRarity()
        {
            var results=new List<Tuple<DecodedCode,HookSite,RarityRegisters>>();
            foreach(var code in Anchored(new byte[]{0x0f,0x2e}))
            {
                var ins=code.Instructions;
                for(int at=5;at+3<ins.Length;at++)
                {
                    if(ins[at].Mnemonic!=Mnemonic.Ucomiss||ins[at].Op1Register!=Register.XMM0||!Nonvolatile(ins[at].Op0Register)||ins[at+1].Mnemonic!=Mnemonic.Jbe)continue;
                    int call=at-1;while(call>=0&&ins[call].Mnemonic==Mnemonic.Nop)call--;
                    if(call<0||ins[call].FlowControl!=FlowControl.Call||ins[call].Op0Kind!=OpKind.NearBranch64)continue;
                    try
                    {
                        int sum=-1;
                        for(int p=Math.Max(0,call-13);p+4<call;p++)
                            if(ins[p].Mnemonic==Mnemonic.Movaps&&ins[p].Op0Register==Register.XMM2&&Nonvolatile(ins[p].Op1Register)&&
                                Enumerable.Range(1,4).All(k=>ins[p+k].Mnemonic==Mnemonic.Addss&&ins[p+k].Op0Register==Register.XMM2&&Nonvolatile(ins[p+k].Op1Register)))sum=p;
                        Need(sum>=0,"Rarity weight sum was not found before the RNG call.");
                        var summed=new[]{ins[sum].Op1Register,ins[sum+1].Op1Register,ins[sum+2].Op1Register,ins[sum+3].Op1Register,ins[sum+4].Op1Register};
                        Need(summed.Distinct().Count()==5,"Rarity weights alias each other.");
                        var info=new InstructionInfoFactory();
                        for(int p=sum+5;p<call;p++)
                        {var i=ins[p];Need(!DecodedCode.Info(info,ref i).GetUsedRegisters().Any(r=>summed.Contains(r.Register)&&DecodedCode.Write(r.Access)),"A weight changes between the total and the RNG call.");}
                        Need(ins[at+2].Mnemonic==Mnemonic.Xor&&ins[at+2].Op0Kind==OpKind.Register&&ins[at+2].Op0Register==ins[at+2].Op1Register&&ins[at+2].Op0Register.GetSize()==4,"Rarity result register is not initialized to zero.");
                        Register result=ins[at+2].Op0Register;var weights=new List<Register>{ins[at].Op0Register};
                        int cursor=code.Index(ins[at+1].NearBranchTarget);Need(cursor>=0,"Rarity branch leaves the function.");
                        if(RR(ins[cursor],Mnemonic.Subss,Register.XMM0,weights[0]))cursor++;
                        else
                        {
                            Need(ins[cursor].Mnemonic==Mnemonic.Addss&&ins[cursor].Op0Register==Register.XMM0&&ins[cursor].IsIPRelativeMemoryOperand,"Rarity first-weight subtraction changed.");
                            float negative=BitConverter.ToSingle(Image.Read(checked((int)ins[cursor].IPRelativeMemoryAddress),4),0);bool constant=false;
                            for(int p=0;p<sum;p++)
                            {
                                var i=ins[p];if(!DecodedCode.Info(info,ref i).GetUsedRegisters().Any(r=>r.Register==weights[0]&&DecodedCode.Write(r.Access)))continue;
                                constant=i.Op0Kind==OpKind.Register&&i.Op0Register==weights[0]&&i.Mnemonic==Mnemonic.Movss&&i.IsIPRelativeMemoryOperand&&BitConverter.ToSingle(Image.Read(checked((int)i.IPRelativeMemoryAddress),4),0)==-negative;
                            }
                            Need(constant&&negative<0,"Rarity constant weight cannot be corroborated.");cursor++;
                        }
                        ulong merge=0;
                        for(int rank=1;rank<=3;rank++)
                        {
                            cursor=SkipNops(ins,cursor);Need(ins[cursor].Mnemonic==Mnemonic.Mov&&ins[cursor].Op0Register==result&&DecodedCode.Immediate(ins[cursor],1,rank),"Rarity rank assignment changed.");cursor++;
                            Need(ins[cursor].Mnemonic==Mnemonic.Ucomiss&&ins[cursor].Op1Register==Register.XMM0&&Nonvolatile(ins[cursor].Op0Register),"Rarity threshold changed.");weights.Add(ins[cursor].Op0Register);cursor++;
                            Need(ins[cursor].Mnemonic==Mnemonic.Ja,"Rarity threshold branch changed.");if(merge==0)merge=ins[cursor].NearBranchTarget;Need(merge==ins[cursor].NearBranchTarget,"Rarity thresholds have different continuations.");cursor++;
                            Need(RR(ins[cursor],Mnemonic.Subss,Register.XMM0,weights[rank]),"Rarity threshold subtraction changed.");cursor++;
                        }
                        Need(ins[cursor].Mnemonic==Mnemonic.Ucomiss&&ins[cursor].Op1Register==Register.XMM0&&Nonvolatile(ins[cursor].Op0Register),"Legendary threshold changed.");weights.Add(ins[cursor].Op0Register);cursor++;
                        Need(ins[cursor].Mnemonic==Mnemonic.Jbe&&ins[cursor].NearBranchTarget==ins[at+2].IP,"Legendary fallback changed.");cursor++;
                        Need(ins[cursor].Mnemonic==Mnemonic.Mov&&ins[cursor].Op0Register==result&&DecodedCode.Immediate(ins[cursor],1,4),"Legendary rank changed.");
                        Need(new HashSet<Register>(weights).SetEquals(summed),"Threshold registers disagree with the weight sum.");int continuation=code.Index(merge);Need(continuation>at,"Rarity continuation is not a decoded boundary.");
                        if(DecodedCode.Full(result)!=Register.RAX)code.DeadAfter(continuation,Register.RAX);code.FlagsDead(continuation);
                        var site=Site("roll",code,at,Cover(code,at,12),continuation,continuation);
                        results.Add(Tuple.Create(code,site,new RarityRegisters{Weights=weights.Select(DecodedCode.Xmm).ToArray(),Result=DecodedCode.Gpr(result)}));
                    }
                    catch(Exception ex){if(!Failures.ContainsKey("Rarity")||!ex.Message.StartsWith("Rarity weight sum"))Failures["Rarity"]="RVA 0x"+ins[at].IP.ToString("x")+": "+ex.Message;}
                }
            }
            var spell=results.GroupBy(r=>r.Item1.Function.Start).Where(g=>g.Count()==1).Select(g=>g.First()).ToArray();
            if(spell.Length==1){Functions["Spell"]=spell[0].Item1;Sites["Spell"]=spell[0].Item2;Rarity["Spell"]=spell[0].Item3;}else Failures["Spell"]="Expected one decoded spell roll; found "+spell.Length+". "+Failure("Rarity");
            var items=results.GroupBy(r=>r.Item1.Function.Start).Where(g=>g.Count()==2).ToArray();
            if(items.Length==1)
            {
                int index=0;foreach(var item in items[0].OrderBy(r=>r.Item2.Rva)){string name="Jewelry"+(++index);Sites[name]=item.Item2;Rarity[name]=item.Item3;}Functions["Jewelry"]=items[0].First().Item1;
            }
            else Failures["Jewelry"]="Expected one decoded item-roll pair; found "+items.Length+". "+Failure("Rarity");
            if(Functions.ContainsKey("Spell")&&Functions.ContainsKey("Jewelry"))Failures.Remove("Rarity");
        }
        string Failure(string name){string reason;return Failures.TryGetValue(name,out reason)?reason:"No matching instruction shape.";}
        void DiscoverHealth()
        {
            var candidates=new List<Tuple<DecodedCode,int,int,int>>();
            foreach(var code in Anchored(new byte[]{0x0f,0x5c}))
            {
                var ins=code.Instructions;
                for(int at=1;at+3<ins.Length;at++)
                {
                    var sub=ins[at];if(sub.Mnemonic!=Mnemonic.Subss||sub.Op1Kind!=OpKind.Memory||sub.MemoryIndex!=Register.None||sub.MemoryBase==Register.RSP||sub.MemoryBase==Register.RBP||sub.MemoryBase==Register.RIP)continue;
                    Register delta=sub.Op0Register,owner=sub.MemoryBase;int field=DecodedCode.Disp(sub);
                    if(ins[at-1].Mnemonic!=Mnemonic.Movaps||ins[at-1].Op0Register!=delta||!DecodedCode.Reg(ins[at-1],1))continue;Register maximum=ins[at-1].Op1Register;
                    if(ins[at+1].Mnemonic!=Mnemonic.Addss||!Store(ins[at+2],Mnemonic.Movss,owner,field,maximum)||ins[at+3].Mnemonic!=Mnemonic.Movss||!DecodedCode.Memory(ins[at+3],owner)||DecodedCode.Disp(ins[at+3])!=field+4)continue;
                    try
                    {
                        int first=at-1,last=at+2;code.DeadAfter(first,Register.RAX);Site("HealthHook",code,first,last,last);
                        Need(field>=0x168&&field<0x10000&&(field&3)==0,"Maximum-health field is out of range.");
                        // Recognize the stat record stores and the HP clamp in the
                        // same basic region. Unrelated code elsewhere is irrelevant.
                        var written=new HashSet<int>();
                        for(int p=at+2;p<ins.Length&&p<at+110;p++)
                        {
                            var i=ins[p];if(i.Op0Kind==OpKind.Memory&&DecodedCode.Memory(i,owner)&&(i.Mnemonic==Mnemonic.Movss||i.Mnemonic==Mnemonic.Mov||i.Mnemonic==Mnemonic.Movlps))
                                for(int b=0;b<i.MemorySize.GetSize();b+=4)written.Add(DecodedCode.Disp(i)+b);
                        }
                        foreach(int reference in RunStat.All.Where(s=>s.Field!=0x628).Select(s=>s.Field))Need(written.Contains(field+reference-0x5c8),"Stat member +0x"+(reference-0x5c8).ToString("x")+" has no decoded store.");
                        bool clamp=false;
                        for(int p=at+3;p<Math.Min(ins.Length-2,at+120);p++)if(ins[p].Mnemonic==Mnemonic.Minss&&ins[p].Op1Register==maximum)
                            for(int q=Math.Max(at+2,p-10);q<p;q++)if(ins[q].Mnemonic==Mnemonic.Movss&&ins[q].Op1Kind==OpKind.Memory&&DecodedCode.Memory(ins[q],owner)&&DecodedCode.Disp(ins[q])==field-0x60)clamp=true;
                        Need(clamp,"Maximum-health result is not tied to the current-HP clamp.");candidates.Add(Tuple.Create(code,first,last,DecodedCode.Xmm(maximum)));
                    }
                    catch(Exception ex){Failures["Health"]=ex.Message;}
                }
            }
            if(candidates.Count!=1){Failures["Health"]="Expected one stat recalculation; found "+candidates.Count+". "+Failure("Health");return;}
            var match=candidates[0];Functions["Health"]=match.Item1;Sites["HealthHook"]=Site("HealthHook",match.Item1,match.Item2,match.Item3,match.Item3);HealthRegister=match.Item4;
            Region("Attributes",DecodedCode.Disp(match.Item1.Instructions[match.Item2+1])-0x68,"Health");
            var body=match.Item1.Instructions;var maximumOperand=body[match.Item2+1];int hp=DecodedCode.Disp(maximumOperand)-0x60;
            for(int p=match.Item3;p+1<body.Length;p++)
            {
                var load=body[p];if(load.Mnemonic!=Mnemonic.Movss||load.Op1Kind!=OpKind.Memory||!DecodedCode.Memory(load,maximumOperand.MemoryBase,4)||DecodedCode.Disp(load)!=hp+4)continue;
                var natural=body[p+1];if(natural.Mnemonic!=Mnemonic.Movss||!DecodedCode.Memory(natural,maximumOperand.MemoryBase,4)||DecodedCode.Disp(natural)!=hp+8)continue;
                if(body.Skip(p+2).Take(35).Any(i=>Store(i,Mnemonic.Movss,maximumOperand.MemoryBase,DecodedCode.Disp(load),load.Op0Register)))Field("shield",DecodedCode.Disp(load),"Health");
            }
        }
        void DiscoverRun()
        {
            var candidates=new List<Tuple<DecodedCode,int>>();
            foreach(var code in Referencing("WOW_ROGUE_SINK_AT"))
            {
                var ins=code.Instructions;
                for(int at=0;at+3<ins.Length&&ins[at].IP<(ulong)(code.Function.Start+400);at++)
                {
                    if(ins[at].Mnemonic!=Mnemonic.Movaps||ins[at].Op1Register!=Register.XMM1||!Nonvolatile(ins[at].Op0Register)||ins[at+1].Mnemonic!=Mnemonic.Mov||ins[at+1].Op1Register!=Register.RCX||ins[at+1].Op0Kind!=OpKind.Register)continue;
                    if(ins[at+2].Mnemonic!=Mnemonic.Cmp||!DecodedCode.Memory(ins[at+2],Register.RCX,1)||!DecodedCode.Immediate(ins[at+2],1,0)||ins[at+3].FlowControl!=FlowControl.ConditionalBranch)continue;
                    try{code.DeadAfter(at,Register.RAX);Site("RunHook",code,at,at+3,at+3);candidates.Add(Tuple.Create(code,at));}catch(Exception ex){Failures["Run"]=ex.Message;}
                }
            }
            if(candidates.Count!=1){Failures["Run"]="Run string/context selected "+candidates.Count+" candidates. "+Failure("Run");return;}
            var match=candidates[0];var list=match.Item1.Instructions;int first=match.Item2;Register game=list[first+1].Op0Register,dt=list[first].Op0Register;
            Functions["Run"]=match.Item1;Sites["RunHook"]=Site("RunHook",match.Item1,first,first+3,first+3);Field("coop",DecodedCode.Disp(list[first+2]),"Run");
            for(int p=first+3;p+3<list.Length;p++)
            {
                var i=list[p];
                if(i.Mnemonic==Mnemonic.Movzx&&i.Op1Kind==OpKind.Memory&&DecodedCode.Memory(i,game,1)&&list[p+1].Mnemonic==Mnemonic.Cmp&&DecodedCode.Immediate(list[p+1],1,1))Field("phase",DecodedCode.Disp(i),"Run");
                if(i.Mnemonic==Mnemonic.Movss&&i.Op1Kind==OpKind.Memory&&DecodedCode.Memory(i,game)&&RR(list[p+1],Mnemonic.Addss,i.Op0Register,dt)&&Store(list[p+2],Mnemonic.Movss,game,DecodedCode.Disp(i),i.Op0Register))Field("clock",DecodedCode.Disp(i),"Run");
                if(i.Mnemonic==Mnemonic.Movss&&i.Op1Kind==OpKind.Memory&&DecodedCode.Memory(i,game)&&Store(list[p+1],Mnemonic.Movss,game,DecodedCode.Disp(i)-0x60,i.Op0Register))
                {Field("maxHp",DecodedCode.Disp(i),"Run");Field("hp",DecodedCode.Disp(list[p+1]),"Run");}
            }
        }
        void DiscoverXp()
        {
            var candidates=new List<Tuple<DecodedCode,int>>();
            // Argument capture and an integer award added back to its own field.
            foreach(var f in Image.Functions.Where(f=>f.Length>100&&f.Length<16000))
            {
                byte[] prefix=Image.Read(f.Start,Math.Min(f.Length,256));bool possible=false;
                for(int p=0;p+1<prefix.Length;p++)if(prefix[p]==0x89&&(prefix[p+1]&0xf8)==0xd0){possible=true;break;}
                if(!possible)continue;DecodedCode code;try{code=Decode(f);}catch{continue;}var ins=code.Instructions;
                for(int at=0;at+3<ins.Length&&ins[at].IP<(ulong)(f.Start+256);at++)
                {
                    var award=ins[at];if(award.Mnemonic!=Mnemonic.Mov||award.Op0Kind!=OpKind.Register||award.Op1Register!=Register.EDX||award.Op0Register.GetSize()!=4)continue;
                    if(ins[at+1].Mnemonic!=Mnemonic.Mov||ins[at+1].Op1Register!=Register.RCX||ins[at+1].Op0Kind!=OpKind.Register)continue;
                    var add=ins[at+2];if(add.Mnemonic!=Mnemonic.Add||add.Op0Register!=award.Op0Register||add.Op1Kind!=OpKind.Memory||!DecodedCode.Memory(add,Register.RCX,4)||!Store(ins[at+3],Mnemonic.Mov,Register.RCX,DecodedCode.Disp(add),award.Op0Register)||DecodedCode.Disp(add)<0x100)continue;
                    try{code.DeadAfter(at,Register.RAX);Site("XpHook",code,at,at+4,at+4);candidates.Add(Tuple.Create(code,at));}catch(Exception ex){Failures["Xp"]=ex.Message;}
                }
            }
            if(candidates.Count!=1){Failures["Xp"]="Expected one XP award entry; found "+candidates.Count+". "+Failure("Xp");return;}
            var match=candidates[0];Functions["Xp"]=match.Item1;Sites["XpHook"]=Site("XpHook",match.Item1,match.Item2,match.Item2+4,match.Item2+4);Field("xp",DecodedCode.Disp(match.Item1.Instructions[match.Item2+2]),"Xp");
        }
        void DiscoverCoop()
        {
            DecodedCode run;if(!Functions.TryGetValue("Run",out run))return;
            var targets=run.Instructions.Where(i=>i.IP<(ulong)(run.Function.Start+512)&&i.FlowControl==FlowControl.UnconditionalBranch&&i.Op0Kind==OpKind.NearBranch64&&
                (i.NearBranchTarget<(ulong)run.Function.Start||i.NearBranchTarget>=(ulong)(run.Function.Start+run.Function.Length))).Select(i=>Image.FunctionAt(checked((int)i.NearBranchTarget))).Where(f=>f!=null).ToArray();
            foreach(var f in targets)
            {
                var code=Decode(f);var ins=code.Instructions;
                for(int at=0;at+5<ins.Length&&ins[at].IP<(ulong)(f.Start+256);at++)
                {
                    if(ins[at].Mnemonic!=Mnemonic.Movaps||ins[at].Op1Register!=Register.XMM1||ins[at+1].Mnemonic!=Mnemonic.Mov||ins[at+1].Op1Register!=Register.RCX||ins[at+2].Mnemonic!=Mnemonic.Movss)continue;
                    var state=ins[at+3];if(state.Mnemonic!=Mnemonic.Movzx||state.Op0Register!=Register.EAX||!DecodedCode.Memory(state,Register.RCX,1)||!RR(ins[at+4],Mnemonic.Test,Register.AL,Register.AL))continue;
                    int offset=DecodedCode.Disp(state);Register owner=ins[at+1].Op0Register;
                    bool running=false;for(int p=0;p+1<ins.Length;p++)if(ins[p].Mnemonic==Mnemonic.Movzx&&DecodedCode.Memory(ins[p],owner,1)&&DecodedCode.Disp(ins[p])==offset&&ins[p+1].Mnemonic==Mnemonic.Cmp&&ins[p+1].Op0Register==Register.AL&&DecodedCode.Immediate(ins[p+1],1,1))running=true;
                    if(!running)continue;
                    Functions["Coop"]=code;Field("coopPhase",offset,"Coop");return;
                }
            }
            Failures["Coop"]="Co-op state byte could not be tied to the run tail call.";
        }
        void DiscoverChoose()
        {
            var candidates=new List<Tuple<DecodedCode,int>>();
            foreach(var code in Anchored(new byte[]{0x48,0x3b}))
            {
                var ins=code.Instructions;
                for(int at=0;at+10<ins.Length&&ins[at].IP<(ulong)(code.Function.Start+256);at++)
                {
                    if(ins[at].Mnemonic!=Mnemonic.Cmp||!DecodedCode.Memory(ins[at],Register.RCX,1)||!DecodedCode.Immediate(ins[at],1,2)||ins[at+1].Mnemonic!=Mnemonic.Jne)continue;
                    if(ins[at+2].Mnemonic!=Mnemonic.Cmp||ins[at+2].Op0Register!=Register.RDX||!DecodedCode.Memory(ins[at+2],Register.RCX,8)||ins[at+3].Mnemonic!=Mnemonic.Jae||ins[at+1].NearBranchTarget!=ins[at+3].NearBranchTarget)continue;
                    if(ins[at+4].Mnemonic!=Mnemonic.Movzx||!DecodedCode.Memory(ins[at+4],Register.RCX,1))continue;
                    int compare=at+5;if(ins[compare].Mnemonic==Mnemonic.Mov&&ins[compare].Op0Kind==OpKind.Memory&&ins[compare].MemoryBase==Register.RBP&&ins[compare].Op1Register==Register.AL)compare++;
                    if(ins[compare].Mnemonic!=Mnemonic.Cmp||!DecodedCode.Immediate(ins[compare],1,255)||ins[compare+1].Mnemonic!=Mnemonic.Je)continue;
                    candidates.Add(Tuple.Create(code,at));
                }
            }
            if(candidates.Count!=1){Failures["Choose"]="Offer phase/bounds/type checks selected "+candidates.Count+" functions.";return;}
            var match=candidates[0];var list=match.Item1.Instructions;int first=match.Item2;
            Functions["Choose"]=match.Item1;Field("phase",DecodedCode.Disp(list[first]),"Choose");Field("offerCount",DecodedCode.Disp(list[first+2]),"Choose");Field("offerKind",DecodedCode.Disp(list[first+4]),"Choose");
            for(int p=first+7;p+3<Math.Min(list.Length,first+18);p++)
            {
                var load=list[p];if(load.Mnemonic!=Mnemonic.Mov||load.Op0Kind!=OpKind.Register||load.Op1Kind!=OpKind.Memory||!DecodedCode.Memory(load,Register.RCX,8))continue;
                int address=p+1;if(list[address].Mnemonic==Mnemonic.Mov&&list[address].Op1Register==Register.RCX)address++;
                var lea=list[address];var shift=list[address+1];
                if(lea.Mnemonic!=Mnemonic.Lea||lea.MemoryBase!=Register.RDX||lea.MemoryIndex!=Register.RDX||lea.MemoryIndexScale!=4||shift.Mnemonic!=Mnemonic.Shl||shift.Op0Register!=lea.Op0Register||!DecodedCode.Immediate(shift,1,5))continue;
                Field("offers",DecodedCode.Disp(load),"Choose");OfferStride=5<<5;
            }
            // Rarity is the final four-byte value written by the roll result into
            // the known offer record. It is separately checked before mode 5.
            OfferRarity=0;
            // The verified offer format is a 160-byte record with an eight-byte
            // tail containing the 32-bit rarity. Derive the tail displacement
            // from the selected record address and its decoded native read.
            if(OfferStride==160)
            {
                var info=new InstructionInfoFactory();
                for(int p=first+7;p<Math.Min(list.Length,first+45);p++)
                {
                    var address=list[p];if(address.Mnemonic!=Mnemonic.Lea||address.Op0Kind!=OpKind.Register||address.MemoryBase!=Register.RAX||address.MemoryIndex!=Register.RCX||address.MemoryIndexScale!=1||DecodedCode.Disp(address)!=0)continue;
                    Register record=address.Op0Register;
                    for(int q=p+1;q<list.Length;q++)
                    {
                        var i=list[q];if(i.Op1Kind==OpKind.Memory&&DecodedCode.Memory(i,record,8)&&DecodedCode.Disp(i)==OfferStride-8){OfferRarity=DecodedCode.Disp(i);break;}
                        if(DecodedCode.Info(info,ref i).GetUsedRegisters().Any(r=>DecodedCode.Full(r.Register)==record&&DecodedCode.Write(r.Access)))break;
                    }
                }
            }
        }
        void DiscoverPrices()
        {
            var candidates=new List<Tuple<DecodedCode,int,int>>();
            foreach(var code in Anchored(BitConverter.GetBytes(0x0290d741)))
            {
                var ins=code.Instructions;
                for(int at=1;at+8<ins.Length;at++)
                {
                    if(ins[at].Mnemonic!=Mnemonic.Mov||!DecodedCode.Immediate(ins[at],1,1)||ins[at].Op0Register!=Register.EAX||ins[at-1].Mnemonic!=Mnemonic.Mov||ins[at-1].Op1Kind!=OpKind.Memory||ins[at-1].MemorySize.GetSize()!=4||ins[at-1].Op0Register.GetSize()!=4||ins[at+1].Mnemonic!=Mnemonic.Test||ins[at+1].Op0Register!=ins[at-1].Op0Register||ins[at+1].Op1Register!=ins[at-1].Op0Register||ins[at+2].Mnemonic!=Mnemonic.Je)continue;
                    int end=code.Index(ins[at+2].NearBranchTarget);if(end<at+3||end>at+75)continue;
                    var cost=ins[end];if(cost.Mnemonic!=Mnemonic.Mov||cost.Op0Kind!=OpKind.Register||cost.Op0Register.GetSize()!=4||!DecodedCode.Immediate(cost,1,100)||cost.Length<5||ins[end+1].Mnemonic!=Mnemonic.Mul||ins[end+1].Op0Register!=cost.Op0Register)continue;
                    try
                    {
                        Need(ins.Skip(at).Take(end-at).Any(i=>i.Mnemonic==Mnemonic.Mov&&DecodedCode.Immediate(i,1,0x0290d741)),"Reroll exponentiation constant changed.");
                        int last=Cover(code,at+1,5);Site("branch",code,at+1,last,end);Site("cost",code,end,end+1,end+1);
                        Need(!ins.Skip(at+3).Take(end-at-3).Any(i=>i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.IndirectCall),"Reroll calculation has side effects.");
                        Need(ins[end+2].Mnemonic==Mnemonic.Mov&&ins[end+2].Op0Kind==OpKind.Register&&ins[end+2].Op0Register.GetSize()==4&&ins[end+2].Op1Register==Register.EAX&&ins[end+3].Mnemonic==Mnemonic.Seto,"Reroll result/overflow publication changed.");
                        var factory=new InstructionInfoFactory();var modified=new HashSet<Register>();
                        for(int p=at+3;p<end;p++)
                        {
                            var i=ins[p];var info=DecodedCode.Info(factory,ref i);
                            Need(!info.GetUsedMemory().Any(m=>DecodedCode.Write(m.Access)||m.Access==OpAccess.CondWrite||m.Access==OpAccess.ReadCondWrite),"Reroll calculation writes memory.");
                            foreach(var use in info.GetUsedRegisters())if((DecodedCode.Write(use.Access)||use.Access==OpAccess.CondWrite||use.Access==OpAccess.ReadCondWrite)&&DecodedCode.Full(use.Register)!=Register.RAX)modified.Add(use.Register);
                        }
                        // Only overwritten temporaries need a liveness proof. Some
                        // paths preserve the paid counter for its later increment.
                        code.DeadAfter(end,modified.ToArray());
                        candidates.Add(Tuple.Create(code,at+1,end));
                    }
                    catch(Exception ex){Failures["Prices"]=ex.Message;}
                }
            }
            if(candidates.Count==0){Failures["Prices"]="No verified reroll calculation. "+Failure("Prices");return;}
            foreach(var match in candidates.OrderBy(c=>c.Item1.Function.Start))
            {
                int index=PriceCount++;string name="Price"+index;Functions[name]=match.Item1;Sites["PriceBranch"+index]=Site("branch",match.Item1,match.Item2,Cover(match.Item1,match.Item2,5),match.Item3);Sites["PriceCost"+index]=Site("cost",match.Item1,match.Item3,match.Item3+1,match.Item3+1);
                var ins=match.Item1.Instructions;var paid=ins[match.Item2-2];Register owner=paid.MemoryBase,price=ins[match.Item3+2].Op0Register;
                foreach(var compare in ins.Skip(match.Item3+1).Take(14))
                    if(compare.Mnemonic==Mnemonic.Cmp&&compare.Op0Kind==OpKind.Memory&&DecodedCode.Memory(compare,owner,4)&&compare.Op1Register==price)Field("gold",DecodedCode.Disp(compare),name);
                for(int p=Math.Max(0,match.Item2-12);p+2<match.Item2-2;p++)
                    if(ins[p].Mnemonic==Mnemonic.Cmp&&ins[p].Op0Kind==OpKind.Memory&&DecodedCode.Memory(ins[p],owner,4)&&DecodedCode.Immediate(ins[p],1,0)&&DecodedCode.Disp(ins[p])==DecodedCode.Disp(paid)-4&&
                        ins.Skip(p+1).Take(2).Any(i=>i.Mnemonic==Mnemonic.Jne&&i.NearBranchTarget>ins[match.Item3].IP))Field("rerolls",DecodedCode.Disp(ins[p]),name);
            }
            // The banish counter has its own bounded evidence; losing this
            // function cannot remove run speed, XP, stats or other resources.
            foreach(var entry in Functions.Where(f=>f.Key.StartsWith("Price")))
            {
                var list=entry.Value.Instructions;
                for(int at=0;at+6<list.Length&&list[at].IP<(ulong)(entry.Value.Function.Start+512);at++)
                {
                    var load=list[at];if(load.Mnemonic!=Mnemonic.Mov||load.Op1Kind!=OpKind.Memory||load.MemoryBase!=Register.R14||load.Op0Register!=Register.R13D||load.MemorySize.GetSize()!=4||DecodedCode.Disp(load)<0x100)continue;
                    if(list[at+1].Mnemonic==Mnemonic.Lea&&(list[at+2].Mnemonic==Mnemonic.Lea||list[at+2].Mnemonic==Mnemonic.Mov))Field("banishes",DecodedCode.Disp(load),entry.Key);
                }
            }
        }
        void DiscoverProfile()
        {
            foreach(string name in new[]{"EnsureClass","SaveProfile","GrowMarks"})
            {
                var matches=new List<DecodedCode>();
                foreach(var signature in BuildCatalog.Functions.Where(s=>s.Name==name))
                    foreach(var f in Image.Functions)try{if(signature.Matches(Image,f))matches.Add(Decode(f));}catch(Exception ex){Failures[name]="Strict helper metadata: "+ex.Message;}
                matches=matches.GroupBy(c=>c.Function.Start).Select(g=>g.First()).ToList();
                if(name=="GrowMarks"&&matches.Count>1)
                {
                    Func<DecodedCode,string> targets=c=>String.Join(",",c.Instructions.Where(i=>i.Op0Kind==OpKind.NearBranch64&&(i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.UnconditionalBranch)&&!((ulong)c.Function.Start<=i.NearBranchTarget&&i.NearBranchTarget<(ulong)(c.Function.Start+c.Function.Length))).Select(i=>i.NearBranchTarget.ToString("x")).ToArray());
                    if(matches.Select(targets).Distinct().Count()==1)matches=new List<DecodedCode>{matches.OrderBy(c=>c.Function.Start).First()};
                }
                if(matches.Count==1)Functions[name]=matches[0];else Failures[name]="Strict save-helper verification found "+matches.Count+" matches.";
            }
            if(!Functions.ContainsKey("EnsureClass")||!Functions.ContainsKey("SaveProfile"))return;
            int ensure=Functions["EnsureClass"].Function.Start,save=Functions["SaveProfile"].Function.Start;
            var actions=new List<DecodedCode>();
            foreach(var f in Image.Functions.Where(f=>f.Length>500&&f.Length<16000))
            {
                // Raw bytes only nominate candidates; decoded direct calls prove them.
                byte[] body=Image.Read(f.Start,f.Length);bool found=false;
                for(int at=0;at+5<=body.Length;at++)if(body[at]==0xe8&&f.Start+at+5+BitConverter.ToInt32(body,at+1)==ensure){found=true;break;}
                if(!found)continue;var code=Decode(f);if(code.Calls(ensure)&&code.Calls(save))actions.Add(code);
            }
            if(actions.Count!=1){Failures["ProfileActions"]="Expected one caller of both strict save helpers; found "+actions.Count+".";return;}
            var action=actions[0];Functions["ProfileActions"]=action;
            // Profile pointer is a server member, captured once and passed to
            // both verified helpers. Its displacement is taken from LEA.
            var possible=new List<int>();
            foreach(var i in action.Instructions)
                if(i.Mnemonic==Mnemonic.Lea&&i.Op0Kind==OpKind.Register&&i.MemoryBase==Register.R14&&i.MemoryIndex==Register.None&&DecodedCode.Disp(i)>=0x100)
                {
                    int at=action.Index(i.IP);Register pointer=i.Op0Register;
                    if(action.Instructions.Skip(at+1).Take(50).Any(x=>RR(x,Mnemonic.Mov,Register.RCX,pointer))&&action.Instructions.Skip(at+1).Take(60).Any(x=>x.FlowControl==FlowControl.Call&&x.NearBranchTarget==(ulong)ensure))possible.Add(DecodedCode.Disp(i));
                }
            if(possible.Distinct().Count()==1)
            {
                int profile=possible[0];Field("profile",profile,"ProfileActions");Field("talentData",profile+0x68,"ProfileActions");Field("talentCount",profile+0x70,"ProfileActions");DiscoverMarksRecord(profile);
            }
            var callers=new List<Tuple<DecodedCode,int>>();
            foreach(var f in Image.Functions.Where(f=>f.Length>1000&&f.Length<100000))
            {
                byte[] body=Image.Read(f.Start,f.Length);bool found=false;for(int p=0;p+5<=body.Length;p++)if(body[p]==0xe8&&f.Start+p+5+BitConverter.ToInt32(body,p+1)==action.Function.Start){found=true;break;}
                if(!found)continue;var code=Decode(f);if(!code.Calls(action.Function.Start))continue;var ins=code.Instructions;
                for(int at=0;at+3<ins.Length&&ins[at].IP<(ulong)(f.Start+300);at++)
                    if(ins[at].Mnemonic==Mnemonic.Mov&&ins[at].Op0Kind==OpKind.Memory&&ins[at].MemoryBase==Register.RBP&&DecodedCode.Immediate(ins[at],1,-2)&&ins[at+1].Mnemonic==Mnemonic.Movapd&&ins[at+1].Op1Register==Register.XMM1&&ins[at+2].Mnemonic==Mnemonic.Xorps&&ins[at+3].Mnemonic==Mnemonic.Cmp&&DecodedCode.Memory(ins[at+3],Register.RCX,4)&&DecodedCode.Immediate(ins[at+3],1,1))
                    {try{code.DeadAfter(at,Register.RAX);Site("ProfileHook",code,at,at+2,at+2);callers.Add(Tuple.Create(code,at));}catch(Exception ex){Failures["Profile"]=ex.Message;}}
            }
            if(callers.Count!=1){Failures["Profile"]="Save-helper call graph selected "+callers.Count+" profile hooks. "+Failure("Profile");return;}
            var caller=callers[0];Functions["Profile"]=caller.Item1;Sites["ProfileHook"]=Site("ProfileHook",caller.Item1,caller.Item2,caller.Item2+2,caller.Item2+2);
        }
        void DiscoverMarksRecord(int profile)
        {
            // Inspect only the marks serialization loop reached by SaveProfile.
            // Other serialization code and its entire-function fingerprint are
            // unrelated to the in-memory edit and need not remain unchanged.
            var save=Functions["SaveProfile"];
            var call=save.Instructions.FirstOrDefault(i=>i.FlowControl==FlowControl.Call&&i.Op0Kind==OpKind.NearBranch64);if(call.Length==0)return;
            var function=Image.FunctionAt(checked((int)call.NearBranchTarget));if(function==null)return;var code=Decode(function);var ins=code.Instructions;
            for(int at=1;at+20<ins.Length;at++)
            {
                var data=ins[at];var count=ins[at+1];var end=ins[at+2];var key=ins[at+3];
                if(data.Mnemonic!=Mnemonic.Mov||data.Op1Kind!=OpKind.Memory||data.MemoryIndex!=Register.None||data.MemorySize.GetSize()!=8||data.MemoryBase==Register.RBP||data.MemoryBase==Register.RSP)continue;
                if(count.Mnemonic!=Mnemonic.Mov||count.Op1Kind!=OpKind.Memory||count.MemoryBase!=data.MemoryBase||DecodedCode.Disp(count)!=DecodedCode.Disp(data)+8||end.Mnemonic!=Mnemonic.Lea||end.MemoryBase!=data.Op0Register||end.MemoryIndex!=count.Op0Register||end.MemoryIndexScale!=8)continue;
                if(key.Mnemonic!=Mnemonic.Lea||!key.IsIPRelativeMemoryOperand)continue;
                byte[] label;try{label=Image.Read(checked((int)key.IPRelativeMemoryAddress),7);}catch{continue;}if(!label.SequenceEqual(new byte[]{6,109,97,114,107,115,124}))continue;
                var reload=ins[at-1];if(reload.Mnemonic!=Mnemonic.Mov||reload.Op0Register!=data.MemoryBase||reload.Op1Kind!=OpKind.Memory||reload.MemoryBase!=Register.RBP)continue;
                int capture=-1;
                for(int p=0;p<at-1;p++)if(ins[p].Mnemonic==Mnemonic.Mov&&ins[p].Op0Kind==OpKind.Memory&&ins[p].MemoryBase==Register.RBP&&DecodedCode.Disp(ins[p])==DecodedCode.Disp(reload)&&ins[p].Op1Register==Register.RDX){capture=p;break;}
                if(capture<0)continue;bool classByte=false,marksValue=false,stride=false;
                for(int p=at+4;p<at+20;p++)
                {
                    var i=ins[p];if(i.Mnemonic==Mnemonic.Add&&i.Op0Register==data.Op0Register&&DecodedCode.Immediate(i,1,8))stride=true;
                    if(i.Mnemonic==Mnemonic.Movzx&&DecodedCode.Memory(i,data.Op0Register,1)&&DecodedCode.Disp(i)==0)classByte=true;
                    if(i.Mnemonic==Mnemonic.Mov&&i.Op1Kind==OpKind.Memory&&DecodedCode.Memory(i,data.Op0Register,4)&&DecodedCode.Disp(i)==4)marksValue=true;
                }
                if(!classByte||!marksValue||!stride)continue;int offset=DecodedCode.Disp(data);if(offset<8||offset>0x1000)continue;
                Functions["MarksRecord"]=code;
                Field("marksCapacity",profile+offset-8,"MarksRecord");Field("marksData",profile+offset,"MarksRecord");Field("marksCount",profile+offset+8,"MarksRecord");return;
            }
            Failures["MarksRecord"]="The marks label, vector fields or eight-byte class/value record changed.";
        }
    }
}
