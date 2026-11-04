$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The 64-bit .NET Framework C# compiler is missing. Install or enable .NET Framework 4.5 or newer, then retry.'
}
$trainerRoot = Split-Path -Parent $PSScriptRoot
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ "/out:$trainerRoot\Azeroth Trainer Plus.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$PSScriptRoot\AzerothTrainer.cs" "$PSScriptRoot\HookEngine.cs" "$PSScriptRoot\TrainerWindow.cs" "$PSScriptRoot\Program.cs" "$PSScriptRoot\AdditionalCode.cs" "$PSScriptRoot\RunEnhancements.cs" "$PSScriptRoot\UnwindSupport.cs" "$PSScriptRoot\TrainerDiagnostics.cs"
if ($LASTEXITCODE -ne 0) { throw 'Trainer compilation failed.' }
Write-Output "Built $trainerRoot\Azeroth Trainer Plus.exe"
