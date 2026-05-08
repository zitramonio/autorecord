$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$venv = Join-Path $PSScriptRoot ".venv"
$python = Join-Path $venv "Scripts\python.exe"
$output = Join-Path $root "artifacts\vendor\gigaam-worker"

if (!(Test-Path $python)) {
    py -3.10 -m venv $venv
}

& $python -m pip install --upgrade pip
& $python -m pip install -r (Join-Path $PSScriptRoot "requirements.txt") pyinstaller

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

& $python -m PyInstaller `
    --noconfirm `
    --clean `
    --onedir `
    --name worker `
    --distpath $output `
    --workpath (Join-Path $PSScriptRoot "build") `
    --specpath $PSScriptRoot `
    --collect-all gigaam `
    --collect-all hydra `
    --collect-all omegaconf `
    --collect-all sentencepiece `
    (Join-Path $PSScriptRoot "worker.py")

$workerDir = Join-Path $output "worker"
$workerExe = Join-Path $workerDir "worker.exe"
if (!(Test-Path $workerExe)) {
    throw "PyInstaller did not create worker.exe."
}

Get-ChildItem $workerDir -Force | Move-Item -Destination $output -Force
Remove-Item $workerDir -Recurse -Force

Write-Host "Done: $(Join-Path $output 'worker.exe')"
