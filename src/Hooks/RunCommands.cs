using System;
namespace AzerothTrainer
{
    internal static class RunCommands
    {
        internal const int Owner=96,LastClock=104,Status=112,Epoch=116;
        internal static readonly int[] Offsets={RunCode.BanishRequest,RunCode.GoldRequest,RunCode.FreeRerollRequest,RunCode.ShieldRequest,RunCode.HealRequest,RunCode.XpRequest,RunCode.AdvanceRequest};

        internal static ulong Pack(ulong status,uint value)
        {
            if((uint)status!=1)throw new Exception("Enter a live run before sending a run action.");
            return (status&0xffffffff00000000UL)|value;
        }

        internal static void Track(Assembler a)
        {
            // RSI = Game; RAX = data. Active states are running/offer (1/2).
            a.Emit("F3 0F 10 86 $clock"); // movss xmm0,[rsi+clock]
            a.Emit("44 0F B6 96 $phase 41 FF CA 41 83 FA 01"); // movzx r10d,state; dec r10d; cmp r10d,1
            a.Branch("0F 87","commandsInactive");
            a.Emit("0F 57 D2 F3 0F 10 8E $hp 0F 2E CA"); // xorps xmm2,xmm2; movss xmm1,HP; ucomiss xmm1,xmm2
            a.Branch("0F 86","commandsInactive"); // dead/NaN HP must not consume queued actions
            a.Emit("48 39 70 60");a.Branch("0F 85","commandsEpoch"); // cmp [rax+Owner],rsi
            a.Emit("0F 2E 40 68");a.Branch("0F 82","commandsEpoch"); // ucomiss clock,[rax+LastClock]
            a.Emit("83 78 70 00");a.Branch("0F 84","commandsEpoch"); // previous run inactive
            a.Branch("E9","commandsReady");
            a.Label("commandsEpoch");
            a.Emit("C7 40 70 00 00 00 00 FF 40 74"); // publish inactive before inc dword [rax+Epoch]
            a.Label("commandsReady");
            a.Emit("48 89 70 60 F3 0F 11 40 68 C7 40 70 01 00 00 00"); // owner,clock; publish active
            a.Branch("E9","commandsTracked");
            a.Label("commandsInactive");
            a.Emit("83 78 70 00");a.Branch("0F 84","commandsIdle");
            a.Emit("C7 40 70 00 00 00 00 FF 40 74"); // invalidate all earlier request packets
            a.Label("commandsIdle");a.Emit("48 89 70 60 F3 0F 11 40 68");
            a.Branch("E9","runDone");
            a.Label("commandsTracked");
        }

        internal static void Take(Assembler a,int offset,string absent)
        {
            // Atomically consume (epoch << 32 | value). A transition after the
            // trainer reads Status cannot turn an old request into a new one.
            a.Emit("49 BA FF FF FF FF FF FF FF FF 4C 87 90");a.Bytes.AddRange(BitConverter.GetBytes(offset)); // mov r10,-1; xchg [rax+offset],r10
            a.Emit("49 83 FA FF");a.Branch("0F 84",absent); // cmp r10,-1
            a.Emit("4D 89 D3 49 C1 EB 20 44 3B 58 74");a.Branch("0F 85",absent); // mov r11,r10; shr r11,32; cmp r11d,[rax+Epoch]
        }
    }
}
