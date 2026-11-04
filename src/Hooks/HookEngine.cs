using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AzerothTrainer
{
    internal static class RarityCode
    {
        internal const int DataOffset=4096;
        internal static byte[] Hex(string s)
        {
            string[] parts=s.Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries);
            byte[] b=new byte[parts.Length];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(parts[i],16);return b;
        }
        internal static byte[] EntryJump(IntPtr target,int size)
        {
            // RAX is dead at each verified entry site. A register-indirect jump
            // is not a Windows x64 epilogue and therefore preserves stack walking.
            var b=new List<byte>(Hex("48 B8"));b.AddRange(BitConverter.GetBytes(target.ToInt64()));b.AddRange(Hex("FF E0"));
            if(size<12)throw new ArgumentOutOfRangeException("size");while(b.Count<size)b.Add(0x90);return b.ToArray();
        }
        internal static byte[] TailJump(IntPtr target)
        {
            // Use the caller-owned outgoing shadow area without changing RSP or
            // registers. ModRM mode 01 deliberately excludes epilogue decoding.
            var b=new List<byte>(Hex("C7 44 24 08"));byte[] pointer=BitConverter.GetBytes(target.ToInt64());
            for(int i=0;i<4;i++)b.Add(pointer[i]);b.AddRange(Hex("C7 44 24 0C"));for(int i=4;i<8;i++)b.Add(pointer[i]);b.AddRange(Hex("FF 64 24 08"));return b.ToArray();
        }
        internal static byte[] Build(IntPtr continuation,int variant)
        {
            // 0: spells -> EAX; 1: jewelry -> ESI; 2: jewelry -> R9D.
            int[] w=variant==0?new int[]{12,6,13,14}:new int[]{14,9,8,15};
            var a=new Assembler();
            a.Sse(0x5e,0,2,true); // Normalize the game's own RNG result.
            a.Rip("F3 0F 10 1D",DataOffset);a.Sse(0x2e,0,3,false);a.Branch("0F 82","legendary");
            a.Sse(0x5c,0,3,true);a.Rip("F3 0F 10 25",DataOffset+4);
            a.Sse(0x5c,4,3,true);a.Sse(0x5e,0,4,true);
            a.Sse(0x28,5,w[0],false);for(int i=1;i<4;i++)a.Sse(0x58,5,w[i],true);
            a.Sse(0x59,0,5,true);a.Emit("31 C0");
            for(int i=0;i<3;i++)
            {
                a.Sse(0x2e,0,w[i],false);a.Branch("0F 82","selected");
                a.Sse(0x5c,0,w[i],true);a.Emit("FF C0");
            }
            a.Branch("E9","selected");a.Label("legendary");a.Emit("B8 04 00 00 00");
            a.Label("selected");
            if(variant==1)a.Emit("89 C6");else if(variant==2)a.Emit("41 89 C1");
            a.Bytes.AddRange(TailJump(continuation));return a.Finish();
        }
    }
    internal sealed class Assembler
    {
        internal readonly List<byte> Bytes=new List<byte>();
        readonly Dictionary<string,int> labels=new Dictionary<string,int>();
        readonly Dictionary<int,string> branches=new Dictionary<int,string>();
        readonly GameLayout layout;
        internal Assembler(GameLayout build=null){layout=build;}
        internal void Emit(string s)
        {
            foreach(string token in s.Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries))
                if(token[0]=='$')
                {
                    if(layout==null)throw new Exception("Game operands require a validated layout.");
                    Bytes.AddRange(BitConverter.GetBytes(layout.Field(token.Substring(1))));
                }
                else Bytes.Add(Convert.ToByte(token,16));
        }
        internal void Label(string s){labels.Add(s,Bytes.Count);}
        internal void Branch(string op,string target){Emit(op);branches.Add(Bytes.Count,target);Emit("00 00 00 00");}
        internal void Rip(string op,int target){Emit(op);Bytes.AddRange(BitConverter.GetBytes(target-(Bytes.Count+4)));}
        internal void Sse(byte op,int dst,int src,bool scalar)
        {
            if(scalar)Bytes.Add(0xf3);int rex=0x40|(dst>=8?4:0)|(src>=8?1:0);
            if(rex!=0x40)Bytes.Add((byte)rex);Bytes.Add(0x0f);Bytes.Add(op);Bytes.Add((byte)(0xc0+(dst%8)*8+src%8));
        }
        internal byte[] Finish()
        {
            foreach(var p in branches){byte[] d=BitConverter.GetBytes(labels[p.Value]-p.Key-4);for(int i=0;i<4;i++)Bytes[p.Key+i]=d[i];}
            return Bytes.ToArray();
        }
    }
    internal static class RunCode
    {
        internal const int UnlimitedBanish=16,UnlimitedReroll=20,AutoHeal=36;
        // Aligned 64-bit mailboxes: high word = run epoch, low word = value bits.
        internal const int BanishRequest=320,GoldRequest=328,FreeRerollRequest=336,ShieldRequest=344,HealRequest=352,XpRequest=360,AdvanceRequest=368;
        internal static byte[] Build(IntPtr start,IntPtr continuation,IntPtr image,GameLayout layout)
        {
            var a=new Assembler(layout);a.Emit("F3 0F 11 4C 24 78"); // movss [rsp+78],xmm1 (unscaled frame time)
            SpeedCode.Emit(a);FrameCode.Save(a);FrameCode.Pointer(a,Native.Add(start,4096));
            RunCommands.Track(a);
            Request(a,BanishRequest,layout.Field("banishes"),"banish");Request(a,GoldRequest,layout.Field("gold"),"gold");
            Request(a,FreeRerollRequest,layout.Field("rerolls"),"free");Request(a,ShieldRequest,layout.Field("shield"),"shield");
            RunCommands.Take(a,XpRequest,"noXp");
            a.Emit("44 89 D2 48 89 F1"); // mov edx,r10d; mov rcx,rsi (Game, grant)
            FrameCode.Call(a,Native.Add(image,layout.Rva("Xp"))); // game XP award: enabled XP scaling also applies to grants
            a.Label("noXp");FrameCode.Pointer(a,Native.Add(start,4096));
            AutoPickCode.Emit(a,start,image,layout);RunStat.Emit(a,layout);
            a.Emit("83 78 10 00");a.Branch("0F 84","noBan"); // cmp [data+UnlimitedBanish],0
            a.Emit("C7 86 $banishes 0F 27 00 00");a.Label("noBan"); // mov [Game+F30],9999
            a.Emit("83 78 14 00");a.Branch("0F 84","noReroll");
            a.Emit("C7 86 $rerolls 0F 27 00 00");a.Label("noReroll"); // mov [Game+62C],9999
            RunCommands.Take(a,HealRequest,"autoHeal");a.Branch("E9","heal");
            a.Label("autoHeal");a.Emit("83 78 24 00");a.Branch("0F 84","noHeal"); // cmp [data+AutoHeal],0
            a.Emit("83 BE $hp 00");a.Branch("0F 8E","noHeal"); // cmp HP,0: no automatic revival
            a.Label("heal");a.Emit("44 8B 96 $maxHp 44 89 96 $hp");a.Label("noHeal"); // copy max HP bits to current HP
            RunCommands.Take(a,AdvanceRequest,"noAdvance");
            a.Emit("66 41 0F 6E C2 F3 0F 58 86 $clock F3 0F 11 86 $clock F3 0F 11 40 68"); // movd xmm0,r10d; addss xmm0,clock; store clock and LastClock
            a.Label("noAdvance");a.Label("runDone");FrameCode.Restore(a);
            a.Emit("80 B9 $coop 00"); // replay cmp [rcx+F5E],0 for the original solo/co-op branch
            a.Bytes.AddRange(RarityCode.TailJump(continuation));return a.Finish();
        }
        static void Request(Assembler a,int request,int field,string label)
        {
            RunCommands.Take(a,request,label);
            a.Emit("44 89 96");a.Bytes.AddRange(BitConverter.GetBytes(field)); // mov [rsi+field],r10d
            a.Label(label);
        }
    }
    internal sealed class MemoryPatch
    {
        readonly IntPtr handle;
        readonly int pid;
        internal readonly IntPtr Site;
        internal readonly byte[] Original;
        readonly int guardLength;
        readonly Func<IntPtr,byte[]> builder;
        readonly int sourceRva;
        bool retained,registered;
        internal IntPtr Cave {get;private set;}
        byte[] installed;
        internal bool Enabled {get;private set;}
        internal MemoryPatch(IntPtr h,int processId,IntPtr site,byte[] original,int guard,Func<IntPtr,byte[]> code,int unwindRva=-1)
        {handle=h;pid=processId;Site=site;Original=original;guardLength=guard;builder=code;sourceRva=unwindRva;}
        internal void Validate()
        {
            if(!Native.Equal(Native.Read(handle,Site,Original.Length),Original))
                throw new Exception("Game code differs from this build, or another trainer is attached. Close the old trainer normally before enabling this one.");
        }
        internal void Data(int offset,byte[] value){Native.Write(handle,Native.Add(Cave,4096+offset),value);}
        internal void Install(byte[] replacement,Action<MemoryPatch> initialize)
        {
            if(Enabled)return;
            if(builder!=null&&Cave==IntPtr.Zero)
            {
                Cave=Native.VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)8192,0x3000,4);
                if(Cave==IntPtr.Zero)throw Native.Error("Cannot allocate trainer memory");
            }
            try
            {
                if(builder!=null)
                {
                    if(!registered)
                    {
                        if(retained)throw new Exception("An earlier unwind registration failed. Close the game before reconnecting.");
                        byte[] generated=builder(Cave);Native.Write(handle,Cave,generated);
                        if(sourceRva>=0){retained=true;UnwindSupport.Register(handle,pid,Cave,generated.Length,sourceRva);registered=true;}
                    }
                    if(initialize!=null)initialize(this);
                    uint old;if(!Native.VirtualProtectEx(handle,Cave,(UIntPtr)4096,0x20,out old))throw Native.Error("Cannot prepare trainer instructions");
                    if(!Native.FlushInstructionCache(handle,Cave,(UIntPtr)4096))throw Native.Error("Cannot refresh trainer instructions");
                    replacement=RarityCode.EntryJump(Cave,Original.Length);
                }
                using(var frozen=new FrozenThreads(pid))
                {
                    frozen.RequireOutside(Site,guardLength);Validate();
                    try{WriteCode(replacement);installed=replacement;Enabled=true;}
                    catch
                    {
                        try{WriteCode(Original);}
                        catch{Enabled=true;installed=replacement;throw new Exception("Code restoration failed. Keep the trainer open and close the game.");}
                        throw;
                    }
                }
            }
            catch{if(!Enabled)Free();throw;}
        }
        internal void Replace(byte[] replacement)
        {
            if(!Enabled){Install(replacement,null);return;}
            if(Native.Equal(installed,replacement))return;
            using(var frozen=new FrozenThreads(pid))
            {
                frozen.RequireOutside(Site,guardLength);RequireOwned();
                byte[] old=installed;
                try{WriteCode(replacement);installed=replacement;}
                catch(Exception primary)
                {
                    try{WriteCode(old);}
                    catch(Exception rollback){throw new AggregateException("Replacing game code failed, and restoring its previous bytes also failed.",primary,rollback);}
                    throw;
                }
            }
        }
        void RequireOwned()
        {if(!Native.Equal(Native.Read(handle,Site,installed.Length),installed))throw new Exception("Another program changed trainer code. Close the game before detaching.");}
        internal void Remove()
        {
            if(!Enabled)return;
            using(var frozen=new FrozenThreads(pid))
            {
                frozen.RequireOutside(Site,guardLength);if(Cave!=IntPtr.Zero)frozen.RequireOutside(Cave,4096);
                byte[] current=Native.Read(handle,Site,Original.Length);
                if(!Native.Equal(current,Original)){RequireOwned();WriteCode(Original);}
                Enabled=false;Free();
            }
        }
        internal void RequireSafe(FrozenThreads frozen)
        {
            if(!Enabled)return;
            frozen.RequireOutside(Site,guardLength);if(Cave!=IntPtr.Zero)frozen.RequireOutside(Cave,4096);
        }
        internal void Abandon(){Enabled=false;Cave=IntPtr.Zero;}
        void Free(){if(retained)return;if(Cave!=IntPtr.Zero){Native.VirtualFreeEx(handle,Cave,UIntPtr.Zero,0x8000);Cave=IntPtr.Zero;}}
        void WriteCode(byte[] bytes)
        {
            uint old;if(!Native.VirtualProtectEx(handle,Site,(UIntPtr)bytes.Length,0x40,out old))throw Native.Error("Cannot change code protection");
            try
            {
                Native.Write(handle,Site,bytes);
                if(!Native.FlushInstructionCache(handle,Site,(UIntPtr)bytes.Length))throw Native.Error("Cannot refresh game instructions");
            }
            finally{uint ignored;if(!Native.VirtualProtectEx(handle,Site,(UIntPtr)bytes.Length,old,out ignored))throw Native.Error("Cannot restore code protection");}
        }
    }
    internal sealed class TrainerOptions
    {
        internal bool SpellLegendary,ItemLegendary,FixedCost,UnlimitedBanish,UnlimitedReroll,AutoHeal,SpeedEnabled,XpEnabled;
        internal int AutoSpellMode,AutoItemMode;internal double AutoPickDelay=0.5;
        internal readonly bool[] StatEnabled=new bool[RunStat.All.Length];
        internal readonly double[] StatValues=Array.ConvertAll(RunStat.All,delegate(RunStat s){return s.Default;});
        internal double SpellChance=50,ItemChance=50,Speed=1,XpMultiplier=1;
        internal int RerollCost=100;
    }
    internal sealed class PatchSession : IDisposable
    {
        internal const string PrivilegeMessage="Cannot access the game. Run the trainer and game as the same Windows user and with matching administrator privileges. If the game is elevated, run the trainer as administrator too.";
        internal readonly Process Game;
        internal string CompatibilitySummary {get{return layout.Summary;}}
        readonly IntPtr handle;
        readonly List<MemoryPatch> all=new List<MemoryPatch>(),rarity=new List<MemoryPatch>(),prices=new List<MemoryPatch>();
        readonly MemoryPatch run,xp,profile,health;
        readonly GameLayout layout;
        uint talentSequence,marksSequence;
        bool disposed;
        internal bool Enabled {get {foreach(var p in all)if(p.Enabled)return true;return false;}}
        internal bool Exited {get {if(disposed)return true;try{return Game.HasExited;}catch(InvalidOperationException){return true;}}}
        internal PatchSession(Process game,IntPtr imageBase,GameLayout build)
        {
            Game=game;layout=build;handle=Native.OpenProcess(0x43A,false,game.Id);
            if(handle==IntPtr.Zero)throw Native.Error(PrivilegeMessage);
            try
            {
                layout.ValidateLive(handle,imageBase);
                AddRarity(imageBase,layout.Site("Spell"),0);AddRarity(imageBase,layout.Site("Jewelry1"),1);AddRarity(imageBase,layout.Site("Jewelry2"),2);
                HookSite tick=layout.Site("RunHook"),award=layout.Site("XpHook"),server=layout.Site("ProfileHook"),maximum=layout.Site("HealthHook");
                run=AddGenerated(imageBase,tick,delegate(IntPtr c){return RunCode.Build(c,Native.Add(imageBase,tick.Continuation),imageBase,layout);});
                xp=AddGenerated(imageBase,award,delegate(IntPtr c){return XpCode.Build(Native.Add(imageBase,award.Continuation),layout);});
                profile=AddGenerated(imageBase,server,delegate(IntPtr c){return ProfileCode.Build(c,Native.Add(imageBase,server.Continuation),imageBase,layout);});
                health=AddGenerated(imageBase,maximum,delegate(IntPtr c){return HealthCode.Build(Native.Add(imageBase,maximum.Continuation),layout);});
                for(int i=0;i<5;i++)
                {
                    HookSite branchSite=layout.Site("PriceBranch"+i),costSite=layout.Site("PriceCost"+i);
                    var branch=new MemoryPatch(handle,game.Id,Native.Add(imageBase,branchSite.Rva),branchSite.Original,branchSite.Guard,null);
                    var cost=new MemoryPatch(handle,game.Id,Native.Add(imageBase,costSite.Rva),costSite.Original,costSite.Guard,null);
                    prices.Add(branch);prices.Add(cost);all.Add(branch);all.Add(cost);
                }
                foreach(var patch in all)patch.Validate();
                foreach(string name in new string[]{"GuardCoop","GuardHud"})
                {
                    var guard=layout.Site(name);
                    if(!Native.Equal(Native.Read(handle,Native.Add(imageBase,guard.Rva),guard.Original.Length),guard.Original))throw new Exception("Another trainer has modified the game. Disable it before connecting.");
                }
            }
            catch{Native.CloseHandle(handle);throw;}
        }
        void AddRarity(IntPtr image,HookSite site,int variant)
        {
            var patch=new MemoryPatch(handle,Game.Id,Native.Add(image,site.Rva),site.Original,site.Guard,delegate(IntPtr unused){return RarityCode.Build(Native.Add(image,site.Continuation),variant);},site.Rva);
            rarity.Add(patch);all.Add(patch);
        }
        MemoryPatch AddGenerated(IntPtr image,HookSite site,Func<IntPtr,byte[]> code)
        {var patch=new MemoryPatch(handle,Game.Id,Native.Add(image,site.Rva),site.Original,site.Guard,code,site.Rva);all.Add(patch);return patch;}
        internal static PatchSession Attach()
        {
            Process selected=GameLocator.FindRunning();
            if(selected==null)throw new Exception("Start Azeroth Survivors yourself or click Launch game, then enable the trainer.");
            try
            {
                var module=selected.MainModule;string path=module.FileName;
                var layout=GameLayout.Load(path);GameLocator.Remember(path);
                return new PatchSession(selected,module.BaseAddress,layout);
            }
            catch{selected.Dispose();throw;}
        }
        internal void Apply(TrainerOptions options)
        {
            ValidateOptions(options);
            if(Exited)throw new Exception("The game has exited.");
            health.Install(null,delegate(MemoryPatch p){p.Data(0,new byte[8]);});
            health.Data(0,BitConverter.GetBytes((ulong)RunStat.All[0].Bits(options.StatValues[0])|(options.StatEnabled[0]?1UL<<32:0)));
            run.Install(null,delegate(MemoryPatch p)
            {
                p.Data(0,BitConverter.GetBytes(options.SpeedEnabled?(float)options.Speed:1f));
                foreach(int offset in RunCommands.Offsets)p.Data(offset,BitConverter.GetBytes(-1L));
                p.Data(RunCommands.Owner,new byte[24]);
                p.Data(AutoPickCode.SpellMode,new byte[40]);p.Data(AutoPickCode.Random,BitConverter.GetBytes(unchecked((uint)Environment.TickCount)|1));p.Data(AutoPickCode.MaxDelta,BitConverter.GetBytes(0.1f));
                p.Data(RunStat.DataOffset,new byte[RunStat.All.Length*RunStat.Stride]);
            });
            run.Data(0,BitConverter.GetBytes(options.SpeedEnabled?(float)options.Speed:1f));
            if(options.XpEnabled){xp.Install(null,delegate(MemoryPatch p){p.Data(0,BitConverter.GetBytes(XpCode.ScaleUnits(options.XpMultiplier)));p.Data(XpCode.CarryOffset,new byte[XpCode.CarrySlots*XpCode.CarryStride]);});xp.Data(0,BitConverter.GetBytes(XpCode.ScaleUnits(options.XpMultiplier)));}else xp.Remove();
            run.Data(AutoPickCode.Delay,BitConverter.GetBytes((float)options.AutoPickDelay));
            run.Data(AutoPickCode.SpellMode,BitConverter.GetBytes(options.AutoSpellMode));run.Data(AutoPickCode.ItemMode,BitConverter.GetBytes(options.AutoItemMode));
            for(int i=0;i<RunStat.All.Length;i++)run.Data(RunStat.DataOffset+i*RunStat.Stride,BitConverter.GetBytes((ulong)RunStat.All[i].Bits(options.StatValues[i])|(options.StatEnabled[i]?1UL<<32:0)));
            profile.Install(null,delegate(MemoryPatch p){p.Data(0,BitConverter.GetBytes(-1L));p.Data(8,BitConverter.GetBytes(-1L));});
            run.Data(RunCode.AutoHeal,BitConverter.GetBytes(options.AutoHeal?1:0));
            run.Data(RunCode.UnlimitedBanish,BitConverter.GetBytes(options.UnlimitedBanish?1:0));
            run.Data(RunCode.UnlimitedReroll,BitConverter.GetBytes(options.UnlimitedReroll?1:0));
            for(int index=0;index<rarity.Count;index++)
            {
                var p=rarity[index];double chance=index==0?options.SpellChance:options.ItemChance;
                if(index==0?options.SpellLegendary:options.ItemLegendary)
                {
                    p.Install(null,delegate(MemoryPatch item){item.Data(0,BitConverter.GetBytes((float)(chance/100.0)));item.Data(4,BitConverter.GetBytes(1f));});
                    // Aligned float, loaded once by the hook: updating it requires no thread pause.
                    p.Data(0,BitConverter.GetBytes((float)(chance/100.0)));
                }
                else p.Remove();
            }
            bool priceChange=false;
            for(int i=0;i<prices.Count;i+=2)
            {
                if(prices[i].Enabled!=options.FixedCost||prices[i+1].Enabled!=options.FixedCost)priceChange=true;
                if(options.FixedCost&&prices[i+1].Enabled)
                {
                    byte[] bytes=Native.Read(handle,prices[i+1].Site,5);
                    if(BitConverter.ToInt32(bytes,1)!=options.RerollCost)priceChange=true;
                }
            }
            if(priceChange)
            {
                // Update the price and its five calculation paths while the game is paused
                // once as a group. Exclude threads in the old calculation, too.
                using(var frozen=new FrozenThreads(Game.Id))
                {
                    for(int i=0;i<prices.Count;i+=2)
                        frozen.RequireOutside(prices[i].Site,(int)(prices[i+1].Site.ToInt64()-prices[i].Site.ToInt64()+12));
                    for(int i=0;i<prices.Count;i+=2)
                    {
                        if(options.FixedCost)
                        {
                            var branch=prices[i];var cost=prices[i+1];
                            byte[] j=new byte[5];j[0]=0xe9;Array.Copy(BitConverter.GetBytes((int)(cost.Site.ToInt64()-branch.Site.ToInt64()-5)),0,j,1,4);
                            byte[] value=(byte[])cost.Original.Clone();Array.Copy(BitConverter.GetBytes(options.RerollCost),0,value,1,4);
                            cost.Replace(value);branch.Replace(j);
                        }
                        else{prices[i].Remove();prices[i+1].Remove();}
                    }
                }
            }
        }
        internal static void ValidateOptions(TrainerOptions o)
        {
            foreach(double p in new double[]{o.SpellChance,o.ItemChance})if(Double.IsNaN(p)||Double.IsInfinity(p)||p<0||p>100)throw new ArgumentOutOfRangeException("chance");
            if(o.AutoSpellMode<0||o.AutoSpellMode>5||o.AutoItemMode<0||o.AutoItemMode>5||Double.IsNaN(o.AutoPickDelay)||Double.IsInfinity(o.AutoPickDelay)||o.AutoPickDelay<0||o.AutoPickDelay>10)throw new ArgumentOutOfRangeException("auto-pick");
            for(int i=0;i<RunStat.All.Length;i++)RunStat.All[i].Bits(o.StatValues[i]);
            foreach(double v in new double[]{o.Speed,o.XpMultiplier})if(Double.IsNaN(v)||Double.IsInfinity(v)||v<0)throw new ArgumentOutOfRangeException("multiplier");
            if(o.Speed>10||o.XpMultiplier<0.1||o.XpMultiplier>100||o.RerollCost<0||o.RerollCost>100000)throw new ArgumentOutOfRangeException("options");
        }
        internal void SetFreeRerolls(int n){Queue(RunCode.FreeRerollRequest,n,9999);}
        internal void Heal(){Queue(RunCode.HealRequest,1,1);}
        internal void GrantXp(int n){Queue(RunCode.XpRequest,n,1000000);}
        internal void SetShield(float n){if(Single.IsNaN(n)||Single.IsInfinity(n)||n<0||n>1000000)throw new ArgumentOutOfRangeException("shield");QueueBits(RunCode.ShieldRequest,BitConverter.GetBytes(n));}
        internal void Advance(float seconds){if(Single.IsNaN(seconds)||Single.IsInfinity(seconds)||seconds<0||seconds>3600)throw new ArgumentOutOfRangeException("seconds");QueueBits(RunCode.AdvanceRequest,BitConverter.GetBytes(seconds));}
        void QueueBits(int offset,byte[] bytes)
        {
            if(Exited||!run.Enabled)throw new Exception("Enable the trainer first.");
            ulong state=BitConverter.ToUInt64(Native.Read(handle,Native.Add(run.Cave,4096+RunCommands.Status),8),0);
            IntPtr owner=new IntPtr(BitConverter.ToInt64(Native.Read(handle,Native.Add(run.Cave,4096+RunCommands.Owner),8),0));
            if(owner==IntPtr.Zero)throw new Exception("Enter a live run before sending a run action.");
            byte phase=Native.Read(handle,Native.Add(owner,layout.Field("phase")),1)[0];
            float hp=BitConverter.ToSingle(Native.Read(handle,Native.Add(owner,layout.Field("hp")),4),0);
            if((phase!=1&&phase!=2)||!(hp>0))throw new Exception("Enter a live run before sending a run action.");
            run.Data(offset,BitConverter.GetBytes(RunCommands.Pack(state,BitConverter.ToUInt32(bytes,0))));
        }
        internal void CancelRunRequests()
        {
            if(Exited||!run.Enabled)throw new Exception("Enable the trainer first.");
            using(var frozen=new FrozenThreads(Game.Id))
                foreach(int offset in RunCommands.Offsets)run.Data(offset,BitConverter.GetBytes(-1L));
        }
        internal uint SetTalentPoints(int classId,int n){if(n<0||n>51)throw new ArgumentOutOfRangeException("points");return QueueProfile(ProfileCode.TalentRequest,classId,n);}
        internal uint SetMarks(int classId,int rawTenths){if(rawTenths<0||rawTenths>10000000)throw new ArgumentOutOfRangeException("marks");return QueueProfile(ProfileCode.MarksRequest,classId,rawTenths);}
        uint QueueProfile(int offset,int classId,int value)
        {
            if(classId<1||classId>11||classId==6||classId==10)throw new ArgumentOutOfRangeException("class");
            if(Exited||!profile.Enabled)throw new Exception("Enable the trainer first.");BackupProfile();
            uint sequence=offset==ProfileCode.TalentRequest?checked(++talentSequence):checked(++marksSequence);
            profile.Data(offset,BitConverter.GetBytes(ProfileCode.PackRequest(sequence,classId,value)));return sequence;
        }
        internal int TalentResult(uint sequence){return ReadProfileResult(ProfileCode.TalentResult,sequence);}
        internal int MarksResult(uint sequence){return ReadProfileResult(ProfileCode.MarksResult,sequence);}
        int ReadProfileResult(int offset,uint sequence){if(Exited||!profile.Enabled)return 0;return ProfileCode.CompletedStatus(BitConverter.ToUInt64(Native.Read(handle,Native.Add(profile.Cave,4096+offset),8),0),sequence);}
        internal static void BackupProfile()
        {
            string source=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"wenilla-rogue","characters.txt");
            if(!File.Exists(source))return;string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"trainer","backups");Directory.CreateDirectory(folder);
            File.Copy(source,Path.Combine(folder,"characters-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".txt"));
        }
        internal void SetBanishes(int value){Queue(RunCode.BanishRequest,value,9999);}
        internal void SetGold(int value){Queue(RunCode.GoldRequest,value,100000);}
        void Queue(int offset,int value,int maximum)
        {
            if(value<0||value>maximum)throw new ArgumentOutOfRangeException("value");
            if(Exited||!run.Enabled)throw new Exception("Enable the trainer first.");
            QueueBits(offset,BitConverter.GetBytes(value));
        }
        internal void Disable()
        {
            if(disposed)return;
            if(Exited){foreach(var p in all)p.Abandon();return;}
            if(!Enabled)return;
            try
            {
                using(var frozen=new FrozenThreads(Game.Id))
                {
                    foreach(var p in all)p.RequireSafe(frozen);
                    for(int i=0;i<prices.Count;i+=2)
                        if(prices[i].Enabled||prices[i+1].Enabled)
                            frozen.RequireOutside(prices[i].Site,(int)(prices[i+1].Site.ToInt64()-prices[i].Site.ToInt64()+12));
                    for(int i=all.Count-1;i>=0;i--)all[i].Remove();
                }
            }
            catch{if(Exited){foreach(var p in all)p.Abandon();return;}throw;}
        }
        public void Dispose(){if(disposed)return;Disable();Native.CloseHandle(handle);Game.Dispose();disposed=true;}
    }
}
