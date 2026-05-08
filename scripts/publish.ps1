$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Autorecord.App\Autorecord.App.csproj"
$output = Join-Path $root "artifacts\publish\Autorecord"
$localDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }

& $dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $output

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$vendorWorker = Join-Path $root "artifacts\vendor\gigaam-worker"
if (Test-Path $vendorWorker) {
    $publishWorker = Join-Path $output "workers\gigaam"
    if (Test-Path $publishWorker) {
        Remove-Item $publishWorker -Recurse -Force
    }

    New-Item -ItemType Directory -Path $publishWorker -Force | Out-Null
    Copy-Item (Join-Path $vendorWorker "*") $publishWorker -Recurse -Force
    Write-Host "GigaAM worker copied: $publishWorker"
}

Write-Host "Done: $(Join-Path $output 'Autorecord.App.exe')"
