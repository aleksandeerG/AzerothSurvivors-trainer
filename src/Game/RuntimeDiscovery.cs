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
        internal readonly Dictionary<string,string> FieldErrors=new Dictionary<string,string>();
        readonly Dictionary<string,int> phaseValues=new Dictionary<string,int>();
        internal int RunningState=-1,OfferState=-1,CoopRunningState=-1;
        readonly Dictionary<string,HashSet<int>> phaseCandidates=new Dictionary<string,HashSet<int>>();
        internal bool PricesComplete;
        internal int HealthRegister=-1,PriceCount,OfferStride,OfferRarity;
        internal bool ReviewedRelease {get{return Image.Hash=="0bb6c557c5f6cfeea212761a33554f9a5647ce7cb7e03b8a163b95489496e908"||Image.Hash=="6ca5f4ba4eb6b26716e4b7d9e03cbd25ddafe7582dcd8e55333e070a28058afa";}}
        readonly List<Tuple<DecodedCode,HookSite,RarityRegisters>> rollCandidates=new List<Tuple<DecodedCode,HookSite,RarityRegisters>>();
        readonly Dictionary<int,DecodedCode> cache=new Dictionary<int,DecodedCode>();
        Dictionary<string,List<int>> labelReferences;bool textOwnersValidated;
        internal RuntimeDiscovery(GameImage image)
        {
            Image=image;
            Pass("Rarity",DiscoverRarity);Pass("Health",DiscoverHealth);Pass("Native text",DiscoverNativeText);Pass("Run",DiscoverRun);Pass("Coop",DiscoverCoop);
            Pass("Xp",DiscoverXp);Pass("Choose",DiscoverChoose);Pass("Prices",DiscoverPrices);
            foreach(string name in UiLabels){string key=name;Pass("UI "+key,()=>DiscoverUiField(key));}
            Pass("Profile",DiscoverProfile);ValidateTextOwners();ValidateRoles();ResolvePhase();Pass("Stat units",DiscoverStatUnits);cache.Clear();
            foreach(string role in new[]{"Xp","Choose"})
                if(Functions.ContainsKey(role))try{Functions[role].RequireTwoIntegerArguments();Functions[role+"Call"]=Functions[role];}catch(Exception ex){Failures[role+"Call"]="Native two-argument call refused: "+ex.Message;}
        }
        void Pass(string name,Action discover)
        {
            var functions=new Dictionary<string,DecodedCode>(Functions);var sites=new Dictionary<string,HookSite>(Sites);
            var fields=Fields.ToDictionary(f=>f.Key,f=>new Dictionary<string,int>(f.Value));var errors=new Dictionary<string,string>(FieldErrors);var rarity=new Dictionary<string,RarityRegisters>(Rarity);
            var states=new Dictionary<string,int>(phaseValues);var phases=phaseCandidates.ToDictionary(p=>p.Key,p=>new HashSet<int>(p.Value));bool complete=PricesComplete;
            int health=HealthRegister,prices=PriceCount,stride=OfferStride,rank=OfferRarity;
            try{discover();}
            catch(Exception ex)
            {
                Functions.Clear();foreach(var pair in functions)Functions.Add(pair.Key,pair.Value);
                Sites.Clear();foreach(var pair in sites)Sites.Add(pair.Key,pair.Value);
                Fields.Clear();foreach(var pair in fields)Fields.Add(pair.Key,pair.Value);
                FieldErrors.Clear();foreach(var pair in errors)FieldErrors.Add(pair.Key,pair.Value);
                phaseValues.Clear();foreach(var pair in states)phaseValues.Add(pair.Key,pair.Value);phaseCandidates.Clear();foreach(var pair in phases)phaseCandidates.Add(pair.Key,pair.Value);PricesComplete=complete;
                Rarity.Clear();foreach(var pair in rarity)Rarity.Add(pair.Key,pair.Value);
                HealthRegister=health;PriceCount=prices;OfferStride=stride;OfferRarity=rank;Failures[name]="Discovery refused: "+ex.Message;
            }
        }
        DecodedCode Decode(ImageFunction f)
        {DecodedCode code;if(!cache.TryGetValue(f.Start,out code)){code=new DecodedCode(Image,f);if(cache.Count>=64)cache.Clear();cache.Add(f.Start,code);}return code;}
        IEnumerable<DecodedCode> Anchored(byte[] bytes)
        {
            foreach(var f in Image.Find(bytes,true).Select(r=>Image.FunctionAt(r)).Where(f=>f!=null).GroupBy(f=>f.Start).Select(g=>g.First()))
            {DecodedCode code;try{code=Decode(f);}catch{continue;}yield return code;}
        }
        static IEnumerable<string> UiLabels {get{return new[]{"gold","rerolls","banishes","level","xp","shield","hpMax","time"}.Concat(RunStat.All.Select(s=>s.Key).Where(k=>k!=null));}}
        IEnumerable<DecodedCode> Referencing(string value)
        {
            if(labelReferences==null)labelReferences=Image.LabelReferences(UiLabels.Concat(new[]{"rerollCost","WOW_ROGUE_SINK_AT",NativeText.ReferenceKey}));
            var found=new HashSet<int>();
            foreach(int at in labelReferences[value])
                {
                    var f=Image.FunctionAt(at);if(f==null||!found.Add(f.Start))continue;DecodedCode code;try{code=Decode(f);}catch{continue;}int index=code.Index((ulong)at);
                    if(index>=0&&code.Instructions[index].IsIPRelativeMemoryOperand)yield return code;
                }
        }
        void DiscoverNativeText()
        {
            foreach(var code in Referencing(NativeText.ReferenceKey))
            try
            {
                for(int at=0;at<code.Instructions.Length;at++)
                {
                    var call=code.Instructions[at];if(call.FlowControl!=FlowControl.Call||call.Op0Kind!=OpKind.NearBranch64)continue;
                    var template=code.Flow.At(at,Register.RDX);var arguments=code.Flow.At(at,Register.R8);
                    if(template==null||template.Root!=6||arguments==null||arguments.Root!=5)continue;
                    int bytes;string[] keys;try{keys=NativeText.Keys(Image,(int)template.Offset,out bytes);}catch{continue;}
                    bool stats=NativeText.StatLine(keys),hud=NativeText.RunLine(keys);
                    if(!stats&&!hud)continue;
                    Need(NativeText.Consumer(Image,(int)call.NearBranchTarget,code),"The native formatter does not establish sequential 16-byte argument records.");
                    var fields=new Dictionary<string,int>();var owners=new HashSet<int>();
                    for(int n=0;n<keys.Length;n++)
                    {
                        var pointer=code.Flow.FrameAt(at,arguments.Offset+n*16);var formatter=code.Flow.FrameAt(at,arguments.Offset+n*16+8);if(pointer==null)continue;
                        var stat=stats?RunStat.All.FirstOrDefault(row=>row.TextKey==keys[n]):null;
                        string field=stat!=null?(stat.Field==0x5c8?"maxHp":stat.Field==0x628?"lives":"stat."+(stat.Key??stat.TextKey)):hud?(keys[n]=="t"?"clock":keys[n]=="lvl"?"level":keys[n]=="xp"?"xp":keys[n]=="hpm"?"maxHp":keys[n]=="sh"?"shield":keys[n]=="gold"?"gold":keys[n]=="lives"?"lives":keys[n]=="rr"?"rerolls":null):null;
                        if(field==null)continue;bool floating=stat!=null?stat.Kind<4:field=="clock"||field=="maxHp"||field=="shield";
                        if(stat!=null&&stat.Kind==5)floating=false;
                        if(stat!=null&&stat.Kind==5)
                        {if(pointer.Root!=5)continue;pointer=code.Flow.FrameAt(at,pointer.Offset);if(pointer==null||pointer.Root<11||pointer.Root>14||pointer.Transform!=2)continue;pointer=new RegisterFlow.Value(pointer.Root-10,pointer.Offset);}
                        if(pointer.Root<1||pointer.Root>4||pointer.Offset<0x100||pointer.Offset>=0x10000||pointer.Transform!=0||!NativeText.Formatter(Image,formatter,floating,code,stat!=null&&stat.Kind==5?8:4))continue;
                        owners.Add(pointer.Root);fields[field]=(int)pointer.Offset;
                    }
                    Need(owners.Count==1&&fields.Count>=6,"Native text fields do not have one corroborated incoming owner.");
                    string role="Text:"+(stats?"Stats:":"Run:")+code.Function.Start.ToString("x")+":"+call.IP.ToString("x");Functions[role]=code;code.Dependencies[(int)template.Offset]=bytes;
                    foreach(var field in fields)Field(field.Key,field.Value,role);
                }
            }
            catch(Exception ex){Failures["Text candidate 0x"+code.Function.Start.ToString("x")]=ex.Message;}
        }
        void DiscoverStatUnits()
        {
            var scripts=Image.Find(Encoding.ASCII.GetBytes("function AZS_Pct(v)"),false).ToArray();if(scripts.Length!=1){Failures["Stat units"]="The native stat-display Lua is missing or ambiguous.";return;}
            foreach(int address in scripts)
            {
                byte[] bytes;try{bytes=Image.Read(address,24000);}catch{continue;}string lua=Encoding.ASCII.GetString(bytes);
                const string percent="return string.format(\"%+.0f%%\", v * 100);";int pct=lua.IndexOf(percent,StringComparison.Ordinal);if(pct<0)continue;
                foreach(var stat in RunStat.All)
                {
                    int at=lua.IndexOf(stat.UnitExpression,StringComparison.Ordinal);if(at<0)continue;
                    string field=stat.Field==0x5c8?"maxHp":stat.Field==0x628?"lives":"stat."+(stat.Key??stat.TextKey);Dictionary<string,int> evidence;if(!Fields.TryGetValue(field,out evidence))continue;
                    string role=evidence.Keys.FirstOrDefault(Functions.ContainsKey);if(role==null)continue;var code=Functions[role];
                    code.Dependencies[address+at]=stat.UnitExpression.Length;code.Dependencies[address+pct]=percent.Length;Functions["Units:"+stat.TextKey]=code;
                }
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
            int previous;if(evidence.TryGetValue(witness,out previous)&&previous!=offset)
            {FieldErrors[name]="Conflicting "+name+" fields in "+witness+": +0x"+previous.ToString("x")+" and +0x"+offset.ToString("x")+".";return;}
            evidence[witness]=offset;
        }
        HookSite Site(string name,DecodedCode code,int first,int last,int continuation,int guardEnd=0)
        {
            code.CheckSite(first,last,continuation,guardEnd);
            if(name=="RunHook"||name=="XpHook")
            {
                code.FlagsDead(first);var factory=new InstructionInfoFactory();
                for(int p=first;p<last;p++){var i=code.Instructions[p];Need(!DecodedCode.Info(factory,ref i).GetUsedRegisters().Any(r=>DecodedCode.Write(r.Access)&&(DecodedCode.Full(r.Register)==Register.RSP||DecodedCode.Full(r.Register)==Register.RBP)),"The replay window changes the native stack frame.");}
                int prologue=code.Index((ulong)(code.Function.Start+Image.UnwindInfo(code.Function)[1]));var expected=code.Flow.At(prologue,Register.RSP);var actual=code.Flow.At(first,Register.RSP);
                Need(expected!=null&&expected.Same(actual),"The hook no longer has its completed native stack frame.");
            }
            int start=checked((int)code.Instructions[first].IP),end=checked((int)code.Instructions[last-1].NextIP),next=checked((int)code.Instructions[continuation].IP);
            var site=new HookSite(Image,code.Function,start,end-start,next,guardEnd==0?end-start:checked((int)code.Instructions[guardEnd].IP-start));
            if(name!="branch"&&name!="cost")
            {
                Need(end-start>=12,"The decoded patch window is shorter than the 12-byte entry jump.");
                if(name!="roll")for(int p=first;p<last;p++)
                {
                    var instruction=code.Instructions[p];
                    Need(!instruction.IsIPRelativeMemoryOperand&&instruction.FlowControl==FlowControl.Next,"Replayed instructions require relocation or have unsupported control flow.");
                }
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
                        Need(DecodedCode.Zero(ins[at+2])&&ins[at+2].Op0Register.GetSize()==4,"Rarity result register is not initialized to zero.");
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
            rollCandidates.AddRange(results);
        }
        bool SpellIterator(DecodedCode roll)
        {
            var ins=roll.Instructions;var factory=new InstructionInfoFactory();
            for(int at=0;at<ins.Length;at++)
            {
                var load=ins[at];if(load.Mnemonic!=Mnemonic.Mov||load.Op1Kind!=OpKind.Memory||load.MemorySize.GetSize()!=8||(DecodedCode.Disp(load)<0||DecodedCode.Disp(load)>=0x10000)||!roll.Flow.Argument(at,load.MemoryBase,1))continue;
                Register owner=load.Op0Register;
                for(int p=at+1;p<ins.Length;p++)
                {var i=ins[p];if((i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.IndirectCall)&&new[]{Register.RAX,Register.RCX,Register.RDX,Register.R8,Register.R9,Register.R10,Register.R11}.Contains(owner))break;if(i.Op1Kind==OpKind.Memory&&DecodedCode.Memory(i,owner)&&DecodedCode.Disp(i)==Known("maxHp"))return true;if(DecodedCode.Info(factory,ref i).GetUsedRegisters().Any(r=>DecodedCode.Full(r.Register)==owner&&DecodedCode.Write(r.Access)))break;}
            }
            return false;
        }
        void AssignRollRoles()
        {
            var spells=new List<Tuple<DecodedCode,HookSite,RarityRegisters>>();var items=new List<Tuple<DecodedCode,Tuple<DecodedCode,HookSite,RarityRegisters>[]>>();
            foreach(var group in rollCandidates.GroupBy(c=>c.Item1.Function.Start))
            try
            {
                var code=group.First().Item1;if(SpellIterator(code)){spells.AddRange(group);continue;}
                foreach(var builder in Callers(code.Function.Start))
                    if((GameContext(builder,0)||PaymentPassesGame(builder.Function.Start))&&builder.Instructions.Any(i=>i.FlowControl==FlowControl.Call&&i.Op0Kind==OpKind.NearBranch64&&i.NearBranchTarget==(ulong)code.Function.Start&&builder.Flow.Argument(builder.Index(i.IP),Register.RDX,0)))items.Add(Tuple.Create(builder,group.ToArray()));
            }
            catch(Exception ex){Failures["Roll candidate 0x"+group.Key.ToString("x")]=ex.Message;}
            if(spells.Count==1){var match=spells[0];Functions["Spell"]=match.Item1;Sites["Spell"]=match.Item2;Rarity["Spell"]=match.Item3;}else Failures["Spell"]="Iterator ownership identifies "+spells.Count+" spell roll sites.";
            if(items.Count==1&&items[0].Item2.Length==2)
            {
                Functions["ItemBuilder"]=items[0].Item1;Functions["Jewelry"]=items[0].Item2[0].Item1;int index=0;
                foreach(var match in items[0].Item2.OrderBy(c=>c.Item2.Rva)){string name="Jewelry"+(++index);Sites[name]=match.Item2;Rarity[name]=match.Item3;}
            }
            else Failures["Jewelry"]="Game-owned item builders: "+items.Count+"; both native roll paths must be recognized.";
            if(Functions.ContainsKey("Spell")&&Functions.ContainsKey("Jewelry"))Failures.Remove("Rarity");
        }
        string Failure(string name){string reason;return Failures.TryGetValue(name,out reason)?reason:"No matching instruction shape.";}
        int Known(string name){return KnownFrom(name,false);}
        int KnownFrom(string name,bool independent)
        {
            if(FieldErrors.ContainsKey(name))return -1;
            Dictionary<string,int> evidence;if(!Fields.TryGetValue(name,out evidence))return -1;
            var values=evidence.Where(w=>Functions.ContainsKey(w.Key)&&(!independent||!w.Key.StartsWith("Text:")&&!w.Key.StartsWith("UI:")&&!w.Key.StartsWith("Units:"))).Select(w=>w.Value).Distinct().ToArray();
            return values.Length==1?values[0]:-1;
        }
        void ValidateTextOwners()
        {
            foreach(string role in Functions.Keys.Where(k=>k.StartsWith("Text:")).ToArray())
            {
                var agreements=new List<string>();var conflicts=new List<string>();
                foreach(var field in Fields.Where(f=>f.Value.ContainsKey(role)))
                {
                    int offset=field.Value[role];
                    foreach(var witness in field.Value.Where(w=>!w.Key.StartsWith("Text:")&&!w.Key.StartsWith("Units:")&&Functions.ContainsKey(w.Key)))
                        if(witness.Value==offset)agreements.Add(witness.Key);else conflicts.Add(field.Key+": "+role+" +0x"+offset.ToString("x")+" disagrees with "+witness.Key+" +0x"+witness.Value.ToString("x"));
                }
                if(agreements.Count==0||conflicts.Count>0){Refuse(role,"Text owner is not corroborated as Game: "+(conflicts.Count>0?String.Join("; ",conflicts.ToArray()):"no independent field agreement."));continue;}
                var code=Functions[role];string anchorName=agreements.OrderBy(name=>name.StartsWith("UI:")?1:0).FirstOrDefault(name=>Functions[name].Function.Start!=code.Function.Start);
                if(anchorName!=null){var anchor=Functions[anchorName];code.Dependencies[anchor.Function.Start]=anchor.Function.Length;foreach(var dependency in anchor.Dependencies.ToArray())if(dependency.Key!=code.Function.Start)code.Dependencies[dependency.Key]=dependency.Value;}
            }
            textOwnersValidated=true;
        }
        void Phase(int offset,string source,int value)
        {
            if(offset<0x100||offset>=0x10000)return;
            HashSet<int> values;if(!phaseCandidates.TryGetValue(source,out values)){values=new HashSet<int>();phaseCandidates.Add(source,values);}values.Add(offset);string key=source+":"+offset;int old;phaseValues[key]=phaseValues.TryGetValue(key,out old)&&old!=value?-1:value;
        }
        static bool ByteImmediate(Instruction i){return i.OpCount>1&&i.GetOpKind(1)>=OpKind.Immediate8&&i.GetOpKind(1)<=OpKind.Immediate32to64&&i.GetImmediate(1)<=255;}
        void ResolvePhase()
        {
            var sources=phaseCandidates.Where(p=>Functions.ContainsKey(p.Key)).ToArray();
            var matches=sources.SelectMany(p=>p.Value).Distinct().Where(offset=>sources.Where(p=>p.Value.Contains(offset)).Select(p=>Functions[p.Key].Function.Start).Distinct().Count()>=2).ToArray();
            if(matches.Length!=1){Failures["phase"]="Run state needs one agreeing offset from at least two independent native handlers; found "+matches.Length+".";return;}
            foreach(var source in sources.Where(p=>p.Value.Contains(matches[0])))Field("phase",matches[0],source.Key);
            int running;if(phaseValues.TryGetValue("Run:"+matches[0],out running))RunningState=running;
            var offers=new[]{"Choose","PriceAction"}.Where(k=>phaseValues.ContainsKey(k+":"+matches[0])).Select(k=>phaseValues[k+":"+matches[0]]).Distinct().ToArray();
            if(offers.Length==1)OfferState=offers[0];
        }
        bool GameContext(DecodedCode code,int argument,bool independent=false)
        {
            var known=new HashSet<int>(new[]{"phase","hp","maxHp","clock","offers","offerCount","offerKind","reviewedStatsBase","level","lives","gold","rerolls","xp"}.Select(name=>KnownFrom(name,independent||!textOwnersValidated)).Where(offset=>offset>=0));
            var found=new HashSet<int>();
            for(int at=0;at<code.Instructions.Length;at++)
            {
                var i=code.Instructions[at];if(i.MemoryBase==Register.None||i.MemoryIndex!=Register.None||!code.Flow.Argument(at,i.MemoryBase,argument))continue;
                if(known.Contains(DecodedCode.Disp(i))&&found.Add(DecodedCode.Disp(i))&&found.Count>=2)return true;
            }
            return false;
        }
        void DiscoverUiField(string key)
        {
            int matches=0;string reason="";
            var candidates=Referencing(key);
            foreach(var code in candidates)
                try{matches+=InspectUiField(code,key);}catch(Exception ex){reason=" Candidate 0x"+code.Function.Start.ToString("x")+": "+ex.Message;}
            Need(matches>0,"No owner-proven native UI value for '"+key+"'."+reason);
        }
        int InspectUiField(DecodedCode code,string key)
        {
            if(!UiSiblings(code))return 0;int matches=0;var flow=code.Flow;
            bool stat=RunStat.All.Any(s=>s.Key==key),scalar=stat||key=="shield"||key=="hpMax"||key=="time";
            for(int at=0;at<code.Instructions.Length;at++)
            {
                var call=code.Instructions[at];if(call.FlowControl!=FlowControl.Call)continue;
                var label=flow.At(at,Register.R8);var length=flow.At(at,Register.R9);var value=flow.StackAt(at,0x20);
                if(label==null||label.Root!=6||length==null||length.Root!=7||length.Offset!=key.Length||value==null||value.Root<11||value.Root>14||value.Floating!=scalar||value.Offset<0x100||value.Offset>=0x10000)continue;
                string text;try{text=Encoding.UTF8.GetString(Image.Read(checked((int)label.Offset),key.Length));}catch{continue;}
                if(!String.Equals(text,key,StringComparison.OrdinalIgnoreCase)||!GameContext(code,value.Root-11,true))continue;
                code.Dependencies[(int)label.Offset]=key.Length;
                string field=stat?"stat."+key:key=="hpMax"?"maxHp":key=="time"?"clock":key,role="UI:"+field+":"+code.Function.Start.ToString("x");Functions[role]=code;Field(field,(int)value.Offset,role);matches++;
            }
            return matches;
        }
        bool UiSiblings(DecodedCode code)
        {
            var labels=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var i in code.Instructions.Where(i=>i.FlowControl==FlowControl.Call))
            {
                int at=code.Index(i.IP);var label=code.Flow.At(at,Register.R8);var length=code.Flow.At(at,Register.R9);
                if(label==null||label.Root!=6||length==null||length.Root!=7||length.Offset<1||length.Offset>32)continue;
                string text;try{text=Encoding.UTF8.GetString(Image.Read((int)label.Offset,(int)length.Offset));}catch{continue;}
                if(new[]{"hp","hpMax","shield","rerolls","rerollCost","storedLevels","banishes","choices","offer","hudphase","time"}.Contains(text,StringComparer.OrdinalIgnoreCase))labels.Add(text);
            }
            return labels.Count>=2;
        }
        bool ReferencesLiteral(DecodedCode code,string text)
        {
            foreach(var i in code.Instructions.Where(i=>i.Mnemonic==Mnemonic.Lea&&i.IsIPRelativeMemoryOperand))
                try{if(Image.Read(checked((int)i.IPRelativeMemoryAddress),text.Length).SequenceEqual(Encoding.UTF8.GetBytes(text))){code.Dependencies[(int)i.IPRelativeMemoryAddress]=text.Length;return true;}}catch{}
            return false;
        }
        void Refuse(string role,string reason)
        {
            Functions.Remove(role);Failures[role]=reason;
        }
        IEnumerable<DecodedCode> Callers(int target)
        {
            foreach(var function in Image.Functions)
            {
                byte[] body=Image.Read(function.Start,function.Length);bool found=false;
                for(int at=0;at+5<=body.Length;at++)if(body[at]==0xe8&&function.Start+at+5+BitConverter.ToInt32(body,at+1)==target){found=true;break;}
                if(!found)continue;DecodedCode code;try{code=Decode(function);}catch{continue;}if(code.Calls(target))yield return code;
            }
        }
        void ValidateRoles()
        {
            for(int step=0;step<4;step++)
            {
                try{if(step==2)AssignRollRoles();ValidateRole(step);}
                catch(Exception ex)
                {
                    string[] roles=step==0?Functions.Keys.Where(k=>k.StartsWith("Price")).ToArray():step==1?new[]{"Xp"}:step==2?new[]{"Jewelry","ItemBuilder"}:new[]{"Spell"};
                    foreach(string role in roles)Refuse(role,"Role proof refused: "+ex.Message);
                }
            }
            try{CheckPriceDisplays();}catch(Exception ex){PricesComplete=false;Failures["Prices"]="Display price coverage is unproven: "+ex.Message;}
            try{CheckPaidCounters();}catch(Exception ex){PricesComplete=false;Failures["Prices"]="Paid-counter coverage is unproven: "+ex.Message;}
            foreach(string field in new[]{"gold","rerolls","banishes"})
            {
                Dictionary<string,int> witnesses;if(!Fields.TryGetValue(field,out witnesses))continue;
                if(!ReviewedRelease||witnesses.Keys.Any(k=>(k.StartsWith("UI:")||k.StartsWith("Text:"))))foreach(string source in witnesses.Keys.Where(k=>k.StartsWith("Price")).ToArray())witnesses.Remove(source);
            }
            if(ReviewedRelease&&Known("reviewedStatsBase")>=0&&!Fields.ContainsKey("lives"))Field("lives",Known("reviewedStatsBase")+0xc8,"Health");
        }
        void CheckPaidCounters()
        {
            var covered=new HashSet<ulong>();var offsets=new HashSet<int>();
            foreach(var entry in Functions.Where(f=>f.Key.StartsWith("Price")&&f.Key!="PriceAction"))
            {
                int index=Int32.Parse(entry.Key.Substring(5));var paid=PricePaid(entry.Value,entry.Value.Index((ulong)Sites["PriceBranch"+index].Rva));covered.Add(paid.IP);offsets.Add(DecodedCode.Disp(paid));
            }
            Need(offsets.Count==1,"Verified paid-price calculations disagree on the counter field.");int offset=offsets.First(),found=0;
            foreach(var code in Anchored(BitConverter.GetBytes(offset)))
            {
                bool possiblePrice=false;
                try
                {
                bool witness=false;
                for(int at=0;at+2<code.Instructions.Length;at++)
                {
                    var load=code.Instructions[at];
                    if(load.Mnemonic!=Mnemonic.Mov||load.Op0Kind!=OpKind.Register||load.Op0Register.GetSize()!=4||load.Op1Kind!=OpKind.Memory||load.MemoryIndex!=Register.None||load.MemorySize.GetSize()!=4||DecodedCode.Disp(load)!=offset)continue;
                    int test=at+1;
                    if(code.Instructions[test].Mnemonic==Mnemonic.Mov&&code.Instructions[test].Op0Register==Register.EAX&&DecodedCode.Immediate(code.Instructions[test],1,1)&&load.Op0Register!=Register.EAX)test++;
                    if(!RR(code.Instructions[test],Mnemonic.Test,load.Op0Register,load.Op0Register))continue;
                    bool priceShape=PaidPriceShape(code,test);possiblePrice|=priceShape;
                    var owner=code.Flow.At(at,load.MemoryBase);
                    if(owner==null||owner.Root<1||owner.Root>4||owner.Offset!=0||!GameContext(code,owner.Root-1))
                    {if(priceShape){PricesComplete=false;Failures["Prices"]="A price-shaped paid-counter candidate has an unproven Game owner at RVA 0x"+load.IP.ToString("x")+".";}continue;}
                    found++;witness=true;
                    if(!covered.Contains(load.IP)){PricesComplete=false;Failures["Prices"]="Unpatched Game paid-counter read/test at RVA 0x"+load.IP.ToString("x")+".";}
                }
                if(witness)Functions["PaidCounter:"+code.Function.Start.ToString("x")]=code;
                }
                catch(Exception ex){Failures["Paid candidate 0x"+code.Function.Start.ToString("x")]="Owner proof unavailable: "+ex.Message;if(possiblePrice||Functions.Any(f=>f.Key.StartsWith("Price")&&f.Value.Function.Start==code.Function.Start)){PricesComplete=false;Failures["Prices"]="A possible paid-price calculation cannot be verified at RVA 0x"+code.Function.Start.ToString("x")+": "+ex.Message;}}
            }
            Need(found>0&&found==covered.Count,"The paid-counter paths and verified price calculations do not agree.");
        }
        static bool PaidPriceShape(DecodedCode code,int test)
        {
            var ins=code.Instructions;int branch=test+1;
            if(branch<ins.Length&&ins[branch].Mnemonic==Mnemonic.Mov&&ins[branch].Op0Register==Register.EAX&&DecodedCode.Immediate(ins[branch],1,1))branch++;
            if(branch>=ins.Length||ins[branch].Mnemonic!=Mnemonic.Je)return false;int end=code.Index(ins[branch].NearBranchTarget);
            return end>branch&&end+1<ins.Length&&ins[end].Mnemonic==Mnemonic.Mov&&ins[end].Op0Kind==OpKind.Register&&ins[end].Op0Register.GetSize()==4&&ins[end].Op1Kind==OpKind.Immediate32&&ins[end].Immediate32<=1000000&&ins[end+1].Mnemonic==Mnemonic.Mul&&ins[end+1].Op0Register==ins[end].Op0Register;
        }
        void CheckPriceDisplays()
        {
            var covered=new HashSet<int>(Functions.Where(f=>f.Key.StartsWith("Price")&&f.Key!="PriceAction").Select(f=>f.Value.Function.Start));
            foreach(var code in Referencing("rerollCost"))
            {
                if(!UiSiblings(code))continue;
                bool display=false;
                foreach(var i in code.Instructions.Where(i=>i.FlowControl==FlowControl.Call))
                {
                    int at=code.Index(i.IP);var label=code.Flow.At(at,Register.R8);var length=code.Flow.At(at,Register.R9);
                    if(label==null||label.Root!=6||length==null||length.Root!=7||length.Offset!=10)continue;
                    if(String.Equals(Encoding.UTF8.GetString(Image.Read((int)label.Offset,10)),"rerollCost",StringComparison.OrdinalIgnoreCase))display=true;
                }
                if(display&&!covered.Contains(code.Function.Start)){PricesComplete=false;Failures["Prices"]="The native rerollCost display has no verified price patch at RVA 0x"+code.Function.Start.ToString("x")+".";}
            }
        }
        void ValidateRole(int step)
        {
            if(step==0)foreach(var entry in Functions.Where(e=>e.Key.StartsWith("Price")).ToArray())
            {
                var code=entry.Value;int index=Int32.Parse(entry.Key.Substring(5));var site=Sites["PriceBranch"+index];int at=code.Index((ulong)site.Rva)-2;var paid=PricePaid(code,at+2);at=code.Index(paid.IP);
                var owner=code.Flow.At(at,paid.MemoryBase);
                if(owner==null||owner.Root<1||owner.Root>4||owner.Offset!=0||!GameContext(code,owner.Root-1))
                {Refuse(entry.Key,"The price counter is not linked to the native Game reroll fields.");continue;}
                for(int p=0;p+3<code.Instructions.Length;p++)
                {
                    var load=code.Instructions[p];var subtract=code.Instructions[p+1];var branch=code.Instructions[p+2];var store=code.Instructions[p+3];
                    if(load.Mnemonic==Mnemonic.Mov&&load.Op0Kind==OpKind.Register&&load.Op0Register.GetSize()==4&&load.Op1Kind==OpKind.Memory&&load.MemorySize.GetSize()==4&&DecodedCode.Disp(load)==Known("gold")&&code.Flow.Argument(p,load.MemoryBase,owner.Root-1)&&
                        subtract.Mnemonic==Mnemonic.Sub&&subtract.Op0Register==load.Op0Register&&subtract.Op1Register==code.Instructions[code.Index((ulong)Sites["PriceCost"+index].Rva)+2].Op0Register&&branch.Mnemonic==Mnemonic.Jb&&
                        Store(store,Mnemonic.Mov,load.MemoryBase,DecodedCode.Disp(load),load.Op0Register))
                    {Functions["PriceAction"]=code;Functions["Payment"]=code;Field("gold",DecodedCode.Disp(load),"Payment");}
                }
            }
            if(Functions.ContainsKey("Run"))Failures.Remove("Run");
            DecodedCode xp;
            if(step==1&&Functions.TryGetValue("Xp",out xp))
            {
                if(Known("xp")<0||!Fields["xp"].Keys.Any(k=>(k.StartsWith("UI:")||k.StartsWith("Text:")))||!GameContext(xp,0))
                    Refuse("Xp","XP needs matching native UI XP evidence and the same Game argument.");
            }
            if(step==0&&Functions.ContainsKey("PriceAction"))
            {
                var action=Functions["PriceAction"];
                foreach(var i in action.Instructions)if(i.Mnemonic==Mnemonic.Cmp&&i.Op0Kind==OpKind.Memory&&i.MemorySize.GetSize()==1&&i.MemoryIndex==Register.None&&ByteImmediate(i)&&action.Flow.Argument(action.Index(i.IP),i.MemoryBase,0))Phase(DecodedCode.Disp(i),"PriceAction",(int)i.GetImmediate(1));
            }
            // A roll count only nominates a role. Its owner must also match the
            // native spell iterator or the direct Game item-generation context.
            DecodedCode roll;
            if(step==2&&Functions.TryGetValue("Jewelry",out roll))
            {
                bool linked=false;
                foreach(var builder in Callers(roll.Function.Start))
                    if((GameContext(builder,0)||PaymentPassesGame(builder.Function.Start))&&builder.Instructions.Any(i=>i.FlowControl==FlowControl.Call&&i.Op0Kind==OpKind.NearBranch64&&i.NearBranchTarget==(ulong)roll.Function.Start&&builder.Flow.Argument(builder.Index(i.IP),Register.RDX,0)))
                    {Functions["ItemBuilder"]=builder;linked=true;}
                if(!linked&&!ReviewedRelease)Refuse("Jewelry","The item roll is not linked to the native reroll builder's Game argument.");
            }
            if(step==3&&Functions.TryGetValue("Spell",out roll)&&!SpellIterator(roll))Refuse("Spell","The spell iterator has no corroborated Game maximum-health access.");
        }
        bool PaymentPassesGame(int target)
        {
            DecodedCode payment;if(!Functions.TryGetValue("PriceAction",out payment))return false;
            return payment.Instructions.Any(i=>(i.FlowControl==FlowControl.Call||i.FlowControl==FlowControl.UnconditionalBranch)&&i.Op0Kind==OpKind.NearBranch64&&i.NearBranchTarget==(ulong)target&&payment.Flow.Argument(payment.Index(i.IP),Register.RCX,0));
        }
        void DiscoverHealth()
        {
            var candidates=new List<Tuple<DecodedCode,int,int,int,int>>();var factory=new InstructionInfoFactory();
            foreach(var code in Anchored(new byte[]{0x0f,0x5c}))
            {
                var ins=code.Instructions;
                for(int at=1;at<ins.Length;at++)
                {
                    var sub=ins[at];if(sub.Mnemonic!=Mnemonic.Subss||sub.Op1Kind!=OpKind.Memory||sub.MemoryIndex!=Register.None||sub.MemoryBase==Register.RSP||sub.MemoryBase==Register.RBP||sub.MemoryBase==Register.RIP)continue;
                    Register delta=sub.Op0Register,owner=sub.MemoryBase;int field=DecodedCode.Disp(sub),copy=-1;
                    for(int p=at-1;p>=Math.Max(0,at-10);p--)
                    {
                        var i=ins[p];if(i.FlowControl!=FlowControl.Next)break;
                        if(!DecodedCode.Info(factory,ref i).GetUsedRegisters().Any(r=>DecodedCode.Full(r.Register)==DecodedCode.Full(delta)&&DecodedCode.Write(r.Access)))continue;
                        if(i.Mnemonic==Mnemonic.Movaps&&i.Op0Register==delta&&DecodedCode.Reg(i,1))copy=p;break;
                    }
                    if(copy<0)continue;Register maximum=ins[copy].Op1Register;int store=-1;
                    for(int p=at+1;p<Math.Min(ins.Length,at+10);p++)
                    {
                        var i=ins[p];if(Store(i,Mnemonic.Movss,owner,field,maximum)){store=p;break;}
                        if(i.FlowControl!=FlowControl.Next||DecodedCode.Info(factory,ref i).GetUsedRegisters().Any(r=>DecodedCode.Full(r.Register)==DecodedCode.Full(maximum)&&DecodedCode.Write(r.Access)))break;
                    }
                    if(store<0||delta==maximum||DecodedCode.Xmm(delta)<0||DecodedCode.Xmm(maximum)<0)continue;
                    bool changed=false;for(int p=copy+1;p<at;p++){var i=ins[p];if(DecodedCode.Info(factory,ref i).GetUsedRegisters().Any(r=>DecodedCode.Full(r.Register)==DecodedCode.Full(maximum)&&DecodedCode.Write(r.Access)))changed=true;}if(changed)continue;
                    try
                    {
                        if(!code.Flow.Argument(at,owner,0))continue;
                        Need(field>=0x100&&field<0x10000&&(field&3)==0,"Maximum-health field is out of range.");
                        var current=new HashSet<int>();
                        for(int p=store+1;p<ins.Length;p++)if(ins[p].Mnemonic==Mnemonic.Minss&&ins[p].Op1Register==maximum)
                            for(int q=Math.Max(store,p-15);q<p;q++)
                            {
                                var load=ins[q];if(load.Mnemonic!=Mnemonic.Movss||load.Op1Kind!=OpKind.Memory||!DecodedCode.Memory(load,owner,4)||!code.Flow.Argument(q,owner,0))continue;
                                bool update=ins.Skip(q+1).Take(p-q-1).Any(i=>Store(i,Mnemonic.Movss,owner,DecodedCode.Disp(load),load.Op0Register));
                                bool applied=ins.Skip(q+1).Take(p-q-1).Any(i=>RR(i,Mnemonic.Addss,load.Op0Register,delta));
                                bool copied=ins[p].Op0Register==load.Op0Register||ins.Skip(q+1).Take(p-q-1).Any(i=>RR(i,Mnemonic.Movaps,ins[p].Op0Register,load.Op0Register));
                                if(update&&applied&&copied)current.Add(DecodedCode.Disp(load));
                            }
                        Need(current.Count==1,"Maximum-health delta and clamp do not identify one current-HP field.");candidates.Add(Tuple.Create(code,copy,store,DecodedCode.Xmm(maximum),current.First()));
                    }
                    catch(Exception ex){Failures["Health candidate 0x"+sub.IP.ToString("x")]=ex.Message;}
                }
            }
            if(candidates.Count!=1){Failures["Health"]="Expected one semantic stat recalculation; found "+candidates.Count+".";return;}
            var match=candidates[0];Functions["Health"]=match.Item1;HealthRegister=match.Item4;var storeInstruction=match.Item1.Instructions[match.Item3];
            Field("hp",match.Item5,"Health");Field("maxHp",DecodedCode.Disp(storeInstruction),"Health");
            if(ReviewedRelease)Field("reviewedStatsBase",DecodedCode.Disp(storeInstruction)-0x68,"Health");
            try{int last=Cover(match.Item1,match.Item2,12);match.Item1.DeadAfter(match.Item2,Register.RAX);Sites["HealthHook"]=Site("HealthHook",match.Item1,match.Item2,last,last);}
            catch(Exception ex){Failures["HealthHook"]="Health fields recognized; floor patch refused: "+ex.Message;}
        }
        internal int RunOwnerRegister=1,RunTimeRegister=1;
        void DiscoverRun()
        {
            var anchored=Referencing("WOW_ROGUE_SINK_AT").ToArray();var candidates=new List<DecodedCode>();
            foreach(var code in (anchored.Length>0?anchored.AsEnumerable():Anchored(new byte[]{0x80,0xb9})))
            try
            {
                var ins=code.Instructions;
                bool owner=ins.TakeWhile(i=>i.IP<(ulong)(code.Function.Start+512)).Any(i=>i.Mnemonic==Mnemonic.Mov&&i.Op0Kind==OpKind.Register&&i.Op1Register==Register.RCX);
                if(!owner)continue;
                bool coop=Enumerable.Range(0,ins.Length).Any(p=>IsCoopDispatch(code,p));if(!coop)continue;
                bool time=ins.Any(i=>i.Mnemonic==Mnemonic.Addss&&i.Op1Kind==OpKind.Register&&IsFrameTime(code.Flow.At(code.Index(i.IP),i.Op1Register)));
                if(owner&&time&&coop)candidates.Add(code);
            }
            catch(Exception ex){Failures["Run candidate 0x"+code.Function.Start.ToString("x")]=ex.Message;}
            var preferred=candidates.Where(c=>anchored.Any(a=>a.Function.Start==c.Function.Start)).ToArray();if(preferred.Length>0)candidates=preferred.ToList();
            if(candidates.Count!=1){Failures["Run"]="Semantic run candidates: "+candidates.Count+". "+String.Join("; ",Failures.Where(f=>f.Key.StartsWith("Run candidate")).Select(f=>f.Key+": "+f.Value).ToArray());return;}
            var run=candidates[0];var list=run.Instructions;Functions["Run"]=run;var healingTargets=new HashSet<int>();var clockTargets=new List<int>();var stateGuards=new List<int>();
            for(int p=0;p<list.Length;p++)
            {
                var i=list[p];if(!run.Flow.Argument(p,i.MemoryBase,0))continue;
                if(IsCoopDispatch(run,p))Field("coop",DecodedCode.Disp(i),"Run");
                if(p+1<list.Length&&i.Mnemonic==Mnemonic.Movzx&&i.MemorySize.GetSize()==1&&list[p+1].Mnemonic==Mnemonic.Cmp&&DecodedCode.Full(list[p+1].Op0Register)==DecodedCode.Full(i.Op0Register)&&ByteImmediate(list[p+1]))stateGuards.Add(p);
                if(p+2<list.Length&&i.Mnemonic==Mnemonic.Movss&&i.Op1Kind==OpKind.Memory&&list[p+1].Mnemonic==Mnemonic.Addss&&list[p+1].Op0Register==i.Op0Register&&IsFrameTime(run.Flow.At(p+1,list[p+1].Op1Register))&&Store(list[p+2],Mnemonic.Movss,i.MemoryBase,DecodedCode.Disp(i),i.Op0Register)){Field("clock",DecodedCode.Disp(i),"Run");clockTargets.Add(p+2);}
                if(p+1<list.Length&&i.Mnemonic==Mnemonic.Movss&&i.Op1Kind==OpKind.Memory&&DecodedCode.Disp(i)==Known("maxHp")&&Store(list[p+1],Mnemonic.Movss,i.MemoryBase,DecodedCode.Disp(list[p+1]),i.Op0Register)&&DecodedCode.Disp(list[p+1])!=Known("maxHp"))healingTargets.Add(DecodedCode.Disp(list[p+1]));
            }
            if(healingTargets.Count==1)Field("hp",healingTargets.First(),"Run");
            foreach(int guard in stateGuards)
            {
                int branch=guard+2,value=-1;
                if(branch<list.Length&&(list[branch].Mnemonic==Mnemonic.Jne||list[branch].Mnemonic==Mnemonic.Je))
                {
                    int target=run.Index(list[branch].NearBranchTarget),equal=list[branch].Mnemonic==Mnemonic.Je?target:branch+1,unequal=list[branch].Mnemonic==Mnemonic.Jne?target:branch+1;
                    if(run.ReachesBefore(equal,guard,clockTargets)==true&&run.ReachesBefore(unequal,guard,clockTargets)==false)value=(int)list[guard+1].GetImmediate(1);
                }
                Phase(DecodedCode.Disp(list[guard]),"Run",value);
            }
            var dispatches=Enumerable.Range(0,list.Length).Where(p=>IsCoopDispatch(run,p)).ToArray();
            var coverage=dispatches.Concat(clockTargets).Concat(Enumerable.Range(0,list.Length).Where(p=>list[p].IP<(ulong)(run.Function.Start+512)&&list[p].FlowControl==FlowControl.UnconditionalBranch&&list[p].Op0Kind==OpKind.NearBranch64&&(list[p].NearBranchTarget<(ulong)run.Function.Start||list[p].NearBranchTarget>=(ulong)(run.Function.Start+run.Function.Length)))).Distinct().ToArray();
            var errors=new List<string>();
            for(int first=Math.Max(0,run.Index((ulong)(run.Function.Start+Image.UnwindInfo(run.Function)[1])));first<list.Length&&list[first].IP<(ulong)(run.Function.Start+512);first++)
            {
                int game=Enumerable.Range(1,15).FirstOrDefault(r=>run.Flow.Argument(first,(Register)((int)Register.RAX+r),0));
                if(!run.Flow.Argument(first,(Register)((int)Register.RAX+game),0))continue;
                int dt=list.Where(i=>i.Mnemonic==Mnemonic.Addss&&i.Op1Kind==OpKind.Register&&IsFrameTime(run.Flow.At(run.Index(i.IP),i.Op1Register))).Select(i=>DecodedCode.Xmm(i.Op1Register)).Concat(new[]{1}).FirstOrDefault(r=>r>=0&&IsFrameTime(run.Flow.At(first,(Register)((int)Register.XMM0+r))));if(!IsFrameTime(run.Flow.At(first,(Register)((int)Register.XMM0+dt))))continue;
                try{Need(run.Dominates(first,coverage),"Run window does not dominate the dispatch, solo clock and co-op tail; coverage is unproven.");int last=Cover(run,first,12);run.DeadAfter(first,Register.RAX);var site=Site("RunHook",run,first,last,last);Sites["RunHook"]=site;RunOwnerRegister=game;RunTimeRegister=dt;return;}
                catch(Exception ex){errors.Add("0x"+list[first].IP.ToString("x")+": "+ex.Message);}
            }
            Failures["RunHook"]="Run fields recognized, but no safe window with Game/frame-time provenance: "+String.Join("; ",errors.GroupBy(e=>e.Substring(e.IndexOf(": ",StringComparison.Ordinal)+2)).Take(8).Select(g=>g.First()).ToArray());
        }
        static bool IsCoopDispatch(DecodedCode code,int at)
        {
            var ins=code.Instructions;var i=ins[at];
            if(at+1>=ins.Length||i.Mnemonic!=Mnemonic.Cmp||i.Op0Kind!=OpKind.Memory||i.MemorySize.GetSize()!=1||i.MemoryIndex!=Register.None||!DecodedCode.Immediate(i,1,0)||i.IP>=(ulong)(code.Function.Start+512))return false;
            int branch=at+1;while(branch<Math.Min(ins.Length,at+6)&&ins[branch].FlowControl==FlowControl.Next&&ins[branch].RflagsModified==0)branch++;
            if(branch>=ins.Length||ins[branch].FlowControl!=FlowControl.ConditionalBranch)return false;
            ulong target=ins[branch].NearBranchTarget;
            return ins.Skip(branch+1).TakeWhile(x=>x.IP<target).Any(x=>x.FlowControl==FlowControl.UnconditionalBranch&&x.Op0Kind==OpKind.NearBranch64&&(x.NearBranchTarget<(ulong)code.Function.Start||x.NearBranchTarget>=(ulong)(code.Function.Start+code.Function.Length)))&&code.Flow.Argument(at,i.MemoryBase,0);
        }
        static bool IsFrameTime(RegisterFlow.Value value){return value!=null&&value.Root==8&&value.Offset==1;}
        static bool XpAward(RegisterFlow.Value value){return value!=null&&(value.Root==9&&value.Offset==1||value.Root==2&&value.Offset==0);}
        void DiscoverXp()
        {
            var candidates=new List<Tuple<DecodedCode,int,int,int>>();int expected=Known("xp");
            var fieldHosts=expected<0?new HashSet<int>():new HashSet<int>(Image.Find(BitConverter.GetBytes(expected),true).Select(Image.FunctionAt).Where(f=>f!=null).Select(f=>f.Start));
            foreach(var f in Image.Functions.Where(f=>f.Length>100&&f.Length<16000))
            {
                byte[] prefix=Image.Read(f.Start,Math.Min(f.Length,256));bool possible=fieldHosts.Contains(f.Start);
                for(int p=0;!possible&&p+1<prefix.Length;p++)if(prefix[p]==0x89&&(prefix[p+1]&0xf8)==0xd0)possible=true;
                if(!possible)continue;DecodedCode code;try{code=Decode(f);}catch{continue;}var ins=code.Instructions;
                for(int at=0;at+1<ins.Length&&ins[at].IP<(ulong)(f.Start+512);at++)
                {
                    var add=ins[at];if(add.Mnemonic!=Mnemonic.Add||add.Op0Kind!=OpKind.Register||add.Op0Register.GetSize()!=4)continue;
                    if(!ins.Skip(at+1).Take(6).Any(i=>i.Mnemonic==Mnemonic.Mov&&i.Op0Kind==OpKind.Memory&&i.MemoryIndex==Register.None&&i.MemorySize.GetSize()==4&&i.Op1Register==add.Op0Register&&(expected<0||DecodedCode.Disp(i)==expected)))continue;
                    try
                    {
                        int offset;Register award;
                        if(add.Op1Kind==OpKind.Memory&&add.MemorySize.GetSize()==4&&add.MemoryIndex==Register.None&&code.Flow.Argument(at,add.MemoryBase,0)&&XpAward(code.Flow.At(at,add.Op0Register)))
                        {offset=DecodedCode.Disp(add);award=add.Op0Register;}
                        else if(add.Op1Kind==OpKind.Register&&XpAward(code.Flow.At(at,add.Op1Register)))
                        {var value=code.Flow.At(at,add.Op0Register);if(value==null||value.Root!=11||value.Floating||value.Transform!=0)continue;offset=checked((int)value.Offset);award=add.Op1Register;}
                        else continue;
                        if(offset<0x100||offset>=0x10000||expected>=0&&offset!=expected)continue;
                        int stored=-1;
                        for(int p=at+1;p<Math.Min(ins.Length,at+7);p++)
                        {
                            var i=ins[p];if(i.Mnemonic==Mnemonic.Mov&&i.Op0Kind==OpKind.Memory&&i.MemoryIndex==Register.None&&i.MemorySize.GetSize()==4&&i.Op1Register==add.Op0Register&&DecodedCode.Disp(i)==offset&&code.Flow.Argument(p,i.MemoryBase,0)){stored=p;break;}
                            if(i.FlowControl!=FlowControl.Next||i.Mnemonic!=Mnemonic.Mov&&i.Mnemonic!=Mnemonic.Nop||i.Op0Kind==OpKind.Register&&DecodedCode.Full(i.Op0Register)==DecodedCode.Full(add.Op0Register))break;
                        }
                        if(stored<0)continue;
                        int capture=-1;
                        if(award==Register.EDX)capture=at;
                        else for(int p=0;p<at;p++)if(ins[p].Mnemonic==Mnemonic.Mov&&ins[p].Op0Register==award&&ins[p].Op1Register==Register.EDX)capture=p;
                        if(capture>=0)candidates.Add(Tuple.Create(code,capture,at,offset));
                    }
                    catch(Exception ex){Failures["Xp candidate 0x"+f.Start.ToString("x")]=ex.Message;}
                }
            }
            if(candidates.Count!=1){Failures["Xp"]="Expected one semantic XP award entry; found "+candidates.Count+".";return;}
            var match=candidates[0];Functions["Xp"]=match.Item1;Field("xp",match.Item4,"Xp");var errors=new List<string>();
            for(int first=match.Item2;first>=Math.Max(0,match.Item1.Index((ulong)(match.Item1.Function.Start+Image.UnwindInfo(match.Item1.Function)[1])));first--)
            {
                if(!match.Item1.Flow.Argument(first,Register.RCX,0)||!XpAward(match.Item1.Flow.At(first,Register.RDX)))continue;
                try{Need(match.Item1.Dominates(first,match.Item2,match.Item3),"XP window does not dominate the award capture and addition.");int last=Cover(match.Item1,first,12);match.Item1.DeadAfter(first,Register.RAX);Sites["XpHook"]=Site("XpHook",match.Item1,first,last,last);return;}
                catch(Exception ex){errors.Add(ex.Message);}
            }
            Failures["XpHook"]="XP award identified; multiplier patch refused: "+String.Join("; ",errors.Distinct().Take(8).ToArray());
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
                    bool running=false;for(int p=0;p+1<ins.Length;p++)if(ins[p].Mnemonic==Mnemonic.Movzx&&DecodedCode.Memory(ins[p],owner,1)&&DecodedCode.Disp(ins[p])==offset&&ins[p+1].Mnemonic==Mnemonic.Cmp&&ins[p+1].Op0Register==Register.AL&&ByteImmediate(ins[p+1])){running=true;CoopRunningState=(int)ins[p+1].GetImmediate(1);}
                    if(!running)continue;
                    Functions["Coop"]=code;Field("coopPhase",offset,"Coop");
                    Register deltaTime=ins[at].Op0Register;
                    for(int p=at+5;p+2<ins.Length;p++)
                    {
                        var load=ins[p];if(load.Mnemonic==Mnemonic.Movss&&load.Op1Kind==OpKind.Memory&&DecodedCode.Memory(load,owner,4)&&code.Flow.Argument(p,owner,0)&&RR(ins[p+1],Mnemonic.Addss,load.Op0Register,deltaTime)&&Store(ins[p+2],Mnemonic.Movss,owner,DecodedCode.Disp(load),load.Op0Register))Field("clock",DecodedCode.Disp(load),"Coop");
                    }
                    return;
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
                    if(ins[at].Mnemonic!=Mnemonic.Cmp||!DecodedCode.Memory(ins[at],Register.RCX,1)||!ByteImmediate(ins[at])||ins[at+1].Mnemonic!=Mnemonic.Jne)continue;
                    bool bounds=ins[at+2].Mnemonic==Mnemonic.Cmp&&DecodedCode.Memory(ins[at+2],Register.RCX,8)&&((ins[at+2].Op0Register==Register.RDX&&ins[at+2].Op1Kind==OpKind.Memory&&ins[at+3].Mnemonic==Mnemonic.Jae)||(ins[at+2].Op0Kind==OpKind.Memory&&ins[at+2].Op1Register==Register.RDX&&ins[at+3].Mnemonic==Mnemonic.Jbe));
                    if(!bounds||ins[at+1].NearBranchTarget!=ins[at+3].NearBranchTarget)continue;
                    if(ins[at+4].Mnemonic!=Mnemonic.Movzx||!DecodedCode.Memory(ins[at+4],Register.RCX,1))continue;
                    int compare=at+5;if(ins[compare].Mnemonic==Mnemonic.Mov&&ins[compare].Op0Kind==OpKind.Memory&&ins[compare].MemoryBase==Register.RBP&&ins[compare].Op1Register==Register.AL)compare++;
                    if(ins[compare].Mnemonic!=Mnemonic.Cmp||DecodedCode.Full(ins[compare].Op0Register)!=DecodedCode.Full(ins[at+4].Op0Register)||!DecodedCode.Immediate(ins[compare],1,255)||ins[compare+1].Mnemonic!=Mnemonic.Je)continue;
                    candidates.Add(Tuple.Create(code,at));
                }
            }
            if(candidates.Count!=1){Failures["Choose"]="Offer phase/bounds/type checks selected "+candidates.Count+" functions.";return;}
            var match=candidates[0];var list=match.Item1.Instructions;int first=match.Item2;
            Functions["Choose"]=match.Item1;Phase(DecodedCode.Disp(list[first]),"Choose",(int)list[first].GetImmediate(1));Field("offerCount",DecodedCode.Disp(list[first+2]),"Choose");Field("offerKind",DecodedCode.Disp(list[first+4]),"Choose");
            for(int p=first+7;p+3<Math.Min(list.Length,first+18);p++)
            {
                var load=list[p];if(load.Mnemonic!=Mnemonic.Mov||load.Op0Kind!=OpKind.Register||load.Op1Kind!=OpKind.Memory||!DecodedCode.Memory(load,Register.RCX,8))continue;
                int address=p+1;if(list[address].Mnemonic==Mnemonic.Mov&&list[address].Op1Register==Register.RCX)address++;
                var lea=list[address];var shift=list[address+1];
                int stride;
                if(lea.Mnemonic==Mnemonic.Imul&&lea.Op0Kind==OpKind.Register&&((lea.OpCount==3&&lea.Op1Register==Register.RDX)||(lea.OpCount==2&&lea.Op0Register==Register.RDX)))stride=checked((int)lea.GetImmediate(lea.OpCount-1));
                else if(lea.Mnemonic==Mnemonic.Lea&&lea.MemoryBase==Register.RDX&&lea.MemoryIndex==Register.RDX&&shift.Mnemonic==Mnemonic.Shl&&shift.Op0Register==lea.Op0Register&&shift.Op1Kind==OpKind.Immediate8&&shift.Immediate8<=10)stride=checked((1+lea.MemoryIndexScale)<<shift.Immediate8);
                else{Field("offers",DecodedCode.Disp(load),"Choose");continue;}
                Need(stride>=8&&stride<=4096&&(stride&7)==0,"Offer stride is not a bounded, aligned record.");
                Field("offers",DecodedCode.Disp(load),"Choose");Need(OfferStride==0||OfferStride==stride,"Conflicting offer strides.");OfferStride=stride;
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
        static Instruction PricePaid(DecodedCode code,int test)
        {
            var ins=code.Instructions;
            for(int p=Math.Max(0,test-3);p<test;p++)
                if(ins[p].Mnemonic==Mnemonic.Mov&&ins[p].Op1Kind==OpKind.Memory&&ins[p].MemorySize.GetSize()==4&&Enumerable.Range(p+1,test-p).Any(q=>RR(ins[q],Mnemonic.Test,ins[p].Op0Register,ins[p].Op0Register)))return ins[p];
            return default(Instruction);
        }
        void DiscoverPrices()
        {
            var candidates=new List<Tuple<DecodedCode,int,int>>();
            var codes=Referencing("rerollCost").Concat(Anchored(BitConverter.GetBytes(0x0290d741))).GroupBy(c=>c.Function.Start).Select(g=>g.First()).ToList();var seen=new HashSet<int>(codes.Select(c=>c.Function.Start));var counters=new HashSet<int>();
            for(int cursor=0;cursor<codes.Count;cursor++)
            {
                var code=codes[cursor];
                var ins=code.Instructions;
                for(int at=1;at+8<ins.Length;at++)
                {
                    if(ins[at+2].Mnemonic!=Mnemonic.Je)continue;
                    var prefix=ins.Skip(at-1).Take(3).ToArray();
                    var loads=prefix.Where(i=>i.Mnemonic==Mnemonic.Mov&&i.Op1Kind==OpKind.Memory&&i.MemorySize.GetSize()==4&&i.Op0Kind==OpKind.Register&&i.Op0Register.GetSize()==4).ToArray();
                    var initials=prefix.Where(i=>i.Mnemonic==Mnemonic.Mov&&i.Op0Register==Register.EAX&&DecodedCode.Immediate(i,1,1)).ToArray();
                    if(loads.Length!=1||initials.Length!=1||loads[0].Op0Register==Register.EAX||!prefix.Any(i=>RR(i,Mnemonic.Test,loads[0].Op0Register,loads[0].Op0Register)&&i.IP>loads[0].IP)||ins[at+2].Mnemonic!=Mnemonic.Je)continue;
                    int end=code.Index(ins[at+2].NearBranchTarget);if(end<at+3||end>at+75)continue;
                    var cost=ins[end];if(cost.Mnemonic!=Mnemonic.Mov||cost.Op0Kind!=OpKind.Register||cost.Op0Register.GetSize()!=4||cost.Op1Kind!=OpKind.Immediate32||cost.Immediate32>1000000||cost.Length<5||ins[end+1].Mnemonic!=Mnemonic.Mul||ins[end+1].Op0Register!=cost.Op0Register)continue;
                    try
                    {
                        int last=Cover(code,at+2,5);Site("branch",code,at+2,last,end);Site("cost",code,end,end+1,end+1);
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
                        candidates.Add(Tuple.Create(code,at+2,end));
                        int offset=DecodedCode.Disp(loads[0]);var owner=code.Flow.At(code.Index(loads[0].IP),loads[0].MemoryBase);
                        if(owner!=null&&owner.Root>=1&&owner.Root<=4&&owner.Offset==0&&GameContext(code,owner.Root-1)&&counters.Add(offset))foreach(var other in Anchored(BitConverter.GetBytes(offset)))if(seen.Add(other.Function.Start))codes.Add(other);
                    }
                    catch(Exception ex){Failures["Prices"]=ex.Message;}
                }
            }
            if(candidates.Count==0){Failures["Prices"]="No verified reroll calculation. "+Failure("Prices");return;}
            PricesComplete=true;
            foreach(var match in candidates.OrderBy(c=>c.Item1.Function.Start))
            {
                int index=PriceCount++;string name="Price"+index;Functions[name]=match.Item1;Sites["PriceBranch"+index]=Site("branch",match.Item1,match.Item2,Cover(match.Item1,match.Item2,5),match.Item3);Sites["PriceCost"+index]=Site("cost",match.Item1,match.Item3,match.Item3+1,match.Item3+1);
                var ins=match.Item1.Instructions;var paid=PricePaid(match.Item1,match.Item2);Register owner=paid.MemoryBase,price=ins[match.Item3+2].Op0Register;
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
            foreach(string name in new[]{"EnsureClass","GrowMarks","WriteCharacters","CharacterPath","SaveRoot"})
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
            DiscoverSaveProfile();
            if(!Functions.ContainsKey("SaveProfile"))return;
            Need(Functions.ContainsKey("WriteCharacters")&&Functions.ContainsKey("CharacterPath")&&Functions.ContainsKey("SaveRoot"),"The native save destination policy is unverified.");
            Need(Functions["SaveProfile"].Calls(Functions["WriteCharacters"].Function.Start)&&Functions["WriteCharacters"].Calls(Functions["CharacterPath"].Function.Start)&&Functions["CharacterPath"].Calls(Functions["SaveRoot"].Function.Start),"The native save destination call chain changed.");
            foreach(string text in new[]{"WOW_ROGUE_SAVE","characters.txt"})Literal(Functions["CharacterPath"],text);
            foreach(string text in new[]{"WOW_ROGUE_SAVE","APPDATA","XDG_DATA_HOME","HOME","wenilla-rogue",".local","share"})Literal(Functions["SaveRoot"],text);
            int ensure=Functions.ContainsKey("EnsureClass")?Functions["EnsureClass"].Function.Start:0,save=Functions["SaveProfile"].Function.Start;
            var actions=new List<DecodedCode>();
            foreach(var f in Image.Functions.Where(f=>f.Length>500&&f.Length<16000))
            {
                // Raw bytes only nominate candidates; decoded direct calls prove them.
                byte[] body=Image.Read(f.Start,f.Length);bool found=false;
                for(int at=0;at+5<=body.Length;at++)if(body[at]==0xe8&&f.Start+at+5+BitConverter.ToInt32(body,at+1)==save){found=true;break;}
                if(!found)continue;var code=Decode(f);if(code.Calls(save)&&(ensure!=0?code.Calls(ensure):ReferencesLiteral(code,"talentxp")))actions.Add(code);
            }
            if(actions.Count!=1){Failures["ProfileActions"]="Expected one native profile action handler; found "+actions.Count+".";return;}
            var action=actions[0];Functions["ProfileActions"]=action;
            // Profile pointer is a server member, captured once and passed to
            // both verified helpers. Its displacement is taken from LEA.
            var possible=new List<int>();
            foreach(var i in action.Instructions)
                if(i.Mnemonic==Mnemonic.Lea&&i.Op0Kind==OpKind.Register&&i.MemoryIndex==Register.None&&DecodedCode.Disp(i)>=0x100&&action.Flow.Argument(action.Index(i.IP),i.MemoryBase,0))
                {
                    int at=action.Index(i.IP);Register pointer=i.Op0Register;
                    int offset=DecodedCode.Disp(i);
                    if((ensure==0||action.Instructions.Any(x=>x.FlowControl==FlowControl.Call&&x.NearBranchTarget==(ulong)ensure&&action.Flow.Argument(action.Index(x.IP),Register.RCX,0,offset)))&&
                        action.Instructions.Any(x=>x.FlowControl==FlowControl.Call&&x.NearBranchTarget==(ulong)save&&action.Flow.Argument(action.Index(x.IP),Register.RCX,0,offset)))possible.Add(offset);
                }
            if(possible.Distinct().Count()==1)
            {
                int profile=possible[0];Field("profile",profile,"ProfileActions");if(ensure!=0){Field("talentData",profile+0x68,"ProfileActions");Field("talentCount",profile+0x70,"ProfileActions");}DiscoverMarksRecord(profile);
            }
            var callers=new List<Tuple<DecodedCode,int>>();
            foreach(var f in Image.Functions.Where(f=>f.Length>1000&&f.Length<100000))
            {
                byte[] body=Image.Read(f.Start,f.Length);bool found=false;for(int p=0;p+5<=body.Length;p++)if(body[p]==0xe8&&f.Start+p+5+BitConverter.ToInt32(body,p+1)==action.Function.Start){found=true;break;}
                if(!found)continue;var code=Decode(f);if(!code.Instructions.Any(i=>i.FlowControl==FlowControl.Call&&i.NearBranchTarget==(ulong)action.Function.Start&&code.Flow.Argument(code.Index(i.IP),Register.RCX,0)))continue;var ins=code.Instructions;
                for(int at=0;at+3<ins.Length&&ins[at].IP<(ulong)(f.Start+300);at++)
                    if(ProfileCapturePair(ins[at],ins[at+1])&&ins[at+2].Mnemonic==Mnemonic.Xorps&&ins[at+3].Mnemonic==Mnemonic.Cmp&&DecodedCode.Memory(ins[at+3],Register.RCX,4)&&DecodedCode.Immediate(ins[at+3],1,1))
                    {try{code.DeadAfter(at,Register.RAX);Site("ProfileHook",code,at,at+2,at+2);callers.Add(Tuple.Create(code,at));}catch(Exception ex){Failures["Profile"]=ex.Message;}}
            }
            if(callers.Count!=1){Failures["Profile"]="Save-helper call graph selected "+callers.Count+" profile hooks. "+Failure("Profile");return;}
            var caller=callers[0];Functions["Profile"]=caller.Item1;Sites["ProfileHook"]=Site("ProfileHook",caller.Item1,caller.Item2,caller.Item2+2,caller.Item2+2);
        }
        static bool ProfileCapturePair(Instruction first,Instruction second)
        {
            var state=first.Mnemonic==Mnemonic.Mov?first:second;var time=first.Mnemonic==Mnemonic.Movapd?first:second;
            return state.IP!=time.IP&&state.Mnemonic==Mnemonic.Mov&&state.Op0Kind==OpKind.Memory&&state.MemoryBase==Register.RBP&&DecodedCode.Immediate(state,1,-2)&&time.Mnemonic==Mnemonic.Movapd&&time.Op0Kind==OpKind.Register&&time.Op1Register==Register.XMM1;
        }
        DecodedCode MarksSerializer(DecodedCode save)
        {
            var matches=new List<DecodedCode>();
            foreach(var call in save.Instructions.Where(i=>i.FlowControl==FlowControl.Call&&i.Op0Kind==OpKind.NearBranch64))
            {
                int at=save.Index(call.IP);var output=save.Flow.At(at,Register.RCX);
                if(output==null||output.Root!=5||!save.Flow.Argument(at,Register.RDX,0))continue;
                var function=Image.FunctionAt(checked((int)call.NearBranchTarget));if(function==null||function.Start!=(int)call.NearBranchTarget)continue;
                var code=Decode(function);if(ReferencesLiteral(code,"\x06marks|"))matches.Add(code);
            }
            return matches.GroupBy(c=>c.Function.Start).Count()==1?matches[0]:null;
        }
        void DiscoverSaveProfile()
        {
            DecodedCode writer;if(!Functions.TryGetValue("WriteCharacters",out writer))return;
            var matches=new List<DecodedCode>();string failure="No unique serializer/file-writer call chain.";
            foreach(var code in Callers(writer.Function.Start))
            {
                try
                {
                    if(MarksSerializer(code)==null)continue;
                    code.RequireSingleIntegerArgument();
                    var factory=new InstructionInfoFactory();
                    for(int at=0;at<code.Instructions.Length;at++)
                    {
                        var i=code.Instructions[at];if(i.MemoryIndex!=Register.None||i.MemoryBase==Register.None||i.Op0Kind!=OpKind.Memory&&i.Op1Kind!=OpKind.Memory)continue;
                        var address=code.Flow.At(at,i.MemoryBase);
                        if(address!=null&&address.Root==5&&address.Offset+unchecked((long)i.MemoryDisplacement64)>=40)
                            Need(!DecodedCode.Info(factory,ref i).GetUsedMemory().Any(m=>DecodedCode.Read(m.Access)),"Save routine reads an incoming stack argument.");
                    }
                    matches.Add(code);
                }
                catch(Exception ex){failure=ex.Message;}
            }
            if(matches.Count==1){Functions["SaveProfile"]=matches[0];Failures.Remove("SaveProfile");}
            else Refuse("SaveProfile","Native save recognition refused ("+matches.Count+" candidates): "+failure);
        }
        void Literal(DecodedCode code,string text)
        {
            byte[] expected=Encoding.UTF8.GetBytes(text);var matches=new List<int>();
            foreach(var i in code.Instructions.Where(i=>i.Mnemonic==Mnemonic.Lea&&i.IsIPRelativeMemoryOperand))
                try{int at=checked((int)i.IPRelativeMemoryAddress);if(Image.Read(at,expected.Length).SequenceEqual(expected))matches.Add(at);}catch{}
            Need(matches.Count>0,"Native save policy no longer references '"+text+"'.");foreach(int at in matches)code.Dependencies[at]=expected.Length;
        }
        void DiscoverMarksRecord(int profile)
        {
            // Inspect only the marks serialization loop reached by SaveProfile.
            // Other serialization code and its entire-function fingerprint are
            // unrelated to the in-memory edit and need not remain unchanged.
            var save=Functions["SaveProfile"];var code=MarksSerializer(save);if(code==null)return;var ins=code.Instructions;
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
                if(!code.Flow.Argument(at,data.MemoryBase,1))continue;
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
