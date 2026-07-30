# Audit de sécurité — SafeDrag 0.9.0

Date : 30 juillet 2026

## Résultat

Aucun problème bloquant connu ne reste dans le périmètre de la livraison portable après corrections. Le produit reste un garde-fou contre les erreurs utilisateur, et non un mécanisme d'isolation face à un programme malveillant local.

## Périmètre audité

- hook souris global et annulation du dépôt ;
- état Maintenir/Bascule et overlay ;
- serveur et clients IPC par named pipe ;
- configuration JSON, whitelist et détection OneDrive ;
- journalisation et rétention ;
- cycle de démarrage/arrêt et System Tray ;
- manifeste Windows, dépendances .NET et package autonome ;
- scripts et extension Shell historique, exclue du package portable.

## Corrections appliquées

- IPC limité au même utilisateur et au même niveau d'élévation avec `PipeOptions.CurrentUserOnly`.
- Première instance de pipe imposée, protocole délimité, taille maximale de 8 Kio et profondeur JSON limitée.
- Délai de 2 secondes pour empêcher un client silencieux de monopoliser le serveur.
- Commandes inconnues et requêtes incomplètes refusées par défaut.
- Sérialisation JSON native dans le client .NET du hook et délai d'une seconde côté client.
- Une seule instance SafeDrag par utilisateur/session.
- Échec visible et arrêt immédiat si le hook souris global ne peut pas être installé.
- Nettoyage complet des états du hook et des ressources System Tray à l'arrêt.
- Suppression de l'ancien service inutilisé qui modifiait les seuils globaux de glisser-déposer Windows.
- Synchronisation des listes de chemins entre le menu et l'IPC.
- Configuration bornée à 1 Mio, 256 chemins et valeurs d'énumération valides.
- Logs limités à 10 Mio par jour, rétention de 14 jours et champs bornés.
- Journalisation désactivée par défaut afin de ne pas conserver de chemins locaux sans choix explicite.
- Manifeste explicite `asInvoker`, `uiAccess=false` et chemins longs activés.
- Scan LocalMask aux sensibilités minimale et standard : aucune détection après vérification d'un faux positif lexical.
- Audit NuGet : aucun paquet vulnérable détecté.

## Seconde lecture locale par Antares-1B

Le modèle spécialisé de localisation de vulnérabilités a été exécuté localement avec Ollama sur les fichiers les plus exposés. Ses résultats ont été traités comme des hypothèses et vérifiés manuellement :

- la présence possible de chemins sensibles dans les logs est réelle et documentée ; la journalisation est désormais désactivée par défaut ;
- le signalement d'une course dans `DragMonitor` est un faux positif : le hook `WH_MOUSE_LL` est rappelé sur le thread qui l'a installé et le timer concerné utilise le même Dispatcher WPF ;
- le signalement du client `CopyHook` est compensé côté frontière de confiance par la validation bornée du serveur IPC, les délais d'attente et le pipe limité à l'utilisateur courant.

## Risques résiduels acceptés

- Un processus malveillant exécuté sous le même compte peut contourner ou perturber un utilitaire fonctionnant au même niveau de confiance.
- Windows UIPI peut empêcher l'annulation dans une application élevée lorsque SafeDrag ne l'est pas.
- La détection dépend de UI Automation ; les contrôles de fichiers entièrement personnalisés peuvent produire un faux négatif.
- L'extension Shell historique reste fail-open pour ne jamais bloquer Explorer si SafeDrag est indisponible. Elle n'est pas nécessaire ni incluse dans le package portable.
- Le binaire n'est pas signé par un certificat de signature de code ; l'intégrité est fournie par SHA-256, pas par une chaîne de confiance éditeur.
- Les journaux optionnels contiennent des chemins locaux potentiellement sensibles.

## Validations

- compilation avec analyseurs .NET complets : 0 erreur, 0 avertissement pour l'application ;
- tests Sprint 3 et tests de durcissement IPC : réussite ;
- tests manuels Explorer et OneCommander : premier drag, tag rouge, tag vert, modes Maintenir et Bascule validés ;
- publication autonome réalisée avec .NET 10.0.10, correctif LTS disponible au 14 juillet 2026 ;
- paquet limité à l'exécutable, la documentation et son empreinte SHA-256, sans symboles de débogage.
