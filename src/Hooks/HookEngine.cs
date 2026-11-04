using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace AzerothTrainer
{
    internal static class RarityCode
    {
        internal const int DataOffset=4096;
        internal const int HookRva=0x5fe1f52, ContinueRva=0x5fe1fc0;
        internal const int SelectionLength=ContinueRva-HookRva;
        internal const string SupportedHash="0bb6c557c5f6cfeea212761a33554f9a5647ce7cb7e03b8a163b95489496e908";
        internal static readonly byte[] Original=Hex("44 0F 2E E0 76 18 31 C0 48 83 7D 60 00 79 66");
        internal static byte[] Hex(string s)
        {
            string[] parts=s.Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries);
            byte[] b=new byte[parts.Length];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(parts[i],16);return b;
        }
        internal static byte[] Jump(IntPtr target,int size)
        {
            if(size<14)throw new ArgumentOutOfRangeException("size");
            var b=new List<byte>(Hex("FF 25 00 00 00 00"));b.AddRange(BitConverter.GetBytes(target.ToInt64()));
            while(b.Count<size)b.Add(0x90);return b.ToArray();
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
        internal static IntPtr JumpTarget(byte[] code){return new IntPtr(BitConverter.ToInt64(code,code[0]==0x48?2:6));}
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
        internal void Emit(string s){Bytes.AddRange(RarityCode.Hex(s));}
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
        internal const int HookRva=SpeedCode.HookRva,HudRva=0x5ef4ed1,CoopRva=0x5ecf814;
        internal static readonly byte[] CoopOriginal=RarityCode.Hex("0F 28 F1 49 89 CD F3 0F 11 8D 00 17 00 00");
        internal static readonly byte[] Original=SpeedCode.Original;
        internal static readonly byte[] HudOriginal=RarityCode.Hex("48 C7 85 68 02 00 00 FE FF FF FF 48 89 D6");
        internal const int BanishRequest=8,GoldRequest=12,UnlimitedBanish=16,UnlimitedReroll=20;
        internal const int FreeRerollRequest=24,ShieldRequest=28,HealRequest=32,AutoHeal=36,XpRequest=40,DamageMinimum=44,PickupMinimum=48,AdvanceRequest=52;
        internal static byte[] Build(IntPtr start,IntPtr continuation,IntPtr image)
        {
            var a=new Assembler();a.Emit("F3 0F 11 4C 24 78");SpeedCode.Emit(a);FrameCode.Save(a);
            a.Emit("44 0F B6 96 63 0F 00 00 41 FF CA 41 83 FA 01");a.Branch("0F 87","runDone");
            FrameCode.Pointer(a,Native.Add(start,4096));
            Request(a,BanishRequest,0xf30,"banish");Request(a,GoldRequest,0x578,"gold");
            Request(a,FreeRerollRequest,0x62c,"free");Request(a,ShieldRequest,0x56c,"shield");
            a.Emit("41 BA FF FF FF FF 44 87 50 28 41 83 FA FF");a.Branch("0F 84","noXp");
            a.Emit("44 89 D2 48 89 F1");FrameCode.Call(a,Native.Add(image,0x5f05680));
            a.Label("noXp");FrameCode.Pointer(a,Native.Add(start,4096));
            AutoPickCode.Emit(a,start,image);RunStat.Emit(a);
            a.Emit("83 78 10 00");a.Branch("0F 84","noBan");a.Emit("C7 86 30 0F 00 00 0F 27 00 00");a.Label("noBan");
            a.Emit("83 78 14 00");a.Branch("0F 84","noReroll");a.Emit("C7 86 2C 06 00 00 0F 27 00 00");a.Label("noReroll");
            a.Emit("41 BA FF FF FF FF 44 87 50 20 41 83 FA FF");a.Branch("0F 85","heal");
            a.Emit("83 78 24 00");a.Branch("0F 84","noHeal");a.Emit("83 BE 68 05 00 00 00");a.Branch("0F 8E","noHeal");
            a.Label("heal");a.Emit("44 8B 96 C8 05 00 00 44 89 96 68 05 00 00");a.Label("noHeal");
            a.Emit("83 78 2C 00");a.Branch("0F 84","noDamage");a.Emit("F3 0F 10 86 CC 05 00 00 F3 0F 5F 40 2C F3 0F 11 86 CC 05 00 00");a.Label("noDamage");
            a.Emit("83 78 30 00");a.Branch("0F 84","noPickup");a.Emit("F3 0F 10 86 E0 05 00 00 F3 0F 5F 40 30 F3 0F 11 86 E0 05 00 00");a.Label("noPickup");
            a.Emit("41 BA FF FF FF FF 44 87 50 34 41 83 FA FF");a.Branch("0F 84","noAdvance");
            a.Emit("66 41 0F 6E C2 F3 0F 58 86 A0 0E 00 00 F3 0F 11 86 A0 0E 00 00");a.Label("noAdvance");
            a.Label("runDone");FrameCode.Restore(a);a.Emit("80 B9 5E 0F 00 00 00");a.Bytes.AddRange(RarityCode.TailJump(continuation));return a.Finish();
        }
        static void Request(Assembler a,int request,int field,string label)
        {
            // Exchange with -1 atomically, so a new queued request cannot be erased
            // by the acknowledgement of the previous one.
            a.Emit("41 BA FF FF FF FF 44 87 50 "+request.ToString("X2")+" 41 83 FA FF");a.Branch("0F 84",label);
            a.Emit("44 89 96");a.Bytes.AddRange(BitConverter.GetBytes(field));
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
        internal double SpellChance=50,ItemChance=50,Speed=1,XpMultiplier=1,DamageMinimum=0,PickupMinimum=0;
        internal int RerollCost=100;
    }
    internal sealed class PatchSession : IDisposable
    {
        internal const string PrivilegeMessage="Cannot access the game. Run the trainer and game as the same Windows user and with matching administrator privileges. If the game is elevated, run the trainer as administrator too.";
        internal readonly Process Game;
        readonly IntPtr handle;
        readonly List<MemoryPatch> all=new List<MemoryPatch>(),rarity=new List<MemoryPatch>(),prices=new List<MemoryPatch>();
        readonly MemoryPatch run,xp,profile;
        uint talentSequence,marksSequence;
        internal bool ApplyComplete {get;private set;}
        bool disposed;
        internal bool Enabled {get {foreach(var p in all)if(p.Enabled)return true;return false;}}
        internal bool Exited {get {if(disposed)return true;try{return Game.HasExited;}catch(InvalidOperationException){return true;}}}
        internal PatchSession(Process game,IntPtr imageBase)
        {
            Game=game;handle=Native.OpenProcess(0x43A,false,game.Id);
            if(handle==IntPtr.Zero)throw Native.Error(PrivilegeMessage);
            try
            {
                AddRarity(imageBase,RarityCode.HookRva,RarityCode.ContinueRva,RarityCode.Original,RarityCode.SelectionLength,0);
                AddRarity(imageBase,0x5f74c3e,0x5f74fa0,RarityCode.Hex("44 0F 2E F0 0F 86 07 03 00 00 31 F6 48 83 3F FF"),0x36c,1);
                AddRarity(imageBase,0x5f74cff,0x5f75270,RarityCode.Hex("44 0F 2E F0 0F 86 12 05 00 00 45 31 C9 E9 5F 05 00 00"),0x571,2);
                IntPtr continuation=Native.Add(imageBase,RunCode.HookRva+RunCode.Original.Length);
                run=new MemoryPatch(handle,game.Id,Native.Add(imageBase,RunCode.HookRva),RunCode.Original,RunCode.Original.Length,delegate(IntPtr c){return RunCode.Build(c,continuation,imageBase);},RunCode.HookRva);all.Add(run);
                xp=AddGenerated(imageBase,XpCode.HookRva,XpCode.Original,delegate(IntPtr c){return XpCode.Build(Native.Add(imageBase,XpCode.HookRva+XpCode.Original.Length));});
                profile=AddGenerated(imageBase,ProfileCode.HookRva,ProfileCode.Original,delegate(IntPtr c){return ProfileCode.Build(c,Native.Add(imageBase,ProfileCode.HookRva+ProfileCode.Original.Length),imageBase);});
                int[] branches={0x5eca83f,0x5ef6bbd,0x5f6e9e9,0x5f6fc3c,0x5f7a4b3};
                int[] costs={0x5eca8bd,0x5ef6c3b,0x5f6ea67,0x5f6fcba,0x5f7a535};
                for(int i=0;i<5;i++)
                {
                    var branch=new MemoryPatch(handle,game.Id,Native.Add(imageBase,branches[i]),RarityCode.Hex(i==4?"45 85 C9 74 7D":"45 85 C0 74 79"),5,null);
                    var cost=new MemoryPatch(handle,game.Id,Native.Add(imageBase,costs[i]),RarityCode.Hex(i==4?"BA 64 00 00 00":"B9 64 00 00 00"),5,null);
                    prices.Add(branch);prices.Add(cost);all.Add(branch);all.Add(cost);
                }
                foreach(var p in all)p.Validate();
                if(!Native.Equal(Native.Read(handle,Native.Add(imageBase,RunCode.CoopRva),RunCode.CoopOriginal.Length),RunCode.CoopOriginal))throw new Exception("An older trainer has modified the co-op step. Close it normally before connecting.");
                if(!Native.Equal(Native.Read(handle,Native.Add(imageBase,RunCode.HudRva),RunCode.HudOriginal.Length),RunCode.HudOriginal))throw new Exception("An older trainer has modified the HUD function. Close it normally before connecting.");
            }
            catch{Native.CloseHandle(handle);throw;}
        }
        void AddRarity(IntPtr b,int hook,int target,byte[] original,int guard,int variant)
        {
            IntPtr end=Native.Add(b,target);
            var p=new MemoryPatch(handle,Game.Id,Native.Add(b,hook),original,guard,delegate(IntPtr unused){return RarityCode.Build(end,variant);},hook);rarity.Add(p);all.Add(p);
        }
        MemoryPatch AddGenerated(IntPtr image,int rva,byte[] original,Func<IntPtr,byte[]> code)
        {var p=new MemoryPatch(handle,Game.Id,Native.Add(image,rva),original,original.Length,code,rva);all.Add(p);return p;}
        internal static PatchSession Attach()
        {
            Process selected=GameLocator.FindRunning();
            if(selected==null)throw new Exception("Start Azeroth Survivors yourself or click Launch game, then enable the trainer.");
            try
            {
                var module=selected.MainModule;string path=module.FileName,hash;
                using(var sha=SHA256.Create())using(var file=File.OpenRead(path))hash=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();
                if(hash!=RarityCode.SupportedHash)throw new Exception("This game build is unsupported. Verified offsets are required after a game update.");
                GameLocator.Remember(path);return new PatchSession(selected,module.BaseAddress);
            }
            catch{selected.Dispose();throw;}
        }
        internal void Apply(TrainerOptions options)
        {
            ValidateOptions(options);ApplyComplete=false;
            if(Exited)throw new Exception("The game has exited.");
            run.Install(null,delegate(MemoryPatch p)
            {
                p.Data(0,BitConverter.GetBytes(options.SpeedEnabled?(float)options.Speed:1f));
                foreach(int offset in new int[]{8,12,24,28,32,40,52})p.Data(offset,BitConverter.GetBytes(-1));
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
            run.Data(RunCode.DamageMinimum,BitConverter.GetBytes((float)options.DamageMinimum));
            run.Data(RunCode.PickupMinimum,BitConverter.GetBytes((float)options.PickupMinimum));
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
            ApplyComplete=true;
        }
        internal static void ValidateOptions(TrainerOptions o)
        {
            foreach(double p in new double[]{o.SpellChance,o.ItemChance})if(Double.IsNaN(p)||Double.IsInfinity(p)||p<0||p>100)throw new ArgumentOutOfRangeException("chance");
            if(o.AutoSpellMode<0||o.AutoSpellMode>5||o.AutoItemMode<0||o.AutoItemMode>5||Double.IsNaN(o.AutoPickDelay)||Double.IsInfinity(o.AutoPickDelay)||o.AutoPickDelay<0||o.AutoPickDelay>10)throw new ArgumentOutOfRangeException("auto-pick");
            for(int i=0;i<RunStat.All.Length;i++)RunStat.All[i].Bits(o.StatValues[i]);
            foreach(double v in new double[]{o.Speed,o.XpMultiplier,o.DamageMinimum,o.PickupMinimum})if(Double.IsNaN(v)||Double.IsInfinity(v)||v<0)throw new ArgumentOutOfRangeException("multiplier");
            if(o.Speed>10||o.XpMultiplier<0.1||o.XpMultiplier>100||o.DamageMinimum>1000||o.PickupMinimum>1000||o.RerollCost<0||o.RerollCost>100000)throw new ArgumentOutOfRangeException("options");
        }
        internal void SetFreeRerolls(int n){Queue(RunCode.FreeRerollRequest,n,9999);}
        internal void Heal(){Queue(RunCode.HealRequest,1,1);}
        internal void GrantXp(int n){Queue(RunCode.XpRequest,n,1000000);}
        internal void SetShield(float n){if(Single.IsNaN(n)||Single.IsInfinity(n)||n<0||n>1000000)throw new ArgumentOutOfRangeException("shield");QueueBits(RunCode.ShieldRequest,BitConverter.GetBytes(n));}
        internal void Advance(float seconds){if(Single.IsNaN(seconds)||Single.IsInfinity(seconds)||seconds<0||seconds>3600)throw new ArgumentOutOfRangeException("seconds");QueueBits(RunCode.AdvanceRequest,BitConverter.GetBytes(seconds));}
        void QueueBits(int offset,byte[] bytes){if(Exited||!run.Enabled)throw new Exception("Enable the trainer first.");run.Data(offset,bytes);}
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
            run.Data(offset,BitConverter.GetBytes(value));
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
