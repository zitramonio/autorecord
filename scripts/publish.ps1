$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Autorecord.App\Autorecord.App.csproj"
$output = Join-Path $root "artifacts\publish\Autorecord"
$localDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }
$vendorWorker = Join-Path $root "artifacts\vendor\gigaam-worker"
$vendorWorkerExe = Join-Path $vendorWorker "worker.exe"
$vendorPyannoteWorker = Join-Path $root "artifacts\vendor\pyannote-community-worker"
$vendorPyannoteWorkerExe = Join-Path $vendorPyannoteWorker "worker.exe"

if (!(Test-Path $vendorWorkerExe)) {
    throw "GigaAM worker artifact is missing: $vendorWorkerExe. Build it with tools\gigaam-worker\build.ps1 before publishing."
}

if (!(Test-Path $vendorPyannoteWorkerExe)) {
    throw "Pyannote Community worker artifact is missing: $vendorPyannoteWorkerExe. Build it with tools\pyannote-community-worker\build.ps1 before publishing."
}

& $dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $output

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$publishWorker = Join-Path $output "workers\gigaam"
if (Test-Path $publishWorker) {
    Remove-Item $publishWorker -Recurse -Force
}

New-Item -ItemType Directory -Path $publishWorker -Force | Out-Null
Copy-Item (Join-Path $vendorWorker "*") $publishWorker -Recurse -Force
Write-Host "GigaAM worker copied: $publishWorker"

$publishPyannoteWorker = Join-Path $output "workers\pyannote-community"
if (Test-Path $publishPyannoteWorker) {
    Remove-Item $publishPyannoteWorker -Recurse -Force
}

New-Item -ItemType Directory -Path $publishPyannoteWorker -Force | Out-Null
Copy-Item (Join-Path $vendorPyannoteWorker "*") $publishPyannoteWorker -Recurse -Force
Write-Host "Pyannote Community worker copied: $publishPyannoteWorker"

Write-Host "Done: $(Join-Path $output 'Autorecord.App.exe')"
