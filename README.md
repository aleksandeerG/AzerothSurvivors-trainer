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

Choose spell and item offers independently: left, middle, right, random, or highest rarity. Delay: 0-10 real-time seconds. Ties choose the leftmost card; missing positions use the last available card. Picks consume offers and can replace gear.

## Run stats

Enable any of 20 stat minimums and click **Apply options**. Stronger bonuses stay. Max HP also applies during stat recalculation, preserving extra HP through level-ups and offers without Auto-heal. **Heal now** refills HP. Armor caps at 60%; DoT stacks at 4. Existing values may remain after Disable.

## Talents and marks

Connect and select a class from the loaded profile. Open its Armory or play it once if it is missing. Set earned points (0-51) or marks; a verified backup is required. Respec before lowering points below allocated ranks. Wait for confirmation and reopen the Armory. Pending edits warn on Disable/Close. Normal unlocks apply.

## Compatibility and lifecycle

Runtime hooks use decoded x64 operations, control flow, string references and call relationships. The run update is anchored by its `WOW_ROGUE_SINK_AT` reference. Rarity decision trees, XP award entry, health recalculation, offer handler and price calculations are recognized independently. Whole-function fingerprints are reserved for the permanent-edit helpers `EnsureClass`, `SaveProfile` and the marks-vector allocator.

Fields and rarity/health registers are extracted from instructions. Witnesses must agree, stay aligned, and avoid overlap. The stat record must retain its verified member layout. Gold, free rerolls, shield, run states and offer vectors have separate evidence. Auto-pick identifies a changed hand from its card records, without depending on a UI-only offer counter. Marks require a small labeled serialization loop that proves the vector and eight-byte record format; unrelated serializer code can change.

Compatibility is checked for **18 independent capabilities**: spell/item chances, paid pricing, speed, XP scaling, healing/clock, gold, free rerolls, shield, banishes, stat minimums, max-HP floor, remaining lives, solo/co-op auto-pick, XP grants, class talents and class marks. The shared run and profile hooks emit only supported operations. Losing a price or co-op handler does not discard unrelated run controls. The UI disables unsupported switches, individual stat rows and resource actions.

Load checks prove instruction boundaries, reachable branch entries, scratch-register liveness and agreement between prologue instructions and unwind metadata. Required links include Run → XP and Choose → Health when those roles are available. Equivalent marks allocators are accepted by verified body and call targets, without depending on neighboring functions. Ambiguous contexts refuse their dependent capabilities.

Before connecting, recognized live function bodies must agree with the checked executable, including ASLR relocations. A live mismatch disables its dependent capabilities. If none remain, connecting is refused. Diagnostics identify failed roles, field witnesses, register liveness, frame mismatches and call links. There is no force-enable option.

SHA-256 identifies the executable in diagnostics; it is **not a version whitelist**. These are the verified reference builds:

| Reference | SHA-256 |
| --- | --- |
| Original desktop layout | `0bb6c557c5f6cfeea212761a33554f9a5647ce7cb7e03b8a163b95489496e908` |
| Updated desktop layout | `6ca5f4ba4eb6b26716e4b7d9e03cbd25ddafe7582dcd8e55333e070a28058afa` |

Runtime recognition tolerates unrelated function changes and extracts the registers/fields used by the supported patterns. It still requires recognizable operations, call conventions and record formats. Reworked logic, heavy inlining, unsupported instruction shapes or another architecture can require a trainer update. Permanent-save helpers deliberately stay strict; an unknown helper disables the affected edit.

Local checks cover both real builds, cross-catalog recognition in both directions, unrelated-code mutations, lost dependencies, redirected calls, bad unwind metadata, simulated live mismatches, poisoned RNG registers, backup refusal and native hook behavior. These recognizers were developed with both builds available. Cross-build and synthetic checks do not prove support for an unseen future release; genuine prediction must be measured on a new build before changing the recognizers. Verification files remain ignored.

Run the trainer and game as the same Windows user with matching administrator privileges. Joining co-op clients cannot control the host's simulation. If the trainer is forcibly terminated while enabled, restart the game to remove its hooks.

Compatibility checking and initial option application run off the UI thread. Data-only updates, including chances, speed and XP scaling, do not pause game threads. Installing/removing hooks and changing price calculations briefly pause threads; a busy-region refusal can be retried. If an enable is only partly successful, the status explicitly offers **Apply options** to finish or **Disable** to restore code.

Thread snapshots repeat until a pass finds no new threads. A 16-pass limit releases captured threads and refuses the operation if they do not settle. An external process can still create a thread after the final snapshot.

Generated hooks use private scratch frames and preserve volatile registers instead of borrowing game locals or exception-state slots. Continuation jumps use a register proven dead at the destination. Windows unwind tables describe the private allocation and the original frame, including restore/tail transitions. Code pages stay registered until the game exits; Disable/Enable reuses them. The executable and MPQs stay unchanged on disk. Permanent edits invoke the native save routine.

## Compatibility diagnostics

The production executable includes two read-only compatibility commands. Run them from the folder containing your trainer executable:

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

Compatibility checks do not establish that every option works throughout a complete run. Full live auto-pick behavior, actual profile serialization, and Armory refresh have not been verified end to end. Use the status messages and check the game's displayed values after a request.

## Profile backups and persistence

Before each permanent edit, the trainer reads the running game's environment and working directory. `WOW_ROGUE_SAVE` overrides the save directory; a relative value is resolved against the game's working directory. Otherwise it uses that process's `%APPDATA%/wenilla-rogue`. The `characters.txt` file is copied to a uniquely named file in `trainer/backups`, flushed and verified byte-for-byte before a request is queued.

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
