namespace AzerothTrainer
{
    // Permanent-edit helpers remain strict: instruction bytes, formulas,
    // record access and unwind layout. Only PC-relative addresses are masked.
    internal static class BuildCatalog
    {
        internal static readonly FunctionSpec[] Functions={
            new FunctionSpec("EnsureClass",443,"afa521ff08ea41cdb6c898a4c1262873b9092c7dea181e2fb89269ca006540f4","419","9c9315d533b3dae94b4ac9966178413d32a1b32e3d8600f0f6334e030b494038"),
            new FunctionSpec("GrowMarks",107,"2836e7326f7614dad48b2d3203aad60b38b716f32e35d40fbf9b3a197f4d6842","60,101","5bcb0710563470dce857126de4fc8e625f02de21d23526261a659891aa2ff3d8"),
            new FunctionSpec("EnsureClass",432,"cfd380e8aa34110147318ff9c27f4a2a1042609d80f30d2263a3b4b2edc01538","225","9c9315d533b3dae94b4ac9966178413d32a1b32e3d8600f0f6334e030b494038"),
            new FunctionSpec("WriteCharacters",316,"5520919b14091d3bd6ea54ae1595933c7bc89790b664528400626e990f0b8488","32,70,95,139,158,195,214,225,234,277,302","552824f57dc22e213491506bfe8954e8934cf01ff5fc431534ad0f232c91be61"),
            new FunctionSpec("CharacterPath",208,"aee6041ed47fb2ae22d2e670fc918b2c5a1c1be53e48f599e2efb354c92a126f","31,46,87,125,138,187","564e7df6ee354a2e66ba7a69dcf2e0be4243640b3338e381477b7bbda70f5fe8"),
            new FunctionSpec("SaveRoot",488,"cf3f1ce862ee887485e65bfc81c167edfec94d048a1b29d3b6e365cb6c9e6dc9","31,46,77,102,112,127,144,159,197,210,268,288,303,345,358,383,396,421,445","a3227a549a79937f0f2634a3a1aecfa9de4a4736cea57dc742256af72349d44a"),
        };
    }
}
