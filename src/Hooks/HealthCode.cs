using System;
namespace AzerothTrainer
{
    internal static class HealthCode
    {
        internal static byte[] Build(IntPtr continuation,GameLayout layout)
        {
            var a=new Assembler();
            // Both validated contexts overwrite RAX before its next read,
            // with no intervening branch. Preserve the live arithmetic
            // flags (including OF) without pushes or changes to the frame.
            a.Emit("9F 0F 90 C0"); // lahf; seto al
            a.Emit("83 3D");a.Bytes.AddRange(BitConverter.GetBytes(4100-(a.Bytes.Count+5)));a.Emit("00"); // cmp dword [rip+enabled],0
            a.Branch("0F 84","original");
            a.SseRip(0x5f,layout.HealthRegister,4096); // maxss decoded calculated-max register,[rip+minimum]
            a.Label("original");a.Emit("04 7F 9E"); // add al,7F (restore OF); sahf
            // Floor the calculated maximum before its delta and HP clamp,
            // preserving current HP through level-ups and offer selection.
            a.Bytes.AddRange(layout.Site("HealthHook").Original);a.Bytes.AddRange(RarityCode.TailJump(continuation,layout.Site("HealthHook").TailRegister));return a.Finish();
        }
    }
}
