# Azeroth Survivors Trainer Plus

A standalone Windows x64 trainer for Azeroth Survivors, created by **[aleksanderG](https://aleksanderg.com/)**.

[More projects](https://aleksanderg.com/) | [Support on Ko-fi](https://ko-fi.com/K3P227JS3D)

Control legendary chances, reroll prices, run speed, XP, resources, automatic offer selection, character stats, and permanent class progression from five tabs. Every option starts off. Connecting alone does not change the selected chances, prices, multipliers, or stat minimums.

## Requirements

- 64-bit Windows with .NET Framework 4.5 or newer (4.8 recommended).
- The supported desktop game executable, build date **2026-11-02**. Compatibility is tied to its SHA-256 and instruction bytes; a matching version name alone is insufficient.
- The trainer executable in the same directory as `AzerothSurvivors.exe` and the game's normal data files.
- The trainer and game running as the same Windows user with matching administrator privileges.

Building uses Windows PowerShell and the 64-bit C# compiler shipped with .NET Framework. No Visual Studio, .NET SDK, NuGet packages, Python, or Cheat Engine is required. Game files are not included in this repository.

## Get the source and build

Clone the repository, enter it, and run the build script:

```powershell
git clone https://github.com/aleksandeerG/AzerothSurvivors-trainer.git
Set-Location .\AzerothSurvivors-trainer
powershell -NoProfile -ExecutionPolicy Bypass -File .\trainer\build.ps1
```

The script produces **`Azeroth Trainer Plus.exe` in the repository root**. Paths are resolved relative to the script, so the build also works when invoked from another directory. Close the trainer before rebuilding to release the output file.

Copy the executable beside your installed `AzerothSurvivors.exe`. Keep the game's existing data directories in place. For example:

```text
Azeroth Survivors/
  AzerothSurvivors.exe
  Azeroth Trainer Plus.exe
  Data/                       (existing game data)
  trainer/
    backups/                  (created when profile edits need a backup)
    game-check.txt            (created by compatibility diagnostics)
```

If you clone directly into the game directory, the build output is already in the right place. The repository's ignore rules keep the game, personal saves, backups, and generated output out of Git.

## Start and use

From the installed game directory:

```powershell
Start-Process '.\Azeroth Trainer Plus.exe'
```

1. Start the game yourself or click **Launch game** in the trainer.
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

- **Override spell legendary chance** and **Override in-run item legendary chance** are separate switches and percentages. Each supports 0-100% in 0.1% steps. The item switch covers both jewelry roll paths: rings, trinkets and amulets. Changes affect future offers and rerolls; main-menu permanent equipment and already offered cards use separate rules.
- **Fixed paid reroll cost** keeps the selected gold price instead of multiplying it after each roll. Set 0 for free paid rerolls. Existing free tokens are consumed first. Paid-roll history is preserved, so the original escalating price resumes from that history when this option is disabled.
- **Unlimited banishes / free rerolls** replenish those counters to 9,999 on simulation ticks.

Legendary percentages apply to each card independently, not the whole hand. The remaining probability follows the game's normal luck weights. Normal upgrade availability still applies; in particular, the game's fallback can promote an exhausted lower rarity even with its random legendary probability set to zero.

## Run and time

- Check **Simulation speed** to enable it; the number alone is inactive while unchecked. It scales the time step used by the local run simulation. 1x is normal, lower values slow it down and up to 10x speeds it up. Zero stops simulated time advancing; client rendering and already-due actions can still update. Combat, cooldowns and the run clock use the scaled step. Above 1x, each step is longer; the trainer does not execute extra normal-sized steps. Fast projectiles and movement can skip collisions, especially near 10x. Use lower speeds if that changes combat too much.
- **Advance clock (seconds)** adds 0-3,600 seconds to the run clock. This can make scheduled bosses due sooner. It does not defeat them or declare victory. Large jumps can make several timed events due together.
- Check **Run XP multiplier** to enable it. It scales incoming run XP from 0.1x to 100x, including XP sent by the resource button. Fractional XP carries across awards per run object: at 0.5x, two 1-XP awards yield 1 XP in total; at 1.5x, they yield 3 XP. Multipliers use thousandths, and carry resets when the multiplier changes or the run clock goes backward. The game's normal level-up and offer generation then handles that XP.
- **Auto-heal while alive / Heal now** refill current HP from the game's computed maximum HP. Auto-heal does not revive a defeated hero and is not a guarantee against lethal damage between updates.
- **Run resource** lets you set gold, remaining banishes, free rerolls or shield, or grant an amount of run XP. Select the resource, choose the value and click **Set / grant**.

The shared mutable tick handles solo and co-op paths. Run requests are consumed at the next active-run/offer update; lobby and results screens leave them pending. Requests sent from a menu wait for a run to update. These edits and XP grants execute before the mutable run step, outside the read-only HUD function. Granted resources, elapsed clock advances and earned upgrades are not undone by Disable. Disabling unlimited options stops replenishment; existing tokens remain. Damage/pickup values already raised can remain until the game next recalculates those stats. Disable restores code, not a snapshot of the run.

## Auto-pick

Spell and in-run item offers have separate switches and selection rules: **Left**, **Middle**, **Right**, **Random**, or **Highest rarity**. Highest rarity chooses the leftmost card when tied. If a requested position is missing, the last available card is used. These controls cover the current run's offers, including guardian/cache jewelry.

The optional delay is 0-10 seconds (initially 0.5), measured using ordinary frame time independently of simulation speed. The timer resets when the game deals/rerolls an offer. One card is selected per update through the game's existing choice handler, which handles applying the upgrade and consuming the offer. Auto-pick can learn spells or replace equipment. It does not optimize a build, reroll, banish, or skip an offer. Both switches start off.

## Run stats

Check individual rows in **Run stats**, enter their minimums, then click **Apply options**. All 20 rows start off:

- Maximum health, damage bonus, haste, critical chance, critical damage and armor.
- Area, extra targets, echo chance, movement speed, regeneration and pickup radius.
- Remaining lives, experience bonus, gold bonus, luck, buff power, DoT stack limit, DoT haste and kill heal.

Percentages use the character sheet's units: a 500% damage bonus means a total multiplier of 6; critical damage 300% means 3x critical damage. Haste 100% limits cooldowns to half their base duration. Armor is limited to 60%; the verified DoT stack limit is 1-4. Health sets a maximum-HP minimum; use **Heal now** to refill current HP. Lives are replenished to the selected minimum. The experience bonus affects the game's pickup scaling and stacks with the separate run XP multiplier if both are enabled.

Stronger normal bonuses are preserved. Disable stops enforcement; modified stats can remain until the game recalculates them, and consumed/granted lives are not rolled back. Lifesteal and thorns are derived from effects and remain unexposed until their effect handling is verified.

## Talents and marks

Select a class explicitly before using these controls:

- **Earned talent points** sets that class's earned budget to 0-51, the cap verified in this desktop build. The trainer sets the corresponding class talent XP through the game's profile structure and invokes the game's save method. It does not allocate talents for you. Lowering below the total already allocated ranks is refused; respec the class first.
- **Class marks** sets the class's balance in 0.1-mark steps. The game's internal representation uses tenths. Gear unlock and Heroic requirements still apply.

These edits affect permanent class progression. Before each request, the trainer copies the current default profile file into **trainer/backups** with a unique name. Each request has a sequence ID; an older completion cannot report success for a newer edit. Success is acknowledged after the game's save method returns. Wait for the update status before closing; reopen the Armory to refresh its display. Talent allocations are chosen between runs. Backups copy the game's encrypted profile as-is; the trainer does not decode or rewrite its save format.

## Compatibility and lifecycle

The standalone x64 executable uses Windows .NET Framework and needs no Python, Cheat Engine or downloaded packages. It validates the executable SHA-256, all 16 patch sites and the unchanged HUD and co-op guards (18 compatibility sites) for the installed desktop build (embedded date 2026-11-02). A game update needs verified offsets.

Run the trainer and game as the same Windows user with matching administrator privileges. Joining co-op clients cannot control the host's simulation. If the trainer is forcibly terminated while enabled, restart the game to remove its hooks.

Compatibility checking and initial option application run off the UI thread. Data-only updates, including chances, speed and XP scaling, do not pause game threads. Installing/removing hooks and changing price calculations briefly pause threads; a busy-region refusal can be retried. If an enable is only partly successful, the status explicitly offers **Apply options** to finish or **Disable** to restore code.

Generated hooks have registered Windows unwind information. They reuse the original frame and avoid extra pushes. Entry and continuation jumps are tested to avoid false Windows epilogue detection. Registered code pages are retained until the game exits so stack walks cannot refer to freed metadata; Disable/Enable within the same window reuses those small allocations. The game's executable and MPQs stay unchanged on disk. Permanent progression buttons intentionally invoke the game's own save routine.

## Compatibility diagnostics

The production executable includes two read-only compatibility commands. Run them from the installed game directory:

```powershell
Start-Process '.\Azeroth Trainer Plus.exe' -ArgumentList '--verify-files' -Wait
Get-Content .\trainer\game-check.txt
```

`--verify-files` works with the game closed. It verifies the on-disk executable hash, 18 instruction sites, and six original unwind frames. It does not launch the game or inspect a live process.

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
| Game file missing / Launch game fails | Place the trainer executable beside `AzerothSurvivors.exe`. |
| Cannot access the game | Match the game's Windows account and administrator privileges. |
| More than one game copy is running | Close extra instances and connect to the game beside this trainer. |
| Older trainer or changed code detected | Disable and close older trainers normally. Restart the game if one was terminated while active. |
| Game is executing code that needs replacing | Wait briefly and retry Apply options or Disable. |
| Partially enabled | Keep the trainer open. Click Apply options to finish or Disable to restore code. Follow any further restoration error instructions before closing. |
| A resource request stays queued | Enter an active run and allow its simulation/offer update to execute. |
| Talents or marks look unchanged | Wait for completion, then close and reopen the Armory. Respec before reducing earned points below allocated ranks. |
| High speed changes combat | Reduce simulation speed; longer simulation steps can skip collisions. |
| Build compiler missing | Install or enable .NET Framework 4.5 or newer with the 64-bit compiler at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. |
| Build cannot overwrite the output | Close the running trainer and retry the build. |

## Source layout

| File | Purpose |
| --- | --- |
| `trainer/build.ps1` | Builds the standalone x64 Windows executable using an explicit production source list |
| `trainer/Program.cs` | Windows Forms entry point and compatibility command dispatch |
| `trainer/TrainerWindow.cs` | Five-tab interface, option validation, request status, and creator links |
| `trainer/AzerothTrainer.cs` | Windows process/memory APIs and thread suspension |
| `trainer/HookEngine.cs` | Supported build data, patch lifecycle, session management, resources, and profile backups |
| `trainer/AdditionalCode.cs` | Run, XP, and permanent progression hook generation |
| `trainer/RunEnhancements.cs` | Stat minimums and automatic offer selection |
| `trainer/UnwindSupport.cs` | Windows x64 unwind metadata registration for generated hooks |
| `trainer/TrainerDiagnostics.cs` | On-disk and live compatibility reporting |

Only production source, the build script, this README, and ignore rules are published. Build outputs, game files, tests, local instructions, research notes, screenshots, reports, saves, and backups stay local.
