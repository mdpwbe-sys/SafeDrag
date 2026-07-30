# Script d'enregistrement HKCU (Per-User)
$clsid = "{7B896489-3F1E-4E7B-8B2B-987813C12A34}"
$dllPath = (Resolve-Path "$PSScriptRoot\bin\Debug\net10.0-windows\SafeExplorer.Hook.comhost.dll").Path

Write-Host "Enregistrement dans HKEY_CURRENT_USER..." -ForegroundColor Yellow

# 1. Enregistrement du CLSID
$clsidKey = "HKCU:\Software\Classes\CLSID\$clsid"
$inprocKey = "$clsidKey\InprocServer32"

if (-not (Test-Path $inprocKey)) {
    New-Item -Path $inprocKey -Force | Out-Null
}
Set-ItemProperty -Path $inprocKey -Name "(Default)" -Value $dllPath
Set-ItemProperty -Path $inprocKey -Name "ThreadingModel" -Value "Apartment"
Write-Host "CLSID enregistre: $inprocKey -> $dllPath" -ForegroundColor Green

# 2. Enregistrement des Shell CopyHook Handlers
$handlers = @(
    "HKCU:\Software\Classes\Directory\ShellEx\CopyHookHandlers\SafeExplorer",
    "HKCU:\Software\Classes\Folder\ShellEx\CopyHookHandlers\SafeExplorer",
    "HKCU:\Software\Classes\Drive\ShellEx\CopyHookHandlers\SafeExplorer"
)

foreach ($h in $handlers) {
    if (-not (Test-Path $h)) {
        New-Item -Path $h -Force | Out-Null
    }
    Set-ItemProperty -Path $h -Name "(Default)" -Value $clsid
    Write-Host "Handler enregistre: $h -> $clsid" -ForegroundColor Green
}

Write-Host "Redemarrage de Windows Explorer..." -ForegroundColor Yellow
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Process explorer.exe

Write-Host "SafeExplorer CopyHook enregistre avec succes !" -ForegroundColor Green
