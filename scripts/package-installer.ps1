param(
    [switch]$SkipPublish,
    [string]$OutputName = "Autorecord-Setup-WithModels.exe"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$publishDir = Join-Path $root "artifacts\publish\Autorecord"
$installerRoot = Join-Path $root "artifacts\installer"
$stagingRoot = Join-Path $installerRoot "staging"
$packageRoot = Join-Path $installerRoot "package"
$payloadZip = Join-Path $packageRoot "payload.zip"
$stubExe = Join-Path $packageRoot "AutorecordInstallerStub.exe"
$installerExe = Join-Path $installerRoot $OutputName
$installerSource = Join-Path $root "tools\installer\AutorecordInstaller.cs"
$appIcon = Join-Path $root "src\Autorecord.App\Assets\AppIcon.ico"
$localModelsRoot = Join-Path $env:LOCALAPPDATA "Autorecord\Models"
$payloadMarker = [System.Text.Encoding]::ASCII.GetBytes("AUTORECORD_PAYLOAD_V1")

$releaseModels = @()

function Assert-DirectoryExists([string]$Path, [string]$Description) {
    if (!(Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description is missing: $Path"
    }
}

function Assert-FileExists([string]$Path, [string]$Description) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
}

function Assert-ChildPath([string]$RootPath, [string]$ChildPath) {
    $fullRoot = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $fullChild = [System.IO.Path]::GetFullPath($ChildPath)
    if (!$fullChild.StartsWith($fullRoot + "\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside expected root. Root: $fullRoot; Path: $fullChild"
    }
}

function Remove-DirectoryIfExists([string]$Path) {
    Assert-ChildPath $installerRoot $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-CSharpCompiler {
    $framework64 = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (Test-Path -LiteralPath $framework64) {
        return $framework64
    }

    $framework = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
    if (Test-Path -LiteralPath $framework) {
        return $framework
    }

    throw "C# compiler for .NET Framework was not found."
}

function Copy-Stream([System.IO.Stream]$Source, [System.IO.Stream]$Destination) {
    $buffer = New-Object byte[] (1024 * 1024)
    while (($read = $Source.Read($buffer, 0, $buffer.Length)) -gt 0) {
        $Destination.Write($buffer, 0, $read)
    }
}

if (!$SkipPublish) {
    $running = Get-Process Autorecord.App, worker -ErrorAction SilentlyContinue
    if ($running) {
        $processes = ($running | ForEach-Object { "$($_.ProcessName) ($($_.Id))" }) -join ", "
        throw "Close Autorecord and transcription workers before publishing. Running: $processes"
    }

    & $publishScript
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Assert-DirectoryExists $publishDir "Publish output"
Assert-FileExists (Join-Path $publishDir "Autorecord.App.exe") "Autorecord executable"
Assert-FileExists $installerSource "Installer source"
Assert-FileExists $appIcon "Application icon"

foreach ($model in $releaseModels) {
    $modelPath = Join-Path $localModelsRoot $model.Id
    Assert-DirectoryExists $modelPath "Release model '$($model.Id)'"

    foreach ($requiredFile in $model.RequiredFiles) {
        Assert-FileExists (Join-Path $modelPath $requiredFile) "Required file for '$($model.Id)'"
    }
}

New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null
Remove-DirectoryIfExists $stagingRoot
Remove-DirectoryIfExists $packageRoot
New-Item -ItemType Directory -Path (Join-Path $stagingRoot "app") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stagingRoot "models") -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

Copy-Item -Path (Join-Path $publishDir "*") -Destination (Join-Path $stagingRoot "app") -Recurse -Force

foreach ($model in $releaseModels) {
    Copy-Item `
        -LiteralPath (Join-Path $localModelsRoot $model.Id) `
        -Destination (Join-Path $stagingRoot "models") `
        -Recurse `
        -Force
}

if (Test-Path -LiteralPath $payloadZip) {
    Remove-Item -LiteralPath $payloadZip -Force
}

$payloadItems = Get-ChildItem -LiteralPath $stagingRoot -Force
Compress-Archive -Path $payloadItems.FullName -DestinationPath $payloadZip -CompressionLevel Optimal -Force

$csc = Get-CSharpCompiler
$references = @(
    "/reference:System.IO.Compression.dll",
    "/reference:System.IO.Compression.FileSystem.dll",
    "/reference:System.Web.Extensions.dll",
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.dll",
    "/reference:System.Core.dll"
)

& $csc `
    "/nologo" `
    "/optimize+" `
    "/target:winexe" `
    "/platform:anycpu" `
    "/win32icon:$appIcon" `
    "/out:$stubExe" `
    $references `
    $installerSource

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (Test-Path -LiteralPath $installerExe) {
    Remove-Item -LiteralPath $installerExe -Force
}

$stubStream = [System.IO.File]::OpenRead($stubExe)
$payloadStream = [System.IO.File]::OpenRead($payloadZip)
$outputStream = [System.IO.File]::Create($installerExe)
try {
    Copy-Stream $stubStream $outputStream
    Copy-Stream $payloadStream $outputStream

    $lengthBytes = [System.BitConverter]::GetBytes([int64]$payloadStream.Length)
    $outputStream.Write($lengthBytes, 0, $lengthBytes.Length)
    $outputStream.Write($payloadMarker, 0, $payloadMarker.Length)
}
finally {
    $outputStream.Dispose()
    $payloadStream.Dispose()
    $stubStream.Dispose()
}

Assert-FileExists $installerExe "Installer"

$payload = Get-Item -LiteralPath $payloadZip
$installer = Get-Item -LiteralPath $installerExe

[pscustomobject]@{
    Installer = $installer.FullName
    InstallerMiB = [math]::Round($installer.Length / 1MB, 1)
    PayloadMiB = [math]::Round($payload.Length / 1MB, 1)
    IncludesModels = ($releaseModels.Id -join ", ")
} | Format-List
