using System;
using System.Collections.Generic;
using System.Linq;
using Iced.Intel;
namespace AzerothTrainer
{
    internal enum TrainerFeature {SpellChance,ItemChance,RerollCost,RunControls,XpScaling,RunResources,Banishes,Stats,HealthFloor,AutoPick,XpGrant,ClassProgression,Lives,CoopAutoPick,ClassMarks,Gold,FreeRerolls,Shield}
    internal sealed class GameLayout
    {
        readonly RuntimeDiscovery discovery;
        readonly Dictionary<string,string> invalid=new Dictionary<string,string>();
        readonly Dictionary<TrainerFeature,string> unavailable=new Dictionary<TrainerFeature,string>();
        readonly Dictionary<TrainerFeature,string[]> dependencies=new Dictionary<TrainerFeature,string[]>();
        internal readonly string Hash,Name="Decoded runtime contracts";
        internal int PriceCount {get{return discovery.PriceCount;}}
        internal int HealthRegister {get{return discovery.HealthRegister;}}
        internal int OfferStride {get{return discovery.OfferStride;}}
        internal int OfferRarity {get{return discovery.OfferRarity;}}
        GameLayout(GameImage image)
        {
            discovery=new RuntimeDiscovery(image);Hash=image.Hash;
            dependencies[TrainerFeature.SpellChance]=new[]{"Spell"};dependencies[TrainerFeature.ItemChance]=new[]{"Jewelry"};
            dependencies[TrainerFeature.RerollCost]=Enumerable.Range(0,PriceCount).Select(i=>"Price"+i).ToArray();
            dependencies[TrainerFeature.RunControls]=new[]{"Run"};dependencies[TrainerFeature.XpScaling]=new[]{"Xp"};
            dependencies[TrainerFeature.RunResources]=new[]{"Run"};dependencies[TrainerFeature.Banishes]=new[]{"Run"};
            dependencies[TrainerFeature.Gold]=new[]{"Run"};dependencies[TrainerFeature.FreeRerolls]=new[]{"Run"};dependencies[TrainerFeature.Shield]=new[]{"Run","Health"};
            dependencies[TrainerFeature.Stats]=new[]{"Run","Health"};dependencies[TrainerFeature.HealthFloor]=new[]{"Health"};
            dependencies[TrainerFeature.AutoPick]=new[]{"Run","Choose"};dependencies[TrainerFeature.XpGrant]=new[]{"Run","Xp"};
            dependencies[TrainerFeature.ClassProgression]=new[]{"Profile","ProfileActions","EnsureClass","SaveProfile"};
            dependencies[TrainerFeature.ClassMarks]=new[]{"Profile","ProfileActions","SaveProfile","GrowMarks","MarksRecord"};
            dependencies[TrainerFeature.Lives]=new[]{"Run","Health","Choose"};
            dependencies[TrainerFeature.CoopAutoPick]=new[]{"Run","Choose","Coop"};Evaluate();
        }
        bool Valid(string name){return discovery.Functions.ContainsKey(name)&&!invalid.ContainsKey(name);}
        string Reason(string name){string reason;return invalid.TryGetValue(name,out reason)||discovery.Failures.TryGetValue(name,out reason)?reason:"No unique decoded instruction context.";}
        static void Need(bool ok,string reason){if(!ok)throw new Exception(reason);}
        void Edge(string caller,string callee){Need(discovery.Functions[caller].Calls(Rva(callee)),caller+": expected decoded call to "+callee+" was not found.");}
        void Fields(params string[] names){foreach(string name in names)Field(name);}
        void Evaluate()
        {
            unavailable.Clear();
            foreach(var dependency in dependencies)
            {
                try
                {
                    foreach(string name in dependency.Value)Need(Valid(name),name+": "+Reason(name));
                    switch(dependency.Key)
                    {
                        case TrainerFeature.RerollCost:Need(PriceCount==5,"The complete five-path reroll calculation was not recognized. "+Reason("Prices"));break;
                        case TrainerFeature.RunResources:Fields("hp","maxHp","phase","clock");break;
                        case TrainerFeature.Gold:Fields("hp","phase","clock","gold");break;
                        case TrainerFeature.FreeRerolls:Fields("hp","phase","clock","rerolls");break;
                        case TrainerFeature.Shield:Fields("hp","phase","clock","shield");break;
                        case TrainerFeature.Banishes:Fields("hp","phase","clock","banishes");break;
                        case TrainerFeature.Stats:Fields("hp","phase","clock","attributes");break;
                        case TrainerFeature.HealthFloor:Fields("maxHp");break;
                        case TrainerFeature.AutoPick:Fields("hp","phase","clock","coop","offers","offerCount","offerKind");Need(OfferStride>0,"Offer element stride was not decoded.");Need(Field("offerCount")==Field("offers")+8,"Offer vector pointer/count spacing changed.");if(Valid("Health"))Edge("Choose","Health");break;
                        case TrainerFeature.CoopAutoPick:Fields("hp","phase","clock","coop","coopPhase","offers","offerCount","offerKind");Need(OfferStride>0,"Offer stride is unavailable.");Edge("Run","Coop");break;
                        case TrainerFeature.XpGrant:Fields("hp","phase","clock");Edge("Run","Xp");break;
                        case TrainerFeature.XpScaling:Fields("xp");break;
                        case TrainerFeature.ClassProgression:Fields("profile","talentData","talentCount");Edge("Profile","ProfileActions");Edge("ProfileActions","EnsureClass");Edge("ProfileActions","SaveProfile");break;
                        case TrainerFeature.ClassMarks:Fields("profile","marksCapacity","marksData","marksCount");Edge("Profile","ProfileActions");Edge("ProfileActions","SaveProfile");break;
                        case TrainerFeature.Lives:
                            Fields("attributes");int field=StatField(0x628);
                            Need(discovery.Functions["Choose"].Instructions.Any(i=>i.MemoryBase!=Register.RSP&&i.MemoryBase!=Register.RBP&&i.MemorySize.GetSize()==4&&unchecked((long)i.MemoryDisplacement64)==field),"Remaining-life field has no independent offer-handler witness.");break;
                    }
                }
                catch(Exception ex){unavailable[dependency.Key]=ex.Message;}
            }
        }
        internal static GameLayout Load(string path){var result=new GameLayout(new GameImage(path));result.RequireAny();return result;}
        void RequireAny(){if(AvailableCount>0)return;var error=new Exception("No trainer features match this game safely. Run --verify-files for detailed recognition failures.");error.Data["CompatibilityDetails"]=Details;throw error;}
        internal bool Supports(TrainerFeature feature){return dependencies.ContainsKey(feature)&&!unavailable.ContainsKey(feature);}
        internal int AvailableCount {get{return dependencies.Count-unavailable.Count;}}
        internal bool Complete {get{return unavailable.Count==0;}}
        internal static string FeatureName(TrainerFeature feature)
        {
            switch(feature)
            {
                case TrainerFeature.SpellChance:return "Spell chance";case TrainerFeature.ItemChance:return "Item chance";case TrainerFeature.RerollCost:return "Reroll cost";
                case TrainerFeature.RunControls:return "Run speed";case TrainerFeature.XpScaling:return "XP multiplier";case TrainerFeature.RunResources:return "Healing and clock";
                case TrainerFeature.Gold:return "Gold";case TrainerFeature.FreeRerolls:return "Free rerolls";case TrainerFeature.Shield:return "Shield";
                case TrainerFeature.Banishes:return "Banishes";case TrainerFeature.Stats:return "Stat minimums";case TrainerFeature.HealthFloor:return "Maximum-health floor";
                case TrainerFeature.AutoPick:return "Auto-pick";case TrainerFeature.CoopAutoPick:return "Co-op auto-pick";case TrainerFeature.XpGrant:return "XP grants";case TrainerFeature.Lives:return "Remaining lives";case TrainerFeature.ClassMarks:return "Class marks";default:return "Class talent points";
            }
        }
        internal string UnavailableNames {get{return String.Join("; ",unavailable.Keys.Select(FeatureName).ToArray());}}
        internal bool HasSite(string name){return discovery.Sites.ContainsKey(name);}
        internal HookSite Site(string name){return discovery.Sites[name];}
        internal byte[] Frame(HookSite site){return UnwindSupport.Normalize(discovery.Image.UnwindInfo(discovery.Image.FunctionAt(site.Rva)));}
        internal int Rva(string function){return discovery.Functions[function].Function.Start;}
        internal RarityRegisters Roll(string name){return discovery.Rarity[name];}
        internal bool HasField(string name){try{Field(name);return true;}catch{return false;}}
        internal int Field(string name)
        {
            Dictionary<string,int> witnesses;Need(discovery.Fields.TryGetValue(name,out witnesses),name+": no decoded field witness.");
            var live=witnesses.Where(w=>Valid(w.Key)).ToArray();Need(live.Length>0,name+": no verified field witness remains.");
            int offset=live[0].Value;Need(live.All(w=>w.Value==offset),name+": decoded field witnesses disagree ("+String.Join(", ",live.Select(w=>w.Key+"=0x"+w.Value.ToString("x")).ToArray())+").");
            int width=Width(name);Need(offset>=0x100&&offset+width<=0x10000&&offset%width==0,name+": decoded field bounds/alignment failed.");
            foreach(var other in discovery.Fields)
            {
                if(other.Key==name||other.Key=="attributes"||name=="attributes"||Server(other.Key)!=Server(name))continue;
                var values=other.Value.Where(w=>Valid(w.Key)).Select(w=>w.Value).Distinct().ToArray();if(values.Length!=1)continue;
                Need((long)offset>=values[0]+Width(other.Key)||(long)values[0]>=offset+width,name+": decoded field overlaps "+other.Key+".");
            }
            return offset;
        }
        static bool Server(string name){return name=="profile"||name.StartsWith("marks")||name.StartsWith("talent");}
        static int Width(string name){return name=="offers"||name=="offerCount"||Server(name)?8:name=="phase"||name=="coop"||name=="coopPhase"||name=="offerKind"?1:4;}
        internal int StatField(int reference){int relative=checked(reference-0x560);Need(relative>=0&&relative+4<=0xd0,"Unknown stat member.");return checked(Field("attributes")+relative);}
        internal void ValidateLive(IntPtr handle,IntPtr imageBase)
        {
            var image=discovery.Image;long delta=imageBase.ToInt64()-image.PreferredBase;
            foreach(var entry in discovery.Functions)
            {
                var f=entry.Value.Function;var ranges=new Dictionary<int,int>(entry.Value.Dependencies);ranges[f.Start]=f.Length;
                foreach(var range in ranges)
                {
                    byte[] expected=image.Read(range.Key,range.Value);
                    foreach(int at in image.Relocations.Where(r=>r>=range.Key&&r+8<=range.Key+range.Value))Array.Copy(BitConverter.GetBytes(unchecked(BitConverter.ToInt64(expected,at-range.Key)+delta)),0,expected,at-range.Key,8);
                    try{if(!Native.Equal(expected,Native.Read(handle,Native.Add(imageBase,range.Key),range.Value)))invalid[entry.Key]="Running instructions or a register-liveness dependency differ from the recognized executable; disable other trainers or restart the game.";}
                    catch(Exception ex){invalid[entry.Key]="Cannot validate running instructions: "+ex.Message;}
                }
            }
            Evaluate();RequireAny();
        }
        internal string Summary {get{return Name+"; "+AvailableCount+"/"+dependencies.Count+" independent capabilities available; "+discovery.Functions.Count+" functions and "+discovery.Sites.Count+" sites recognized. SHA-256 "+Hash;}}
        internal string Details {get{return Summary+Environment.NewLine+String.Join(Environment.NewLine,dependencies.Keys.Select(f=>FeatureName(f)+": "+(Supports(f)?"available":"UNAVAILABLE: "+unavailable[f])).ToArray())+Environment.NewLine+"Decoded fields:"+Environment.NewLine+String.Join(Environment.NewLine,discovery.Fields.Select(f=>f.Key+": "+String.Join(", ",f.Value.Select(w=>w.Key+" +0x"+w.Value.ToString("x")+(Valid(w.Key)?"":" (unavailable)")).ToArray())).ToArray())+Environment.NewLine+"Recognition failures:"+Environment.NewLine+String.Join(Environment.NewLine,discovery.Failures.Select(f=>f.Key+": "+f.Value).ToArray());}}
    }
}
