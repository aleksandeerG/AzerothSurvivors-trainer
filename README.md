# Azeroth Survivors Trainer Plus

A standalone Windows x64 trainer for Azeroth Survivors, created by **[aleksanderG](https://aleksanderg.com/)**.

[More projects](https://aleksanderg.com/) | [Support on Ko-fi](https://ko-fi.com/K3P227JS3D)

Control legendary chances, reroll prices, run speed, XP, resources, automatic offer selection, character stats, and permanent class progression from five tabs. Every option starts off. Connecting alone does not change the selected chances, prices, multipliers, or stat minimums.

## Requirements

- 64-bit Windows with .NET Framework 4.5 or newer (4.8 recommended).
- A desktop game whose runtime code matches the decoded operation patterns. Both reference builds below are supported; permanent edits also require verified save helpers.
- The supported game installed anywhere on your PC. The trainer can run from any folder.
- The trainer and game running as the same Windows user with matching administrator privileges.

Building uses Windows PowerShell and the 64-bit C# compiler shipped with .NET Framework. The first build downloads Iced 1.21.0 from NuGet and verifies its pinned SHA-256. Later builds use the ignored `.local/dependencies` cache. No Visual Studio, .NET SDK, NuGet CLI, Python, or Cheat Engine is required. Game files are not included.

The decoder and its MIT license are embedded in the resulting executable. The trainer stays a single portable file and needs no package downloads at runtime. Decoder source and license: [icedland/iced](https://github.com/icedland/iced).

## Get the source and build

Clone the repository, enter it, and run the build script:

```powershell
git clone https://github.com/aleksandeerG/AzerothSurvivors-trainer.git
Set-Location .\AzerothSurvivors-trainer
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

The script produces **`build\Azeroth Trainer Plus.exe` inside the repository**. Source and output paths are resolved relative to the script, so the build also works when invoked from another directory. Build output is ignored by Git.

Run `build\Azeroth Trainer Plus.exe` directly, or move it to any writable folder. The trainer detects a running game automatically. **Launch game** uses a remembered location or the adjacent game; otherwise it opens a folder picker. Renamed executables can be identified from their game marker, window title, or remembered path. Your selected or detected location is remembered in `%LOCALAPPDATA%\AzerothSurvivorsTrainer\game-path.txt`.

Optionally, copy the executable beside the game during the build. For a repository inside the game directory, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -GameDirectory '..'
```

For a game installed elsewhere, pass its directory instead:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -GameDirectory 'F:\Games\Azeroth Survivors'
```

`-GameDirectory` must contain `AzerothSurvivors.exe`. Relative game paths are resolved from your current PowerShell directory. Only the trainer executable is copied; the game's data and runtime backups stay in place.

Example layout when using the optional copy step:

```text
Azeroth Survivors/
  AzerothSurvivors.exe
  Azeroth Trainer Plus.exe
  Data/                       (existing game data)
  AzerothSurvivors-trainer/    (Git repository)
    .git/
    README.md
    src/
    scripts/
      build.ps1
    build/                    (ignored compiled output)
  trainer/
    backups/                  (created when profile edits need a backup)
    game-check.txt            (created by compatibility diagnostics)
```

Keep the repository in its own directory. When cloning from the game directory, Git creates the `AzerothSurvivors-trainer` child folder automatically. The game directory itself is outside the repository. The `trainer` folder beside the trainer executable holds its runtime reports and profile backups, wherever you run it.

## Start and use

From the repository root (or open the executable in Explorer):

```powershell
Start-Process '.\build\Azeroth Trainer Plus.exe'
```

1. Start the game yourself or click **Launch game** and choose its folder if prompted. You can skip Launch game when the game is already running; Enable trainer finds it automatically.
2. Select the options you want in the five tabs, then click **Enable trainer**.
3. After changing switches, values, or selection rules, click **Apply options**.
4. Resource, healing, clock, talent, and marks buttons send their requests immediately when clicked; an active trainer connection is required.
5. Click **Disable** or close the trainer normally to restore the game's original code.

Run edits wait for an active run update. Permanent class edits show completion in the status area; wait for that acknowledgement before closing. Credits and clickable website and Ko-fi links appear at the bottom of the window.

## Capabilities at a glance

| Tab | Controls |
| --- | --- |
| Rarity and rolls | Separate spell/item legendary chances, fixed paid reroll prices, unlimited banishes and free rerolls |
| Run and time | Simulation speed, XP multiplier, auto-heal, immediate healing, gold, banishes, rerolls, shield, XP grants, clock advances |
| Talents and marks | Earned talent-point budget and marks for each supported class, with profile backups |
| Auto-pick | Independent spell/item selection rules and a delay |
| Run stats | 20 individually enabled minimums, preserving stronger normal bonuses |

## Rarity and rolls

Set separate legendary chances for spell and item cards (0-100%). Changes affect future run offers. Set a fixed paid reroll price, including zero, or enable unlimited banishes/free rerolls. Existing free tokens are spent first; permanent menu equipment is unaffected.

## Run and time

Set speed (0-10x), XP gain (0.1-100x), healing, resources, or a clock advance (up to 3,600 seconds). XP grants use the enabled XP multiplier. High speeds can skip collisions; stat recalculation can reduce shield.

Run actions require a live run. **Cancel queued actions** discards pending requests. Changes in the observed run owner, active state, or clock invalidate old requests. Disable does not undo completed actions.

## Auto-pick

Choose spell and item offers independently: left, middle, right, random, or highest rarity. Hands of 1-3 cards are supported; a larger hand pauses auto-pick and displays its count without reading its records. Delay: 0-10 real-time seconds. Ties choose the leftmost card; missing positions use the last available card. Picks consume offers and can replace gear.

## Run stats

Enable available stat rows and click **Apply options**. Native stat text and the embedded Lua display establish field meanings and encodings independently of the executable hash. Both reference builds and their hash-only variants expose all 20 rows. The UI and diagnostics show the available row count; missing encoding evidence requires confirmation before applying minimums. Haste uses the character sheet's cooldown reduction: 50% writes a 0.5 cooldown factor, and 100% writes zero. Stronger bonuses stay. Max HP also applies during stat recalculation, preserving extra HP through level-ups and offers without Auto-heal. **Heal now** refills HP. Armor caps at 60%; DoT stacks at 4. Existing values may remain after Disable.

## Talents and marks

Connect, open **Talents and marks**, and select a class from the loaded profile. Open its Armory or play it once if it is missing. Set earned points (0-51) or marks; a verified backup is required. Every permanent edit on an unreviewed build requires an explicit confirmation, with No selected by default: marks still assume tenths, and the native units have not been established for that build. Respec before lowering points below allocated ranks. Wait for confirmation and reopen the Armory. Pending edits warn on Disable/Close. Normal unlocks apply.

## Compatibility and lifecycle

Runtime hooks use decoded x64 operations, control flow and native call relationships. String references nominate functions, but an unrelated debug-string helper cannot suppress decoded discovery. Run candidates come from stores to the independently validated HUD clock field, regardless of the owner register or the expression used to advance time. Without that HUD witness, the proven field-plus-frame-time expression remains a fallback. Forwarded updates are dispatch targets, not Run roots; the parent update takes precedence over its co-op child. The clock witness selects the real clock when other timers exist. Run, XP and Health discovery retain semantic field evidence before choosing a patch window. Safe windows require known Game/frame-time provenance, a completed stack frame, dead scratch registers and safe flags. A Run window must dominate dispatches, solo clock stores and forwarded time updates; a window reached only in co-op is refused. An XP window must dominate every recognized award capture and store in its entry. The generated Run hook captures and scales the proven incoming frame-time argument, including when the game applies another time scale before updating its clock. Native UI values provide XP, gold and free-reroll evidence independently of price calculations.

Register and stack-spill provenance traces Game/server arguments across instructions and control-flow merges. A unique byte shape is insufficient: XP is discovered from a field-plus-award store before text-owner validation, then must match the native UI XP field and its Game argument; item rolls link to the native reroll builder; spell rolls must have the native iterator/Game context. These roles are assigned from ownership evidence independently of the number of recognized roll sites. Both native item roll paths must still be covered. Losing one item path cannot reclassify the remaining one as a spell. Profile addresses must flow from the server into the verified native save entry; talent actions also require the class initializer. The profile hook must call that action handler with its own server object.

Run-state compares are collected as candidates. At least two independent native handlers must agree on one phase offset; the native run/offer comparisons also provide their state values, so the hook does not assume states 1 and 2. A running-state equality arm must reach a native clock update while its inequality arm skips that update; an inverted or ambiguous guard is refused; unrelated compares and out-of-range candidates do not discard the run hook. Field evidence is retained rather than overwritten. Conflicting offsets, mismatched source registers, bad bounds, or overlapping fields disable their dependent capabilities. Discovery passes are isolated. A malformed candidate cannot discard independent spell, price, XP or profile features. Offer stride is decoded from native index arithmetic, including `imul` and `lea`/`shl`; vector pointer/count fields are checked independently; highest-rarity selection currently requires the reviewed 160-byte offer record. Pricing accepts a decoded base cost and path count, with a verified native payment action. The paid-counter field comes from verified price calculations, without assuming `rerolls + 4`. Completeness enumerates Game-owned reads/tests of that field; each must map to a verified calculation. Native price displays nominate candidates, and their counter accesses nominate the other paths. A changed exponentiation constant can represent a balance change and does not by itself reject fixed pricing. Unrelated candidates with unresolvable owner flow are isolated. A price-shaped candidate with unresolvable ownership refuses completeness, as does a damaged verified path. Every native `rerollCost` display must also have coverage. The patch leaves counter loading, accumulator initialization and testing intact, allowing independent instructions to exchange order. A missing path disables fixed pricing as a whole.

Compatibility is checked for **18 capabilities**: spell/item chances, paid pricing, speed, XP scaling, healing/clock, gold, free rerolls, shield, banishes, stat minimums, max-HP floor, remaining lives, solo/co-op auto-pick, XP grants, class talents and class marks. Each hook is trial-built at load, including the combined run hook. Missing operands, oversized generated code, unsafe replay instructions, and patch windows too short for their entry jump are rejected before Apply. Generated hooks prefer allocation within relative-jump range and use a six-byte relative jump. Run and XP can use a six-byte window when a longer window is unsafe; these short sites refuse installation if near allocation fails. An ignored CS prefix prevents the relative jump from being treated as a completed Windows epilogue. A local `RtlVirtualUnwind` probe must confirm that the native frame is preserved; otherwise the 12-byte absolute form is required. Sites with at least 12 bytes retain that fallback. Lives require every field used by run tracking.

Load checks cover decoded instruction boundaries, incoming direct branches from other runtime-function fragments, relative jump-table targets and proven unsigned table bounds, scratch-register and return-value liveness, and agreement between prologue instructions and unwind metadata. Unresolved indirect branches refuse affected hook sites. Unguarded relative tables retain every decoded target up to the table boundary, including targets after non-instruction data entries. A discarded non-instruction target may not enter patched bytes. Guarded tables remain strict; an invalid target within their count is refused. Live validation includes all bytes read from a table, including its terminating entry. Replayed instructions must be free of branches; Run and XP can relocate RIP-relative operands, with range checks before installation; the health hook also validates both `addss` operands. Equivalent marks allocators require matching bodies and call targets.

Stat meanings come from native value flow rather than store order or whole-file identity. Format templates are found by their contents, without requiring `hp` to be first or any particular key to remain present. At least six known stat keys nominate a sheet; individual keys then prove only their own rows. The character-sheet text exports keys such as `hp`, `dmg`, `cm`, `cnt` and `liv`; another native line supplies run time, level, XP, maximum HP, shield, gold, lives and free rerolls. The supported Rust format parser retains sequential argument indices across presentation flags, precision and changed separators. A key may follow separator punctuation and whitespace, but never another identifier: `;cm=`, `|cm=` and `, cm=` are accepted, while `o.cm=` and `x cm=` remain anonymous. An unrecognized key or unsupported flag loses only that argument; explicit indices and unknown argument tags still refuse the line. Formatter load types are checked separately. Its native consumer must show the 16-byte argument stride, paired value/formatter reads and sequential cursor; both `xor r,r` and `sub r,r` whole-register zeroing are accepted, and the record stride can use `shl` or `imul`. Indexed arguments, unknown bytecode or an unproven consumer fall back to the existing HUD label witnesses; another Rust formatting ABI can still require an update. Each text call must have one argument owner, agree with at least one independently proven field and conflict with none. A failed owner check refuses the entire text source, including its otherwise unique rows; matching numeric offsets within that source cannot establish its own Game identity. Its owner anchor is retained as a live-code dependency.

The embedded Lua establishes each supported stat's display conversion, including percentage bases and the cooldown reduction. Unit discovery runs separately against the remaining verified field witnesses. In particular, the HUD run line can retain lives and its units when the sheet text cannot be recognized. The DoT stack proof also requires the native `min(field, 3) + 1` value flow; its raw minimum remains one less than the displayed count. Armor retains its HUD witness. All 20 rows, including lives, can therefore be proven without a reviewed executable hash. The former `attributes` diagnostic is now named `reviewedStatsBase`; it is only a fallback base for the exact reviewed layouts. Per-row offsets, unit evidence and refusal reasons remain visible in diagnostics.

UI recognition requires any two sibling run labels, rather than one mandatory `rerolls` label. Labels use one shared reference index and accept arbitrary ASCII capitalization, including `GOLD`. The HUD fallback still requires the label in R8, length in R9 and value at `rsp + 0x20`; changing that call convention can disable dependent features. Owner checks can use UI-proven XP, gold and reroll fields. A Game-owned price comparison and matching load/subtract/borrow-check/store payment establish gold without its UI label. Native `shield`, `hpMax` and `time` labels provide independent witnesses; unreviewed builds no longer infer shield from `hp + 4`. Independent captures and calculations may move outside the original patch window. XP collects semantic adders before selecting the unique entry that matches validated `UI:xp`; unmatched candidates publish no XP witness and cannot discredit a text owner. It recognizes either operand order, independent scheduling, `add [field],reg` and `lea` for its field-plus-award expression, and the offer handler accepts the commuted count comparison with the corresponding unsigned branch. Spell closure members are identified by their Game field use rather than a fixed capture displacement. Rarity totals track scalar values through independent scheduling and require five unchanged weight registers at the RNG call. Spell-result zeroing and profile entry capture ordering accept their supported equivalent forms. Function identity and fields are checked before patchability; an unsafe floor or multiplier window does not erase independent field evidence. The XP and Choose entries must prove that extra integer, floating and incoming stack arguments are unused before the trainer calls them. XP grants remain independent of the multiplier hook. Inlining the native Choose-to-Health or Run-to-XP call no longer removes an otherwise proven action. Co-op dispatch can use a call or a tail jump on either branch arm; the nonzero co-op arm must reach that update while the zero arm does not. An unproven dispatch disables auto-pick while retaining independent solo run features. Co-op auto-pick is unavailable whenever the base auto-pick code cannot be emitted. XP scaling is independent of the health hook; health recognition derives current HP from its native delta/store/clamp instead of assuming a fixed gap below maximum HP.

Marks identify `SaveProfile` through a unique serializer/file-writer call chain, its profile argument, unused incoming integer/stack argument checks, and the labeled eight-byte marks record. The entire save-function hash is no longer required. The serializer proves the marks record and destination, but copies the integer value without proving its displayed scale. Unreviewed builds therefore require confirmation of the tenths assumption before every marks edit; a verified backup remains mandatory. Destination literals and the live dependencies are checked. Talents still require a reviewed release because their nested allocated-rank schema is not yet established independently. Save-path helpers and the marks allocator retain strict fingerprints: literal discovery alone does not prove environment-variable priority or capacity spacing. A changed helper can still disable permanent edits safely.

Before connecting, recognized live functions and supporting code/data must agree with the checked executable, including ASLR relocations. A mismatch disables dependent capabilities; no capabilities means connection is refused. Reports include the executable hash, embedded `AzerothSurvivors/<version>` token, fields and refusal reasons. The token is informational: both archived builds report `0.2.0`. Canonically named or explicitly remembered game executables do not require that token; it helps identify renamed copies.

Enabling with all options off installs no hooks. Spell-only selection installs only its rarity hook. Run actions start their tracker when requested; open **Talents and marks** to activate profile tracking and load classes. Max-health and XP hooks install only while their options are enabled.

These are the verified reference builds:

| Reference | SHA-256 |
| --- | --- |
| Original desktop layout | `0bb6c557c5f6cfeea212761a33554f9a5647ce7cb7e03b8a163b95489496e908` |
| Updated desktop layout | `6ca5f4ba4eb6b26716e4b7d9e03cbd25ddafe7582dcd8e55333e070a28058afa` |

Runtime recognition tolerates unrelated function changes for semantically verified features and extracts the registers/fields used by supported patterns. Stat rows and lives use native text/encoding evidence; unproven rows remain disabled unless covered by an exact reviewed fallback. Talent edits still require their exact reviewed release. It still requires recognizable operations, call conventions and record formats. Reworked logic, heavy inlining, unsupported instruction shapes or another architecture can require a trainer update. An unresolved indirect jump still refuses the affected hook host because its incoming patch targets cannot be bounded safely; no fallback bypasses that check. The native save-entry call graph can tolerate body changes; its file/path helpers and allocator stay strict. A changed argument contract, record layout, destination or ambiguous entry disables the affected edit.

Local checks include rebased text owners, text sources without independent agreement, co-op-only window rejection, XP dominance, renamed/reordered sheet keys, commuted arithmetic/comparisons, moved closure members, equivalent zeroing, profile capture swaps and inverted run guards. They also cover both real builds, hash-only changes, added state compares, single-witness refusal, all five damaged price paths, changed balance constants, moved paid counters, independent instruction moves beyond the original windows, extra action arguments, renumbered run states, uppercase UI labels, moved Game/stat fields, changed Lua units, unsupported format bytecode, transformed float values, lost health/offer handlers, changed HP spacing, foreign-fragment branches, removed save/talent hashes, additional save arguments, backup refusal and native hook behavior. These recognizers were developed with both builds available. Cross-build and synthetic checks do not prove support for an unseen future release; genuine prediction must be measured on a new build before changing the recognizers. Verification files remain ignored.

Run the trainer and game as the same Windows user with matching administrator privileges. Joining co-op clients cannot control the host's simulation. If the trainer is forcibly terminated while enabled, restart the game to remove its hooks.

Compatibility checking and initial option application run off the UI thread. Data-only updates, including chances, speed and XP scaling, do not pause game threads. Installing/removing hooks and changing price calculations briefly pause threads; a busy-region refusal can be retried. If an enable is only partly successful, the status explicitly offers **Apply options** to finish or **Disable** to restore code.

Thread snapshots repeat until a pass finds no new threads. A 16-pass limit releases captured threads and refuses the operation if they do not settle. An external process can still create a thread after the final snapshot.

Generated hooks use private scratch frames and preserve volatile registers instead of borrowing game locals or exception-state slots. Continuation jumps use a register proven dead at the destination. Windows unwind tables describe the private allocation and the original frame, including restore/tail transitions. Code pages stay registered until the game exits; Disable/Enable reuses them. The executable and MPQs stay unchanged on disk. Permanent edits invoke the native save routine.

## Compatibility diagnostics

The production executable includes two read-only compatibility commands and an archive command. Run them from the folder containing your trainer executable:

```powershell
Start-Process '.\Azeroth Trainer Plus.exe' -ArgumentList '--verify-files' -Wait
Get-Content .\trainer\game-check.txt
```

`--verify-files` works with the game closed. It checks decoded patterns, call relationships, frame safety and fields. Its report lists capability availability, reasons, witnesses and executable hash. It does not launch the game or install hooks. Pass a game directory or executable explicitly when needed, including a renamed executable: `--verify-files "F:\Games\Azeroth Survivors"`.

To inspect an already running game, close other trainers normally and run:

```powershell
Start-Process '.\Azeroth Trainer Plus.exe' -ArgumentList '--verify-game' -Wait
Get-Content .\trainer\game-check.txt
```

`--verify-game` checks the running game without installing hooks. Disable other trainers first. Reports say **PASS** when every capability is available, **PARTIAL** when some remain, or **FAIL** when connecting is unsafe. Exit code 0 means a usable capability remains; 1 means verification failed; 2 means no report could be written. Reports normally use `trainer/game-check.txt`. If that folder is unwritable, they fall back to `%LOCALAPPDATA%/AzerothSurvivorsTrainer/reports`, then `%TEMP%/AzerothSurvivorsTrainer`; the path is printed to standard output. The UI performs the same checks on connection.

Before changing recognizers for a newly installed release, archive its executable and record how many features the existing trainer recognizes:

```powershell
Start-Process '.\Azeroth Trainer Plus.exe' -ArgumentList '--archive-build' -Wait
Get-Content .\trainer\game-check.txt
```

The executable is copied and hashed before PE parsing, so an unparseable update is preserved with its failure report. The archive stores one executable per game SHA-256 under `trainer/reference-builds`, with a recognition report named by the trainer's SHA-256. Existing reports are preserved. Pass a game directory or executable after `--archive-build` when needed. The command launches no game and changes no saves; each release copy uses approximately 150 MB. Run this with the released trainer **before editing recognizers for the next game release**, then compare its preserved baseline with the revised trainer. This is the blind compatibility score; synthetic mutations are not a substitute. A release used to develop recognizers is not an unseen-version test.

Compatibility checks do not establish that every option works throughout a complete run. Full live auto-pick behavior, actual profile serialization, and Armory refresh have not been verified end to end. Use the status messages and check the game's displayed values after a request.

## Profile backups and persistence

Before each permanent edit, the trainer verifies the native save-destination call chain and strings, then reads the running game's environment and working directory. `WOW_ROGUE_SAVE` is the **complete save file path**, including its filename. Relative paths use the game's working directory. Without an override, the verified policy uses `%APPDATA%/wenilla-rogue/characters.txt`, then `XDG_DATA_HOME`, then `HOME/.local/share`. The effective file is copied to a unique file in `trainer/backups`, flushed and verified byte-for-byte before queuing the edit. A changed native destination disables permanent edits even if an old save still exists.

Missing/unreadable saves, unresolved paths, unwritable backups or changing backup contents refuse the edit. Class IDs come from loaded talent/marks records; unknown future IDs are shown numerically. To restore, close the game and trainer, preserve the current save, and copy the chosen backup over the effective `characters.txt`. Other save files are not backed up. Rebuilding preserves backups.

Disable restores executable instructions. It does not undo granted XP, resources, talent/marks edits, elapsed time, chosen offers, or saved progression. Unlimited counters and enforced stats can remain at their current values until normal gameplay consumes or recalculates them.

## Troubleshooting

| Symptom | What to do |
| --- | --- |
| Unrecognized code/layout or ambiguous signature | Run `--verify-files` for the failing context. A changed contract needs verified compatibility signatures; editing the executable hash cannot make it compatible. |
| Some options are greyed out after connecting | Those groups did not pass compatibility checks. Verified groups remain usable; run `--verify-files` for detailed reasons. |
| Game not found | Click Launch game and choose the folder containing `AzerothSurvivors.exe`, or start the game yourself and click Enable trainer. |
| Cannot access the game | Match the game's Windows account and administrator privileges. |
| More than one game copy is running | Close extra instances, then enable the trainer. |
| Older trainer or changed code detected | Disable and close older trainers normally. Restart the game if one was terminated while active. |
| Game is executing code that needs replacing | Wait briefly and retry Apply options or Disable. |
| Partially enabled | Keep the trainer open. Click Apply options to finish or Disable to restore code. Follow any further restoration error instructions before closing. |
| A resource request stays queued | Enter an active run and allow its simulation/offer update to execute. |
| Talents or marks look unchanged | Wait for completion, then close and reopen the Armory. Respec before reducing earned points below allocated ranks. |
| High speed changes combat | Reduce simulation speed; longer simulation steps can skip collisions. |
| Build compiler missing | Install or enable .NET Framework 4.5 or newer with the 64-bit compiler at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. |
| Build or install cannot overwrite the output | Close the trainer using that executable and retry. |
| GameDirectory is rejected | Pass the installed game directory containing `AzerothSurvivors.exe`; relative paths start at your current PowerShell directory. |

## Source layout

| File | Purpose |
| --- | --- |
| `scripts/build.ps1` | Builds into `build` using an explicit production source list; optionally installs the executable with `-GameDirectory` |
| `src/Program.cs` | Windows Forms entry point and compatibility command dispatch |
| `src/Game/GameLocator.cs` | Running-game discovery and remembered installation location |
| `src/Game/GameImage.cs` | PE sections, runtime-function ranges, executable hashing, and ASLR relocation parsing |
| `src/Game/GameLayout.cs` | Independent capabilities, field agreement/bounds, call checks and live validation |
| `src/Game/RuntimeDiscovery.cs` | Runtime operation recognizers, string/call anchors, fields and registers |
| `src/Game/NativeText.cs` | Bounded Rust format parsing, argument-consumer checks and native formatter types |
| `src/Game/RegisterFlow.cs` | Argument, register, stack-spill and pointer provenance across control flow |
| `src/Game/DecodedCode.cs` | Iced decoding, control flow, liveness and prologue/unwind proofs |
| `src/Game/CatalogContracts.cs` | Strict helper fingerprints and decoded patch boundaries |
| `src/Game/BuildCatalog.cs` | Verified permanent-edit helper signatures |
| `src/Game/ProfileBackup.cs` | Target-process environment/path resolution and verified backups |
| `src/UI/TrainerWindow.cs` | Five-tab interface, option validation, request status, and creator links |
| `src/Hooks/HookEngine.cs` | Layout-aware instruction assembly, patch lifecycle, session management, resources, and profile backups |
| `src/Hooks/AdditionalCode.cs` | Run, XP, and permanent progression hook generation |
| `src/Hooks/RunEnhancements.cs` | Stat minimums and automatic offer selection |
| `src/Hooks/HealthCode.cs` | Max-HP floor during recalculation, before the game's HP clamp |
| `src/Hooks/RunCommands.cs` | Run epochs and atomic request consumption to reject stale actions |
| `src/Interop/Native.cs` | Windows process/memory APIs and thread suspension |
| `src/Interop/UnwindSupport.cs` | Windows x64 unwind metadata registration for generated hooks |
| `src/Diagnostics/TrainerDiagnostics.cs` | On-disk and live compatibility reporting |

Only production source, the build script, this README, and ignore rules are published. Build outputs, game files, tests, local instructions, research notes, screenshots, reports, saves, and backups stay local.
