param(
    [string]$GameDirectory
)

$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The 64-bit .NET Framework C# compiler is missing. Install or enable .NET Framework 4.5 or newer, then retry.'
}
$trainerRoot = Split-Path -Parent $PSScriptRoot
$installDirectory = $null
if ($GameDirectory) {
    $installDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path
    if (-not (Test-Path -LiteralPath (Join-Path $installDirectory 'AzerothSurvivors.exe') -PathType Leaf)) {
        throw 'GameDirectory must contain AzerothSurvivors.exe.'
    }
}

$outputDirectory = Join-Path $trainerRoot 'build'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputPath = Join-Path $outputDirectory 'Azeroth Trainer Plus.exe'
$sourceFiles = @(
    'src\Program.cs'
    'src\UI\TrainerWindow.cs'
    'src\Hooks\HookEngine.cs'
    'src\Hooks\AdditionalCode.cs'
    'src\Hooks\RunEnhancements.cs'
    'src\Interop\Native.cs'
    'src\Interop\UnwindSupport.cs'
    'src\Diagnostics\TrainerDiagnostics.cs'
) | ForEach-Object { Join-Path $trainerRoot $_ }

& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ "/out:$outputPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Trainer compilation failed.' }
Write-Output "Built $outputPath"

if ($installDirectory) {
    $installedPath = Join-Path $installDirectory 'Azeroth Trainer Plus.exe'
    Copy-Item -LiteralPath $outputPath -Destination $installedPath -Force
    Write-Output "Installed $installedPath"
}
