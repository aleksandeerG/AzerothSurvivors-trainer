using System;
namespace AzerothTrainer
{
    internal static class HealthCode
    {
        internal const int HookRva=0x5eefd0b;
        // movaps xmm2,xmm6; subss xmm2,[rsi+5C8]; addss xmm10,xmm4.
        internal static readonly byte[] Original=RarityCode.Hex("0F 28 D6 F3 0F 5C 96 C8 05 00 00 F3 44 0F 58 D4");
        internal static byte[] Build(IntPtr continuation)
        {
            var a=new Assembler();
            // RAX is dead here: the next read follows mov rax,[rbp+18] at
            // 5EEFDEC, with no intervening branch. Preserve the live arithmetic
            // flags (including OF) without pushes or changes to the frame.
            a.Emit("9F 0F 90 C0"); // lahf; seto al
            a.Emit("83 3D");a.Bytes.AddRange(BitConverter.GetBytes(4100-(a.Bytes.Count+5)));a.Emit("00"); // cmp dword [rip+enabled],0
            a.Branch("0F 84","original");
            a.Rip("F3 0F 5F 35",4096); // maxss xmm6,[rip+minimum]
            a.Label("original");a.Emit("04 7F 9E"); // add al,7F (restore OF); sahf
            // Floor the calculated maximum before its delta and HP clamp,
            // preserving current HP through level-ups and offer selection.
            a.Bytes.AddRange(Original);a.Bytes.AddRange(RarityCode.TailJump(continuation));return a.Finish();
        }
    }
}
