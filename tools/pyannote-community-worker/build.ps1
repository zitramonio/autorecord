param(
    [string]$PythonExe = ""
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$venv = Join-Path $PSScriptRoot ".venv"
$python = Join-Path $venv "Scripts\python.exe"
$output = Join-Path $root "artifacts\vendor\pyannote-community-worker"

function Invoke-Checked {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
    }
}

if (!(Test-Path $python)) {
    if ($PythonExe) {
        Invoke-Checked $PythonExe @("-m", "venv", $venv)
        if (!(Test-Path $python)) {
            throw "Failed to create Pyannote Community worker venv with PythonExe: $PythonExe"
        }
    }
    else {
        $created = $false
        foreach ($version in @("3.10", "3.11", "3.12")) {
            py "-$version" -m venv $venv
            if ($LASTEXITCODE -eq 0 -and (Test-Path $python)) {
                $created = $true
                break
            }

            if (Test-Path $venv) {
                Remove-Item $venv -Recurse -Force
            }
        }

        if (!$created) {
            throw "Python 3.10, 3.11, or 3.12 is required to build the Pyannote Community worker. Pass -PythonExe to use a specific interpreter."
        }
    }
}

Invoke-Checked $python @("-m", "pip", "install", "--upgrade", "pip")
Invoke-Checked $python @("-m", "pip", "install", "-r", (Join-Path $PSScriptRoot "requirements.txt"), "pyinstaller")

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

Invoke-Checked $python @(
    "-m", "PyInstaller",
    "--noconfirm",
    "--clean",
    "--onedir",
    "--name", "worker",
    "--distpath", $output,
    "--workpath", (Join-Path $PSScriptRoot "build"),
    "--specpath", $PSScriptRoot,
    "--collect-all", "pyannote.audio",
    "--collect-all", "pyannote.core",
    "--collect-all", "pyannote.database",
    "--collect-all", "pyannote.pipeline",
    "--collect-all", "lightning_fabric",
    "--collect-all", "torchmetrics",
    (Join-Path $PSScriptRoot "worker.py")
)

$workerDir = Join-Path $output "worker"
$workerExe = Join-Path $workerDir "worker.exe"
if (!(Test-Path $workerExe)) {
    throw "PyInstaller did not create worker.exe."
}

Get-ChildItem $workerDir -Force | Move-Item -Destination $output -Force
Remove-Item $workerDir -Recurse -Force

Write-Host "Done: $(Join-Path $output 'worker.exe')"
