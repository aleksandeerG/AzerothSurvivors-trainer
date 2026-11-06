using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
        internal static byte[] EntryJump(IntPtr source,IntPtr target,int size)
        {
            long delta=target.ToInt64()-source.ToInt64()-6;
            // The ignored CS prefix keeps Windows from mistaking a bare JMP
            // for an epilogue while the native stack frame is still allocated.
            if(size>=6&&delta>=Int32.MinValue&&delta<=Int32.MaxValue&&UnwindSupport.RelativeEntrySafe())
            {var bytes=new List<byte>{0x2e,0xe9};bytes.AddRange(BitConverter.GetBytes((int)delta));while(bytes.Count<size)bytes.Add(0x90);return bytes.ToArray();}
            if(size<12)throw new Exception("Relative entry range or Windows unwind validation requires a 12-byte fallback window.");
            return EntryJump(target,size);
        }
        internal static void Replay(Assembler a,IntPtr cave,IntPtr image,HookSite site)
        {
            var bytes=(byte[])site.Original.Clone();
            var decoder=Iced.Intel.Decoder.Create(64,new Iced.Intel.ByteArrayCodeReader(bytes));decoder.IP=(ulong)Native.Add(image,site.Rva).ToInt64();
            ulong original=decoder.IP;int emitted=a.Bytes.Count;
            while(decoder.IP<original+(ulong)bytes.Length)
            {
                Iced.Intel.Instruction instruction;decoder.Decode(out instruction);
                if(instruction.IsInvalid)throw new Exception("Invalid replay instruction.");
                if(!instruction.IsIPRelativeMemoryOperand)continue;
                var offsets=decoder.GetConstantOffsets(ref instruction);
                long next=cave.ToInt64()+emitted+(long)(instruction.NextIP-original);
                long delta=unchecked((long)instruction.IPRelativeMemoryAddress)-next;
                if(offsets.DisplacementSize!=4||delta<Int32.MinValue||delta>Int32.MaxValue)throw new Exception("Replay target is outside the near hook's displacement range.");
                Array.Copy(BitConverter.GetBytes((int)delta),0,bytes,(int)(instruction.IP-original)+offsets.DisplacementOffset,4);
            }
            a.Bytes.AddRange(bytes);
        }
        internal static byte[] TailJump(IntPtr target,int register=11)
        {
            // A decoded liveness proof selects R10 or R11. This uses neither
            // game locals nor outgoing shadow space; register mode also avoids
            // Windows treating the jump as a completed function epilogue.
            if(register!=10&&register!=11)throw new ArgumentOutOfRangeException("register");
            var b=new List<byte>{0x49,(byte)(0xb8+register-8)};b.AddRange(BitConverter.GetBytes(target.ToInt64()));b.AddRange(new byte[]{0x41,0xff,(byte)(0xe0+register-8)});return b.ToArray();
        }
        internal static byte[] Build(IntPtr continuation,RarityRegisters registers,int tailRegister=11)
        {
            int[] w=registers.Weights;var a=new Assembler();FrameCode.Enter(a);FrameCode.Save(a);
            // XMM2 is volatile across the game's RNG call. Reconstruct its total
            // from the five decoded callee-saved weight registers instead.
            a.Sse(0x28,2,w[0],false);for(int i=1;i<5;i++)a.Sse(0x58,2,w[i],true);
            a.Sse(0x5e,0,2,true);
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
            a.Branch("E9","selected");a.Label("legendary");a.Sse(0x5e,0,3,true);a.Sse(0x59,0,w[4],true);a.Emit("B8 04 00 00 00");
            a.Label("selected");
            a.Emit("89 84 24 10 01 00 00 F3 0F 11 84 24 18 01 00 00"); // selected rank and residual RNG value
            FrameCode.Restore(a);a.LoadStack32(registers.Result,0x110);a.Emit("F3 0F 10 84 24 18 01 00 00");FrameCode.Leave(a);
            a.Bytes.AddRange(TailJump(continuation,tailRegister));return a.Finish();
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
        internal void SseRip(byte op,int register,int target)
        {
            Emit("F3");if(register>=8)Emit("44");Emit("0F");Bytes.Add(op);Bytes.Add((byte)(5+(register%8)*8));Bytes.AddRange(BitConverter.GetBytes(target-(Bytes.Count+4)));
        }
        internal void LoadStack32(int register,int offset)
        {if(register>=8)Emit("44");Emit("8B");Bytes.Add((byte)(0x84+(register%8)*8));Emit("24");Bytes.AddRange(BitConverter.GetBytes(offset));}
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
            var a=new Assembler(layout);FrameCode.Enter(a);FrameCode.Save(a);
            a.Emit("48 89 B4 24 10 01 00 00"); // save RSI; capture proven Game and frame-time registers
            a.Bytes.Add((byte)(0x48|(layout.RunOwnerRegister>=8?4:0)));a.Emit("89");a.Bytes.Add((byte)(0xc6+((layout.RunOwnerRegister&7)<<3)));
            if(layout.RunTimeRegister>=0){a.Emit("F3");if(layout.RunTimeRegister>=8)a.Emit("44");a.Emit("0F 11");a.Bytes.Add((byte)(0x44+((layout.RunTimeRegister&7)<<3)));a.Emit("24 78");}
            FrameCode.Pointer(a,Native.Add(start,4096));
            bool actions=layout.Supports(TrainerFeature.RunResources),banishes=layout.Supports(TrainerFeature.Banishes);
            bool tracking=actions||banishes||layout.Supports(TrainerFeature.Gold)||layout.Supports(TrainerFeature.FreeRerolls)||layout.Supports(TrainerFeature.Shield)||layout.Supports(TrainerFeature.Stats)||layout.Supports(TrainerFeature.AutoPick)||layout.Supports(TrainerFeature.XpGrant)||layout.Supports(TrainerFeature.Lives);
            if(tracking)RunCommands.Track(a,layout);
            if(banishes)
            {
                Request(a,BanishRequest,layout.Field("banishes"),"banish");a.Emit("83 78 10 00");a.Branch("0F 84","noBan");a.Emit("C7 86 $banishes 0F 27 00 00");a.Label("noBan");
            }
            if(layout.Supports(TrainerFeature.Gold))Request(a,GoldRequest,layout.Field("gold"),"gold");
            if(layout.Supports(TrainerFeature.Shield))Request(a,ShieldRequest,layout.Field("shield"),"shield");
            if(layout.Supports(TrainerFeature.FreeRerolls))
            {
                Request(a,FreeRerollRequest,layout.Field("rerolls"),"free");
                a.Emit("83 78 14 00");a.Branch("0F 84","noReroll");a.Emit("C7 86 $rerolls 0F 27 00 00");a.Label("noReroll");
            }
            if(actions)
            {
                RunCommands.Take(a,HealRequest,"autoHeal");a.Branch("E9","heal");a.Label("autoHeal");a.Emit("83 78 24 00");a.Branch("0F 84","noHeal");a.Emit("83 BE $hp 00");a.Branch("0F 8E","noHeal");
                a.Label("heal");a.Emit("44 8B 96 $maxHp 44 89 96 $hp");a.Label("noHeal");
                RunCommands.Take(a,AdvanceRequest,"noAdvance");a.Emit("66 41 0F 6E C2");
                if(layout.RunClockDirection<0)a.Emit("0F 57 C9 F3 0F 5C C8 0F 28 C1"); // negate the requested seconds for a countdown
                a.Emit("F3 0F 58 86 $clock F3 0F 11 86 $clock F3 0F 11 40 68");a.Label("noAdvance");
            }
            if(layout.Supports(TrainerFeature.XpGrant))
            {
                RunCommands.Take(a,XpRequest,"noXp");a.Emit("44 89 D2 48 89 F1");FrameCode.Call(a,Native.Add(image,layout.Rva("Xp")));a.Label("noXp");FrameCode.Pointer(a,Native.Add(start,4096));
            }
            if(layout.Supports(TrainerFeature.AutoPick))AutoPickCode.Emit(a,start,image,layout);
            if(layout.Supports(TrainerFeature.Stats)||layout.Supports(TrainerFeature.Lives))RunStat.Emit(a,layout);
            a.Label("runDone");a.Emit("48 8B B4 24 10 01 00 00");FrameCode.Restore(a);if(layout.Supports(TrainerFeature.RunControls))a.SseRip(0x59,layout.RunTimeRegister,4096);FrameCode.Leave(a);
            // Replay the decoded register assignments and branch comparison.
            RarityCode.Replay(a,start,image,layout.Site("RunHook"));
            a.Bytes.AddRange(RarityCode.TailJump(continuation,layout.Site("RunHook").TailRegister));return a.Finish();
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
        readonly byte[] sourceFrame;
        bool retained,registered;
        internal IntPtr Cave {get;private set;}
        byte[] installed;
        internal bool Enabled {get;private set;}
        internal MemoryPatch(IntPtr h,int processId,IntPtr site,byte[] original,int guard,Func<IntPtr,byte[]> code,int unwindRva=-1,byte[] frame=null)
        {handle=h;pid=processId;Site=site;Original=original;guardLength=guard;builder=code;sourceRva=unwindRva;sourceFrame=frame;}
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
                Cave=AllocateNear();
                if(Cave==IntPtr.Zero&&Original.Length>=12)Cave=Native.VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)8192,0x3000,4);
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
                        if(sourceRva>=0){retained=true;UnwindSupport.Register(handle,pid,Cave,generated.Length,sourceRva,sourceFrame);registered=true;}
                    }
                    if(initialize!=null)initialize(this);
                    uint old;if(!Native.VirtualProtectEx(handle,Cave,(UIntPtr)4096,0x20,out old))throw Native.Error("Cannot prepare trainer instructions");
                    if(!Native.FlushInstructionCache(handle,Cave,(UIntPtr)4096))throw Native.Error("Cannot refresh trainer instructions");
                    replacement=RarityCode.EntryJump(Site,Cave,Original.Length);
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
        IntPtr AllocateNear()
        {
            long origin=Site.ToInt64(),low=Math.Max(65536,origin-0x7fff0000L),high=Math.Min(0x00007ffffffeffffL,origin+0x7fff0000L);
            var candidates=new List<long>();
            for(long at=low;at<high;)
            {
                Native.MemoryRegion region;
                if(Native.VirtualQueryEx(handle,new IntPtr(at),out region,(UIntPtr)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MemoryRegion)))==UIntPtr.Zero)break;
                long end=checked(region.BaseAddress.ToInt64()+(long)region.RegionSize.ToUInt64());if(end<=at)break;
                if(region.State==0x10000)
                {
                    long first=(Math.Max(at,region.BaseAddress.ToInt64())+65535)&~65535L,last=(Math.Min(end,high)-8192)&~65535L;
                    if(first<=last)candidates.Add(Math.Max(first,Math.Min(last,origin&~65535L)));
                }
                at=end;
            }
            foreach(long address in candidates.OrderBy(p=>Math.Abs(p-origin)))
            {IntPtr result=Native.VirtualAllocEx(handle,new IntPtr(address),(UIntPtr)8192,0x3000,4);if(result!=IntPtr.Zero)return result;}
            return IntPtr.Zero;
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
        internal bool ReviewedRelease {get{return layout.ReviewedRelease;}}
        internal int AvailableStatCount {get{return layout.AvailableStatCount;}}
        internal bool StatUnitsVerified {get{return layout.StatUnitsVerified;}}
        internal string CompatibilityDetails {get{return layout.Details;}}
        internal string UnavailableFeatures {get{return layout.UnavailableNames;}}
        internal bool Supports(TrainerFeature feature){return layout.Supports(feature);}
        internal bool SupportsStat(int index){return layout.SupportsStat(index);}
        internal bool HighestRaritySupported {get{return layout.OfferRarity>0;}}
        internal int UnsupportedAutoPickCount
        {
            get
            {
                if(Exited||run==null||!run.Enabled||currentOptions.AutoSpellMode==0&&currentOptions.AutoItemMode==0)return 0;
                if(BitConverter.ToInt32(Native.Read(handle,Native.Add(run.Cave,4096+RunCommands.Status),4),0)!=1)return 0;
                return BitConverter.ToInt32(Native.Read(handle,Native.Add(run.Cave,4096+AutoPickCode.UnsupportedCount),4),0);
            }
        }
        internal int[] AvailableClasses
        {
            get
            {
                if(Exited||profile==null||!profile.Enabled)return new int[0];
                IntPtr owner=new IntPtr(BitConverter.ToInt64(Native.Read(handle,Native.Add(profile.Cave,4096+ProfileCode.Owner),8),0));if(owner==IntPtr.Zero)return new int[0];
                var classes=new HashSet<int>();
                foreach(bool talent in new[]{true,false})
                {
                    if(!layout.HasField(talent?"talentCount":"marksCount"))continue;
                    long count=BitConverter.ToInt64(Native.Read(handle,Native.Add(owner,layout.Field(talent?"talentCount":"marksCount")),8),0);
                    if(count<0||count>256)throw new Exception("The game's class record count is invalid.");if(count==0)continue;
                    IntPtr data=new IntPtr(BitConverter.ToInt64(Native.Read(handle,Native.Add(owner,layout.Field(talent?"talentData":"marksData")),8),0));
                    int stride=talent?40:8;byte[] records=Native.Read(handle,data,checked((int)count*stride));
                    for(int i=0;i<count;i++){int id=records[i*stride+(talent?4:0)];if(id!=0)classes.Add(id);}
                }
                return classes.OrderBy(id=>id).ToArray();
            }
        }
        readonly IntPtr handle;
        readonly List<MemoryPatch> all=new List<MemoryPatch>(),rarity=new List<MemoryPatch>{null,null,null},prices=new List<MemoryPatch>();
        readonly MemoryPatch run,xp,profile,health;
        readonly GameLayout layout;
        uint talentSequence,marksSequence;
        bool disposed,connected;
        TrainerOptions currentOptions=new TrainerOptions();
        internal bool Connected {get{return connected||Enabled;}}
        internal bool HasRunHook {get{return run!=null;}}
        internal bool Enabled {get {foreach(var p in all)if(p.Enabled)return true;return false;}}
        internal bool Exited {get {if(disposed)return true;try{return Game.HasExited;}catch(InvalidOperationException){return true;}}}
        internal PatchSession(Process game,IntPtr imageBase,GameLayout build)
        {
            Game=game;layout=build;handle=Native.OpenProcess(0x43A,false,game.Id);
            if(handle==IntPtr.Zero)throw Native.Error(PrivilegeMessage);
            try
            {
                layout.ValidateLive(handle,imageBase);
                if(Supports(TrainerFeature.SpellChance))AddRarity(imageBase,layout.Site("Spell"),0);
                if(Supports(TrainerFeature.ItemChance)){AddRarity(imageBase,layout.Site("Jewelry1"),1);AddRarity(imageBase,layout.Site("Jewelry2"),2);}
                if(layout.HasRunHook)
                {
                    HookSite tick=layout.Site("RunHook");
                    run=AddGenerated(imageBase,tick,delegate(IntPtr c){return RunCode.Build(c,Native.Add(imageBase,tick.Continuation),imageBase,layout);});
                }
                if(Supports(TrainerFeature.XpScaling)){HookSite award=layout.Site("XpHook");xp=AddGenerated(imageBase,award,delegate(IntPtr c){return XpCode.Build(Native.Add(imageBase,award.Continuation),layout,c,imageBase);});}
                if(Supports(TrainerFeature.HealthFloor)){HookSite maximum=layout.Site("HealthHook");health=AddGenerated(imageBase,maximum,delegate(IntPtr c){return HealthCode.Build(Native.Add(imageBase,maximum.Continuation),layout);});}
                if(Supports(TrainerFeature.ClassProgression)||Supports(TrainerFeature.ClassMarks))
                {
                    HookSite server=layout.Site("ProfileHook");
                    profile=AddGenerated(imageBase,server,delegate(IntPtr c){return ProfileCode.Build(c,Native.Add(imageBase,server.Continuation),imageBase,layout);});
                }
                for(int i=0;Supports(TrainerFeature.RerollCost)&&i<layout.PriceCount;i++)
                {
                    HookSite branchSite=layout.Site("PriceBranch"+i),costSite=layout.Site("PriceCost"+i);
                    var branch=new MemoryPatch(handle,game.Id,Native.Add(imageBase,branchSite.Rva),branchSite.Original,branchSite.Guard,null);
                    var cost=new MemoryPatch(handle,game.Id,Native.Add(imageBase,costSite.Rva),costSite.Original,costSite.Guard,null);
                    prices.Add(branch);prices.Add(cost);all.Add(branch);all.Add(cost);
                }
                foreach(var patch in all)patch.Validate();
            }
            catch{Native.CloseHandle(handle);throw;}
        }
        void AddRarity(IntPtr image,HookSite site,int variant)
        {
            var registers=layout.Roll(variant==0?"Spell":"Jewelry"+variant);
            var patch=new MemoryPatch(handle,Game.Id,Native.Add(image,site.Rva),site.Original,site.Guard,delegate(IntPtr unused){return RarityCode.Build(Native.Add(image,site.Continuation),registers,site.TailRegister);},site.Rva,layout.Frame(site));
            rarity[variant]=patch;all.Add(patch);
        }
        MemoryPatch AddGenerated(IntPtr image,HookSite site,Func<IntPtr,byte[]> code)
        {var patch=new MemoryPatch(handle,Game.Id,Native.Add(image,site.Rva),site.Original,site.Guard,code,site.Rva,layout.Frame(site));all.Add(patch);return patch;}
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
            RequireOptions(options);
            if(Exited)throw new Exception("The game has exited.");
            if(health!=null)
            {
                if(options.StatEnabled[0])
                {
                    health.Install(null,delegate(MemoryPatch p){p.Data(0,new byte[8]);});
                    health.Data(0,BitConverter.GetBytes((ulong)RunStat.All[0].Bits(options.StatValues[0])|1UL<<32));
                }
                else health.Remove();
            }
            if(xp!=null)
            {
                if(options.XpEnabled){xp.Install(null,delegate(MemoryPatch p){p.Data(0,BitConverter.GetBytes(XpCode.ScaleUnits(options.XpMultiplier)));p.Data(XpCode.CarryOffset,new byte[XpCode.CarrySlots*XpCode.CarryStride]);});xp.Data(0,BitConverter.GetBytes(XpCode.ScaleUnits(options.XpMultiplier)));}else xp.Remove();
            }
            if(run!=null)
            {
                bool needed=options.SpeedEnabled||options.AutoHeal||options.UnlimitedBanish||options.UnlimitedReroll||options.AutoSpellMode!=0||options.AutoItemMode!=0||options.StatEnabled.Where((on,index)=>index!=0).Any(on=>on);
                if(needed||run.Enabled&&RunPending())ApplyRunOptions(options);else run.Remove();
            }
            for(int index=0;index<rarity.Count;index++)
            {
                var p=rarity[index];if(p==null)continue;double chance=index==0?options.SpellChance:options.ItemChance;
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
                    byte[] bytes=Native.Read(handle,prices[i+1].Site,prices[i+1].Original.Length);
                    if(BitConverter.ToInt32(bytes,bytes.Length-4)!=options.RerollCost)priceChange=true;
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
                            byte[] j=new byte[branch.Original.Length];for(int b=0;b<j.Length;b++)j[b]=0x90;j[0]=0xe9;Array.Copy(BitConverter.GetBytes((int)(cost.Site.ToInt64()-branch.Site.ToInt64()-5)),0,j,1,4);
                            byte[] value=(byte[])cost.Original.Clone();Array.Copy(BitConverter.GetBytes(options.RerollCost),0,value,value.Length-4,4);
                            cost.Replace(value);branch.Replace(j);
                        }
                        else{prices[i].Remove();prices[i+1].Remove();}
                    }
                }
            }
            currentOptions=options;connected=true;
        }
        bool RunPending()
        {
            return RunCommands.Offsets.Any(offset=>BitConverter.ToInt64(Native.Read(handle,Native.Add(run.Cave,4096+offset),8),0)!=-1);
        }
        internal void PrepareProfile()
        {
            if(Exited||!Connected)throw new Exception("Enable the trainer first.");
            if(profile==null)throw new Exception("Class edits are unavailable for this game.");
            profile.Install(null,delegate(MemoryPatch p){p.Data(0,BitConverter.GetBytes(-1L));p.Data(8,BitConverter.GetBytes(-1L));p.Data(16,new byte[24]);});
        }
        void PrepareRun()
        {
            if(Exited||!Connected||run==null)throw new Exception("Enable the trainer first.");
            if(!run.Enabled)ApplyRunOptions(currentOptions);
            var timer=Stopwatch.StartNew();
            while(timer.ElapsedMilliseconds<400)
            {
                if(BitConverter.ToInt64(Native.Read(handle,Native.Add(run.Cave,4096+RunCommands.Owner),8),0)!=0)return;
                System.Threading.Thread.Sleep(5);
            }
            throw new Exception("Waiting for a game update. Resume the game and retry the action.");
        }
        void ApplyRunOptions(TrainerOptions options)
        {
            run.Install(null,delegate(MemoryPatch p)
            {
                p.Data(0,BitConverter.GetBytes(options.SpeedEnabled?(float)options.Speed:1f));
                foreach(int offset in RunCommands.Offsets)p.Data(offset,BitConverter.GetBytes(-1L));
                p.Data(RunCommands.Owner,new byte[24]);
                p.Data(AutoPickCode.SpellMode,new byte[40]);p.Data(AutoPickCode.Random,BitConverter.GetBytes(unchecked((uint)Environment.TickCount)|1));p.Data(AutoPickCode.MaxDelta,BitConverter.GetBytes(0.1f));
                p.Data(RunStat.DataOffset,new byte[RunStat.All.Length*RunStat.Stride]);
                p.Data(AutoPickCode.UnsupportedCount,new byte[4]);
            });
            run.Data(0,BitConverter.GetBytes(options.SpeedEnabled?(float)options.Speed:1f));
            run.Data(AutoPickCode.Delay,BitConverter.GetBytes((float)options.AutoPickDelay));
            run.Data(AutoPickCode.SpellMode,BitConverter.GetBytes(options.AutoSpellMode));run.Data(AutoPickCode.ItemMode,BitConverter.GetBytes(options.AutoItemMode));
            for(int i=0;i<RunStat.All.Length;i++)run.Data(RunStat.DataOffset+i*RunStat.Stride,BitConverter.GetBytes((ulong)RunStat.All[i].Bits(options.StatValues[i])|(options.StatEnabled[i]?1UL<<32:0)));
            run.Data(RunCode.AutoHeal,BitConverter.GetBytes(options.AutoHeal?1:0));
            run.Data(RunCode.UnlimitedBanish,BitConverter.GetBytes(options.UnlimitedBanish?1:0));
            run.Data(RunCode.UnlimitedReroll,BitConverter.GetBytes(options.UnlimitedReroll?1:0));
        }
        void Require(TrainerFeature feature)
        {if(!Supports(feature))throw new Exception(GameLayout.FeatureName(feature)+": unavailable for this game build. An updated trainer is required for this feature.");}
        void RequireOptions(TrainerOptions o)
        {
            if(o.SpellLegendary)Require(TrainerFeature.SpellChance);
            if(o.ItemLegendary)Require(TrainerFeature.ItemChance);
            if(o.FixedCost)Require(TrainerFeature.RerollCost);
            if(o.SpeedEnabled)Require(TrainerFeature.RunControls);if(o.XpEnabled)Require(TrainerFeature.XpScaling);
            if(o.AutoHeal)Require(TrainerFeature.RunResources);if(o.UnlimitedReroll)Require(TrainerFeature.FreeRerolls);if(o.UnlimitedBanish)Require(TrainerFeature.Banishes);
            if(o.AutoSpellMode!=0||o.AutoItemMode!=0)Require(TrainerFeature.AutoPick);
            if((o.AutoSpellMode==5||o.AutoItemMode==5)&&layout.OfferRarity==0)throw new Exception("Highest rarity is unavailable: the offer rarity field was not recognized.");
            for(int i=0;i<o.StatEnabled.Length;i++)if(o.StatEnabled[i]){Require(StatFeature(i));if(!layout.SupportsStat(i))throw new Exception(RunStat.All[i].Name+": the stat field meaning is unproven for this game.");}
        }
        internal static TrainerFeature StatFeature(int index){return index==0?TrainerFeature.HealthFloor:RunStat.All[index].Field==0x628?TrainerFeature.Lives:TrainerFeature.Stats;}
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
            Require(offset==RunCode.BanishRequest?TrainerFeature.Banishes:offset==RunCode.XpRequest?TrainerFeature.XpGrant:offset==RunCode.GoldRequest?TrainerFeature.Gold:offset==RunCode.FreeRerollRequest?TrainerFeature.FreeRerolls:offset==RunCode.ShieldRequest?TrainerFeature.Shield:TrainerFeature.RunResources);
            PrepareRun();
            ulong state=BitConverter.ToUInt64(Native.Read(handle,Native.Add(run.Cave,4096+RunCommands.Status),8),0);
            IntPtr owner=new IntPtr(BitConverter.ToInt64(Native.Read(handle,Native.Add(run.Cave,4096+RunCommands.Owner),8),0));
            if(owner==IntPtr.Zero)throw new Exception("Enter a live run before sending a run action.");
            byte phase=Native.Read(handle,Native.Add(owner,layout.Field("phase")),1)[0];
            float hp=BitConverter.ToSingle(Native.Read(handle,Native.Add(owner,layout.Field("hp")),4),0);
            if((phase!=layout.RunningState&&phase!=layout.OfferState)||!(hp>0))throw new Exception("Enter a live run before sending a run action.");
            run.Data(offset,BitConverter.GetBytes(RunCommands.Pack(state,BitConverter.ToUInt32(bytes,0))));
        }
        internal void CancelRunRequests()
        {
            if(run==null)throw new Exception("Run actions are unavailable for this game build.");
            if(Exited||!Connected)throw new Exception("Enable the trainer first.");if(run==null||!run.Enabled)return;
            using(var frozen=new FrozenThreads(Game.Id))
                foreach(int offset in RunCommands.Offsets)run.Data(offset,BitConverter.GetBytes(-1L));
        }
        internal uint SetTalentPoints(int classId,int n,bool confirmUnreviewed=false){if(n<0||n>51)throw new ArgumentOutOfRangeException("points");return QueueProfile(ProfileCode.TalentRequest,classId,n,confirmUnreviewed);}
        internal uint SetMarks(int classId,int rawTenths,bool confirmUnreviewed=false){if(rawTenths<0||rawTenths>10000000)throw new ArgumentOutOfRangeException("marks");return QueueProfile(ProfileCode.MarksRequest,classId,rawTenths,confirmUnreviewed);}
        uint QueueProfile(int offset,int classId,int value,bool confirmUnreviewed)
        {
            Require(offset==ProfileCode.MarksRequest?TrainerFeature.ClassMarks:TrainerFeature.ClassProgression);
            if(!layout.ReviewedRelease&&!confirmUnreviewed)throw new Exception("Confirm the unreviewed build and unverified value units before a permanent edit.");
            if(classId<1||classId>255)throw new ArgumentOutOfRangeException("class");
            PrepareProfile();
            using(var frozen=new FrozenThreads(Game.Id))
            {
                if(!AvailableClasses.Contains(classId))throw new Exception("That class is not present in the loaded game profile. Open its Armory or play it once before editing.");
                ProfileBackup.Create(handle);
                uint sequence=offset==ProfileCode.TalentRequest?checked(++talentSequence):checked(++marksSequence);
                profile.Data(offset,BitConverter.GetBytes(ProfileCode.PackRequest(sequence,classId,value)));return sequence;
            }
        }
        internal int TalentResult(uint sequence){return ReadProfileResult(ProfileCode.TalentResult,sequence);}
        internal int MarksResult(uint sequence){return ReadProfileResult(ProfileCode.MarksResult,sequence);}
        int ReadProfileResult(int offset,uint sequence){if(Exited||profile==null||!profile.Enabled)return 0;return ProfileCode.CompletedStatus(BitConverter.ToUInt64(Native.Read(handle,Native.Add(profile.Cave,4096+offset),8),0),sequence);}
        internal void SetBanishes(int value){Queue(RunCode.BanishRequest,value,9999);}
        internal void SetGold(int value){Queue(RunCode.GoldRequest,value,100000);}
        void Queue(int offset,int value,int maximum)
        {
            if(value<0||value>maximum)throw new ArgumentOutOfRangeException("value");
            QueueBits(offset,BitConverter.GetBytes(value));
        }
        internal void Disable()
        {
            if(disposed)return;
            if(Exited){foreach(var p in all)p.Abandon();connected=false;return;}
            if(!Enabled){connected=false;return;}
            try
            {
                using(var frozen=new FrozenThreads(Game.Id))
                {
                    foreach(var p in all)p.RequireSafe(frozen);
                    for(int i=0;i<prices.Count;i+=2)
                        if(prices[i].Enabled||prices[i+1].Enabled)
                            frozen.RequireOutside(prices[i].Site,(int)(prices[i+1].Site.ToInt64()-prices[i].Site.ToInt64()+12));
                    for(int i=all.Count-1;i>=0;i--)all[i].Remove();
                    connected=false;
                }
            }
            catch{if(Exited){foreach(var p in all)p.Abandon();connected=false;return;}throw;}
        }
        public void Dispose(){if(disposed)return;Disable();Native.CloseHandle(handle);Game.Dispose();disposed=true;}
    }
}
