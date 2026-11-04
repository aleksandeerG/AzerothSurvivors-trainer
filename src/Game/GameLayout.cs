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
        internal readonly FunctionSpec Previous;
        internal FunctionSpec(string name,int size,string hash,string masks,string frame,FunctionSpec previous=null)
        {Name=name;Size=size;Hash=hash;Frame=frame;Masks=String.IsNullOrEmpty(masks)?new int[0]:Array.ConvertAll(masks.Split(','),Int32.Parse);Previous=previous;}
        internal bool Matches(GameImage image,ImageFunction function)
        {
            if(function.Length!=Size)return false;
            byte[] body=image.Read(function.Start,Size),normalized=(byte[])body.Clone();
            foreach(int at in Masks){if(at<0||at+4>body.Length)throw new Exception("Invalid signature mask.");Array.Clear(normalized,at,4);}
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
    internal sealed class GameLayout
    {
        sealed class MatchedFunction
        {
            internal ImageFunction Range;internal byte[] Body;internal int[] Relocations;
        }
        readonly Dictionary<string,MatchedFunction> functions=new Dictionary<string,MatchedFunction>();
        readonly Dictionary<string,HookSite> sites=new Dictionary<string,HookSite>();
        readonly LayoutSpec spec;
        readonly long preferredBase;
        internal readonly string Hash,Name;
        internal bool Updated {get{return spec.Updated;}}
        GameLayout(GameImage image,LayoutSpec layout)
        {
            spec=layout;Name=layout.Name;Hash=image.Hash;preferredBase=image.PreferredBase;
            foreach(var signature in layout.Functions)
            {
                ImageFunction found=null;
                for(int index=0;index<image.Functions.Count;index++)
                {
                    var candidate=image.Functions[index];
                    if(!signature.Matches(image,candidate))continue;
                    if(signature.Previous!=null&&(index==0||!signature.Previous.Matches(image,image.Functions[index-1])))continue;
                    if(found!=null)throw new Exception(signature.Name+": ambiguous function signature.");
                    found=candidate;
                }
                if(found==null)throw new Exception(signature.Name+": code or stack layout has changed.");
                functions.Add(signature.Name,new MatchedFunction{Range=found,Body=image.Read(found.Start,found.Length),Relocations=image.Relocations.Where(r=>r>=found.Start&&r+8<=found.Start+found.Length).Select(r=>r-found.Start).ToArray()});
            }
            foreach(var site in layout.Sites)sites.Add(site.Name,new HookSite(image,functions[site.Function].Range,site));
            // Explicitly tie the injected callbacks to the game's existing call
            // graph. Matching a byte sequence elsewhere must not select a helper.
            ValidateEdge("Xp","Health");ValidateEdge("Run","Coop");ValidateEdge("Profile","ProfileActions");
            ValidateEdge("ProfileActions","EnsureClass");ValidateEdge("ProfileActions","SaveProfile");ValidateEdge("SaveProfile","SerializeProfile");
        }
        void ValidateEdge(string caller,string callee)
        {
            var source=functions[caller];int target=functions[callee].Range.Start;
            // Relative call/jump operands are the only masked E8/E9 references.
            var signature=spec.Functions.First(f=>f.Name==caller);
            foreach(int displacement in signature.Masks)
                if(displacement>0&&(source.Body[displacement-1]==0xe8||source.Body[displacement-1]==0xe9)&&source.Range.Start+displacement+4+BitConverter.ToInt32(source.Body,displacement)==target)return;
            throw new Exception(caller+": expected call to "+callee+" was not found.");
        }
        internal static GameLayout Load(string path)
        {
            var image=new GameImage(path);var failures=new List<string>();
            foreach(var layout in BuildCatalog.Layouts)
            {
                try{return new GameLayout(image,layout);}
                catch(Exception ex){failures.Add(layout.Name+": "+ex.Message);}
            }
            throw new Exception("Game code or memory layout is not recognized. Updated compatibility signatures are required. "+String.Join(" | ",failures.ToArray()));
        }
        internal HookSite Site(string name){return sites[name];}
        internal int Rva(string function){return functions[function].Range.Start;}
        internal int HealthRegister {get{return Updated?14:6;}}
        internal int Field(string name)
        {
            switch(name)
            {
                case "hp":return Updated?0x5f8:0x568;
                case "xp":return Updated?0x5f4:0x564;
                case "shield":return Updated?0x5fc:0x56c;
                case "gold":return Updated?0x608:0x578;
                case "maxHp":return Updated?0x658:0x5c8;
                case "rerolls":return Updated?0x6bc:0x62c;
                case "banishes":return Updated?0x10b4:0xf30;
                case "phase":return Updated?0x10e9:0xf63;
                case "coop":return Updated?0x10e3:0xf5e;
                case "offerKind":return Updated?0x10e7:0xf61;
                case "coopPhase":return Updated?0x10ea:0xf64;
                case "offers":return Updated?0x860:0x738;
                case "offerCount":return Updated?0x868:0x740;
                case "offerNumber":return Updated?0x1054:0xecc;
                case "clock":return Updated?0x1028:0xea0;
                case "profile":return Updated?0x15f0:0x1400;
                case "talentData":return Field("profile")+0x68;
                case "talentCount":return Field("profile")+0x70;
                case "marksCapacity":return Field("profile")+0x180;
                case "marksData":return Field("profile")+0x188;
                case "marksCount":return Field("profile")+0x190;
                default:throw new Exception("Unknown game field: "+name);
            }
        }
        internal int StatField(int reference){return Updated?reference+0x90:reference;}
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
        internal string Summary {get{return Name+"; "+functions.Count+" function signatures, 17 patch sites and 2 guards validated. SHA-256 "+Hash;}}
    }
}
