using System;
namespace AzerothTrainer
{
    // Reuse verified scratch slots in the completed original frame. No pushes,
    // extra stack frame, or nonvolatile-register changes are added. XMM5 uses
    // RSP+20: every injected call has <=4 register arguments. The verified run
    // frames keep live UnwindHelp at RSP+D8 or RSP+E8; neither is scratch space.
    internal static class FrameCode
    {
        internal static void Save(Assembler a)
        {
            a.Emit("48 89 44 24 40 48 89 4C 24 48 48 89 54 24 50 4C 89 44 24 58 4C 89 4C 24 60 4C 89 54 24 68 4C 89 5C 24 70"); // mov [rsp+40..70],rax,rcx,rdx,r8..r11
            for(int i=0;i<6;i++){a.Emit("0F 11");a.Bytes.Add((byte)(0x84+i*8));a.Emit("24");a.Bytes.AddRange(BitConverter.GetBytes(i==5?0x20:0x80+i*16));} // movups [rsp+slot],xmm0..5
        }
        internal static void Restore(Assembler a)
        {
            for(int i=0;i<6;i++){a.Emit("0F 10");a.Bytes.Add((byte)(0x84+i*8));a.Emit("24");a.Bytes.AddRange(BitConverter.GetBytes(i==5?0x20:0x80+i*16));} // movups xmm0..5,[rsp+slot]
            a.Emit("48 8B 44 24 40 48 8B 4C 24 48 48 8B 54 24 50 4C 8B 44 24 58 4C 8B 4C 24 60 4C 8B 54 24 68 4C 8B 5C 24 70"); // mov rax,rcx,rdx,r8..r11,[rsp+40..70]
        }
        internal static void Pointer(Assembler a,IntPtr pointer){a.Emit("48 B8");a.Bytes.AddRange(BitConverter.GetBytes(pointer.ToInt64()));}
        internal static void Call(Assembler a,IntPtr target){a.Emit("49 BB");a.Bytes.AddRange(BitConverter.GetBytes(target.ToInt64()));a.Emit("41 FF D3");}
    }
    internal static class SpeedCode
    {
        internal static void Emit(Assembler a)
        {a.Rip("F3 0F 59 0D",4096);a.Emit("0F 28 F1 48 89 CE");} // mulss xmm1,speed; movaps xmm6,xmm1; mov rsi,rcx
    }
    internal static class XpCode
    {
        internal const int CarryOffset=64,CarrySlots=32,CarryStride=32;
        internal static int ScaleUnits(double multiplier)
        {
            if(Double.IsNaN(multiplier)||Double.IsInfinity(multiplier)||multiplier<0.1||multiplier>100)throw new ArgumentOutOfRangeException("XP multiplier");
            return (int)Math.Round(multiplier*1000,MidpointRounding.AwayFromZero);
        }
        internal static byte[] Build(IntPtr target,GameLayout layout)
        {
            var a=new Assembler(layout);
            // Integer thousandths preserve every fraction across awards. Separate
            // owner slots prevent interleaved heroes from sharing a remainder.
            a.Emit("85 D2");a.Branch("0F 84","original"); // test edx,edx: zero award keeps fractional carry
            a.Rip("4C 8D 1D",4096+CarryOffset);a.Emit("B8 20 00 00 00"); // lea r11,carry; mov eax,32
            a.Label("find");a.Emit("49 39 0B");a.Branch("0F 84","found"); // cmp [r11],rcx (owner)
            a.Emit("49 83 3B 00");a.Branch("0F 84","reset");a.Emit("49 83 C3 20 FF C8");a.Branch("0F 85","find"); // empty slot, or add r11,32; dec eax
            a.Rip("4C 8D 1D",4096+CarryOffset);a.Branch("E9","reset");
            a.Label("found");a.Rip("44 8B 05",4096);a.Emit("45 3B 43 10");a.Branch("0F 85","reset"); // mov r8d,multiplier; cmp r8d,[r11+10]
            a.Emit("F3 0F 10 81 $clock 41 0F 2E 43 14");a.Branch("0F 82","reset");a.Branch("E9","scale"); // movss xmm0,clock; ucomiss xmm0,lastClock
            a.Label("reset");a.Rip("44 8B 05",4096);a.Emit("49 89 0B 49 C7 43 08 00 00 00 00 45 89 43 10");
            a.Label("scale");a.Emit("F3 0F 10 81 $clock F3 41 0F 11 43 14");
            a.Emit("89 D0 49 0F AF C0 49 03 43 08 31 D2 41 B8 E8 03 00 00 49 F7 F0 49 89 53 08"); // mov eax,edx; imul rax,r8; add rax,carry; xor edx,edx; div r8(1000); store remainder
            // Avoid wrapping the game's 32-bit current-XP addition at large awards.
            a.Emit("44 8B 91 $xp 41 F7 D2 4C 39 D0 49 0F 47 C2 89 C2"); // mov r10d,currentXP; not r10d; cmp rax,r10; cmova rax,r10; mov edx,eax
            a.Label("original");a.Bytes.AddRange(layout.Site("XpHook").Original);a.Bytes.AddRange(RarityCode.TailJump(target));return a.Finish();
        }
    }
    internal static class ProfileCode
    {
        internal const int TalentRequest=0,MarksRequest=8,TalentResult=16,MarksResult=24;
        internal static ulong PackRequest(uint sequence,int classId,int value)
        {if(sequence==0||classId<1||classId>255||value<0||value>0xffffff)throw new ArgumentOutOfRangeException("request");return ((ulong)sequence<<32)|((uint)classId<<24)|(uint)value;}
        internal static int CompletedStatus(ulong acknowledgement,uint expected)
        {return expected!=0&&(uint)(acknowledgement>>32)==expected?(int)(acknowledgement&0xffffffff):0;}
        static void Ack(Assembler a,int offset,int code)
        {
            a.Emit("4C 8B 94 24 08 01 00 00 49 C1 EA 20 49 C1 E2 20 49 83 CA "+code.ToString("X2")); // request sequence in high word; OR result into low word
            a.Emit("48 8B 84 24 00 01 00 00 4C 89 50 "+offset.ToString("X2")); // reload data pointer; mov [rax+result],r10
        }
        internal static byte[] Build(IntPtr start,IntPtr target,IntPtr image,GameLayout layout)
        {
            var a=new Assembler(layout);a.Bytes.AddRange(layout.Site("ProfileHook").Original);FrameCode.Save(a);
            FrameCode.Pointer(a,Native.Add(start,4096));a.Emit("48 89 84 24 00 01 00 00"); // data pointer
            a.Emit("49 BA FF FF FF FF FF FF FF FF 4C 87 10 49 83 FA FF");a.Branch("0F 84","marks"); // mov r10,-1; xchg [rax],r10; cmp r10,-1
            a.Emit("4C 89 94 24 08 01 00 00"); // atomic class/point request
            a.Emit("4C 89 D2 48 C1 EA 18 48 8B 4C 24 48 48 81 C1 $profile 45 31 C0"); // class -> DL; RCX=server profile; R8D=0 award
            FrameCode.Call(a,Native.Add(image,layout.Rva("EnsureClass"))); // Ensure a class record exists, using the game's allocator.
            a.Emit("48 8B 4C 24 48 4C 8B 94 24 08 01 00 00 4C 89 D2 48 C1 EA 18 41 81 E2 FF FF FF 00 4C 8B 81 $talentData 4C 8B 89 $talentCount");
            a.Label("talentLoop");a.Emit("4D 85 C9");a.Branch("0F 84","talentMissing");
            a.Emit("41 38 50 04");a.Branch("0F 84","talentFound");a.Emit("49 83 C0 28 49 FF C9");a.Branch("E9","talentLoop");
            a.Label("talentFound"); // Sum allocated ranks before lowering the earned-point budget.
            a.Emit("4D 8B 48 10 49 8B 50 18 45 31 DB"); // mov r9,allocated ranks; mov rdx,length; xor r11d,r11d
            a.Label("spentLoop");a.Emit("48 85 D2");a.Branch("0F 84","spentDone");
            a.Emit("41 0F B6 49 08 41 01 CB 49 83 C1 10 48 FF CA");a.Branch("E9","spentLoop"); // movzx ecx,rank; add r11d,ecx; add r9,16; dec rdx
            a.Label("spentDone");a.Emit("45 39 DA");a.Branch("0F 82","talentTooLow");
            // XP threshold for p earned points: (15*p*p + 145*p)/2, capped at 51.
            a.Emit("44 89 D1 41 0F AF CA 6B C9 0F 45 69 D2 91 00 00 00 44 01 D1 D1 E9 41 89 08");
            a.Branch("E9","saveTalent");
            a.Label("talentTooLow");Ack(a,TalentResult,2);a.Branch("E9","marks");
            a.Label("talentMissing");Ack(a,TalentResult,3);a.Branch("E9","marks");
            a.Label("saveTalent");a.Emit("48 8B 4C 24 48 48 81 C1 $profile");FrameCode.Call(a,Native.Add(image,layout.Rva("SaveProfile")));Ack(a,TalentResult,1);
            a.Label("marks");a.Emit("48 8B 84 24 00 01 00 00 49 BA FF FF FF FF FF FF FF FF 4C 87 50 08 49 83 FA FF");a.Branch("0F 84","done");
            a.Emit("4C 89 94 24 08 01 00 00 4C 89 D2 48 C1 EA 18 41 81 E2 FF FF FF 00 48 8B 4C 24 48 4C 8B 81 $marksData 4C 8B 89 $marksCount 45 31 DB");
            a.Label("marksLoop");a.Emit("4D 39 CB");a.Branch("0F 83","marksNew");
            a.Emit("43 38 14 D8");a.Branch("0F 84","marksFound");a.Emit("49 FF C3");a.Branch("E9","marksLoop");
            a.Label("marksNew");a.Emit("4C 3B 89 $marksCapacity");a.Branch("0F 82","marksSpace");
            a.Emit("48 81 C1 $marksCapacity");FrameCode.Call(a,Native.Add(image,layout.Rva("GrowMarks")));
            a.Emit("48 8B 4C 24 48 4C 8B 81 $marksData 4C 8B 99 $marksCount 4C 8B 94 24 08 01 00 00 4C 89 D2 48 C1 EA 18 41 81 E2 FF FF FF 00");
            a.Label("marksSpace");a.Emit("4B C7 04 D8 00 00 00 00 43 88 14 D8 48 FF 81 $marksCount");
            a.Label("marksFound");a.Emit("47 89 54 D8 04"); // mov [r8+r11*8+4],r10d (marks in tenths)
            a.Emit("48 8B 4C 24 48 48 81 C1 $profile");FrameCode.Call(a,Native.Add(image,layout.Rva("SaveProfile")));Ack(a,MarksResult,1);
            a.Label("done");FrameCode.Restore(a);a.Bytes.AddRange(RarityCode.TailJump(target));return a.Finish();
        }
    }
}
