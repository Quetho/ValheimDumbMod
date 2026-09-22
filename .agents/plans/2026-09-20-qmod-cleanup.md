## Goal

Nettoyer, optimiser et réorganiser le mod Qmod (19 fichiers C#, ~7000 lignes) sans changer aucun comportement visible : factoriser le code dupliqué, expliciter le flux de ticks caché, découper la classe géante ThorTp (1828 lignes), réorganiser ModConfig et les sections du fichier `.cfg` avec migration des réglages existants, ranger les fichiers en dossiers, corriger les micro-bugs et appliquer des micro-optimisations ciblées.

## Success Criteria

- Le mod compile sans erreur ni nouveau warning (méthode Roslyn directe, voir Validation).
- Comportement en jeu strictement identique : mêmes features, mêmes defaults, mêmes textes affichés.
- Les réglages et keybinds existants de l'utilisateur sont préservés (migration du `.cfg` + backup).
- Plus de flux caché : chaque tick est déclenché depuis un point d'entrée explicite.
- ThorTp découpé en fichiers partiels à responsabilité unique ; chaque dossier regroupe une feature.
- Aucun commit : le travail reste non commité pour revue.

## Context And Current Facts

- Projet : mod Valheim BepInEx + Jotunn, C# net48, namespace unique `Qmod`, 19 `.cs` à plat dans `Qmod/`. Entrée : `Qmod/Qmod.cs` (`Awake` : Bind config, Init graphismes, `Harmony.CreateAndPatchAll`).
- Config : `Qmod/ModConfig.cs`, ~50 entrées, 8 sections hétérogènes (`0. Graphiques`, `0. Nuage magique`, `Eclair`, `Farming`, `HUD`, `Camera`, `Keybinds`, `Craft`). Tous les toggles clavier sauf Nuage/Éclair sont isolés dans `Keybinds`, loin de leur feature.
- Flux de ticks caché actuel : patch `Hud.Update` → `StatusEffectHud.Tick` → `ModInput.Tick` + `UnarmedHudHider.Tick` → `CinematicIdleCamera.Tick` (en plus de son tick via patch `GameCamera.GetCameraPosition`, avec garde anti-double par frame). `MagicBush.Tick` vient d'un patch `GameCamera.LateUpdate`.
- Duplications relevées : message HUD centré (6+ sites : `SuperSampling.NotifyToggle`, `WaterShader.NotifyToggle`, `BoarButcher.ShowPouik`, `MagicBush.TrySummon`, `ChestPull`, `ThorTp`), garde joueur local `!player || IsDead || IsTeleporting` (~12 sites avec variantes), résolution de prefab `ZNetScene.GetPrefab` + fallback + warning (4 sites), forward aplati (3 sites).
- `ThorTp.cs` (1828 lignes) mélange : construction UI (panel/pages/onglets/lignes), voyage, pull/go-to joueurs, scan d'atterrissage, RPC, coroutines, persistance.
- Persistance TP : fichier nommé `com.aeons.qmod.tp.json` mais contenu TSV maison (`W\t`, `P\t`) ; `Escape` double les `\` mais `Unescape` est l'identité (noms avec `\` corrompus au round-trip).
- Points chauds perf : `ThorBolt.AimMask()` refait `LayerMask.GetMask` (9 strings) à chaque scan ; `CinematicIdleCamera` fait ~6 réflexions `GetField` par frame quand actif ; `BoarButcher` (patch sur chaque `Damage` du jeu) fait les vérifications string avant les vérifications pas chères.
- Divers : champ `MagicBush.visual` écrit mais jamais relu ; `using UnityEngine;` inutilisé dans `BoarButcher.cs` ; mélange `Object.Destroy` / `UnityEngine.Object.Destroy` ; `PullPlayer` : condition `distant == false || from != Vector3.zero` équivalente à "position connue" ; scripts `rename.sh` / `RenameSolution.ps1` = restes one-shot du template JotunnModStub déjà appliqué ; `Qmod/README.md` et `Package/README.md` = stubs vides du template.
- Pas de harnais de test dans le repo ; le build MSBuild complet est impossible dans cet environnement (sandbox sans réseau, verrou NuGet) ; la compilation directe Roslyn contre les assemblies publicized du jeu est la méthode de vérification établie (verte à 1.0.44).

## Constraints And Non-goals

- Contraintes : zéro changement de comportement visible ; namespace `Qmod` inchangé ; noms de sections/clés du `.cfg` migrés avec préservation des valeurs + backup ; pas de commit/push ; pas de nouveau framework de test.
- Non-goals : aucune nouvelle feature ; aucun changement de defaults, de textes affichés ou de tuning (dégâts, vitesses, rayons) ; pas de renommage de classes/membres publics internes au-delà du strict nécessaire ; pas de migration Unity ou Jotunn ; pas de réécriture du format de persistance TP (on garde le TSV, on corrige juste l'extension et l'escape).

## Key Decisions

- **Découpe ThorTp en `partial class` plutôt qu'en classes séparées** : même classe, fichiers à responsabilité unique (`ThorTp.Menu.cs`, `ThorTp.Travel.cs`, `ThorTp.Players.cs`, `ThorTp.Store.cs`). Risque quasi nul (aucun changement d'API interne), revue facile. Rejeté : extraction en classes `TpMenu`/`TpTravel`/etc., trop invasif pour un gain identique à la relecture.
- **Dossiers physiques, namespace inchangé** : `Core/`, `Camera/`, `Combat/`, `Craft/`, `Farming/`, `Graphics/`, `Hud/`, `Mounts/`. Le `.csproj` SDK-style globs `**/*.cs` : aucun changement projet. Rejeté : namespaces par dossier (`Qmod.Combat`), churn inutile pour un mod solo.
- **Ticks explicites** : `ModInput.Tick()` déplacé dans `Qmod.Update` (tous ses appels se gardent eux-mêmes : pas de joueur en menu = no-op, vérifié site par site) ; le patch `Hud.Update` appelle directement `UnarmedHudHider.Tick` + `StatusEffectHud.Tick` ; suppression de l'appel redondant `CinematicIdleCamera.Tick()` dans `UnarmedHudHider` (déjà tické chaque frame via le patch `GetCameraPosition` → `TryOverride` → `Tick`, garde de frame inchangée).
- **Config : renommage des sections + co-localisation des keybinds, avec migration fichier**. Sections cibles : `01. Graphiques`, `02. Nuage magique`, `03. Eclair`, `04. Craft`, `05. Farming`, `06. HUD`, `07. Camera`, suppression de `Keybinds` (chaque bind rejoint sa feature, noms d'entrées inchangés). Migration : réécriture idempotente du `.cfg` avant `Bind` (parse en sections, applique renommages/déplacements, écrit + backup `.bak` uniquement si changement), puis `Config.Reload()`, puis `Bind`, `ScrubOrphans` conservé. Rejeté : renommage sans migration (perte des réglages utilisateur).
- **Fichier TP `.json` → `.txt`** avec copie one-shot de l'ancien vers le nouveau s'il existe (ancien fichier conservé, jamais supprimé) ; `Escape` simplifié (plus de doublement `\`, inutile en TSV) pour symétrie avec `Unescape`.
- **Helpers `Util` minimaux, comportement prouvé identique** : `NotifyCenter(string)`, `ResolvePrefab(name, fallback?)`, `LocalPlayerAlive()`, `CanAct(player)` (= vivant + pas en téléportation), `FlatForward(yaw)`. Chaque site d'adoption est vérifié condition par condition avant remplacement.

## Recommended Approach

Refactor mécanique en 7 unités ordonnées, chacune compilée séparément : helpers d'abord (utilisés par la suite), découplage des ticks, découpe ThorTp, réorg config + migration, déplacements de fichiers en dernier (diffs fonctionnels revus avant les purs déplacements), puis perf/micro-fixes et docs. Chaque unité est revue comme un tout ; l'ordre minimise les conflits entre unités.

## Work Plan

- **U1 — Helpers `Util` + adoption** : ajouter `NotifyCenter`, `ResolvePrefab`, `LocalPlayerAlive`, `CanAct`, `FlatForward` dans `Util.cs` (déplacé dans `Core/` en U5) ; remplacer les ~25 sites dupliqués en vérifiant l'équivalence exacte des conditions (les variantes `InBed`/`IsAttached`/messages spécifiques restent aux appelants). Fichiers : `Util.cs`, `BoarButcher`, `BoarSpawn`, `ChestPull`, `CultivatorHarvest`, `MagicBush`, `StatusEffectHud`, `SuperSampling`, `WaterShader`, `ThorBolt`, `ThorLightning`, `ThorTp`, `UnarmedHudHider`.
- **U2 — Ticks explicites** : `Qmod.Update` appelle `ModInput.Tick()` ; patch `Hud.Update` appelle `UnarmedHudHider.Tick(hud)` puis `StatusEffectHud.Tick(hud)` (réduit à `EnsureCreated` + `Refresh`) ; retirer `CinematicIdleCamera.Tick()` de `UnarmedHudHider.Tick`. Fichiers : `Qmod.cs`, `StatusEffectHud.cs`, `UnarmedHudHider.cs`.
- **U3 — Découpe ThorTp** : `partial class ThorTp` en 4 fichiers (`Menu` : panel/pages/onglets/lignes/spawn ; `Travel` : `Travel*`, landing scan, coroutines, guardian ; `Players` : lignes joueurs, pull/go-to, resolve peer, RPC ; `Store` : `TpPoint`, load/save/parse/escape). + fix `Escape`, + renommage `.json`→`.txt` avec copie one-shot, + simplification de la condition `PullPlayer`. Fichiers : `ThorTp*.cs` (créés dans `Combat/`).
- **U4 — Réorg `ModConfig`** : `Bind()` découpé en méthodes par feature (`BindGraphics`, `BindCloud`, `BindLightning`, `BindCraft`, `BindFarming`, `BindHud`, `BindCamera`), constantes pour toutes les sections, nouvelles sections `01.`–`07.` + keybinds co-localisés (table exacte : Supersampling/Water→Graphiques ; Cultivate/SpawnBoar→Farming ; Status/Unarmed/Hugin→HUD ; Yotei/Cinematic/Swap→Camera), migration idempotente du `.cfg` + backup `.bak`, garde anti-reload sur l'écriture propre dans `Qmod`. Fichiers : `ModConfig.cs`, `Qmod.cs`.
- **U5 — Dossiers** : déplacements seuls, namespace inchangé : `Core/` (`Qmod`, `ModConfig`, `ModInput`, `Util`), `Camera/` (`YoteiCamera`, `CinematicIdleCamera`), `Combat/` (`ThorLightning`, `ThorBolt`, `ThorTp*`), `Craft/` (`ChestPull`, `CultivatorHarvest`), `Farming/` (`BoarButcher`, `BoarSpawn`), `Graphics/` (`SuperSampling`, `WaterShader`), `Hud/` (`StatusEffectHud`, `UnarmedHudHider`, `HuginMute`), `Mounts/` (`MagicBush`). Aucun changement de contenu.
- **U6 — Perf + micro-fixes** : cache statique du masque `AimMask` (ThorBolt) ; cache statique des `FieldInfo` DOF + reset du cache dans `Stop()` (CinematicIdleCamera) ; réordonnancement des gardes `BoarButcher` (pas cher d'abord) ; `MagicBush.visual` → variable locale ; suppression du `using` inutile ; normalisation `Object.Destroy`. Fichiers : `ThorBolt.cs`, `CinematicIdleCamera.cs`, `BoarButcher.cs`, `MagicBush.cs`, `StatusEffectHud.cs`, `ThorTp.Menu.cs`.
- **U7 — Docs + version** : remplir `Qmod/README.md` (features, install, changelog, known issues) ; resynchroniser `Package/README.md` + `manifest.json` ; supprimer `scripts/rename.sh` et `scripts/RenameSolution.ps1` (one-shot obsolètes) ; bump version 1.0.44 → 1.0.45 (`Qmod.cs` + `manifest.json`).

## Validation Plan

- Après chaque unité U1–U7 : compilation Roslyn directe de tous les sources contre les DLL publicized (`Qmod/bin/Debug/net48` + ref assemblies net48 + facade netstandard), 0 erreur exigé ; le fichier de réponse existe déjà (régénérer les chemins après U5).
- Revue ciblée par unité : U1 (équivalence des conditions remplacées), U2 (garde de frame + no-op en menu), U3 (aucune logique changée hors les 3 fix listés), U4 (migration testée sur une copie du `.cfg` : valeurs préservées, backup créé, idempotence au 2e passage).
- Check in-game manuel (par l'utilisateur, le jeu ne tourne pas ici) : ouvrir forge/établi (bouton coffres), menu TP (destinations conservées), toggles clavier, caméra Yotei + cinématique idle après 60 s, nuage, éclair ; vérifier `com.aeons.qmod.cfg.bak` créé une fois.
- Étape la plus risquée : U4 (migration du `.cfg`) — validation par test sur copie + backup obligatoire avant toute écriture.

## Risks / Rollback

- Risque principal : migration `.cfg` défectueuse → réglages perdus. Mitigations : backup `.bak` systématique avant écriture, migration idempotente, test sur copie, `ScrubOrphans` conservé après.
- Risque secondaire : découplage des ticks (U2) change un ordre d'appel implicite. Mitigation : aucune dépendance d'ordre entre `ModInput`, `UnarmedHudHider` et `StatusEffectHud` (vérifié : états disjoints), gardes conservées.
- Rollback : travail non commité, correctifs localisés par unité ; en cas de doute sur une unité, `sl revert` des fichiers concernés (à la demande, jamais sans validation).

## Open Questions

- Confirmer le renommage des sections `01.`–`07.` + co-localisation des keybinds (vs réorg code seul, sections inchangées) ?
- Confirmer le renommage du fichier TP `.json` → `.txt` ?
- Confirmer la suppression des scripts `rename.sh` / `RenameSolution.ps1` et le remplissage des README ?
- Confirmer le bump de version 1.0.44 → 1.0.45 ?
