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
        internal FunctionSpec(string name,int size,string hash,string masks,string frame)
        {Name=name;Size=size;Hash=hash;Frame=frame;Masks=String.IsNullOrEmpty(masks)?new int[0]:Array.ConvertAll(masks.Split(','),Int32.Parse);}
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
            return GameImage.Digest(UnwindSupport.Normalize(image.UnwindInfo(function)))==Frame;
        }
    }
    internal sealed class HookSite
    {
        internal readonly int Rva,Continuation,Guard;
        internal int TailRegister=-1;
        // RAX is live at the site, so the RAX-based absolute entry is unsafe.
        internal bool NearOnly;
        internal readonly byte[] Original;
        internal HookSite(GameImage image,ImageFunction function,int start,int size,int continuation,int guard)
        {
            Rva=start;Continuation=continuation;Guard=guard;
            long end=(long)function.Start+function.Length;
            if(start<function.Start||size<=0||(long)start+size>end||guard<size||(long)start+guard>end||continuation<function.Start||continuation>=end)throw new Exception("Invalid hook context.");
            Original=image.Read(start,size);
        }
    }
}
