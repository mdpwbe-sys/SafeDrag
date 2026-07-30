# Script de désenregistrement (Exécuter en tant qu'Administrateur)
$dllPath = "$PSScriptRoot\bin\Debug\net10.0-windows\SafeExplorer.Hook.comhost.dll"

Write-Host "🗑️ Désenregistrement du serveur COM..." -ForegroundColor Yellow
if (Test-Path $dllPath) {
    regsvr32 /u /s "$dllPath"
}

$keys = @(
    "Registry::HKEY_CLASSES_ROOT\Directory\ShellEx\CopyHookHandlers\SafeExplorer",
    "Registry::HKEY_CLASSES_ROOT\Folder\ShellEx\CopyHookHandlers\SafeExplorer",
    "Registry::HKEY_CLASSES_ROOT\Drive\ShellEx\CopyHookHandlers\SafeExplorer"
)

foreach ($key in $keys) {
    if (Test-Path $key) {
        Remove-Item -Path $key -Recurse -Force
        Write-Host "❌ Clé supprimée : $key" -ForegroundColor Red
    }
}

Write-Host "🔄 Redémarrage de Windows Explorer..." -ForegroundColor Yellow
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Process explorer.exe

Write-Host "✅ Désenregistrement terminé avec succès !" -ForegroundColor Green
