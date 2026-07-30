# SafeDrag

SafeDrag protège les glisser-déposer de fichiers sous Windows avec une autorisation volontaire : tant que le tag est rouge, le dépôt est annulé ; lorsqu'il devient vert, le dépôt est autorisé.

## Utilisation

1. Lancez `SafeDrag.exe`.
2. Commencez à glisser un fichier depuis l'Explorateur, OneCommander ou un gestionnaire exposant ses éléments à Windows UI Automation.
3. Autorisez le dépôt avec la touche configurée :
   - **Maintenir** : gardez la touche ou le clic droit enfoncé, relâchez le clic gauche, puis relâchez l'autorisation.
   - **Bascule** : appuyez une fois pour autoriser le dépôt en cours.
4. Utilisez l'icône de notification pour changer de touche, de mode ou quitter l'application.

La configuration est enregistrée dans `%LocalAppData%\SafeExplorer\Settings.json`. La journalisation est désactivée par défaut. Lorsqu'elle est activée, les journaux sont conservés au maximum 14 jours dans `%LocalAppData%\SafeExplorer\Logs`, avec une limite de 10 Mio par jour.

## Package portable

La distribution `SafeDrag-0.9.0-win-x64.zip` est autonome : elle embarque le runtime .NET Desktop corrigé et ne demande pas d'installation de .NET, de privilèges administrateur ni d'enregistrement COM. Décompressez l'archive puis lancez `SafeDrag.exe`.

Systèmes ciblés : Windows 10/11 x64, session de bureau standard. Une seule instance par utilisateur et par session peut fonctionner.

## Limites de sécurité

SafeDrag réduit les déplacements accidentels par glisser-déposer. Ce n'est pas une frontière de sécurité contre un logiciel malveillant exécuté sous le même compte.

- Une application lancée avec un niveau d'intégrité supérieur peut ne pas accepter les événements d'annulation d'une instance SafeDrag non élevée.
- Un gestionnaire de fichiers entièrement personnalisé qui n'expose pas ses éléments à UI Automation peut ne pas être détecté.
- Les opérations par ligne de commande, API, presse-papiers ou automatisation ne sont pas interceptées.
- Le binaire n'est pas signé numériquement ; Windows SmartScreen peut donc afficher un avertissement sur une autre machine.
- Les logs, lorsqu'ils sont activés, contiennent les chemins source et destination. Ils peuvent être désactivés depuis le menu.

## Construction

Prérequis : SDK .NET 10 à jour et Windows x64.

```powershell
.\scripts\package.ps1
```

Le script produit l'exécutable autonome, l'archive ZIP et `SHA256SUMS.txt` dans `artifacts\`.

Consultez [SECURITY_AUDIT.md](SECURITY_AUDIT.md) pour le résultat de l'audit et [SECURITY.md](SECURITY.md) pour signaler une vulnérabilité.
