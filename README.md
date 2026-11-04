# Azeroth Survivors Trainer Plus

A standalone Windows x64 trainer for Azeroth Survivors, created by **[aleksanderG](https://aleksanderg.com/)**.

[More projects](https://aleksanderg.com/) | [Support on Ko-fi](https://ko-fi.com/K3P227JS3D)

Control legendary chances, reroll prices, run speed, XP, resources, automatic offer selection, character stats, and permanent class progression from five tabs. Every option starts off. Connecting alone does not change the selected chances, prices, multipliers, or stat minimums.

## Requirements

- 64-bit Windows with .NET Framework 4.5 or newer (4.8 recommended).
- The supported desktop game executable, build date **2026-11-02**. Compatibility is tied to its SHA-256 and instruction bytes; a matching version name alone is insufficient.
- The supported game installed anywhere on your PC. The trainer can run from any folder.
- The trainer and game running as the same Windows user with matching administrator privileges.

Building uses Windows PowerShell and the 64-bit C# compiler shipped with .NET Framework. No Visual Studio, .NET SDK, NuGet packages, Python, or Cheat Engine is required. Game files are not included in this repository.

## Get the source and build

Clone the repository, enter it, and run the build script:

```powershell
git clone https://github.com/aleksandeerG/AzerothSurvivors-trainer.git
Set-Location .\AzerothSurvivors-trainer
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

The script produces **`build\Azeroth Trainer Plus.exe` inside the repository**. Source and output paths are resolved relative to the script, so the build also works when invoked from another directory. Build output is ignored by Git.

Run `build\Azeroth Trainer Plus.exe` directly, or move it to any writable folder. The trainer detects a running game automatically. **Launch game** uses the game beside the trainer or a remembered location; if neither exists, it opens a folder picker. Your selected or detected location is remembered in `%LOCALAPPDATA%\AzerothSurvivorsTrainer\game-path.txt`.

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

Select a class, then set earned points (0-51) or marks. Changes are permanent, with backups in `trainer/backups`. Respec before lowering points below allocated ranks. Wait for confirmation and reopen the Armory. Disable and Close warn about pending edits, which may be cancelled. Normal unlock requirements apply.

## Compatibility and lifecycle

The standalone x64 executable uses Windows .NET Framework and needs no Python, Cheat Engine or downloaded packages. It validates the executable SHA-256, all 17 patch sites and the unchanged HUD and co-op guards (19 compatibility sites) for the installed desktop build (embedded date 2026-11-02). A game update needs verified offsets.

Run the trainer and game as the same Windows user with matching administrator privileges. Joining co-op clients cannot control the host's simulation. If the trainer is forcibly terminated while enabled, restart the game to remove its hooks.

Compatibility checking and initial option application run off the UI thread. Data-only updates, including chances, speed and XP scaling, do not pause game threads. Installing/removing hooks and changing price calculations briefly pause threads; a busy-region refusal can be retried. If an enable is only partly successful, the status explicitly offers **Apply options** to finish or **Disable** to restore code.

Thread snapshots repeat until a pass finds no new threads. A 16-pass limit releases captured threads and refuses the operation if they do not settle. An external process can still create a thread after the final snapshot.

Generated hooks have registered Windows unwind information. They reuse the original frame and avoid extra pushes. Entry and continuation jumps are tested to avoid false Windows epilogue detection. Registered code pages are retained until the game exits so stack walks cannot refer to freed metadata; Disable/Enable within the same window reuses those small allocations. The game's executable and MPQs stay unchanged on disk. Permanent progression buttons intentionally invoke the game's own save routine.

## Compatibility diagnostics

The production executable includes two read-only compatibility commands. Run them from the folder containing your trainer executable:

```powershell
Start-Process '.\Azeroth Trainer Plus.exe' -ArgumentList '--verify-files' -Wait
Get-Content .\trainer\game-check.txt
```

`--verify-files` works with the game closed. It verifies the on-disk executable hash, 19 instruction sites, and seven original unwind frames. It does not launch the game or install hooks. It can read a running process's executable path to find the game; otherwise it uses the adjacent or remembered executable. If needed, pass a game directory explicitly: `--verify-files "F:\Games\Azeroth Survivors"`.

To inspect an already running game, close other trainers normally and run:

```powershell
Start-Process '.\Azeroth Trainer Plus.exe' -ArgumentList '--verify-game' -Wait
Get-Content .\trainer\game-check.txt
```

`--verify-game` checks the hash, access to the process, module base, gameplay hook sites, HUD/co-op guards, and pricing sites without installing hooks. An already enabled trainer changes these sites, so disable it first. Both commands return exit code 0 on success or 1 on failure, and overwrite `trainer/game-check.txt` with the latest result. The ordinary UI performs compatibility checks when connecting as well.

Compatibility checks do not establish that every option works throughout a complete run. Full live auto-pick behavior, actual profile serialization, and Armory refresh have not been verified end to end. Use the status messages and check the game's displayed values after a request.

## Profile backups and persistence

Before each permanent class edit, the trainer copies `%APPDATA%\wenilla-rogue\characters.txt`, if present, into `trainer\backups` beside the trainer executable. Copies have unique names. Keep backups you may need; rebuilding does not remove them.

To restore a backup, close the game and trainer, preserve another copy of your current profile, and copy the chosen backup over the original profile file. Other profiles are not backed up by this mechanism. A missing source profile produces no backup. Ensure the trainer's directory is writable before editing progression.

Disable restores executable instructions. It does not undo granted XP, resources, talent/marks edits, elapsed time, chosen offers, or saved progression. Unlimited counters and enforced stats can remain at their current values until normal gameplay consumes or recalculates them.

## Troubleshooting

| Symptom | What to do |
| --- | --- |
| Unsupported build | Run `--verify-files`. Offsets and expected bytes must be reverified for a changed game executable; bypassing the hash is insufficient. |
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
| `src/UI/TrainerWindow.cs` | Five-tab interface, option validation, request status, and creator links |
| `src/Hooks/HookEngine.cs` | Supported build data, instruction assembly, patch lifecycle, session management, resources, and profile backups |
| `src/Hooks/AdditionalCode.cs` | Run, XP, and permanent progression hook generation |
| `src/Hooks/RunEnhancements.cs` | Stat minimums and automatic offer selection |
| `src/Hooks/HealthCode.cs` | Max-HP floor during recalculation, before the game's HP clamp |
| `src/Hooks/RunCommands.cs` | Run epochs and atomic request consumption to reject stale actions |
| `src/Interop/Native.cs` | Windows process/memory APIs and thread suspension |
| `src/Interop/UnwindSupport.cs` | Windows x64 unwind metadata registration for generated hooks |
| `src/Diagnostics/TrainerDiagnostics.cs` | On-disk and live compatibility reporting |

Only production source, the build script, this README, and ignore rules are published. Build outputs, game files, tests, local instructions, research notes, screenshots, reports, saves, and backups stay local.
