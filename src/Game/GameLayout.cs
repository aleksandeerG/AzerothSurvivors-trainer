using System;
using System.Collections.Generic;
using System.Linq;
namespace AzerothTrainer
{
    internal sealed class FunctionSpec
    {
        internal readonly string Name,Hash,Frame;
        internal readonly int Size;
        internal readonly int[] Masks;
        internal readonly FieldOperand[] Operands;
        internal readonly FunctionSpec Previous;
        internal FunctionSpec(string name,int size,string hash,string masks,string frame,FunctionSpec previous=null,string operands="")
        {Name=name;Size=size;Hash=hash;Frame=frame;Masks=String.IsNullOrEmpty(masks)?new int[0]:Array.ConvertAll(masks.Split(','),Int32.Parse);Previous=previous;Operands=String.IsNullOrEmpty(operands)?new FieldOperand[0]:Array.ConvertAll(operands.Split(','),s=>new FieldOperand(s));}
        internal bool Matches(GameImage image,ImageFunction function,MemoryContracts memory)
        {
            if(function.Length!=Size)return false;
            byte[] body=image.Read(function.Start,Size),normalized=(byte[])body.Clone();
            foreach(int at in Masks){if(at<0||at+4>body.Length)throw new Exception("Invalid signature mask.");Array.Clear(normalized,at,4);}
            var discovered=new Dictionary<string,int>();
            foreach(var operand in Operands)
            {
                if(operand.Position<0||operand.Position+4>body.Length)throw new Exception("Invalid memory operand position.");
                int displacement=BitConverter.ToInt32(body,operand.Position),previous;
                if(!memory.Valid(operand,displacement))return false;
                int start=checked(displacement-operand.Relative);
                if(discovered.TryGetValue(operand.Group,out previous)&&previous!=start)return false;
                discovered[operand.Group]=start;Array.Clear(normalized,operand.Position,4);
            }
            if(GameImage.Digest(normalized)!=Hash)return false;
            foreach(int at in Masks)
            {
                if(at<0||at+4>body.Length)throw new Exception("Invalid signature mask.");
                int target=checked(function.Start+at+4+BitConverter.ToInt32(body,at));
                bool branch=at>0&&(body[at-1]==0xe8||body[at-1]==0xe9||(at>1&&body[at-2]==0x0f&&(body[at-1]&0xf0)==0x80));
                if(branch&&!image.Executable(target,1))return false;
                if(!branch&&!image.Mapped(target))return false; // Includes zero-initialized virtual data.
            }
            return GameImage.Digest(UnwindSupport.FrameFor(function.Start,image.Path))==Frame;
        }
    }
    internal sealed class SiteSpec
    {
        internal readonly string Name,Function;
        internal readonly int Offset,Size,Continuation,Guard;
        internal SiteSpec(string name,string function,int offset,int size,int continuation=0,int guard=0)
        {Name=name;Function=function;Offset=offset;Size=size;Continuation=continuation==0?offset+size:continuation;Guard=guard==0?size:guard;}
    }
    internal sealed class HookSite
    {
        internal readonly int Rva,Continuation,Guard;
        internal readonly byte[] Original;
        internal HookSite(GameImage image,ImageFunction function,SiteSpec spec)
        {
            Rva=checked(function.Start+spec.Offset);Continuation=checked(function.Start+spec.Continuation);Guard=spec.Guard;
            if(spec.Offset<0||spec.Size<=0||spec.Offset+spec.Size>function.Length||spec.Offset+spec.Guard>function.Length||spec.Continuation<0||spec.Continuation>function.Length)throw new Exception("Invalid hook context.");
            Original=image.Read(Rva,spec.Size);
        }
    }
    internal sealed class LayoutSpec
    {
        internal readonly string Name;
        internal readonly bool Updated;
        internal readonly FunctionSpec[] Functions;
        internal readonly SiteSpec[] Sites;
        internal LayoutSpec(string name,bool updated,FunctionSpec[] functions,SiteSpec[] sites){Name=name;Updated=updated;Functions=functions;Sites=sites;}
    }
    internal enum TrainerFeature { SpellChance,ItemChance,RerollCost,RunControls,ClassProgression }
    internal sealed class GameLayout
    {
        sealed class MatchedFunction
        {
            internal ImageFunction Range;internal byte[] Body;internal int[] Relocations;
            internal FunctionSpec Signature;internal LayoutSpec Source;
            internal readonly HashSet<string> Origins=new HashSet<string>();
        }
        readonly Dictionary<string,MatchedFunction> functions=new Dictionary<string,MatchedFunction>();
        readonly Dictionary<string,HookSite> sites=new Dictionary<string,HookSite>();
        readonly Dictionary<string,string> failures=new Dictionary<string,string>();
        readonly Dictionary<TrainerFeature,string> unavailable=new Dictionary<TrainerFeature,string>();
        static readonly Dictionary<TrainerFeature,string[]> requirements=new Dictionary<TrainerFeature,string[]>{
            {TrainerFeature.SpellChance,new[]{"Spell"}},
            {TrainerFeature.ItemChance,new[]{"Jewelry"}},
            {TrainerFeature.RerollCost,new[]{"Price0","Hud","Price2","Price3","Price4"}},
            {TrainerFeature.RunControls,new[]{"Run","Coop","Xp","Health","Choose"}},
            {TrainerFeature.ClassProgression,new[]{"Profile","ProfileActions","EnsureClass","SaveProfile","SerializeProfile","GrowMarks"}}
        };
        readonly MemoryContracts memory;
        readonly long preferredBase;
        internal readonly string Hash,Name;
        internal bool Updated {get{return functions.ContainsKey("Health")&&functions["Health"].Source.Updated;}}
        GameLayout(GameImage image)
        {
            Hash=image.Hash;preferredBase=image.PreferredBase;memory=new MemoryContracts();
            foreach(string name in BuildCatalog.Layouts.SelectMany(l=>l.Functions).Select(f=>f.Name).Distinct())
            {
                MatchedFunction found=null;bool ambiguous=false;
                foreach(var layout in BuildCatalog.Layouts)
                {
                    var signature=layout.Functions.FirstOrDefault(f=>f.Name==name);if(signature==null)continue;
                    for(int index=0;index<image.Functions.Count;index++)
                    {
                        var candidate=image.Functions[index];
                        try
                        {
                            if(!signature.Matches(image,candidate,memory))continue;
                            if(signature.Previous!=null&&(index==0||!signature.Previous.Matches(image,image.Functions[index-1],memory)))continue;
                        }
                        catch(Exception ex){failures[name]="unverifiable context: "+ex.Message;continue;}
                        if(found!=null)
                        {
                            if(found.Range.Start!=candidate.Start||!Equivalent(found,layout,signature)){ambiguous=true;continue;}
                            found.Origins.Add(layout.Name);continue;
                        }
                        found=new MatchedFunction{Range=candidate,Body=image.Read(candidate.Start,candidate.Length),Signature=signature,Source=layout,
                            Relocations=image.Relocations.Where(r=>r>=candidate.Start&&r+8<=candidate.Start+candidate.Length).Select(r=>r-candidate.Start).ToArray()};
                        found.Origins.Add(layout.Name);
                    }
                }
                if(ambiguous){failures[name]="ambiguous function signature or hook contract";continue;}
                if(found==null){if(!failures.ContainsKey(name))failures[name]="code, member spacing or stack layout has changed";continue;}
                functions.Add(name,found);memory.Observe(name,found.Signature.Operands,found.Body);
                foreach(var site in found.Source.Sites.Where(s=>s.Function==name))sites.Add(site.Name,new HookSite(image,found.Range,site));
            }
            var origins=new HashSet<string>(BuildCatalog.Layouts.Select(l=>l.Name));
            foreach(var function in functions.Values)origins.IntersectWith(function.Origins);
            Name=origins.Count==0?"Discovered desktop contracts":origins.OrderBy(s=>s).First();
            // Explicitly tie the injected callbacks to the game's existing call
            // graph. Matching a byte sequence elsewhere must not select a helper.
            foreach(var feature in requirements)
            {
                var missing=feature.Value.Where(name=>!functions.ContainsKey(name)).ToArray();
                if(missing.Length!=0){unavailable[feature.Key]=String.Join(", ",missing.Select(name=>name+": "+failures[name]).ToArray());continue;}
                try
                {
                    if(feature.Key==TrainerFeature.RunControls)
                    {
                        ValidateEdge("Xp","Health");ValidateEdge("Run","Coop");
                        memory.Require("Attributes","Offers","Clock","OfferNumber","Banishes","Phase","Coop","OfferKind","CoopPhase");
                    }
                    if(feature.Key==TrainerFeature.ClassProgression)
                    {
                        ValidateEdge("Profile","ProfileActions");ValidateEdge("ProfileActions","EnsureClass");
                        ValidateEdge("ProfileActions","SaveProfile");ValidateEdge("SaveProfile","SerializeProfile");
                        memory.Require("Profile");
                    }
                }
                catch(Exception ex){unavailable[feature.Key]=ex.Message;}
            }
        }
        static bool Equivalent(MatchedFunction found,LayoutSpec layout,FunctionSpec signature)
        {
            var previous=found.Signature;
            if(previous.Hash!=signature.Hash||previous.Size!=signature.Size||previous.Frame!=signature.Frame||!previous.Masks.SequenceEqual(signature.Masks))return false;
            Func<FieldOperand,string> operand=o=>o.Group+":"+o.Position+":"+o.Relative+":"+o.Width;
            if(!previous.Operands.Select(operand).SequenceEqual(signature.Operands.Select(operand)))return false;
            if(signature.Name=="Health"&&found.Source.Updated!=layout.Updated)return false;
            Func<SiteSpec,string> site=s=>s.Name+":"+s.Offset+":"+s.Size+":"+s.Continuation+":"+s.Guard;
            return found.Source.Sites.Where(s=>s.Function==signature.Name).Select(site).SequenceEqual(layout.Sites.Where(s=>s.Function==signature.Name).Select(site));
        }
        void ValidateEdge(string caller,string callee)
        {
            var source=functions[caller];int target=functions[callee].Range.Start;
            // Relative call/jump operands are the only masked E8/E9 references.
            var signature=Template(caller);
            foreach(int displacement in signature.Masks)
                if(displacement>0&&(source.Body[displacement-1]==0xe8||source.Body[displacement-1]==0xe9)&&source.Range.Start+displacement+4+BitConverter.ToInt32(source.Body,displacement)==target)return;
            throw new Exception(caller+": expected call to "+callee+" was not found.");
        }
        internal static GameLayout Load(string path)
        {
            var candidate=new GameLayout(new GameImage(path));if(candidate.AvailableCount>0)return candidate;
            var error=new Exception("No trainer features match this game safely. An updated trainer is required. Run --verify-files for details.");
            error.Data["CompatibilityDetails"]=candidate.Details;throw error;
        }
        internal bool HasSite(string name){return sites.ContainsKey(name);}
        internal bool Supports(TrainerFeature feature){return requirements.ContainsKey(feature)&&!unavailable.ContainsKey(feature);}
        internal int AvailableCount {get{return requirements.Count-unavailable.Count;}}
        internal bool Complete {get{return AvailableCount==requirements.Count;}}
        internal static string FeatureName(TrainerFeature feature)
        {
            switch(feature)
            {
                case TrainerFeature.SpellChance:return "Spell chance";
                case TrainerFeature.ItemChance:return "Item chance";
                case TrainerFeature.RerollCost:return "Reroll cost";
                case TrainerFeature.RunControls:return "Run controls, stats and auto-pick";
                default:return "Class progression";
            }
        }
        internal string UnavailableNames {get{return String.Join("; ",unavailable.Keys.Select(FeatureName).ToArray());}}
        internal HookSite Site(string name){return sites[name];}
        internal int Rva(string function){return functions[function].Range.Start;}
        internal FunctionSpec Template(string function){return functions[function].Signature;}
        internal int HealthRegister {get{return Updated?14:6;}}
        internal int Field(string name){return memory.Field(name);}
        internal int StatField(int reference){return memory.StatField(reference);}
        internal void ValidateLive(IntPtr handle,IntPtr imageBase)
        {
            long delta=imageBase.ToInt64()-preferredBase;
            foreach(var entry in functions)
            {
                var function=entry.Value;byte[] expected=(byte[])function.Body.Clone();
                foreach(int offset in function.Relocations)Array.Copy(BitConverter.GetBytes(unchecked(BitConverter.ToInt64(expected,offset)+delta)),0,expected,offset,8);
                if(!Native.Equal(expected,Native.Read(handle,Native.Add(imageBase,function.Range.Start),expected.Length)))throw new Exception(entry.Key+": running game code differs from the validated executable. Disable other trainers or restart the game.");
            }
        }
        internal string Summary {get{return Name+"; "+AvailableCount+"/5 feature groups available; "+functions.Count+"/18 function contracts and "+sites.Count+" hook/guard sites validated. SHA-256 "+Hash;}}
        internal string Details
        {
            get{return Summary+Environment.NewLine+String.Join(Environment.NewLine,requirements.Keys.Select(feature=>FeatureName(feature)+": "+(Supports(feature)?"available":"UNAVAILABLE: "+unavailable[feature])).ToArray())+Environment.NewLine+"Discovered memory regions:"+Environment.NewLine+memory.Details+Environment.NewLine+"Function contracts:"+Environment.NewLine+String.Join(Environment.NewLine,functions.Select(f=>f.Key+": RVA 0x"+f.Value.Range.Start.ToString("x")+"; "+String.Join(" / ",f.Value.Origins.OrderBy(n=>n).ToArray())).ToArray());}
        }
    }
}
