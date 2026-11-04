using System;
using System.Collections.Generic;
using System.Linq;
namespace AzerothTrainer
{
    // Each operand is a reviewed memory displacement in a complete instruction
    // template. Registers, opcode, access width and control flow remain fixed.
    internal sealed class FieldOperand
    {
        internal readonly string Group;
        internal readonly int Position,Relative,Width;
        internal FieldOperand(string text)
        {
            string[] parts=text.Split(':');if(parts.Length!=4)throw new Exception("Invalid memory operand contract.");
            Group=parts[0];Position=Int32.Parse(parts[1]);Relative=Int32.Parse(parts[2]);Width=Int32.Parse(parts[3]);
        }
    }
    internal sealed class MemoryContracts
    {
        sealed class Region
        {
            internal string Name;internal int Size,Alignment;internal bool Server;
        }
        readonly Region[] regions;
        readonly Dictionary<string,int> bases=new Dictionary<string,int>();
        readonly Dictionary<string,HashSet<string>> witnesses=new Dictionary<string,HashSet<string>>();
        readonly Dictionary<string,string> failures=new Dictionary<string,string>();
        internal MemoryContracts()
        {
            regions=new[]{
                new Region{Name="Attributes",Size=0xd0,Alignment=4},
                new Region{Name="Offers",Size=24,Alignment=8},
                new Region{Name="Clock",Size=4,Alignment=4},
                new Region{Name="OfferNumber",Size=4,Alignment=4},
                new Region{Name="Banishes",Size=4,Alignment=4},
                new Region{Name="Phase",Size=1,Alignment=1},
                new Region{Name="Coop",Size=1,Alignment=1},
                new Region{Name="OfferKind",Size=1,Alignment=1},
                new Region{Name="CoopPhase",Size=1,Alignment=1},
                new Region{Name="Profile",Size=0x198,Alignment=8,Server=true}
            };
        }
        Region Find(string name){var region=regions.FirstOrDefault(r=>r.Name==name);if(region==null)throw new Exception("Unknown memory contract: "+name);return region;}
        internal bool Valid(FieldOperand operand,int displacement)
        {
            var region=Find(operand.Group);long start=(long)displacement-operand.Relative;
            return operand.Relative>=0&&operand.Width>0&&operand.Relative+operand.Width<=region.Size&&start>=0x100&&start+region.Size<=0x10000&&start%region.Alignment==0;
        }
        internal void Observe(string function,FieldOperand[] operands,byte[] body)
        {
            foreach(var operand in operands)
            {
                int value=checked(BitConverter.ToInt32(body,operand.Position)-operand.Relative),previous;
                if(!bases.TryGetValue(operand.Group,out previous)){bases.Add(operand.Group,value);witnesses.Add(operand.Group,new HashSet<string>());}
                else if(previous!=value)failures[operand.Group]="memory operands disagree across matched functions";
                witnesses[operand.Group].Add(function);
            }
        }
        internal void Require(params string[] names)
        {
            foreach(string name in names)
            {
                if(!bases.ContainsKey(name))throw new Exception(name+": no validated memory operand witness.");
                string reason;if(failures.TryGetValue(name,out reason))throw new Exception(name+": "+reason+".");
                var region=Find(name);
                foreach(var other in regions)
                {
                    int at;if(other.Name==name||other.Server!=region.Server||!bases.TryGetValue(other.Name,out at))continue;
                    if((long)bases[name]<at+other.Size&&(long)at<bases[name]+region.Size)throw new Exception(name+": discovered memory regions overlap.");
                }
            }
        }
        int At(string group,int relative){Require(group);return checked(bases[group]+relative);}
        internal int Field(string name)
        {
            switch(name)
            {
                case "xp":return At("Attributes",4);
                case "hp":return At("Attributes",8);
                case "shield":return At("Attributes",12);
                case "gold":return At("Attributes",24);
                case "maxHp":return At("Attributes",0x68);
                case "rerolls":return At("Attributes",0xcc);
                case "offers":return At("Offers",8);
                case "offerCount":return At("Offers",16);
                case "clock":return At("Clock",0);
                case "offerNumber":return At("OfferNumber",0);
                case "banishes":return At("Banishes",0);
                case "phase":return At("Phase",0);
                case "coop":return At("Coop",0);
                case "offerKind":return At("OfferKind",0);
                case "coopPhase":return At("CoopPhase",0);
                case "profile":return At("Profile",0);
                case "talentData":return At("Profile",0x68);
                case "talentCount":return At("Profile",0x70);
                case "marksCapacity":return At("Profile",0x180);
                case "marksData":return At("Profile",0x188);
                case "marksCount":return At("Profile",0x190);
                default:throw new Exception("Unknown game field: "+name);
            }
        }
        internal int StatField(int reference)
        {
            // RunStat uses reference offsets as stable IDs, never as live addresses.
            int relative=checked(reference-0x560);
            if(relative<0||relative+4>Find("Attributes").Size)throw new Exception("Unknown stat member.");
            return At("Attributes",relative);
        }
        internal string Details
        {
            get{return String.Join(Environment.NewLine,regions.Where(r=>bases.ContainsKey(r.Name)).Select(r=>r.Name+": +0x"+bases[r.Name].ToString("x")+" (witnesses: "+String.Join(", ",witnesses[r.Name].OrderBy(n=>n).ToArray())+(failures.ContainsKey(r.Name)?"; CONFLICT":"")+")").ToArray());}
        }
    }
}
