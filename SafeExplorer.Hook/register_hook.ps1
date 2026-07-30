# Script d'enregistrement (Exécuter en tant qu'Administrateur)
$clsid = "{7B896489-3F1E-4E7B-8B2B-987813C12A34}"
$dllPath = "$PSScriptRoot\bin\Debug\net10.0-windows\SafeExplorer.Hook.comhost.dll"

if (-not (Test-Path $dllPath)) {
    Write-Host "❌ DLL non trouvée à l'emplacement: $dllPath" -ForegroundColor Red
    exit 1
}

Write-Host "📦 Enregistrement du serveur COM : $dllPath" -ForegroundColor Yellow
regsvr32 /s "$dllPath"

$keys = @(
    "Registry::HKEY_CLASSES_ROOT\Directory\ShellEx\CopyHookHandlers\SafeExplorer",
    "Registry::HKEY_CLASSES_ROOT\Folder\ShellEx\CopyHookHandlers\SafeExplorer",
    "Registry::HKEY_CLASSES_ROOT\Drive\ShellEx\CopyHookHandlers\SafeExplorer"
)

foreach ($key in $keys) {
    if (-not (Test-Path $key)) {
        New-Item -Path $key -Force | Out-Null
    }
    Set-ItemProperty -Path $key -Name "(Default)" -Value $clsid
    Write-Host "✅ Clé enregistrée : $key -> $clsid" -ForegroundColor Green
}

Write-Host "🔄 Redémarrage de Windows Explorer pour charger le Hook..." -ForegroundColor Yellow
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Process explorer.exe

Write-Host "✅ Enregistrement du Shell Extension Hook terminé avec succès !" -ForegroundColor Green
