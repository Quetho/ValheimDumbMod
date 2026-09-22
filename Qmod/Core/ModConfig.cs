using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace Qmod
{
    internal static class ModConfig
    {
        private const string Graphics = "01. Graphiques";
        private const string Cloud = "02. Nuage magique";
        private const string Lightning = "03. Eclair";
        private const string Craft = "04. Craft";
        private const string Farming = "05. Farming";
        private const string Hud = "06. HUD";
        private const string Camera = "07. Camera";
        private const string Build = "08. Construction";
        private const string Mounts = "09. Montures";
        private const string Debug = "10. Debug";
        private const string Ranges = "11. Rayons";

        internal static ConfigEntry<bool> SupersamplingEnabled;
        internal static ConfigEntry<float> SupersamplingScale;
        internal static ConfigEntry<bool> WaterShaderEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleSupersampling;
        internal static ConfigEntry<KeyboardShortcut> ToggleWaterShader;

        internal static ConfigEntry<bool> MagicBushEnabled;
        internal static ConfigEntry<string> MagicBushPrefab;
        internal static ConfigEntry<float> MagicBushSpeed;
        internal static ConfigEntry<float> MagicBushSprintSpeed;
        internal static ConfigEntry<float> MagicBushAcceleration;
        internal static ConfigEntry<float> MagicBushMaxAltitude;
        internal static ConfigEntry<float> MagicBushScale;
        internal static ConfigEntry<KeyboardShortcut> ToggleMagicBush;
        internal static ConfigEntry<KeyboardShortcut> MagicBushThrust;
        internal static ConfigEntry<KeyboardShortcut> MagicBushBrake;
        internal static ConfigEntry<KeyboardShortcut> MagicBushStrafeUp;
        internal static ConfigEntry<KeyboardShortcut> MagicBushStrafeDown;

        internal static ConfigEntry<bool> LightningEnabled;
        internal static ConfigEntry<bool> LightningMessage;
        internal static ConfigEntry<KeyboardShortcut> StrikeLightning;
        internal static ConfigEntry<KeyboardShortcut> ToggleTpMenu;
        internal static ConfigEntry<KeyboardShortcut> AimLightning;

        internal static ConfigEntry<bool> CraftPullEnabled;
        internal static ConfigEntry<float> CraftPullRadius;

        internal static ConfigEntry<string> KillMessage;
        internal static ConfigEntry<bool> CultivateHarvestEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleCultivateHarvest;
        internal static ConfigEntry<KeyboardShortcut> SpawnBoar;

        internal static ConfigEntry<bool> StatusHudEnabled;
        internal static ConfigEntry<bool> UnarmedHudHideEnabled;
        internal static ConfigEntry<float> UnarmedHudHideDelay;
        internal static ConfigEntry<bool> HuginDisabled;
        internal static ConfigEntry<bool> FireplaceSmokeEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleStatusHud;
        internal static ConfigEntry<KeyboardShortcut> ToggleUnarmedHudHide;
        internal static ConfigEntry<KeyboardShortcut> ToggleHugin;

        internal static ConfigEntry<bool> YoteiCameraEnabled;
        internal static ConfigEntry<bool> YoteiAutoShoulder;
        internal static ConfigEntry<float> YoteiShoulderOffset;
        internal static ConfigEntry<float> YoteiDistanceBoost;
        internal static ConfigEntry<float> YoteiSprintDistance;
        internal static ConfigEntry<float> YoteiCombatZoom;
        internal static ConfigEntry<float> YoteiHeightOffset;
        internal static ConfigEntry<float> YoteiSmoothness;
        internal static ConfigEntry<float> YoteiSprintFov;
        internal static ConfigEntry<bool> CinematicIdleEnabled;
        internal static ConfigEntry<float> CinematicIdleDelay;
        internal static ConfigEntry<float> CinematicSubjectRadius;
        internal static ConfigEntry<KeyboardShortcut> ToggleYoteiCamera;
        internal static ConfigEntry<KeyboardShortcut> ToggleCinematicIdle;
        internal static ConfigEntry<KeyboardShortcut> StartCinematic;
        internal static ConfigEntry<KeyboardShortcut> SwapShoulder;

        internal static ConfigEntry<bool> FreePlacementEnabled;
        internal static ConfigEntry<bool> BuildPullEnabled;

        internal static ConfigEntry<bool> WolfRideEnabled;
        internal static ConfigEntry<float> WolfRideSaddleHeight;

        internal static ConfigEntry<string> DebugUnsynchronized;

        internal static ConfigEntry<float> ComfortRadius;
        internal static ConfigEntry<float> WorkbenchRadius;
        internal static ConfigEntry<float> StonecutterRadius;
        internal static ConfigEntry<float> ArtisanRadius;
        internal static ConfigEntry<float> UpgradeRadius;

        private const string odintoken = "ODINISMYKING";

        // Seule la valeur odintoken débloque buisson magique, foudre et menu
        // TP ; toute autre valeur bloque. Paramètre global bindé (visible
        // et éditable comme les autres). Entrée absente = bloqué.
        internal static bool IsOdin()
        {
            return DebugUnsynchronized != null && DebugUnsynchronized.Value != null &&
                string.Equals(DebugUnsynchronized.Value.Trim(), odintoken, StringComparison.Ordinal);
        }

        internal static void Bind(ConfigFile config)
        {
            if (ConfigMigration.MigrateFile(config.ConfigFilePath))
            {
                config.Reload();
                Jotunn.Logger.LogInfo("Config Qmod migrée vers les sections 01.-09. (backup .bak)");
            }

            BindGraphics(config);
            BindCloud(config);
            BindLightning(config);
            BindCraft(config);
            BindFarming(config);
            BindHud(config);
            BindCamera(config);
            BindBuild(config);
            BindMounts(config);
            BindDebug(config);
            BindRanges(config);

            ScrubOrphans(config);
        }

        private static void BindGraphics(ConfigFile config)
        {
            SupersamplingEnabled = config.Bind(Graphics, "SupersamplingEnabled", true,
                "Rendu interne plus net (SSAA), puis réduit à l'écran. Plus gourmand");
            SupersamplingScale = config.Bind(Graphics, "SupersamplingScale", 1.5f,
                new ConfigDescription("Multiplicateur de résolution interne. 1 = off, 1.5 = 150 %, 2 = 200 %",
                    new AcceptableValueRange<float>(1f, 2.5f)));
            WaterShaderEnabled = config.Bind(Graphics, "WaterShaderEnabled", true,
                "Eau plus nette (normals, réfraction, foam). Visuel only, pas les vagues physiques");
            ToggleSupersampling = BindKey(config, Graphics, "ToggleSupersampling", "Activer/désactiver le supersampling");
            ToggleWaterShader = BindKey(config, Graphics, "ToggleWaterShader", "Activer/désactiver le look d'eau");
        }

        private static void BindCloud(ConfigFile config)
        {
            MagicBushEnabled = config.Bind(Cloud, "Enabled", true,
                "Nuage magique. Appeler / renvoyer avec Toggle");
            MagicBushPrefab = config.Bind(Cloud, "Prefab", "Bush01",
                "Prefab visuel. Bush01, RaspberryBush, BlueberryBush, CloudberryBush, Bush02_en...");
            MagicBushSpeed = config.Bind(Cloud, "Speed", 22f,
                new ConfigDescription("Vitesse près du sol (m/s)", new AcceptableValueRange<float>(8f, 50f)));
            MagicBushSprintSpeed = config.Bind(Cloud, "SprintSpeed", 36f,
                new ConfigDescription("Vitesse en l'air (m/s)", new AcceptableValueRange<float>(12f, 70f)));
            MagicBushAcceleration = config.Bind(Cloud, "Acceleration", 12f,
                new ConfigDescription("Réactivité (plus haut = plus pêchu)", new AcceptableValueRange<float>(4f, 30f)));
            MagicBushMaxAltitude = config.Bind(Cloud, "MaxAltitude", 250f,
                new ConfigDescription("Plafond (m)", new AcceptableValueRange<float>(40f, 1500f)));
            MagicBushScale = config.Bind(Cloud, "Scale", 1.25f,
                new ConfigDescription("Taille du buisson", new AcceptableValueRange<float>(0.6f, 3f)));
            ToggleMagicBush = config.Bind(Cloud, "Toggle", new KeyboardShortcut(KeyCode.None),
                "Appeler / renvoyer le nuage. Vide / None = non bindé");
            MagicBushThrust = config.Bind(Cloud, "Thrust", new KeyboardShortcut(KeyCode.W),
                "Avancer (maintenir). Près du sol tu glisses, en l'air tu suis le regard");
            MagicBushBrake = config.Bind(Cloud, "Brake", new KeyboardShortcut(KeyCode.S),
                "Frein (maintenir)");
            MagicBushStrafeUp = config.Bind(Cloud, "StrafeUp", new KeyboardShortcut(KeyCode.Space),
                "Monter (maintenir)");
            MagicBushStrafeDown = config.Bind(Cloud, "StrafeDown", new KeyboardShortcut(KeyCode.LeftControl),
                "Descendre (maintenir)");
        }

        private static void BindLightning(ConfigFile config)
        {
            LightningEnabled = config.Bind(Lightning, "Enabled", true,
                "Éclair de l'Oblitérateur sur le joueur. Les autres le voient, même sans Qmod");
            LightningMessage = config.Bind(Lightning, "Message", true,
                "Afficher le message de Thor quand tu déclenches l'éclair");
            StrikeLightning = config.Bind(Lightning, "Strike", new KeyboardShortcut(KeyCode.None),
                "Frappe d'éclair sur toi. Vide / None = non bindé");
            ToggleTpMenu = config.Bind(Lightning, "Menu", new KeyboardShortcut(KeyCode.None),
                "Ouvrir / fermer le menu de téléportation. Vide / None = non bindé");
            AimLightning = config.Bind(Lightning, "Aim", new KeyboardShortcut(KeyCode.None),
                "Foudre dirigée : bras, éclair, pointer, frappe horizontale vers le visuel. Vide / None = non bindé");
        }

        private static void BindCraft(ConfigFile config)
        {
            CraftPullEnabled = config.Bind(Craft, "CraftPullEnabled", true,
                "Bouton 'Pull' (fabrication + amélioration, toutes stations) quand il manque des matériaux");
            CraftPullRadius = config.Bind(Craft, "CraftPullRadius", 50f,
                new ConfigDescription("Rayon (m) autour du joueur pour chercher les coffres", new AcceptableValueRange<float>(5f, 150f)));
        }

        private static void BindFarming(ConfigFile config)
        {
            KillMessage = config.Bind(Farming, "KillMessage", "pouik pouik",
                "Message affiché au centre quand tu tues un sanglier avec le butcher knife");
            CultivateHarvestEnabled = config.Bind(Farming, "CultivateHarvestEnabled", true,
                "Le mode cultiver du cultivateur fait sortir les légumes prêts du sol");
            ToggleCultivateHarvest = BindKey(config, Farming, "ToggleCultivateHarvest", "Activer/désactiver la récolte au cultivateur");
            SpawnBoar = BindKey(config, Farming, "SpawnBoar", "Faire apparaître un sanglier 2★ apprivoisé devant toi");
        }

        private static void BindHud(ConfigFile config)
        {
            StatusHudEnabled = config.Bind(Hud, "StatusHudEnabled", true,
                "Liste des bonus/malus en bas à droite");
            UnarmedHudHideEnabled = config.Bind(Hud, "UnarmedHudHideEnabled", true,
                "Cache le HUD si aucune arme/outil en main pendant UnarmedHudHideDelay secondes");
            UnarmedHudHideDelay = config.Bind(Hud, "UnarmedHudHideDelay", 10f,
                new ConfigDescription("Délai avant de cacher le HUD (mains vides)", new AcceptableValueRange<float>(1f, 60f)));
            HuginDisabled = config.Bind(Hud, "HuginDisabled", true,
                "Empêche Hugin de spawn et de rejouer les tutos. Munin n'est pas touché");
            FireplaceSmokeEnabled = config.Bind(Hud, "FireplaceSmokeEnabled", true,
                "Affiche l'évacuation de la fumée (évacuée / bloquée) dans le survol des feux");
            ToggleStatusHud = BindKey(config, Hud, "ToggleStatusHud", "Afficher/masquer le HUD des effets");
            ToggleUnarmedHudHide = BindKey(config, Hud, "ToggleUnarmedHudHide", "Activer/désactiver le masquage HUD mains vides");
            ToggleHugin = BindKey(config, Hud, "ToggleHugin", "Activer/désactiver Hugin (tutos)");
        }

        private static void BindCamera(ConfigFile config)
        {
            YoteiCameraEnabled = config.Bind(Camera, "YoteiCameraEnabled", true,
                "Caméra type Ghost of Yotei (épaule, recul, FOV sprint, zoom combat)");
            YoteiAutoShoulder = config.Bind(Camera, "YoteiAutoShoulder", true,
                "Épaule gauche/droite selon murs, déplacement et regard");
            YoteiShoulderOffset = config.Bind(Camera, "YoteiShoulderOffset", 0.42f,
                new ConfigDescription("Amplitude du décalage d'épaule", new AcceptableValueRange<float>(0f, 1.2f)));
            YoteiDistanceBoost = config.Bind(Camera, "YoteiDistanceBoost", 0.7f,
                new ConfigDescription("Recul en exploration", new AcceptableValueRange<float>(0f, 3f)));
            YoteiSprintDistance = config.Bind(Camera, "YoteiSprintDistance", 0.85f,
                new ConfigDescription("Recul supplémentaire en sprint", new AcceptableValueRange<float>(0f, 3f)));
            YoteiCombatZoom = config.Bind(Camera, "YoteiCombatZoom", 0.55f,
                new ConfigDescription("Rapprochement au combat", new AcceptableValueRange<float>(0f, 2f)));
            YoteiHeightOffset = config.Bind(Camera, "YoteiHeightOffset", 0.12f,
                new ConfigDescription("Hauteur extra", new AcceptableValueRange<float>(-0.5f, 1f)));
            YoteiSmoothness = config.Bind(Camera, "YoteiSmoothness", 6.5f,
                new ConfigDescription("Lissage (plus haut = plus réactif)", new AcceptableValueRange<float>(1f, 20f)));
            YoteiSprintFov = config.Bind(Camera, "YoteiSprintFov", 8f,
                new ConfigDescription("FOV ajouté en sprint", new AcceptableValueRange<float>(0f, 20f)));
            CinematicIdleEnabled = config.Bind(Camera, "CinematicIdleEnabled", true,
                "Caméra cinématique après un temps sans input");
            CinematicIdleDelay = config.Bind(Camera, "CinematicIdleDelay", 60f,
                new ConfigDescription("Secondes sans input avant la cinématique", new AcceptableValueRange<float>(10f, 300f)));
            CinematicSubjectRadius = config.Bind(Camera, "CinematicSubjectRadius", 40f,
                new ConfigDescription("Rayon (m) pour trouver un PNJ, monstre ou animal comme sujet", new AcceptableValueRange<float>(10f, 150f)));
            ToggleYoteiCamera = BindKey(config, Camera, "ToggleYoteiCamera", "Activer/désactiver la caméra Yotei");
            ToggleCinematicIdle = BindKey(config, Camera, "ToggleCinematicIdle", "Activer/désactiver la caméra cinématique idle");
            StartCinematic = BindKey(config, Camera, "StartCinematic", "Lancer tout de suite un plan cinématique");
            SwapShoulder = BindKey(config, Camera, "SwapShoulder", "Forcer l'épaule gauche/droite (bloque l'auto ~12 s)");
        }

        private static void BindBuild(ConfigFile config)
        {
            FreePlacementEnabled = config.Bind(Build, "FreePlacementEnabled", true,
                "Les pièces peuvent se croiser / se chevaucher (fantôme vert) et les points d'ancrage restent actifs sur un emplacement déjà occupé. S'applique aussi au terrain");
            BuildPullEnabled = config.Bind(Build, "BuildPullEnabled", true,
                "Menu marteau : stock coffres affiché après 1 s de survol, clic droit sur une pièce = pull pour x10 (ou le max couvert). Rayon : CraftPullRadius");
        }

        private static void BindMounts(ConfigFile config)
        {
            WolfRideEnabled = config.Bind(Mounts, "WolfRideEnabled", true,
                "Monter les loups apprivoisés avec Ctrl+E (système de selle natif, comme l'Asksvin)");
            WolfRideSaddleHeight = config.Bind(Mounts, "WolfRideSaddleHeight", 0.85f,
                new ConfigDescription("Hauteur du cavalier sur le dos du loup (m), appliquée en direct",
                    new AcceptableValueRange<float>(0.2f, 1.5f)));
        }

        private static ConfigEntry<KeyboardShortcut> BindKey(ConfigFile config, string section, string name, string description)
        {
            return config.Bind(section, name, new KeyboardShortcut(KeyCode.None),
                description + ". Vide / None = non bindé");
        }

        private static void BindDebug(ConfigFile config)
        {
            DebugUnsynchronized = config.Bind(Debug, "debugunsychronized", "NONONO",
                "Paramètre technique interne. Laisser sur NONONO sauf indication.");
        }

        private static void BindRanges(ConfigFile config)
        {
            ComfortRadius = config.Bind(Ranges, "ComfortRadius", 0f,
                new ConfigDescription("Rayon (m) de recherche des pièces de confort pour le bonus reposé. 0 = vanilla (10 m)",
                    new AcceptableValueRange<float>(0f, 60f)));
            WorkbenchRadius = config.Bind(Ranges, "WorkbenchRadius", 0f,
                new ConfigDescription("Rayon de construction de l'établi. 0 = vanilla",
                    new AcceptableValueRange<float>(0f, 150f)));
            StonecutterRadius = config.Bind(Ranges, "StonecutterRadius", 0f,
                new ConfigDescription("Rayon de construction du tailleur de pierre. 0 = vanilla",
                    new AcceptableValueRange<float>(0f, 150f)));
            ArtisanRadius = config.Bind(Ranges, "ArtisanRadius", 0f,
                new ConfigDescription("Rayon de construction de la table d'artisan. 0 = vanilla",
                    new AcceptableValueRange<float>(0f, 150f)));
            UpgradeRadius = config.Bind(Ranges, "UpgradeRadius", 0f,
                new ConfigDescription("Distance max des améliorations autour des stations (chaudrons, forge, établi, forge noire...). 0 = vanilla",
                    new AcceptableValueRange<float>(0f, 150f)));
        }

        // BepInEx garde les anciennes sections (Graphics, 1. Nuage magique, etc.)
        // dans OrphanedEntries : on les vire pour que le menu in-game soit propre.
        private static void ScrubOrphans(ConfigFile config)
        {
            PropertyInfo prop = typeof(ConfigFile).GetProperty("OrphanedEntries",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null)
            {
                return;
            }

            Dictionary<ConfigDefinition, string> orphans = prop.GetValue(config) as Dictionary<ConfigDefinition, string>;
            if (orphans == null || orphans.Count == 0)
            {
                return;
            }

            orphans.Clear();
            config.Save();
        }
    }
}
