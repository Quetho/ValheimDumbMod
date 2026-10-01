# Heidr — hypothèse d’une caméra neuve

**Statut :** prototype dans `HeidrCamera.cs`. Seidr / Yotei / AutoShoulder restent tels quels.
**Nom :** **Heidr** (vieux norrois *heiðr* : ciel clair, lumineux ; aussi le nom que la Völuspá donne à la voyante quand elle pratique le *seiðr*).
**Classe visée, plus tard :** `HeidrCamera`, clés `Heidr*`, mode à part dans le cycle.

Seidr est la pratique, le tissage déjà en jeu. Heidr est le ciel dégagé : on voit les étoiles parce que l’architecture est claire, pas parce qu’on a ajouté un blend de plus.

---

## 1. Pourquoi une caméra neuve

Seidr marche. Les contextes, l’épaule à confirmation, le hip intérieur, le skip arc 1.0.88, le mystic : c’est un instrument accordé. Le greffer encore (Composer, deux solveurs collision, pins image, ownership table, SDAZ, 1€) revient à **réécrire Seidr en gardant son état**. Chaque canal (FOV, boom, mystic, sondes) écrit déjà dans le même `extraOffset` / `fovTarget`. Les deux bugs graves (DOF pieds + nearBlur, FOV cible 0) sont nés de ça.

La littérature (§3, §12, Uncharted, Cinemachine) dit : **plusieurs modules simples + un stack**, pas un cerveau unique qui mélange désir et collision dans un SmoothDamp.

Heidr repart de zéro à côté. Seidr reste le mode vivant. Le jour où Heidr est bon, on choisit. On ne tue pas Seidr pour « améliorer » Seidr.

---

## 2. Contrat de famille (écrit avant le premier offset)

Valheim : la souris **possède le yaw** chaque frame. WASD avance dans le cadre. C’est un hybride.

| Famille | Heidr |
| --- | --- |
| Explo / sprint | Uncharted léger : sondes, épaule auto, on peut croiser la caméra |
| Combat tendu / arc / build / lit / menu | GoW / RE4 : silence auto, pull-in only, le joueur vise |

Ça se décide **par canal et par état**, table au §6. Pas un lerp entre les deux familles.

---

## 3. Ce que Seidr a déjà et qu’on reprend comme *idée*

- Blender de poids de contextes, asymétrique, hystérésis.
- Confirmation d’épaule (`pendingTime`).
- Intérieur = contrat hanche + distance max, pas juste un smooth plus lent.
- Skip visée / lit / build.
- Trauma borné, roll clampé.
- Config 100 % à soi, zéro clé Yotei.

## 4. Ce qu’on ne reprend pas

- Un seul `extraOffset` qui est à la fois désir, collision, mystic, lead et respiration.
- FOV et DOF comme sous-produits du même blend.
- Sondes d’explo et pull-in de combat dans le même `allowPull` / `allowShoulder`.
- Deadzone verticale qui risque d’avaler une descente hanche (déjà soignée dans Seidr, trop fragile pour en dépendre).
- `SetTempFOV` avec une cible absurde, restore DOF incomplet.
- Autolook / yaw auto tant que la souris vise.

---

## 5. Architecture cible (atteindre les étoiles)

Le pipeline du §12, posé comme **loi** pas comme commentaire :

```
contextes (poids, hystérésis)
    → contraintes désirées   Size, Facing, LevelAt, ViewAt, FOV, DOF
        → prédiction t+0.66 s  (confirmation 0.36 + lead 0.30 ; vitesse perso + caméra)
            → légalisation       pull-in  XOR  push latéral
                → filtre           ressort critique par axe ; 1€ sur les sondes
                    → ownership      un auteur par canal, restore sinon
                        → pose         extraOffset + SetTempFOV + CameraDof
```

Vanilla `GameCamera` reste en dessous : `m_3rdOffset`, `UpdateFOV` puis `UpdateCamera`. Heidr **compose**, il ne réimplémente pas un GameCamera.

### 5.1 Modules (chacun une chose)

| Module | Contrat | Silence |
| --- | --- | --- |
| **Explore** | Size moyen, Facing ~30–40° épaule, Level At poitrine | — |
| **Sprint** | Size plus petit (recul), FOV SDAZ (flux optique écran constant) | visée, menu |
| **Combat** | Size plus grand (pull-in), ViewAt recentré, Facing réduit, **yaw auto off** | — |
| **Action** | punch underdamped borné (dodge, land) | visée |
| **Intérieur** | Level At hanche, Size max serré, plafond = hystérésis one-way | — |
| **Mystic** | template Halper : scale Size / Facing / height angle, dérive lente, DOF tête | visée, intérieur, combat |
| **Aim** (arc) | **aucun** module auto ; fade d’offset puis ClearTempFov + Restore DOF | c’est lui le propriétaire |

Un blender de **poids de contrats**, pas un blender d’offsets. Chaque module sort un *desired* (contraintes + tolérances). Le solveur mélange les desired, **puis** légalise.

**CamDroid** (Drucker 1995, vérifié par le workflow) : un module actif à la fois, branches booléennes, liste de contraintes = boîte noire. Heidr vise ça. Aim n’est pas un poids à 0.2, c’est un **cut de module**. Le solveur d’optimisation est dans la thèse MIT 1994, non lue : on commence par pins Gleicher + dead zone, pas par un CSP.

Collision après le désir, pas dedans : **grille de rays** Haigh-Hutchinson (influence si bloqué) puis **sphère ≥ near-clip** pour ne pas manger le frustum. Pull-in possible via **tau** (Lee : τ = taille / expansion) plutôt qu’un seuil en mètres.

### 5.2 Body / Aim (Cinemachine, Gleicher)

- **Body** : boom, hauteur, latéral. Ressort critique, halflife par axe (latéral vif, vertical lent, boom moyen). One-way sous plafond.
- **Aim** : pin de la **tête** dans le cadre (Gleicher through-the-lens). Dead zone 8–12 % du cadre, soft 20 %, hard au-delà. Micro-mouvement du perso **à l’intérieur de la dead zone = la caméra ne chasse pas**. C’est ça qui tue le pompage, plus qu’un smoothTime × 2.5.
- Collision **n’écrit pas** dans le damping Body. État à part (Cinemachine `PositionCorrection`).

### 5.3 Deux régimes collision (Uncharted, Oskam)

- **Explo** : éventail de sondes, push latéral, confirmation N pas. Prédiction 0.66 s (confirmation 0.36 + lead 0.30) sur le côté d’épaule (warp Halper dans le sens de `flatVel`). Option : « sphère de fuite » Oskam (où le perso va disparaître) → on prépare le push, on ne téléporte pas.
- **Combat / visée** : spherecast look-at → caméra, **pull-in only, never yaw**.

Valheim procédural : pas de roadmap Oskam monde entier. Prédiction locale + sondes, ça suffit. Rails / topo (Jovane) = donjon, plus tard, volumes.

### 5.4 Canaux

FOV, yaw auto, boom, hauteur, latéral, DOF, roll, pitch drift : **huit canaux**. Chacun a un auteur ou personne. Personne = restore vanilla **ce frame**, pas un fade vers 0.

FOV sprint = SDAZ (Igarashi) : add proportionnel à la vitesse, borné, rendu dès Aim/menu.

DOF mystic = focalLength **tête**, nearBlur seulement si proche, restore **tous** les champs (déjà le contrat CameraDof actuel). Off si intérieur ou Aim. Hillaire : le flou n’est aimé que s’il suit le regard / la tête.

Sondes (`leftClear`…) : **1€ filter** avant d’entrer dans le solveur. Follow perso : ressort critique.

### 5.5 PoV / PoA (Neitzel)

Heidr est un PoV **semi-subjectif** (Mitsehen, on voit *avec* l’avatar) + PoA intra, direct, centré.

Dès `IsDrawingBow`, le cadre **est** le PoA (Fatal Frame / Splinter Cell OTS figé). Heidr se tait. Le perso à l’écran reste l’ancre avatariale (Klevjer, Black, Carmack) : on ne le floute pas, on ne le perd pas en FOV 0, on ne clippe pas dans le mesh.

### 5.6 Debug dès le premier proto

Gizmos : dead/soft/hard zone tête, sondes, sphère de fuite, auteur de chaque canal overlay. Giors : sans ça on règle à l’aveugle.

---

## 6. Table d’ownership Heidr

Même grille que le §22 du rapport, figée ici comme spec du mode. Seidr a la sienne dans le code actuel ; Heidr naît avec celle-ci. Les 8 lignes sont des lignes d’affichage, pas 1:1 les 8 canaux possédés du §5.4 : « Yaw souris » est une entrée joueur (pas un canal) et « Latéral » n’a pas de ligne propre.

| Canal | Explo | Sprint | Combat | Arc | Lit / build | Menu / carte | Mystic | Ciné |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Yaw souris | joueur | joueur | joueur | joueur | vanilla | — | joueur | ciné |
| Yaw auto / épaule | Heidr + pending | Heidr réduit | **off** | **off** | off | off | relâché | ciné |
| Boom | Heidr | Heidr recul | pull-in | fade → vanilla | vanilla | — | Size+ | ciné |
| Hauteur | Heidr | Heidr | Heidr | fade | vanilla | — | template | ciné |
| FOV | vanilla | SDAZ Heidr | petit add | **Clear** | vanilla | vanilla | souffle si owner | ciné |
| DOF | vanilla AF | vanilla | vanilla | vanilla | vanilla | — | tête, pas intérieur | ciné |
| Roll / trauma | off | rebond | punch | **off** | off | off | clamp ±2.5° | ciné |
| Pitch drift | off | off | off | **off** | off | off | borné | ciné |

---

## 7. Ce que « les étoiles » veulent dire, concrètement

Un follow dont on ne sent plus la caméra, sauf quand un module a **le droit** de parler.

- Intérieur : hanche tenue, pas de pompe plafond, perso ancré dans la dead zone.
- Arc : vanilla visée, zéro glitch FOV, zéro mystic.
- Sprint : le monde ne pulse pas (SDAZ + Size compensé, Vertigo *volontaire* interdit en explo).
- Coin de maison : l’épaule a déjà choisi le côté, 0.3 s avant le mur.
- Idle long : mystic comme template, ou rail (Sanokho / Galvane), jamais un deuxième cerveau.
- Debug : on *voit* qui écrit le yaw ce frame.

Toric, PVR 32×32, ML Burelli, Corps-Monteur : réserve. Lock-on deux cibles (Burg / tore) = un module Aim plus tard, pas le follow de base.

---

## 8. Coexistence

Cycle actuel : Off → Yotei → AutoShoulder → Seidr → Off.

Plus tard, **si** on code : … → Seidr → **Heidr** → Off. Config `Heidr*` isolée, même règle que Seidr vs Yotei. Fichier `Qmod/Camera/HeidrCamera.cs` séparé. On ne touche pas `SeidrCamera.cs` pour « préparer » Heidr.

Jusqu’au 2026-09-27 ce fichier était la spec. Heidr est maintenant codé (voir §9). Seidr reste le mode accordé ; Heidr est le mode en tuning.

---

## 9. Exécution 2026-09-27 + revue spec-vs-code

Implémenté en une passe, dans l’ordre : Composer + ownership (squelette `HeidrCamera.cs`, états Explo/Combat/Aim/Paused) → deux régimes collision → épaule auto à confirmation → prédiction t+0.35 s (arrière/plafond) → 1€ sur les 7 sondes → état Sprint + FOV SDAZ (patch `UpdateCamera`) → intérieur (abri/donjon/serré → hanche + Size max) → mystic + DOF tête (patch `UpdateDOF`) → action/trauma + shake (patch `GetCameraPosition`). Clés `Heidr*` isolées (16 au total), cycle `Seidr → Heidr → Off`, swap manuel branché. `SeidrCamera.cs` intact (vérifié : zéro référence Heidr). Build roslyn exit 0, `check-patches.ps1` 36 params / 0 erreur.

Revue par subagent (lecture seule, spec vs code), verdicts :

- **OK** : Composer à zones (dead 0.10 / soft 0.20, aspect corrigé), deux régimes collision (explo = clamp boom + épaule, combat = pull-in only), épaule + pending (3 pas, 0.36 s, hold sprint, gel combat/visée), prédiction arrière/plafond, SDAZ proportionnel borné + rendu, DOF tête + restore complet, mystic (template + silences), légalisation-avant-filtre, coexistence §8.
- **Écarts graves** :
  1. Ownership **déclarative, pas appliquée** : `OwnerOf()` ne gate que le Composer. FOV/DOF/pitch/roll écrivent sans consulter la table. La spec voulait une loi (§5, §6), c’est un affichage + overlay.
  2. **Blend d’offsets, pas de contrats** : les blends scalent directement shoulder/boom/hauteur dans un seul `extraOffset` — le pattern que le §4 interdit de reprendre. Pas de contraintes Size/Facing avec tolérances, pas de module actif unique CamDroid. On a un « Seidr propre », pas la rupture Heidr.
  3. **Collision = sondes unitaires** (`ProbeClearance`, un SphereCast r=0.14), pas grille Haigh-Hutchinson + sphère ≥ near-clip (§24.2). Seuils en mètres, pas de tau (§24.3).
- **Déviations mineures** : FOV fadé au lieu de Clear immédiat à l’arc (§6 : Arc = Clear) ; FOV drivé en Explo pendant la décroissance de `combatBlend` (owner = Vanilla) ; DOF résiduel appliqué pendant le skip Aim au lieu de Restore ; souffle ±0.05° même à mystic = 0 (§6 : off hors Mystic) ; pas d’état Menu (Heidr drive en menu, seul l’idle reset) ; ViewAt combat non recentré (ancre fixe, gain réduit seulement) ; punch en MoveTowards, pas underdamped ; FOV Action +3 non prévu ; overlay 2D complet mais pas de gizmos 3D ni sphère de fuite ; pas de prédiction côté épaule (§13.4 partiel).
- **Codé non prévu** : détection donjon + `confineBlend` (idée Seidr réutilisée) ; `mystic > 0.6` bloque l’auto-épaule ; FOV Action +3 (voir ci-dessus).
- **Risques ouverts** : tuning 1€ / zones / SDAZ jamais validé en jeu ; `mysticBlend` fadé exponentiellement mais `trauma` linéaire → résidus DOF/shake en Aim ; états Aim/Paused résolus mais corps du patch inatteignable via `ShouldSkip` → la table Aim ne gouverne que l’overlay debug ; montures (`MagicBush`) non gérées (ni sprint ni boom — le Composer compense seul, non testé).

Suites proposées, dans l’ordre : (1) petits bugs réels (Clear FOV arc, Restore DOF en Aim, souffle à mystic = 0, état Menu) ; (2) ownership exécutée (chaque writer consulte `OwnerOf()`) ; (3) contrats Size/Facing — le vrai chantier Heidr, à décider consciemment. Hors plan confirmé : autolook (§4), rails/topo, lock-on.

### Corrections 2026-09-27 (bugs + ownership)

(1) et (2) sont dans `HeidrCamera.cs`. Le FOV hors propriétaire passe par `ClearTempFov` ce frame (arc, menu, explo, lit, build), y compris pendant la décroissance de `combatBlend`. Le DOF mystic est Restore dès que l’owner n’est plus Heidr ou que le poids tombe sous 0,02, y compris en skip. Le pitch n’a plus de plancher à mystic = 0, et le template mystic ne s’applique qu’en explo. `HeidrState.Menu` (`IsMenuBlocking`) prend le même cut que l’arc : le corps du patch ne drive pas, l’idle est remis à zéro. Roll, pitch, yaw auto, FOV, DOF, latéral, hauteur et boom consultent `OwnerOf` avant d’écrire. Le FOV d’action +3 n’est plus écrit (absent de la table). Le punch spatial d’action (boom) reste.

### Pipeline 2026-09-27 (fermeture du plan)

Le blend d'offsets est remplacé par un contrat. Un module primaire (Explo, Sprint ou Combat) émet Size, Facing, LevelAt, ViewAt. Le fondu de `combatBlend` lerp deux contrats, il ne multiplie plus un offset. L'intérieur contraint le LevelAt (hanche) et pose un Size max. Le mystic scale le contrat d'explo seulement. Le Composer pin le ViewAt du contrat : au combat il est recentré (x = 0, y = 0,5). Tolérances sur facing et size ; la descente de hauteur n'est pas avalée.

La collision est une correction après le ressort. Sphère de rayon ≥ near-clip, grille 3×3 vers la tête, frein quand le tau arrière passe sous 0,5 s. Explo pousse latéralement si l'autre côté est libre, sinon pull-in. Combat : pull-in seulement. Prédiction t+0,35 s aussi sur les côtés : le score d'épaule prend le min(présent, futur). Au mystic la marge d'épaule s'élargit au lieu de bloquer le swap. Le punch d'action est un oscillateur sous-amorti borné, ajouté après le ressort. `MagicBush` : poussée = sprint, combat coupé, Size +0,45 et LevelAt +0,5. Overlay : contrat, tau, sondes projetées, sphère de fuite.

Pas de solveur CSP (thèse Drucker non lue) : pins Gleicher + dead zone, comme le §5.1 le demande. Hors plan, confirmé : autolook, rails / topo, lock-on. Le tuning (1€, zones, SDAZ, tau) reste à valider en jeu.

### Exécution 2026-09-27 (ce qui empêchait la spec de se sentir)

Le pull de la sphère near-clip n'est plus ramené à 0,75 m : il passe en entier. Tau et grille ne rajoutent que le déficit que la sphère n'a pas déjà couvert, lissé à part (0,08 s) et borné à 0,60 m. La grille 3×3 inclut le rayon central et ne pousse plus de côté. Les sondes d'environnement utilisent cette même sphère, à la place du `ProbeClearance` de rayon 0,14. Le tau lit la fermeture brute (échantillon présent, et écart présent/futur sur l'horizon), pas la dérivée du 1€. Le présent est filtré ; le futur latéral et arrière reste brut. Le plafond futur reste filtré.

L'horizon d'épaule est la confirmation (0,36 s) plus 0,30 s, cap 5,5 m, pour que le côté soit choisi avant le mur. Le FOV de sprint suit la vitesse seule.
### Correctif 2026-10-01 (SPC-01)

La spec disait « t+0.35 s » (§5, §5.3, §9 pipeline) mais le code a toujours calculé horizon = confirmation + lead = 0,66 s (`PredictHorizon`). Le 0,35 s était une scorie de rédaction, pas une valeur codée. Tranché : **0,66 s partout** (const `PredictHorizon`, §5, §5.3). Les mentions 0.35 du §9 ci-dessus sont historiques, ne pas les « recorriger ». Le boom retire l'équivalent angulaire de cet ajout, donc le mur qui coupe le recul ne coupe plus le FOV en même temps. L'intérieur ne rallonge plus le `smoothTime`. `actionBlend` n'est plus calculé : le punch part sur le front de roulade et d'atterrissage.
