param(
    [string]$DotNetPath = "dotnet",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.9.0"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "SafeDrag.csproj"
$artifactRoot = Join-Path $projectRoot "artifacts"
$packageName = "SafeDrag-$Version-$Runtime"
$publishDir = Join-Path $artifactRoot $packageName
$zipPath = Join-Path $artifactRoot "$packageName.zip"

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

function Invoke-DotNet([string[]]$Arguments, [string]$FailureMessage) {
    # Start-Process keeps the script reliable when invoked from PowerShell,
    # Windows Terminal, or through WSL interoperability.
    $process = Start-Process `
        -FilePath $DotNetPath `
        -ArgumentList $Arguments `
        -WorkingDirectory $projectRoot `
        -Wait `
        -NoNewWindow `
        -PassThru

    if ($process.ExitCode -ne 0) { throw $FailureMessage }
}

$quotedProject = '"{0}"' -f $project
$quotedPublishDir = '"{0}"' -f $publishDir

Invoke-DotNet `
    -Arguments @("restore", $quotedProject, "--runtime", $Runtime) `
    -FailureMessage "La restauration .NET a échoué."

Invoke-DotNet `
    -Arguments @(
        "publish", $quotedProject,
        "--configuration", "Release",
        "--runtime", $Runtime,
        "--self-contained", "true",
        "--no-restore",
        "--output", $quotedPublishDir,
        "-p:Version=$Version"
    ) `
    -FailureMessage "La publication .NET a échoué."

# Les symboles ne sont pas nécessaires à l'exécution et peuvent contenir des
# chemins de compilation. Le package public reste volontairement minimal.
Get-ChildItem -Path $publishDir -Filter "*.pdb" -File | Remove-Item -Force

Copy-Item (Join-Path $projectRoot "README.md") $publishDir
Copy-Item (Join-Path $projectRoot "SECURITY_AUDIT.md") $publishDir

$exePath = Join-Path $publishDir "SafeDrag.exe"
if (-not (Test-Path $exePath)) { throw "SafeDrag.exe est absent du package." }

$hash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash *SafeDrag.exe" | Set-Content -Path (Join-Path $publishDir "SHA256SUMS.txt") -Encoding ascii

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Package : $zipPath"
Write-Host "SHA-256 : $hash"
