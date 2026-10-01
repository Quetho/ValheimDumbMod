using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace Qmod
{
    internal static class ModConfig
    {
        private const string Options = "0 - options";
        private const string Camera = "1 - camera";
        private const string Graphics = "2 - graphismes";
        private const string Hud = "3 - hud";
        private const string Farming = "4 - farming";
        private const string Mounts = "5 - montures";
        private const string Debug = "6 - debug";
        private const string Transport = "7 - transport";

        internal static ConfigEntry<bool> SupersamplingEnabled;
        internal static ConfigEntry<float> SupersamplingScale;
        internal static ConfigEntry<bool> WaterShaderEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleSupersampling;
        internal static ConfigEntry<KeyboardShortcut> ToggleWaterShader;

        internal static ConfigEntry<bool> CraftPullEnabled;
        internal static ConfigEntry<float> CraftPullRadius;
        internal static ConfigEntry<bool> ChestDumpEnabled;
        internal static ConfigEntry<KeyboardShortcut> ChestDump;

        internal static ConfigEntry<string> KillMessage;
        internal static ConfigEntry<bool> CultivateHarvestEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleCultivateHarvest;

        internal static ConfigEntry<bool> StatusHudEnabled;
        internal static ConfigEntry<bool> UnarmedHudHideEnabled;
        internal static ConfigEntry<float> UnarmedHudHideDelay;
        internal static ConfigEntry<bool> UnarmedHudKeepCrosshair;
        internal static ConfigEntry<bool> UnarmedHudKeepStamina;
        internal static ConfigEntry<bool> UnarmedHudKeepMap;
        internal static ConfigEntry<bool> HuginDisabled;
        internal static ConfigEntry<bool> FireplaceSmokeEnabled;
        internal static ConfigEntry<bool> BuildStabilityHudEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleStatusHud;
        internal static ConfigEntry<KeyboardShortcut> ToggleUnarmedHudHide;
        internal static ConfigEntry<KeyboardShortcut> ToggleHugin;

        internal static ConfigEntry<ShoulderCameraMode> CameraMode;
        internal static ConfigEntry<float> SeidrIdleDelay;
        internal static ConfigEntry<float> SeidrSway;
        internal static ConfigEntry<float> SeidrShoulderOffset;
        internal static ConfigEntry<float> SeidrDistanceBoost;
        internal static ConfigEntry<float> SeidrSprintDistance;
        internal static ConfigEntry<float> SeidrCombatZoom;
        internal static ConfigEntry<float> SeidrHeightOffset;
        internal static ConfigEntry<float> SeidrSmoothness;
        internal static ConfigEntry<float> SeidrSprintFov;
        internal static ConfigEntry<float> SeidrInteriorHeight;
        internal static ConfigEntry<float> HeidrShoulderOffset;
        internal static ConfigEntry<float> HeidrDistanceBoost;
        internal static ConfigEntry<float> HeidrCombatZoom;
        internal static ConfigEntry<float> HeidrHeightOffset;
        internal static ConfigEntry<float> HeidrSmoothness;
        internal static ConfigEntry<float> HeidrViewX;
        internal static ConfigEntry<float> HeidrViewY;
        internal static ConfigEntry<float> HeidrDeadZone;
        internal static ConfigEntry<float> HeidrSoftZone;
        internal static ConfigEntry<float> HeidrSprintDistance;
        internal static ConfigEntry<float> HeidrSprintFovRate;
        internal static ConfigEntry<float> HeidrSprintFovMax;
        internal static ConfigEntry<float> HeidrInteriorHeight;
        internal static ConfigEntry<float> HeidrIdleDelay;
        internal static ConfigEntry<float> HeidrSway;
        internal static ConfigEntry<bool> HeidrDebug;
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

        internal static ConfigEntry<bool> WindsurfEnabled;
        internal static ConfigEntry<string> WindsurfCloneBase;
        internal static ConfigEntry<string> WindsurfBoardPrefab;
        internal static ConfigEntry<bool> WindsurfHideHull;
        internal static ConfigEntry<float> WindsurfBoardScale;
        internal static ConfigEntry<float> WindsurfBoardHeight;
        internal static ConfigEntry<float> WindsurfSailForce;
        internal static ConfigEntry<float> WindsurfSailFactor;
        internal static ConfigEntry<float> WindsurfDragFactor;
        internal static ConfigEntry<float> WindsurfHeelFactor;
        internal static ConfigEntry<float> WindsurfSpeedFactor;
        internal static ConfigEntry<float> WindsurfBalanceY;
        internal static ConfigEntry<float> WindsurfPivotZ;
        internal static ConfigEntry<float> WindsurfMaxPitch;
        internal static ConfigEntry<float> WindsurfMaxRoll;

        internal static ConfigEntry<string> DebugUnsynchronized;

        internal static ConfigEntry<float> ComfortRadius;
        internal static ConfigEntry<float> ConstructionRadius;
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
                Jotunn.Logger.LogInfo("Config Qmod migrée (0 - options … 6 - debug, backup .bak)");
            }

            BindRanges(config);
            BindBuild(config);
            BindCraft(config);
            BindCamera(config);
            BindGraphics(config);
            BindHud(config);
            BindFarming(config);
            BindMounts(config);
            BindTransport(config);
            BindDebug(config);
            MagicBush.ReadBinds(config);
            ThorLightning.ReadBinds(config);

            ScrubOrphans(config);
        }

        private static void BindGraphics(ConfigFile config)
        {
            SupersamplingEnabled = config.Bind(Graphics, "SupersamplingEnabled", false,
                "Rendu interne plus net (SSAA), puis réduit à l'écran. Plus gourmand");
            SupersamplingScale = config.Bind(Graphics, "SupersamplingScale", 1.5f,
                new ConfigDescription("Multiplicateur de résolution interne. 1 = off, 1.5 = 150 %, 2 = 200 %",
                    new AcceptableValueRange<float>(1f, 2.5f)));
            WaterShaderEnabled = config.Bind(Graphics, "WaterShaderEnabled", true,
                "Eau plus nette (normals, réfraction, foam). Visuel only, pas les vagues physiques");
            ToggleSupersampling = BindKey(config, Graphics, "ToggleSupersampling", "Activer/désactiver le supersampling");
            ToggleWaterShader = BindKey(config, Graphics, "ToggleWaterShader", "Activer/désactiver le look d'eau");
        }

        private static void BindCraft(ConfigFile config)
        {
            CraftPullEnabled = config.Bind(Options, "CraftPullEnabled", true,
                "Bouton 'Pull' (fabrication + amélioration, toutes stations) quand il manque des matériaux");
            CraftPullRadius = config.Bind(Options, "CraftPullRadius", 50f,
                new ConfigDescription("Rayon (m) autour du joueur pour chercher les coffres", new AcceptableValueRange<float>(5f, 150f)));
            ChestDumpEnabled = config.Bind(Options, "ChestDumpEnabled", true,
                "Bouton coffre au-dessus de l'armure (à droite de l'inventaire) et raccourci : range l'inventaire dans les coffres proches qui ne contiennent qu'une ressource et ont de la place. Rayon : CraftPullRadius");
            ChestDump = BindKey(config, Options, "ChestDump", "Ranger l'inventaire dans les coffres mono-ressource proches");
        }

        private static void BindFarming(ConfigFile config)
        {
            KillMessage = config.Bind(Farming, "KillMessage", "pouik pouik",
                "Message affiché au centre quand tu tues un sanglier avec le butcher knife");
            CultivateHarvestEnabled = config.Bind(Farming, "CultivateHarvestEnabled", true,
                "Le mode cultiver du cultivateur fait sortir les légumes prêts du sol");
            ToggleCultivateHarvest = BindKey(config, Farming, "ToggleCultivateHarvest", "Activer/désactiver la récolte au cultivateur");
        }

        private static void BindHud(ConfigFile config)
        {
            StatusHudEnabled = config.Bind(Hud, "StatusHudEnabled", true,
                "Liste des bonus/malus en bas à droite");
            UnarmedHudHideEnabled = config.Bind(Hud, "UnarmedHudHideEnabled", true,
                "Cache le HUD si aucune arme/outil en main pendant UnarmedHudHideDelay secondes");
            UnarmedHudHideDelay = config.Bind(Hud, "UnarmedHudHideDelay", 10f,
                new ConfigDescription("Délai avant de cacher le HUD (mains vides)", new AcceptableValueRange<float>(1f, 60f)));
            UnarmedHudKeepCrosshair = config.Bind(Hud, "UnarmedHudKeepCrosshair", false,
                "Masquage HUD mains vides : garder le crosshair visible");
            UnarmedHudKeepStamina = config.Bind(Hud, "UnarmedHudKeepStamina", false,
                "Masquage HUD mains vides : garder la barre d'endurance visible");
            UnarmedHudKeepMap = config.Bind(Hud, "UnarmedHudKeepMap", false,
                "Masquage HUD mains vides : garder la minimap visible");
            HuginDisabled = config.Bind(Hud, "HuginDisabled", true,
                "Empêche Hugin de spawn et de rejouer les tutos. Munin n'est pas touché");
            FireplaceSmokeEnabled = config.Bind(Hud, "FireplaceSmokeEnabled", true,
                "Affiche l'évacuation de la fumée (évacuée / bloquée) dans le survol des feux");
            BuildStabilityHudEnabled = config.Bind(Hud, "BuildStabilityHudEnabled", true,
                "Mode build : affiche à droite du crosshair la stabilité de la pièce visée");
            ToggleStatusHud = BindKey(config, Hud, "ToggleStatusHud", "Afficher/masquer le HUD des effets");
            ToggleUnarmedHudHide = BindKey(config, Hud, "ToggleUnarmedHudHide", "Activer/désactiver le masquage HUD mains vides");
            ToggleHugin = BindKey(config, Hud, "ToggleHugin", "Activer/désactiver Hugin (tutos)");
        }

        private static void BindCamera(ConfigFile config)
        {
            CameraMode = config.Bind(Camera, "CameraMode", MigratedCameraMode(config),
                "Mode caméra : Off = vanilla, Yotei = épaule fixe (swap manuel), AutoShoulder = base Yotei + épaule auto (murs, déplacement, regard), Seidr = contextuelle (combat, sprint, mystique), Heidr = prototype Composer + ownership");
            SeidrIdleDelay = config.Bind(Camera, "SeidrIdleDelay", 8f,
                new ConfigDescription("Secondes sans input avant la dérive mystique (Seidr)", new AcceptableValueRange<float>(2f, 60f)));
            SeidrSway = config.Bind(Camera, "SeidrSway", 1f,
                new ConfigDescription("Amplitude du souffle caméra Seidr (respiration, dérive, roulis). 0 = rigide", new AcceptableValueRange<float>(0f, 2f)));
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
            // Seidr : réglages 100 % propres, plus rien de partagé avec Yotei.
            // Première install : héritage one-shot des valeurs Yotei déjà
            // chargées (défaut du Bind), ensuite chaque mode vit sa vie.
            SeidrShoulderOffset = config.Bind(Camera, "SeidrShoulderOffset", YoteiShoulderOffset.Value,
                new ConfigDescription("Seidr : amplitude du décalage d'épaule", new AcceptableValueRange<float>(0f, 1.2f)));
            SeidrDistanceBoost = config.Bind(Camera, "SeidrDistanceBoost", YoteiDistanceBoost.Value,
                new ConfigDescription("Seidr : recul en exploration", new AcceptableValueRange<float>(0f, 3f)));
            SeidrSprintDistance = config.Bind(Camera, "SeidrSprintDistance", YoteiSprintDistance.Value,
                new ConfigDescription("Seidr : recul supplémentaire en sprint", new AcceptableValueRange<float>(0f, 3f)));
            SeidrCombatZoom = config.Bind(Camera, "SeidrCombatZoom", YoteiCombatZoom.Value,
                new ConfigDescription("Seidr : rapprochement au combat", new AcceptableValueRange<float>(0f, 2f)));
            SeidrHeightOffset = config.Bind(Camera, "SeidrHeightOffset", -0.55f,
                new ConfigDescription("Seidr : hauteur extra hors intérieur, relative aux yeux (plus bas = hanche)",
                    new AcceptableValueRange<float>(-1.2f, 1f)));
            SeidrSmoothness = config.Bind(Camera, "SeidrSmoothness", YoteiSmoothness.Value,
                new ConfigDescription("Seidr : lissage (plus haut = plus réactif)", new AcceptableValueRange<float>(1f, 20f)));
            SeidrSprintFov = config.Bind(Camera, "SeidrSprintFov", YoteiSprintFov.Value,
                new ConfigDescription("Seidr : FOV ajouté en sprint", new AcceptableValueRange<float>(0f, 20f)));
            SeidrInteriorHeight = config.Bind(Camera, "SeidrInteriorHeight", -0.95f,
                new ConfigDescription("Seidr : hauteur à l'intérieur / au serré, relative aux yeux (hanche ≈ -0.95, tête ≈ 0)",
                    new AcceptableValueRange<float>(-1.5f, 0.5f)));
            // Heidr : réglages 100 % propres, valeurs fixes (pas d'héritage Seidr).
            HeidrShoulderOffset = config.Bind(Camera, "HeidrShoulderOffset", 0.42f,
                new ConfigDescription("Heidr : amplitude du décalage d'épaule", new AcceptableValueRange<float>(0f, 1.2f)));
            HeidrDistanceBoost = config.Bind(Camera, "HeidrDistanceBoost", 0.7f,
                new ConfigDescription("Heidr : recul en exploration", new AcceptableValueRange<float>(0f, 3f)));
            HeidrCombatZoom = config.Bind(Camera, "HeidrCombatZoom", 0.55f,
                new ConfigDescription("Heidr : rapprochement au combat (pull-in)", new AcceptableValueRange<float>(0f, 2f)));
            HeidrHeightOffset = config.Bind(Camera, "HeidrHeightOffset", -0.4f,
                new ConfigDescription("Heidr : hauteur extra, relative aux yeux (poitrine ≈ -0.4, tête ≈ 0)",
                    new AcceptableValueRange<float>(-1.2f, 1f)));
            HeidrSmoothness = config.Bind(Camera, "HeidrSmoothness", 6.5f,
                new ConfigDescription("Heidr : lissage Body (plus haut = plus réactif)", new AcceptableValueRange<float>(1f, 20f)));
            HeidrViewX = config.Bind(Camera, "HeidrViewX", 0.15f,
                new ConfigDescription("Heidr : ancrage horizontal de la tête dans le cadre (0 = centre)", new AcceptableValueRange<float>(0f, 0.3f)));
            HeidrViewY = config.Bind(Camera, "HeidrViewY", 0.6f,
                new ConfigDescription("Heidr : ancrage vertical de la tête dans le cadre", new AcceptableValueRange<float>(0.3f, 0.8f)));
            HeidrDeadZone = config.Bind(Camera, "HeidrDeadZone", 0.1f,
                new ConfigDescription("Heidr : demi-largeur de la dead zone Composer (cadre)", new AcceptableValueRange<float>(0.02f, 0.2f)));
            HeidrSoftZone = config.Bind(Camera, "HeidrSoftZone", 0.2f,
                new ConfigDescription("Heidr : fin de la soft zone Composer (cadre)", new AcceptableValueRange<float>(0.05f, 0.35f)));
            HeidrSprintDistance = config.Bind(Camera, "HeidrSprintDistance", 0.85f,
                new ConfigDescription("Heidr : recul supplémentaire en sprint", new AcceptableValueRange<float>(0f, 3f)));
            HeidrSprintFovRate = config.Bind(Camera, "HeidrSprintFovRate", 1.2f,
                new ConfigDescription("Heidr : FOV SDAZ par m/s en sprint (flux optique constant)", new AcceptableValueRange<float>(0f, 3f)));
            HeidrSprintFovMax = config.Bind(Camera, "HeidrSprintFovMax", 9f,
                new ConfigDescription("Heidr : FOV SDAZ maximal en sprint", new AcceptableValueRange<float>(0f, 20f)));
            HeidrInteriorHeight = config.Bind(Camera, "HeidrInteriorHeight", -0.55f,
                new ConfigDescription("Heidr : hauteur à l'intérieur / au serré, relative aux yeux (torse ≈ -0.55, explo -0.4, tête ≈ 0)",
                    new AcceptableValueRange<float>(-1.5f, 0.5f)));
            HeidrIdleDelay = config.Bind(Camera, "HeidrIdleDelay", 8f,
                new ConfigDescription("Heidr : secondes sans input avant la dérive mystique", new AcceptableValueRange<float>(2f, 60f)));
            HeidrSway = config.Bind(Camera, "HeidrSway", 1f,
                new ConfigDescription("Heidr : amplitude du souffle (dérive mystique, DOF). 0 = rigide", new AcceptableValueRange<float>(0f, 2f)));
            HeidrDebug = config.Bind(Camera, "HeidrDebug", false,
                "Heidr : overlay debug (état, owners par canal, zones Composer)");
            CinematicIdleEnabled = config.Bind(Camera, "CinematicIdleEnabled", true,
                "Caméra cinématique après un temps sans input");
            CinematicIdleDelay = config.Bind(Camera, "CinematicIdleDelay", 60f,
                new ConfigDescription("Secondes sans input avant la cinématique", new AcceptableValueRange<float>(10f, 300f)));
            CinematicSubjectRadius = config.Bind(Camera, "CinematicSubjectRadius", 40f,
                new ConfigDescription("Rayon (m) pour trouver un PNJ, monstre ou animal comme sujet", new AcceptableValueRange<float>(10f, 150f)));
            ToggleYoteiCamera = BindKey(config, Camera, "ToggleYoteiCamera", "Caméra : cycle Off -> Yotei -> AutoShoulder -> Seidr -> Heidr");
            ToggleCinematicIdle = BindKey(config, Camera, "ToggleCinematicIdle", "Activer/désactiver la caméra cinématique idle");
            StartCinematic = BindKey(config, Camera, "StartCinematic", "Lancer tout de suite un plan cinématique");
            SwapShoulder = BindKey(config, Camera, "SwapShoulder", "Forcer l'épaule gauche/droite (en AutoShoulder/Seidr, bloque l'auto ~6 s)");
        }

        // Anciennes clés YoteiCameraEnabled / YoteiAutoShoulder (non bindées
        // donc orphelines) -> valeur par défaut du nouveau mode. Les vieilles
        // clés sont ensuite nettoyées par ScrubOrphans.
        private static ShoulderCameraMode MigratedCameraMode(ConfigFile config)
        {
            Dictionary<ConfigDefinition, string> orphans = Orphans(config);
            if (orphans == null)
            {
                return ShoulderCameraMode.Off;
            }

            string rawEnabled;
            if (!orphans.TryGetValue(new ConfigDefinition(Camera, "YoteiCameraEnabled"), out rawEnabled) ||
                rawEnabled == null ||
                !rawEnabled.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return ShoulderCameraMode.Off;
            }

            string rawAuto;
            if (orphans.TryGetValue(new ConfigDefinition(Camera, "YoteiAutoShoulder"), out rawAuto) &&
                rawAuto != null &&
                rawAuto.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return ShoulderCameraMode.Yotei;
            }

            return ShoulderCameraMode.AutoShoulder;
        }

        private static void BindBuild(ConfigFile config)
        {
            FreePlacementEnabled = config.Bind(Options, "FreePlacementEnabled", true,
                "Les pièces peuvent se croiser / se chevaucher (fantôme vert) et les points d'ancrage restent actifs sur un emplacement déjà occupé. S'applique aussi au terrain");
            BuildPullEnabled = config.Bind(Options, "BuildPullEnabled", true,
                "Menu marteau : stock coffres affiché après 1 s de survol, clic droit sur une pièce = pull pour x1, Shift + clic droit = pull pour x5 (tout ou rien : rien pris si stock ou place insuffisants). Rayon : CraftPullRadius");
        }

        private static void BindMounts(ConfigFile config)
        {
            WolfRideEnabled = config.Bind(Mounts, "WolfRideEnabled", true,
                "Monter les loups apprivoisés avec Ctrl+E (système de selle natif, comme l'Asksvin)");
            WolfRideSaddleHeight = config.Bind(Mounts, "WolfRideSaddleHeight", 0.85f,
                new ConfigDescription("Hauteur du cavalier sur le dos du loup (m), appliquée en direct",
                    new AcceptableValueRange<float>(0.2f, 1.5f)));
        }

        private static void BindTransport(ConfigFile config)
        {
            WindsurfEnabled = config.Bind(Transport, "WindsurfEnabled", true,
                "Planche à voile (proto) : clone du radeau avec un banc vanilla en guise de planche, posable au marteau sur l'eau. Prend effet au lancement");
            WindsurfCloneBase = config.Bind(Transport, "WindsurfCloneBase", "Raft",
                "Prefab bateau cloné pour la physique Ship (Raft, Karve ou VikingShip). Prend effet au lancement");
            WindsurfBoardPrefab = config.Bind(Transport, "WindsurfBoardPrefab", "piece_bench01",
                "Prefab vanilla utilisé comme planche (visuel seul). Replis automatiques puis cube bois si introuvable. Prend effet au lancement");
            WindsurfHideHull = config.Bind(Transport, "WindsurfHideHull", true,
                "Cache la coque du bateau cloné (mât, voile et gréement conservés). Prend effet au lancement");
            WindsurfBoardScale = config.Bind(Transport, "WindsurfBoardScale", 1.6f,
                new ConfigDescription("Échelle de la planche (banc agrandi, hero scale face aux vagues). Prend effet au lancement",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            WindsurfBoardHeight = config.Bind(Transport, "WindsurfBoardHeight", 0.35f,
                new ConfigDescription("Hauteur de la planche au-dessus du pont (m). Prend effet au lancement",
                    new AcceptableValueRange<float>(-1f, 2f)));
            WindsurfSailForce = config.Bind(Transport, "WindsurfSailForce", 0f,
                new ConfigDescription("Coefficient de voile Ship.m_sailForceFactor. 0 = auto (drakkar x WindsurfSailFactor), sinon valeur absolue. Rechargé à chaud",
                    new AcceptableValueRange<float>(0f, 30f)));
            WindsurfSailFactor = config.Bind(Transport, "WindsurfSailFactor", 1.5f,
                new ConfigDescription("Multiplicateur de voile vs drakkar en mode auto (WindsurfSailForce=0). Rechargé à chaud",
                    new AcceptableValueRange<float>(0.5f, 5f)));
            WindsurfDragFactor = config.Bind(Transport, "WindsurfDragFactor", 1f,
                new ConfigDescription("Freinage eau vs drakkar. <1 = VMax plus haute, plus de glisse. Rechargé à chaud",
                    new AcceptableValueRange<float>(0.1f, 2f)));
            WindsurfHeelFactor = config.Bind(Transport, "WindsurfHeelFactor", 1f,
                new ConfigDescription("Stabilité vs drakkar. <1 = tangue et roule plus (réalisme), trop bas = chavire. Rechargé à chaud",
                    new AcceptableValueRange<float>(0.1f, 3f)));
            WindsurfSpeedFactor = config.Bind(Transport, "WindsurfSpeedFactor", 1.5f,
                new ConfigDescription("Plafond de vitesse vs drakkar. Rechargé à chaud",
                    new AcceptableValueRange<float>(0.5f, 5f)));
            WindsurfBalanceY = config.Bind(Transport, "WindsurfBalanceY", -0.1f,
                new ConfigDescription("Hauteur du centre de masse (m, 0 = flottaison). Plus bas = stable, plus haut = joueur. Rechargé à chaud",
                    new AcceptableValueRange<float>(-1f, 0.5f)));
            WindsurfPivotZ = config.Bind(Transport, "WindsurfPivotZ", -0.5f,
                new ConfigDescription("Position avant/arrière du pivot (m, négatif = arrière, sous le rider). Rechargé à chaud",
                    new AcceptableValueRange<float>(-1.5f, 0.5f)));
            WindsurfMaxPitch = config.Bind(Transport, "WindsurfMaxPitch", 18f,
                new ConfigDescription("Butée de tangage (degrés, nez haut ou bas). Au-delà la planche est ramenée. Rechargé à chaud",
                    new AcceptableValueRange<float>(5f, 80f)));
            WindsurfMaxRoll = config.Bind(Transport, "WindsurfMaxRoll", 35f,
                new ConfigDescription("Butée de gîte (degrés, sur les côtés). La voile cesse de pencher avant cette limite. Rechargé à chaud",
                    new AcceptableValueRange<float>(5f, 80f)));
            WindsurfSailForce.SettingChanged += OnWindsurfTuningChanged;
            WindsurfSailFactor.SettingChanged += OnWindsurfTuningChanged;
            WindsurfDragFactor.SettingChanged += OnWindsurfTuningChanged;
            WindsurfHeelFactor.SettingChanged += OnWindsurfTuningChanged;
            WindsurfSpeedFactor.SettingChanged += OnWindsurfTuningChanged;
            WindsurfBalanceY.SettingChanged += OnWindsurfTuningChanged;
            WindsurfPivotZ.SettingChanged += OnWindsurfTuningChanged;
        }

        private static void OnWindsurfTuningChanged(object sender, EventArgs e)
        {
            Windsurf.RefreshLiveTuning();
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
            ComfortRadius = config.Bind(Options, "ComfortRadius", 0f,
                new ConfigDescription("Rayon de confort (m) : pièces prises en compte pour le bonus reposé. 0 = vanilla (10 m)",
                    new AcceptableValueRange<float>(0f, 60f)));

            Dictionary<ConfigDefinition, string> orphans = Orphans(config);
            bool hadConstruction = HasEntry(orphans, "ConstructionRadius");
            float legacyConstruction = MaxLegacyRadius(orphans);
            ConstructionRadius = config.Bind(Options, "ConstructionRadius", 0f,
                new ConfigDescription("Rayon de construction (m) : toutes les stations (établi, forge, forge noire, tailleur de pierre, table d'artisan, chaudron, table de Galdr, table de préparation). 0 = vanilla",
                    new AcceptableValueRange<float>(0f, 150f)));
            if (!hadConstruction && legacyConstruction > 0f)
            {
                ConstructionRadius.Value = Mathf.Clamp(legacyConstruction, 0f, 150f);
            }

            UpgradeRadius = config.Bind(Options, "UpgradeRadius", 0f,
                new ConfigDescription("Rayon des améliorations (m) : distance max des extensions autour de leur station. 0 = vanilla",
                    new AcceptableValueRange<float>(0f, 150f)));
        }

        // Anciens rayons par station (établi, tailleur, artisan). La plus
        // grande valeur non nulle devient ConstructionRadius, une fois.
        private static float MaxLegacyRadius(Dictionary<ConfigDefinition, string> orphans)
        {
            if (orphans == null)
            {
                return 0f;
            }

            float max = 0f;
            string[] keys = { "WorkbenchRadius", "StonecutterRadius", "ArtisanRadius" };
            for (int i = 0; i < keys.Length; i++)
            {
                string raw;
                if (!TryLegacy(orphans, keys[i], out raw))
                {
                    continue;
                }

                float value;
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ||
                    float.TryParse(raw, out value))
                {
                    max = Mathf.Max(max, value);
                }
            }

            return max;
        }

        private static bool HasEntry(Dictionary<ConfigDefinition, string> orphans, string key)
        {
            string ignored;
            return TryLegacy(orphans, key, out ignored);
        }

        // La migration a déjà renommé la section, mais un fichier non réécrit
        // peut encore porter les anciens rayons sous 11. Rayons.
        private static bool TryLegacy(Dictionary<ConfigDefinition, string> orphans, string key, out string raw)
        {
            raw = null;
            if (orphans == null)
            {
                return false;
            }

            if (orphans.TryGetValue(new ConfigDefinition(Options, key), out raw))
            {
                return true;
            }

            return orphans.TryGetValue(new ConfigDefinition("11. Rayons", key), out raw);
        }

        internal static bool TryGetOrphan(ConfigFile config, string section, string key, out string value)
        {
            value = null;
            Dictionary<ConfigDefinition, string> orphans = Orphans(config);
            if (orphans == null)
            {
                return false;
            }

            return orphans.TryGetValue(new ConfigDefinition(section, key), out value);
        }

        private static Dictionary<ConfigDefinition, string> Orphans(ConfigFile config)
        {
            PropertyInfo prop = typeof(ConfigFile).GetProperty("OrphanedEntries",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null)
            {
                return null;
            }

            return prop.GetValue(config) as Dictionary<ConfigDefinition, string>;
        }

        // BepInEx garde les anciennes sections (Graphics, 1. Nuage magique, etc.)
        // dans OrphanedEntries : on les vire pour que le menu in-game soit propre.
        private static void ScrubOrphans(ConfigFile config)
        {
            Dictionary<ConfigDefinition, string> orphans = Orphans(config);
            if (orphans == null || orphans.Count == 0)
            {
                return;
            }

            List<ConfigDefinition> drop = new List<ConfigDefinition>();
            foreach (KeyValuePair<ConfigDefinition, string> pair in orphans)
            {
                if (!MagicBush.PreserveOrphan(pair.Key.Section, pair.Key.Key) &&
                    !ThorLightning.PreserveOrphan(pair.Key.Section, pair.Key.Key))
                {
                    drop.Add(pair.Key);
                }
            }

            if (drop.Count == 0)
            {
                return;
            }

            for (int i = 0; i < drop.Count; i++)
            {
                orphans.Remove(drop[i]);
            }

            config.Save();
        }
    }
}
