param(
    [string]$GameDirectory
)

$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The 64-bit .NET Framework C# compiler is missing. Install or enable .NET Framework 4.5 or newer, then retry.'
}
$trainerRoot = Split-Path -Parent $PSScriptRoot
$packageDirectory = Join-Path $trainerRoot '.local\dependencies\iced.1.21.0'
$decoderLibrary = Join-Path $packageDirectory 'lib\net45\Iced.dll'
$packagePath = Join-Path $trainerRoot '.local\dependencies\iced.1.21.0.nupkg'
$packageHash = 'D31613617E3DDF91239BBC94A8C587849B4236E8F8A2A2BA59EA2397A1487C7A'
if (-not (Test-Path -LiteralPath $packagePath)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $packagePath) -Force | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/iced/1.21.0/iced.1.21.0.nupkg' -OutFile $packagePath
}
if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne $packageHash) { throw 'The cached decoder package failed its SHA-256 check. Remove that package and rebuild.' }
# Extract the verified assembly on every build; a changed cache DLL is never used.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageArchive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $decoderLibrary) -Force | Out-Null
    [IO.Compression.ZipFileExtensions]::ExtractToFile($packageArchive.GetEntry('lib/net45/Iced.dll'),$decoderLibrary,$true)
    [IO.Compression.ZipFileExtensions]::ExtractToFile($packageArchive.GetEntry('LICENSE.txt'),(Join-Path $packageDirectory 'LICENSE.txt'),$true)
} finally { $packageArchive.Dispose() }
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
    'src\Game\GameLocator.cs'
    'src\Game\GameImage.cs'
    'src\Game\DecodedCode.cs'
    'src\Game\RegisterFlow.cs'
    'src\Game\NativeText.cs'
    'src\Game\CatalogContracts.cs'
    'src\Game\RuntimeDiscovery.cs'
    'src\Game\ProfileBackup.cs'
    'src\Game\GameLayout.cs'
    'src\Game\BuildCatalog.cs'
    'src\UI\TrainerWindow.cs'
    'src\Hooks\HookEngine.cs'
    'src\Hooks\AdditionalCode.cs'
    'src\Hooks\RunEnhancements.cs'
    'src\Hooks\HealthCode.cs'
    'src\Hooks\RunCommands.cs'
    'src\Interop\Native.cs'
    'src\Interop\UnwindSupport.cs'
    'src\Diagnostics\TrainerDiagnostics.cs'
) | ForEach-Object { Join-Path $trainerRoot $_ }

& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ "/out:$outputPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$decoderLibrary" "/resource:$decoderLibrary,Iced.dll" "/resource:$(Join-Path $packageDirectory 'LICENSE.txt'),Iced.LICENSE.txt" "/resource:$(Join-Path $trainerRoot 'src\Assets\kofi-support.png'),AzerothTrainer.KofiSupport.png" $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Trainer compilation failed.' }
Write-Output "Built $outputPath"

if ($installDirectory) {
    $installedPath = Join-Path $installDirectory 'Azeroth Trainer Plus.exe'
    Copy-Item -LiteralPath $outputPath -Destination $installedPath -Force
    Write-Output "Installed $installedPath"
}
