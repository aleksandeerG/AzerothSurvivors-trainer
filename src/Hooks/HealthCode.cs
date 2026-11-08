using System;
namespace AzerothTrainer
{
    internal static class HealthCode
    {
        internal static byte[] Build(IntPtr continuation,GameLayout layout)
        {
            var a=new Assembler();
            // The floor is written before the entry jump and the hook is
            // removed when disabled, so no gate is needed. Flags, RAX and the
            // stack stay untouched; the site may keep flags and RAX live.
            a.SseRip(0x5f,layout.HealthRegister,4096); // maxss decoded calculated-max register,[rip+minimum]
            // Floor the calculated maximum before its delta and HP clamp,
            // preserving current HP through level-ups and offer selection.
            a.Bytes.AddRange(layout.Site("HealthHook").Original);a.Bytes.AddRange(RarityCode.TailJump(continuation,layout.Site("HealthHook").TailRegister));return a.Finish();
        }
    }
}
