# SafeDrag

SafeDrag est un utilitaire Windows qui protège les glisser-déposer de fichiers par une autorisation volontaire. Le dépôt est annulé tant que le tag est rouge et autorisé lorsqu'il devient vert.

La version portable 0.9.0 fonctionne avec l'Explorateur Windows, OneCommander et les gestionnaires qui exposent leurs éléments à Windows UI Automation. Elle est autonome pour Windows 10/11 x64 et ne demande ni installation de .NET, ni privilèges administrateur, ni extension Shell.

## Organisation du dépôt

- `SafeDrag/` : application WPF, documentation et script de packaging ;
- `SafeExplorer.Shared/` : modèles de configuration et de journalisation partagés ;
- `SafeExplorer.Hook.Test/` : tests Sprint 3 et durcissement IPC ;
- `SafeExplorer.Hook/` : prototype historique d'extension Shell, audité mais exclu du package portable.

## Construire le package

Depuis PowerShell avec un SDK .NET 10 à jour :

```powershell
cd SafeDrag
.\scripts\package.ps1
```

Le résultat est créé dans `SafeDrag\artifacts\`. L'utilisation détaillée se trouve dans [`SafeDrag/README.md`](SafeDrag/README.md).

## Sécurité

Consultez [`SECURITY_AUDIT.md`](SECURITY_AUDIT.md) pour l'audit de la version 0.9.0 et [`SECURITY.md`](SECURITY.md) pour signaler une vulnérabilité.
