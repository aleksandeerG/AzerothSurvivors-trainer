using System;
namespace AzerothTrainer
{
    internal sealed class RunStat
    {
        internal readonly string Name,Unit;internal readonly int Field,Kind;internal readonly double Min,Max,Default;
        // Kind: 0 raw float, 1 percent bonus on a base of one, 2 fraction,
        // 3 haste (reciprocal cooldown), 4 integer, 5 total DOT stacks (base one).
        internal RunStat(string name,string unit,int field,int kind,double min,double max,double value)
        {Name=name;Unit=unit;Field=field;Kind=kind;Min=min;Max=max;Default=value;}
        internal uint Bits(double value)
        {
            if(Double.IsNaN(value)||Double.IsInfinity(value)||value<Min||value>Max||(Kind>=4&&value!=Math.Floor(value)))throw new ArgumentOutOfRangeException(Name);
            if(Kind>=4)return (uint)(Kind==5?value-1:value);
            float raw=(float)(Kind==1?1+value/100:(Kind==2?value/100:(Kind==3?1/(1+value/100):value)));
            return BitConverter.ToUInt32(BitConverter.GetBytes(raw),0);
        }
        internal const int DataOffset=128,Stride=8;
        internal static readonly RunStat[] All={
            new RunStat("Maximum health","HP",0x5c8,0,1,1000000,2500),
            new RunStat("Damage bonus","%",0x5cc,1,0,10000,500),
            new RunStat("Haste bonus","%",0x5d0,3,0,1000,100),
            new RunStat("Critical chance","%",0x5d4,2,0,100,100),
            new RunStat("Critical damage","% total",0x5d8,2,100,5000,300),
            new RunStat("Armor","% reduction",0x5e8,2,0,60,60),
            new RunStat("Area bonus","%",0x5f4,1,0,1000,100),
            new RunStat("Extra targets","targets",0x600,4,0,32,5),
            new RunStat("Echo chance","%",0x604,2,0,100,50),
            new RunStat("Movement speed bonus","%",0x5dc,1,0,300,100),
            new RunStat("Regeneration","HP / second",0x5f0,0,0,100000,100),
            new RunStat("Pickup radius","yards",0x5e0,0,0,1000,50),
            new RunStat("Remaining lives","lives",0x628,4,0,99,5),
            new RunStat("Experience bonus","%",0x5e4,1,0,10000,100),
            new RunStat("Gold bonus","%",0x5fc,1,0,10000,100),
            new RunStat("Luck bonus","%",0x5f8,2,0,10000,100),
            new RunStat("Buff power bonus","%",0x60c,2,0,1000,100),
            new RunStat("DoT stack limit","stacks",0x610,5,1,4,4),
            new RunStat("DoT haste bonus","%",0x614,2,0,1000,100),
            new RunStat("Kill heal","HP / kill",0x608,0,0,100000,50)
        };
        internal static void Emit(Assembler a)
        {
            for(int i=0;i<All.Length;i++)
            {
                var stat=All[i];int at=DataOffset+i*Stride;string done="stat"+i;
                // Each value and enabled flag is published as one aligned word.
                a.Emit("83 B8");a.Bytes.AddRange(BitConverter.GetBytes(at+4));a.Emit("00");a.Branch("0F 84",done);
                if(stat.Kind>=4)
                {
                    a.Emit("44 8B 90");a.Bytes.AddRange(BitConverter.GetBytes(at));
                    a.Emit("44 3B 96");a.Bytes.AddRange(BitConverter.GetBytes(stat.Field));a.Branch("0F 86",done);
                    a.Emit("44 89 96");a.Bytes.AddRange(BitConverter.GetBytes(stat.Field));
                }
                else
                {
                    a.Emit("F3 0F 10 80");a.Bytes.AddRange(BitConverter.GetBytes(at));
                    a.Emit(stat.Kind==3?"F3 0F 5D 86":"F3 0F 5F 86");a.Bytes.AddRange(BitConverter.GetBytes(stat.Field));
                    a.Emit("F3 0F 11 86");a.Bytes.AddRange(BitConverter.GetBytes(stat.Field));
                }
                a.Label(done);
            }
        }
    }
    internal static class AutoPickCode
    {
        internal const int ChooseRva=0x5f76700,SpellMode=56,ItemMode=60,Owner=64,Offer=72,Elapsed=76,Delay=80,Random=84,Count=88,MaxDelta=92;
        internal static readonly string[] Rules={"Left card","Middle card","Right card","Random card","Highest rarity (leftmost tie)"};
        internal static void Emit(Assembler a,IntPtr start,IntPtr image)
        {
            a.Emit("80 BE 63 0F 00 00 02");a.Branch("0F 85","autoReset");
            a.Emit("80 BE 5E 0F 00 00 00");a.Branch("0F 84","autoLive");a.Emit("80 BE 64 0F 00 00 01");a.Branch("0F 85","autoReset");a.Label("autoLive");
            a.Emit("83 BE 68 05 00 00 00");a.Branch("0F 8E","autoReset");
            a.Emit("0F B6 96 61 0F 00 00 83 FA 02");a.Branch("0F 87","autoReset");
            a.Emit("44 8B 40 38 85 D2");a.Branch("0F 84","autoMode");a.Emit("44 8B 40 3C");
            a.Label("autoMode");a.Emit("45 85 C0");a.Branch("0F 84","autoReset");
            a.Emit("4C 8B 9E 40 07 00 00 49 83 FB 01");a.Branch("0F 82","autoReset");a.Emit("49 83 FB 03");a.Branch("0F 87","autoReset");
            a.Emit("4C 8B 8E 38 07 00 00 4D 85 C9");a.Branch("0F 84","autoReset");
            a.Emit("48 39 70 40");a.Branch("0F 85","autoOffer");
            a.Emit("8B 96 CC 0E 00 00 3B 50 48");a.Branch("0F 84","autoWait");
            a.Label("autoOffer");a.Emit("48 89 70 40 8B 96 CC 0E 00 00 89 50 48 C7 40 4C 00 00 00 00");
            a.Label("autoWait");a.Emit("F3 0F 10 44 24 78 0F 57 D2 F3 0F 5F C2 F3 0F 5D 40 5C F3 0F 58 40 4C F3 0F 11 40 4C 0F 2E 40 50");a.Branch("0F 82","autoDone");
            a.Emit("C7 40 4C 00 00 00 00 41 83 F8 05");a.Branch("0F 84","autoHighest");a.Emit("41 83 F8 04");a.Branch("0F 84","autoRandom");
            a.Emit("41 8D 50 FF 41 8D 4B FF 39 CA 0F 47 D1");a.Branch("E9","autoChoose");
            a.Label("autoRandom");a.Emit("44 69 50 54 0D 66 19 00 41 81 C2 5F F3 6E 3C 44 89 50 54 44 89 D0 31 D2 41 F7 F3");a.Branch("E9","autoChoose");
            a.Label("autoHighest");a.Emit("31 C9 31 D2 45 8B 91 98 00 00 00");
            a.Label("autoScan");a.Emit("41 8B 81 98 00 00 00 44 39 D0");a.Branch("0F 86","autoNext");a.Emit("41 89 C2 89 CA");
            a.Label("autoNext");a.Emit("49 81 C1 A0 00 00 00 FF C1 44 39 D9");a.Branch("0F 82","autoScan");
            a.Label("autoChoose");a.Emit("48 89 F1");FrameCode.Call(a,Native.Add(image,ChooseRva));
            FrameCode.Pointer(a,Native.Add(start,4096));a.Emit("FF 40 58");a.Branch("E9","autoDone");
            a.Label("autoReset");a.Emit("C7 40 4C 00 00 00 00 48 C7 40 40 00 00 00 00");
            a.Label("autoDone");FrameCode.Pointer(a,Native.Add(start,4096));
        }
    }
}
