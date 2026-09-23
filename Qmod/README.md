# Qmod

Mod Valheim fourre-tout : graphismes, caméra, HUD, craft, éclair et téléportation.
Requiert BepInEx et Jotunn (voir `manifest.json`).

## Installation (manual)

1. Installer BepInExPack_Valheim puis Jotunn.
2. Copier `Qmod.dll` dans `<Valheim>/BepInEx/plugins/Qmod/`.
3. Lancer le jeu. La config est dans `<Valheim>/BepInEx/config/com.aeons.qmod.cfg`
   (rechargée à chaud quand le fichier change).

## Features

- **Graphismes** : supersampling (SSAA) avec multiplicateur réglable, look d'eau
  amélioré (normals, réfraction, foam). Toggles clavier disponibles.
- **Nuage magique** : buisson volant chevauchable, appel/renvoi au clavier,
  vol près du sol ou suivi du regard, plafond et vitesses réglables.
- **Éclair** : frappe d'éclair sur soi (visible par tous), foudre dirigée
  (dégâts + stagger), menu de téléportation avec destinations persistées
  par monde, TP vers/à les joueurs, et page de spawn de ressources.
- **Craft** : bouton "Pull" dans l'onglet fabrication
  (forge/établi) qui prend les matériaux manquants dans les coffres
  proches (rayon réglable, défaut 50 m). Le pull du marteau reprend
  les mêmes coffres. Bouton coffre au-dessus de
  l'armure, à droite de l'inventaire (et raccourci), pour ranger
  l'inventaire dans les coffres proches qui ne contiennent qu'une
  ressource et ont de la place. Pull et rangement n'utilisent que les
  coffres posés par le joueur ; un coffre sans poseur connu est ignoré
  s'il est dans la zone d'une balise qui appartient à quelqu'un d'autre. Le transfert est bloqué 10 s avant
  et 10 s après la sauvegarde serveur (bouton rouge). Le mode cultiver du cultivateur
  récolte les légumes prêts.
- **Farming** : message configurable au kill sanglier avec le butcher knife,
  spawn d'un sanglier 2 étoiles apprivoisé.
- **HUD** : liste des bonus/malus en bas à droite, masquage auto du HUD
  mains vides après un délai, Hugin (tutos) désactivé.
- **Caméra** : mode Yotei (épaule auto gauche/droite, recul en exploration
  et sprint, zoom combat, FOV sprint) et caméra cinématique après
  inactivité (sujet joueur/PNJ, profondeur de champ).

Tous les toggles clavier sont non bindés par défaut : les définir dans
le fichier de config, sections `01.` à `07.`.

## Changelog

- **1.0.45** : réorganisation interne (dossiers par feature, ThorTp découpé,
  sections config `01.`–`07.` avec migration automatique + backup `.bak`,
  keybinds regroupés avec leur feature), fix du round-trip des noms de
  destinations TP, fichier TP renommé en `.txt`, micro-optimisations.
- **1.0.44** : bouton "Récupérer (coffres)" forge/établi.

## Known issues

- Le supersampling est gourmand en GPU ; baisser le multiplicateur si besoin.
- La récupération depuis les coffres côté client sur serveur dédié est
  peu testée (fonctionne en solo et en hébergé).
