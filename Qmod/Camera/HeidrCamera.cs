using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace Qmod
{
    // Mode Heidr (prototype) : Composer + ownership, isolé de Seidr.
    // Spec : research/camera/heidr.md (gitignoré). SeidrCamera.cs intact.
    //
    // Contenu du prototype :
    //   Body    : un module actif (Explo, Sprint ou Combat) sort un
    //             contrat Size / Facing / LevelAt / ViewAt. Intérieur et
    //             mystic sont des contraintes sur ce contrat, pas un
    //             empilement de multiplicateurs. Ressort critique par axe.
    //   Aim     : pin de la tête dans le cadre, zones dead / soft / hard.
    //             La tête dans la dead zone = la caméra ne chasse pas.
    //   Ownership : table OwnerOf (canal × état), consultée par chaque
    //             writer. Hors propriétaire : FOV Clear immédiat, DOF
    //             Restore, roll et pitch silencieux. L'offset d'arc, de
    //             lit, de build et de menu fond (~0.3 s) puis reset.
    //   Collision : après le ressort (PositionCorrection). Les sondes
    //             d'environnement utilisent la même sphère ≥ near-clip
    //             que le légaliseur. Le pull de cette sphère passe en
    //             entier (pas de plafond). Tau, grille (rayon central
    //             inclus, pull seulement) et push latéral sont une
    //             correction douce, lissée à part. Explo pousse si
    //             l'autre côté est libre, sinon pull-in. Combat : pull-in
    //             only. Le tau lit la fermeture brute, pas le 1€.
    //             Après la collision vanilla, la caméra est repoussée
    //             hors du corps (near-clip 0,5 + marge). Le zoom max,
    //             la garde et le pull des coins ne traversent plus le
    //             perso : sinon le near plane le découpe et il devient
    //             transparent.
    //   Épaule  : auto en explo (sondes + côté futur + warp vitesse),
    //             confirmation, gel combat / visée, hold 6 s après swap.
    //             Au mystic l'épaule est relâchée (marge plus large).
    //   Prédiction : horizon = confirmation + 0,30 s, cap 5,5 m, pour
    //             que le côté soit choisi ~0,3 s avant le mur.
    //   Sprint  : recul selon la place derrière. FOV SDAZ selon la
    //             vitesse seule, borné, coupé dès la visée. Clavier
    //             on/off : les deux n'arment qu'après ~0,45 s de course
    //             tenue, montent en ~0,3 s, redescendent en ~0,15 s.
    //             L'épaule et les sondes restent immédiates. Le boom est
    //             raccourci de l'équivalent angulaire de ce FOV : le
    //             sujet ne rétrécit qu'une fois.
    //   Intérieur : contrat seul — Level At torse, Size max serré,
    //             hystérésis plafond. Le ressort ne ralentit pas.
    //   Mystic  : template Halper sur l'explo (facing relâché, Size+,
    //             dérive lente), DOF sur la tête. Silence hors explo.
    //             DOF et pitch d'explo sont propriétaires-Heidr mais
    //             auto-restorés quand le poids mystic est nul (colonne
    //             Mystic de la table, repliée sur l'état Explo).
    //   Action  : punch sous-amorti borné (roulade, atterrissage),
    //             ajouté après le ressort. Trauma + shake en sprint et
    //             combat, roulis plafonné. Silence en visée.
    //   Sondes : le présent est filtré 1€ (Casiez) avant l'épaule et
    //             l'intérieur. Le futur latéral et arrière reste brut,
    //             sinon le lookahead est mangé par le filtre. Le plafond
    //             futur reste filtré : l'hystérésis ne doit pas s'armer
    //             sur un pic.
    //   Monture : MagicBush. Poussée = contrat sprint. Combat coupé.
    //             Size et LevelAt relevés pour dégager le buisson.
    //   Debug   : overlay (état, owners, contrat, tau) et gizmos écran
    //             (sondes, sphère de fuite, correction).
    //
    // Hors plan, comme la spec : autolook, rails, lock-on. Pas de
    // solveur CSP : pins Gleicher + dead zone. La collision vanilla
    // sous-jacente reste active (GameCamera).
    // Config 100 % Heidr (Heidr*) : aucun réglage Seidr/Yotei partagé.
    internal static class HeidrCamera
    {
        private enum HeidrState
        {
            Explo,
            Sprint,
            Combat,
            Aim,
            Paused,
            Menu
        }

        private enum Channel
        {
            Lateral,
            Height,
            Boom,
            Fov,
            Dof,
            Roll,
            Pitch,
            YawAuto
        }

        private enum Owner
        {
            Heidr,
            Vanilla,
            Off
        }

        // Table d'ownership, sous-ensemble prototype du §6 de heidr.md.
        // Vanilla = un autre système possède (ou fade vers lui).
        // Off = silence explicite (aucune écriture).
        private static Owner OwnerOf(Channel channel, HeidrState state)
        {
            switch (state)
            {
                case HeidrState.Explo:
                    switch (channel)
                    {
                        case Channel.Lateral:
                        case Channel.Height:
                        case Channel.Boom:
                        case Channel.YawAuto:
                        case Channel.Dof:
                        case Channel.Pitch:
                            return Owner.Heidr;
                        case Channel.Fov:
                            return Owner.Vanilla;
                        default:
                            return Owner.Off;
                    }

                case HeidrState.Sprint:
                    switch (channel)
                    {
                        case Channel.Lateral:
                        case Channel.Height:
                        case Channel.Boom:
                        case Channel.Fov:
                        case Channel.YawAuto:
                        case Channel.Roll:
                            return Owner.Heidr;
                        case Channel.Dof:
                            return Owner.Vanilla;
                        default:
                            return Owner.Off;
                    }

                case HeidrState.Combat:
                    switch (channel)
                    {
                        case Channel.Lateral:
                        case Channel.Height:
                        case Channel.Boom:
                        case Channel.Fov:
                        case Channel.Roll:
                            return Owner.Heidr;
                        case Channel.Dof:
                            return Owner.Vanilla;
                        default:
                            return Owner.Off;
                    }

                default:
                    // Aim (arc), Paused (lit / build) et Menu : cut total.
                    switch (channel)
                    {
                        case Channel.Lateral:
                        case Channel.Height:
                        case Channel.Boom:
                        case Channel.Fov:
                        case Channel.Dof:
                            return Owner.Vanilla;
                        default:
                            return Owner.Off;
                    }
            }
        }

        // État Body (lissé par axe : latéral vif, boom moyen, vertical doux).
        private static Vector3 extraOffset;
        private static float smLat;
        private static float smVer;
        private static float smBoom;
        private static float velLat;
        private static float velVer;
        private static float velBoom;
        private static float combatBlend;
        private static float shelterBlend;
        private static float dungeonBlend;
        private static float confineBlend;
        private static float interiorBlend;
        private static float hipBlend;
        private static bool dungeonRaw;
        private static float dungeonProbeTimer;

        // Épaule : swap manuel + auto à confirmation (voir UpdateShoulderAuto).
        private static float shoulderSign = 1f;
        private static float shoulderVel;
        private static float targetSign = 1f;

        // Sondes au rayon near-clip. Le futur est à l'horizon de prédiction.
        private static float rearClear = 4f;
        private static float rightClear = 3f;
        private static float leftClear = 3f;
        private static float frontClear = 4f;
        private static float ceilingClear = 2.5f;
        private static float rearFar = 8f;
        private static float rightFar = 3f;
        private static float leftFar = 3f;
        private static float ceilingFar = 2.5f;
        private static float rearClosing;
        private static float prevRawRear = 4f;
        private static float rawGap = 4f;
        private static bool probesReady;
        private static bool ceilingBlocked;
        private static float envProbeTimer;
        private static float allowShoulder = 1.2f;
        private static float smSoftPull;
        private static float velSoftPull;
        private static float smPush;
        private static float velPush;
        private static float debugHard;
        private static float debugBody;

        private const float ProbeStep = 0.12f;
        private const float CeilingBlockAt = 0.6f;
        private const float CeilingReleaseAt = 0.9f;

        // 1€ : une instance par sonde. Constantes de départ, à régler en jeu.
        private const float EuroMinCutoff = 1.2f;
        private const float EuroBeta = 0.1f;
        private const float EuroDcutoff = 1.0f;

        private static readonly OneEuroFilter euroRear = new OneEuroFilter();
        private static readonly OneEuroFilter euroRight = new OneEuroFilter();
        private static readonly OneEuroFilter euroLeft = new OneEuroFilter();
        private static readonly OneEuroFilter euroFront = new OneEuroFilter();
        private static readonly OneEuroFilter euroCeiling = new OneEuroFilter();
        private static readonly OneEuroFilter euroCeilingFar = new OneEuroFilter();
        private static readonly RaycastHit[] legalHits = new RaycastHit[12];

        // Punch d'action : oscillateur sous-amorti, hors du ressort Body.
        private static float punchPos;
        private static float punchVel;
        private const float PunchOmega = 14f;
        private const float PunchZeta = 0.32f;
        private const float PunchImpulse = 2.8f;
        private const float PunchLimit = 0.32f;

        private const float FacingTol = 0.02f;
        private const float SizeTol = 0.03f;
        private const float LevelUpTol = 0.02f;
        private const float TauBrake = 0.5f;
        private const float EscapeRadius = 0.55f;
        private const float InteriorSizeMax = 0.4f;
        private const float MountSizeAdd = 0.45f;
        private const float MountLevelAdd = 0.5f;

        // Confirmation d'épaule : le nouveau côté doit gagner 3 pas
        // de suite (0.36 s), swaps espacés d'au moins 1.2 s.
        private static float shoulderTimer;
        private static float pendingSide;
        private static float pendingTime;
        private static float lastSwapTime = -10f;
        private static float autoResumeAt;

        private const float ShoulderConfirmTime = 0.36f;
        private const float ShoulderMinInterval = 1.2f;
        private const float ShoulderMargin = 0.7f;
        private const float SprintSwapBlockAt = 0.4f;

        // La confirmation mange un lookahead trop court. L'horizon couvre
        // le temps de confirmation plus le lead voulu avant le mur.
        private const float PredictLead = 0.30f;
        private const float PredictCap = 5.5f;
        private const float SoftCorrSmooth = 0.08f;
        private const float SoftPullCap = 0.6f;

        // Correction Composer maximale (m), anti-emballement du feedback.
        private const float MaxAimCorrection = 0.6f;

        // Sortie FOV (SDAZ sprint + petit add combat).
        private static float fovTarget;
        private static bool appliedFov;
        private static float debugFovAdd;

        private const float CombatFovAdd = 2f;

        // Engagement visuel du sprint. L'état Sprint reste immédiat
        // (épaule, sondes). Seuls le recul et le FOV attendent une course
        // tenue, pour qu'un appui bref ne fasse pas pulser le cadre.
        private const float SprintCommit = 0.45f;
        private const float SprintRiseSpeed = 1f / 0.30f;
        private const float SprintFallSpeed = 1f / 0.15f;
        private static float sprintHold;
        private static float sprintWeight;

        // Action : punch + trauma. Le punch est l'impulsion, pas un blend.
        private static float trauma;
        private static float prevVy;
        private static bool wasDodging;

        // Mystique : template sur l'explo + DOF tête.
        private static float mysticBlend;
        private static float idleTime;
        private static float pitchDrift;
        private static float debugDofW;
        private static readonly CameraDof mysticDof = new CameraDof();

        // Snapshot debug (écrit par le patch, lu par l'overlay).
        private static HeidrState debugState;
        private static bool debugActive;
        private static Vector3 debugHeadUv = new Vector3(0.5f, 0.5f, 1f);
        private static Vector2 debugAnchor = new Vector2(0.35f, 0.6f);
        private static string debugZoneX = "-";
        private static string debugZoneY = "-";
        private static float debugLatCorr;
        private static float debugVerCorr;
        private static float debugTau = 8f;
        private static float debugSize;
        private static float debugFacing;
        private static float debugLevel;
        private static string debugModule = "Explo";
        private static Vector3 debugOrigin;
        private static Vector3 debugRight;
        private static Vector3 debugUp;
        private static Vector3 debugBack;
        private static Vector3 debugFuture;
        private static Vector3 debugCorr;
        private static HeidrDebugOverlay overlay;
        private static Texture2D overlayPixel;
        private static string overlayText = string.Empty;
        private static float overlayTextAt = -10f;

        private static bool ShouldSkip(GameCamera camera, Player player)
        {
            // Le menu (inventaire, carte, pause, chat) coupe Heidr comme
            // l'arc : le corps du patch ne drive pas.
            return CameraUtil.ShouldSkip(ShoulderCameraMode.Heidr, camera, player)
                || Util.IsMenuBlocking();
        }

        private static HeidrState ResolveState(Player player)
        {
            if (!player)
            {
                return HeidrState.Paused;
            }

            if (player.IsDrawingBow())
            {
                return HeidrState.Aim;
            }

            if (player.InBed() || player.InPlaceMode())
            {
                return HeidrState.Paused;
            }

            if (Util.IsMenuBlocking())
            {
                return HeidrState.Menu;
            }

            // Buisson : pas de contrat combat. La poussée prend le sprint.
            if (MagicBush.IsRiding)
            {
                return MagicBush.IsThrusting ? HeidrState.Sprint : HeidrState.Explo;
            }

            if (player.UseMeleeCamera() || player.InAttack() || player.IsBlocking())
            {
                return HeidrState.Combat;
            }

            if (player.IsRunning() && !player.IsSneaking())
            {
                return HeidrState.Sprint;
            }

            return HeidrState.Explo;
        }

        private static void ResetState()
        {
            extraOffset = Vector3.zero;
            smLat = 0f;
            smVer = 0f;
            smBoom = 0f;
            velLat = 0f;
            velVer = 0f;
            velBoom = 0f;
            combatBlend = 0f;
            shelterBlend = 0f;
            dungeonBlend = 0f;
            confineBlend = 0f;
            interiorBlend = 0f;
            hipBlend = 0f;
            dungeonRaw = false;
            dungeonProbeTimer = 0f;
            mysticBlend = 0f;
            idleTime = 0f;
            pitchDrift = 0f;
            debugDofW = 0f;
            trauma = 0f;
            prevVy = 0f;
            wasDodging = false;
            shoulderVel = 0f;
            rearClear = 4f;
            rightClear = 3f;
            leftClear = 3f;
            frontClear = 4f;
            ceilingClear = 2.5f;
            rearFar = 8f;
            rightFar = 3f;
            leftFar = 3f;
            ceilingFar = 2.5f;
            rearClosing = 0f;
            prevRawRear = 4f;
            rawGap = 4f;
            probesReady = false;
            punchPos = 0f;
            punchVel = 0f;
            ceilingBlocked = false;
            allowShoulder = 1.2f;
            smSoftPull = 0f;
            velSoftPull = 0f;
            smPush = 0f;
            velPush = 0f;
            debugHard = 0f;
            debugBody = 0f;
            sprintHold = 0f;
            sprintWeight = 0f;
            pendingSide = 0f;
            pendingTime = 0f;
            autoResumeAt = 0f;
            euroRear.Reset();
            euroRight.Reset();
            euroLeft.Reset();
            euroFront.Reset();
            euroCeiling.Reset();
            euroCeilingFar.Reset();
            debugLatCorr = 0f;
            debugVerCorr = 0f;
            debugZoneX = "-";
            debugZoneY = "-";
        }

        // Fondu de sortie : quand Heidr perd un canal (arc, lit, build,
        // changement de mode...), on dissipe l'offset en ~0.3 s au lieu
        // de couper net. Reset complet une fois dissipé. L'épaule est
        // conservée dans les deux cas.
        private static void FadeState(float dt)
        {
            float k = CameraUtil.FadeExp(dt);
            combatBlend *= k;
            shelterBlend *= k;
            dungeonBlend *= k;
            confineBlend *= k;
            interiorBlend *= k;
            hipBlend *= k;
            mysticBlend *= k;
            sprintWeight *= k;
            sprintHold = 0f;
            pitchDrift *= k;
            trauma = Mathf.Max(0f, trauma - dt * 1.8f);
            smSoftPull *= k;
            smPush *= k;
            velSoftPull *= k;
            velPush *= k;
            smLat *= k;
            smVer *= k;
            smBoom *= k;
            velLat *= k;
            velVer *= k;
            velBoom *= k;
            shoulderVel *= k;
            extraOffset *= k;
            punchPos *= k;
            punchVel *= k;
            if (extraOffset.sqrMagnitude < 0.000001f && combatBlend < 0.01f
                && shelterBlend < 0.01f && dungeonBlend < 0.01f && confineBlend < 0.01f && mysticBlend < 0.01f
                && sprintWeight < 0.01f
                && Mathf.Abs(punchPos) < 0.01f && smSoftPull < 0.01f && smPush < 0.01f)
            {
                ResetState();
            }
        }

        // Réponse Composer par axe (unités cadre, x corrigé de l'aspect) :
        // 0 dans la dead zone, gain 0.5 en soft, gain 1 au-delà (hard).
        // Signe = sens du décalage image désiré.
        internal static float ZoneResponse(float error, float dead, float soft)
        {
            float a = Mathf.Abs(error);
            if (a <= dead)
            {
                return 0f;
            }

            float s = Mathf.Sign(error);
            if (a <= soft)
            {
                return s * (a - dead) * 0.5f;
            }

            return s * ((soft - dead) * 0.5f + (a - soft));
        }

        internal static string ZoneName(float error, float dead, float soft)
        {
            float a = Mathf.Abs(error);
            if (a <= dead)
            {
                return "dead";
            }

            return a <= soft ? "soft" : "hard";
        }

        [HarmonyPatch(typeof(GameCamera), "GetCameraOffset")]
        private static class GetCameraOffsetPatch
        {
            private static void Postfix(GameCamera __instance, Player player, ref Vector3 __result)
            {
                SyncOverlay();
                if (ShouldSkip(__instance, player))
                {
                    if (Util.IsMenuBlocking())
                    {
                        idleTime = 0f;
                    }

                    FadeState(Time.deltaTime);
                    debugState = ResolveState(player);
                    debugActive = false;
                    if (ModConfig.CameraMode != null && ModConfig.CameraMode.Value == ShoulderCameraMode.Heidr
                        && !CinematicIdleCamera.IsActive)
                    {
                        __result += extraOffset;
                    }

                    return;
                }

                float dt = Time.deltaTime;
                float now = Time.time;
                HeidrState state = ResolveState(player);
                debugState = state;
                debugActive = true;

                float combatTarget = state == HeidrState.Combat ? 1f : 0f;
                combatBlend = Mathf.MoveTowards(combatBlend, combatTarget, dt * (combatTarget > combatBlend ? 4.5f : 1.8f));

                shoulderSign = Mathf.SmoothDamp(shoulderSign, targetSign, ref shoulderVel, 0.45f, Mathf.Infinity, dt);

                Transform eye = player.m_eye ? player.m_eye : player.transform;
                Vector3 right = eye.right;
                Vector3 up = Vector3.up;
                Vector3 back = -eye.forward;
                Vector3 origin = eye.position;

                Vector3 flatVel = player.GetVelocity();
                flatVel.y = 0f;
                float horizon = ShoulderConfirmTime + PredictLead;
                Vector3 predShift = flatVel * horizon;
                if (predShift.magnitude > PredictCap)
                {
                    predShift *= PredictCap / predShift.magnitude;
                }

                UpdateProbes(player, __instance, origin, origin + predShift, right, eye.forward, dt, horizon);
                float ceilingEff = Mathf.Min(ceilingClear, ceilingFar);
                if (ceilingEff < CeilingBlockAt)
                {
                    ceilingBlocked = true;
                }
                else if (ceilingEff > CeilingReleaseAt)
                {
                    ceilingBlocked = false;
                }

                if (OwnerOf(Channel.YawAuto, state) == Owner.Heidr)
                {
                    UpdateShoulderAuto(player, eye.right, dt, state == HeidrState.Sprint);
                }
                else
                {
                    // Yaw auto off : la série de confirmation est rompue.
                    pendingSide = 0f;
                    pendingTime = 0f;
                }

                float hSpeed = flatVel.magnitude;
                float tau = rearClosing > 0.25f ? rawGap / rearClosing : 8f;
                debugTau = tau;
                float roomBehind = Mathf.Min(rearClear, rearFar) - __instance.m_distance;
                float sprintRoom = Mathf.Clamp01(roomBehind / 2.5f);

                // ---- Intérieur : abri, donjon, serré ----
                float shelterTarget = player.InShelter() ? 1f : Mathf.Clamp01(player.m_coverPercentage);
                shelterBlend = DampAsymmetric(shelterBlend, shelterTarget, dt, 2f, 1f);
                dungeonProbeTimer -= dt;
                if (dungeonProbeTimer <= 0f)
                {
                    dungeonProbeTimer = 0.25f;
                    dungeonRaw = Location.IsInsideNoBuildLocation(player.transform.position);
                }

                dungeonBlend = DampAsymmetric(dungeonBlend, dungeonRaw ? 1f : 0f, dt, 2f, 1f);
                interiorBlend = Mathf.Max(shelterBlend, dungeonBlend);
                float sideAvg = (rightClear + leftClear) * 0.5f;
                float confineTarget = 1f - Mathf.Clamp01(Mathf.Min(sideAvg, frontClear * 0.7f + 0.6f, roomBehind + 1f) / 3f);
                confineTarget = Mathf.Max(confineTarget, interiorBlend * 0.75f);
                confineBlend = DampAsymmetric(confineBlend, confineTarget, dt, 1.2f, 0.8f);
                hipBlend = Mathf.Max(interiorBlend, confineBlend);
                if (ceilingBlocked)
                {
                    hipBlend = Mathf.Max(hipBlend, 0.85f);
                }

                sprintRoom *= 1f - interiorBlend * 0.7f;

                debugOrigin = origin;
                debugRight = right;
                debugUp = up;
                debugBack = back;
                debugFuture = origin + predShift;

                // ---- Mystique : idle en explo dégagé uniquement ----
                if (CameraUtil.HasPlayerInput() || Util.IsMenuBlocking() || !Util.IsAlive(player))
                {
                    idleTime = 0f;
                }
                else
                {
                    idleTime += dt;
                }

                float mysticTarget = idleTime >= ModConfig.HeidrIdleDelay.Value && state == HeidrState.Explo &&
                                     hSpeed < 2f && hipBlend < 0.5f ? 1f : 0f;
                mysticBlend = DampAsymmetric(mysticBlend, mysticTarget, dt, 0.3f, 3f);

                // ---- Action : impulsion sur le front de roulade / atterrissage ----
                float vy = player.GetVelocity().y;
                bool dodging = player.InDodge();
                if (dodging && !wasDodging)
                {
                    trauma = Mathf.Min(1f, trauma + 0.25f);
                    punchVel += PunchImpulse;
                }

                if (prevVy < -9f && vy > -1f)
                {
                    trauma = Mathf.Min(1f, trauma + 0.3f);
                    punchVel += PunchImpulse;
                }

                wasDodging = dodging;
                prevVy = vy;
                trauma = Mathf.Max(0f, trauma - dt * 1.8f);

                // Course tenue : le poids reste à 0 pendant SprintCommit, puis
                // monte. Au relâchement il redescend plus vite. L'état Sprint
                // lui-même ne bouge pas (épaule, sondes).
                if (state == HeidrState.Sprint)
                {
                    sprintHold += dt;
                }
                else
                {
                    sprintHold = 0f;
                }

                float sprintTarget = state == HeidrState.Sprint && sprintHold >= SprintCommit ? 1f : 0f;
                sprintWeight = DampAsymmetric(sprintWeight, sprintTarget, dt, SprintRiseSpeed, SprintFallSpeed);

                // ---- Contrat : un module primaire, puis les contraintes ----
                // Le recul sprint suit le poids, y compris le fondu de
                // relâchement encore en explo. Combat / visée ne le gardent pas.
                bool sprintLens = sprintWeight > 0.001f
                    && (state == HeidrState.Sprint || state == HeidrState.Explo);
                Contract primary = sprintLens
                    ? SprintContract(shoulderSign, sprintRoom * sprintWeight)
                    : ExploreContract(shoulderSign);
                if (combatBlend > 0.001f)
                {
                    primary = LerpContract(primary, CombatContract(shoulderSign), combatBlend);
                }

                primary.level = Mathf.Lerp(primary.level, ModConfig.HeidrInteriorHeight.Value, hipBlend);
                primary.size = Mathf.Min(primary.size, Mathf.Lerp(99f, InteriorSizeMax, interiorBlend));
                primary.facing *= Mathf.Lerp(1f, 0.5f, interiorBlend);

                pitchDrift = 0f;
                if (state == HeidrState.Explo && mysticBlend > 0.001f && hipBlend < 0.5f)
                {
                    float sway = ModConfig.HeidrSway.Value;
                    primary.facing = primary.facing * (1f - mysticBlend * 0.55f)
                                     + Mathf.Sin(now * 0.39f) * 0.18f * mysticBlend * sway;
                    primary.size += mysticBlend * 0.7f;
                    primary.level += mysticBlend * 0.3f + Mathf.Sin(now * 1.05f) * 0.05f * mysticBlend * sway;
                    if (OwnerOf(Channel.Pitch, state) == Owner.Heidr)
                    {
                        float breath = sway * mysticBlend;
                        pitchDrift = -1.2f * mysticBlend + Mathf.Sin(now * 0.7f + 0.6f) * 0.15f * breath;
                    }
                }

                if (MagicBush.IsRiding)
                {
                    primary.size += MountSizeAdd;
                    primary.level += MountLevelAdd;
                }

                // SDAZ suit la vitesse, multiplié par l'engagement. La place
                // derrière ne le coupe pas : le mur retire le recul, pas le FOV.
                float fovAdd = 0f;
                if (sprintLens)
                {
                    fovAdd = sprintWeight * Mathf.Min(
                        ModConfig.HeidrSprintFovRate.Value * hSpeed,
                        ModConfig.HeidrSprintFovMax.Value);
                }
                else if (OwnerOf(Channel.Fov, state) == Owner.Heidr && state == HeidrState.Combat)
                {
                    fovAdd = combatBlend * CombatFovAdd;
                }

                if (fovAdd > 0.01f && OwnerOf(Channel.Boom, state) == Owner.Heidr)
                {
                    float dist = __instance.m_distance + primary.size;
                    primary.size = Mathf.Max(-0.5f, primary.size - FovSizeCompensation(dist, __instance.m_fovBase, fovAdd));
                }

                fovTarget = __instance.m_fovBase + fovAdd;
                debugFovAdd = fovAdd;

                debugModule = mysticBlend > 0.6f && state == HeidrState.Explo ? "Mystic" : state.ToString();
                debugSize = primary.size;
                debugFacing = primary.facing;
                debugLevel = primary.level;

                // Composer : pin tête sur le ViewAt du contrat, pas sur l'offset.
                float latCorr = 0f;
                float verCorr = 0f;
                if (OwnerOf(Channel.Lateral, state) == Owner.Heidr)
                {
                    ComputeAimCorrection(player, shoulderSign, primary.viewX, primary.viewY, out latCorr, out verCorr);
                }
                else
                {
                    debugZoneX = "-";
                    debugZoneY = "-";
                }

                latCorr *= 1f - mysticBlend * 0.5f;
                verCorr *= 1f - mysticBlend * 0.5f;
                debugLatCorr = latCorr;
                debugVerCorr = verCorr;
                primary.facing += latCorr;
                primary.level += verCorr;

                if (OwnerOf(Channel.Lateral, state) != Owner.Heidr)
                {
                    primary.facing = 0f;
                }

                if (OwnerOf(Channel.Height, state) != Owner.Heidr)
                {
                    primary.level = 0f;
                }

                if (OwnerOf(Channel.Boom, state) != Owner.Heidr)
                {
                    primary.size = 0f;
                }

                // ---- Filtre : le ressort suit le contrat, pas la collision ----
                float smoothTime = Mathf.Clamp(1.6f / Mathf.Max(1f, ModConfig.HeidrSmoothness.Value), 0.06f, 1f);
                float latT = primary.facing;
                float verT = primary.level;
                float boomT = primary.size;
                if (Mathf.Abs(latT - smLat) < FacingTol)
                {
                    latT = smLat;
                }

                if (Mathf.Abs(boomT - smBoom) < SizeTol)
                {
                    boomT = smBoom;
                }

                // La descente (hanche) n'est pas avalée. Seul le micro-haut l'est.
                if (verT > smVer && verT - smVer < LevelUpTol)
                {
                    verT = smVer;
                }

                if (ceilingBlocked && verT > smVer)
                {
                    verT = smVer;
                }

                smLat = Mathf.SmoothDamp(smLat, latT, ref velLat, smoothTime * 0.8f, Mathf.Infinity, dt);
                smVer = Mathf.SmoothDamp(smVer, verT, ref velVer, smoothTime * 1.4f, Mathf.Infinity, dt);
                smBoom = Mathf.SmoothDamp(smBoom, boomT, ref velBoom, smoothTime, Mathf.Infinity, dt);

                // ---- Légalisation : correction à part, pull-in XOR push ----
                Vector3 corr = Legalize(player, __instance, state, origin, right, up, back, smLat, smVer, smBoom, dt);
                debugCorr = corr;
                StepPunch(dt);
                extraOffset = right * smLat + up * smVer + back * (smBoom + punchPos) + corr;
                __result += extraOffset;
            }
        }

        internal static void ManualSwap()
        {
            targetSign = -targetSign;
            lastSwapTime = Time.time;
            autoResumeAt = Time.time + CameraUtil.ManualHoldSeconds;
            pendingSide = 0f;
            pendingTime = 0f;
            Jotunn.Logger.LogInfo(targetSign > 0f ? "Épaule droite" : "Épaule gauche");
        }

        // Épaule auto, explo + sprint. Scores = sonde actuelle et sonde
        // à l'horizon (confirmation + lead), plus le warp vitesse.
        // Au mystic la marge s'élargit : relâché, pas coupé.
        private static void UpdateShoulderAuto(Player player, Vector3 eyeRight, float dt, bool sprintReduced)
        {
            if (Time.time < autoResumeAt)
            {
                return;
            }

            float sideNow = targetSign > 0f ? Mathf.Min(rightClear, rightFar) : Mathf.Min(leftClear, leftFar);
            if (sprintReduced && sideNow > SprintSwapBlockAt)
            {
                pendingSide = 0f;
                pendingTime = 0f;
                return;
            }

            shoulderTimer -= dt;
            if (shoulderTimer > 0f)
            {
                return;
            }

            shoulderTimer = ProbeStep;

            float rightScore = Mathf.Min(rightClear, rightFar);
            float leftScore = Mathf.Min(leftClear, leftFar);
            Vector3 velocity = player.GetVelocity();
            velocity.y = 0f;
            if (velocity.sqrMagnitude > 0.35f)
            {
                float lateral = Vector3.Dot(velocity.normalized, eyeRight);
                rightScore += Mathf.Max(0f, lateral) * 0.6f;
                leftScore += Mathf.Max(0f, -lateral) * 0.6f;
            }

            float margin = ShoulderMargin * (1f + mysticBlend * 1.5f);
            float desired = targetSign;
            if (leftScore > rightScore + margin)
            {
                desired = -1f;
            }
            else if (rightScore > leftScore + margin)
            {
                desired = 1f;
            }

            if (desired == targetSign)
            {
                pendingSide = 0f;
                pendingTime = 0f;
                return;
            }

            if (pendingSide != desired)
            {
                pendingSide = desired;
                pendingTime = 0f;
                return;
            }

            pendingTime += ProbeStep;
            if (pendingTime >= ShoulderConfirmTime && Time.time - lastSwapTime > ShoulderMinInterval)
            {
                targetSign = desired;
                lastSwapTime = Time.time;
                pendingSide = 0f;
                pendingTime = 0f;
            }
        }

        // Mesure la tête dans le cadre (pose de la frame précédente, retard
        // standard d'un Composer) et convertit l'écart en correction monde.
        // Signe négatif : parallaxe — caméra à droite = sujet à gauche du
        // cadre (orientation fixe, seul l'offset bouge).
        private static void ComputeAimCorrection(Player player, float side, float viewX, float viewY, out float latCorr, out float verCorr)
        {
            latCorr = 0f;
            verCorr = 0f;
            Camera main = Camera.main;
            if (!main || !player)
            {
                return;
            }

            Vector3 head = player.GetHeadPoint();
            if (head.sqrMagnitude < 0.01f)
            {
                head = player.transform.position + Vector3.up * 1.2f;
            }

            Vector3 uv = main.WorldToViewportPoint(head);
            if (uv.z < 0.1f)
            {
                return;
            }

            float aspect = main.aspect;
            if (aspect < 0.1f)
            {
                aspect = 16f / 9f;
            }

            float anchorX = 0.5f - side * viewX;
            float anchorY = viewY;
            debugHeadUv = uv;
            debugAnchor = new Vector2(anchorX, anchorY);

            float dead = ModConfig.HeidrDeadZone.Value;
            float soft = Mathf.Max(ModConfig.HeidrSoftZone.Value, dead + 0.01f);
            float ex = (anchorX - uv.x) * aspect;
            float ey = anchorY - uv.y;
            debugZoneX = ZoneName(ex, dead, soft);
            debugZoneY = ZoneName(ey, dead, soft);

            float dist = Mathf.Max(0.5f, Vector3.Distance(main.transform.position, head));
            float halfH = Mathf.Tan(main.fieldOfView * 0.5f * Mathf.Deg2Rad) * dist;
            latCorr = Mathf.Clamp(-ZoneResponse(ex, dead, soft) * halfH, -MaxAimCorrection, MaxAimCorrection);
            verCorr = Mathf.Clamp(-ZoneResponse(ey, dead, soft) * halfH, -MaxAimCorrection, MaxAimCorrection);
        }

        [HarmonyPatch(typeof(GameCamera), "GetCameraPosition")]
        private static class GetCameraPositionPatch
        {
            private static void Postfix(GameCamera __instance, float dt, ref Vector3 pos, ref Quaternion rot)
            {
                Player player = Player.m_localPlayer;
                if (ShouldSkip(__instance, player))
                {
                    if (ModConfig.CameraMode != null && ModConfig.CameraMode.Value == ShoulderCameraMode.Heidr
                        && !CinematicIdleCamera.IsActive)
                    {
                        ApplyShake(ref pos, ref rot);
                    }

                    return;
                }

                ApplyShake(ref pos, ref rot);
                KeepOutsideBody(player, ref pos);
            }
        }

        // Le near-clip vanilla vaut 0,5 m. S'il coupe le mesh, les faces
        // avant disparaissent et le perso a l'air transparent. On garde
        // la caméra dehors, du côté du boom, même si un mur demande
        // l'inverse : le near-clip du mur se réduit déjà tout seul.
        private const float NearClear = 0.52f;
        private const float BodySlack = 0.18f;
        private const float HeadRadius = 0.24f;
        private const float MaxBodyPush = 1.1f;

        private static void KeepOutsideBody(Player player, ref Vector3 pos)
        {
            if (!player || !player.m_eye)
            {
                debugBody = 0f;
                return;
            }

            Vector3 before = pos;
            Vector3 prefer = -player.m_eye.forward;
            prefer.y *= 0.25f;
            if (prefer.sqrMagnitude < 0.04f)
            {
                prefer = -player.m_eye.forward;
            }

            prefer.Normalize();
            if (player.m_collider)
            {
                pos = PushOffCollider(pos, player.m_collider, NearClear + BodySlack, prefer);
            }

            Vector3 head = player.GetHeadPoint();
            if (head.sqrMagnitude < 0.01f)
            {
                head = player.m_eye.position;
            }

            pos = PushOffPoint(pos, head, HeadRadius + NearClear, prefer);
            if (player.IsBlocking() && player.m_visEquipment && player.m_visEquipment.m_leftItemInstance)
            {
                pos = PushOffInstance(pos, player.m_visEquipment.m_leftItemInstance, NearClear + 0.06f, prefer);
            }

            debugBody = Vector3.Distance(before, pos);
        }

        private static Vector3 PushOffCollider(Vector3 pos, Collider col, float minDist, Vector3 prefer)
        {
            if (!col || !col.enabled)
            {
                return pos;
            }

            Vector3 surface = col.ClosestPoint(pos);
            Vector3 away = pos - surface;
            if (away.sqrMagnitude < 0.0004f || Vector3.Dot(away, prefer) < 0f)
            {
                Vector3 back = col.ClosestPoint(surface + prefer * 3.5f);
                if ((back - surface).sqrMagnitude < 0.0004f)
                {
                    back = col.ClosestPoint(col.bounds.center + prefer * 2f);
                }

                return LimitPush(pos, back + prefer * minDist);
            }

            float dist = away.magnitude;
            if (dist >= minDist)
            {
                return pos;
            }

            return LimitPush(pos, surface + away * (minDist / dist));
        }

        private static Vector3 PushOffPoint(Vector3 pos, Vector3 center, float minDist, Vector3 prefer)
        {
            Vector3 away = pos - center;
            if (away.sqrMagnitude < 0.0004f || Vector3.Dot(away, prefer) < 0f)
            {
                return LimitPush(pos, center + prefer * minDist);
            }

            float dist = away.magnitude;
            if (dist >= minDist)
            {
                return pos;
            }

            return LimitPush(pos, center + away * (minDist / dist));
        }

        private static Vector3 PushOffInstance(Vector3 pos, GameObject instance, float minDist, Vector3 prefer)
        {
            if (!instance)
            {
                return pos;
            }

            Collider[] cols = instance.GetComponentsInChildren<Collider>();
            bool solid = false;
            for (int i = 0; i < cols.Length; i++)
            {
                Collider col = cols[i];
                if (!col || !col.enabled || col.isTrigger)
                {
                    continue;
                }

                solid = true;
                pos = PushOffCollider(pos, col, minDist, prefer);
            }

            if (solid)
            {
                return pos;
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer || !renderer.enabled)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                Vector3 surface = bounds.ClosestPoint(pos);
                Vector3 away = pos - surface;
                if (away.sqrMagnitude < 0.0004f)
                {
                    surface = bounds.ClosestPoint(bounds.center + prefer * (bounds.extents.magnitude + 1f));
                    pos = LimitPush(pos, surface + prefer * minDist);
                    continue;
                }

                if (Vector3.Dot(away, prefer) < 0f)
                {
                    continue;
                }

                float dist = away.magnitude;
                if (dist < minDist)
                {
                    pos = LimitPush(pos, surface + away * (minDist / dist));
                }
            }

            return pos;
        }

        private static Vector3 LimitPush(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float mag = delta.magnitude;
            if (mag <= MaxBodyPush || mag < 0.0001f)
            {
                return to;
            }

            return from + delta * (MaxBodyPush / mag);
        }

        // Secousse : bruit cohérent (Perlin), rotation dominante, roulis
        // plafonné anti-nausée. Roll et pitch ne s'écrivent que si la
        // table en donne la propriété ce frame (silence à l'arc, au menu,
        // et à mystic = 0).
        private static void ApplyShake(ref Vector3 pos, ref Quaternion rot)
        {
            Player player = Player.m_localPlayer;
            HeidrState state = ResolveState(player);
            bool live = !ShouldSkip(GameCamera.instance, player);
            bool allowRoll = live && OwnerOf(Channel.Roll, state) == Owner.Heidr;
            bool allowPitch = live && OwnerOf(Channel.Pitch, state) == Owner.Heidr && mysticBlend > 0.001f;
            if (!allowRoll && !allowPitch)
            {
                return;
            }

            float shake = allowRoll ? trauma * trauma : 0f;
            float yawShake = 0f;
            float pitchShake = 0f;
            float rollShake = 0f;
            Vector3 posShake = Vector3.zero;
            if (shake > 0.0001f)
            {
                float t = Time.time * 28f;
                yawShake = (Mathf.PerlinNoise(t, 3.7f) - 0.5f) * 2f * 1.2f * shake;
                pitchShake = (Mathf.PerlinNoise(t, 9.1f) - 0.5f) * 2f * 0.9f * shake;
                rollShake = (Mathf.PerlinNoise(t, 5.3f) - 0.5f) * 2f * 2.2f * shake;
                posShake = rot * new Vector3(
                    (Mathf.PerlinNoise(t, 1.3f) - 0.5f) * 0.1f * shake,
                    (Mathf.PerlinNoise(t, 7.9f) - 0.5f) * 0.1f * shake,
                    0f);
            }

            float totalRoll = Mathf.Clamp(rollShake, -2.5f, 2.5f);
            float totalPitch = (allowPitch ? pitchDrift : 0f) + pitchShake;
            if (Mathf.Abs(totalRoll) > 0.01f || Mathf.Abs(totalPitch) > 0.01f || Mathf.Abs(yawShake) > 0.01f)
            {
                rot *= Quaternion.Euler(totalPitch, yawShake, totalRoll);
            }

            pos += posShake;
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        private static class UpdateCameraPatch
        {
            private static void Postfix(GameCamera __instance)
            {
                Player player = Player.m_localPlayer;
                bool heidrMode = ModConfig.CameraMode != null
                    && ModConfig.CameraMode.Value == ShoulderCameraMode.Heidr
                    && !CinematicIdleCamera.IsActive;
                HeidrState state = ResolveState(player);
                // L'explo rend le FOV à vanilla, sauf pendant le fondu de
                // relâchement : Clear à cette frame couperait le SDAZ net.
                bool sprintRelease = sprintWeight > 0.02f && state == HeidrState.Explo;
                bool ownsFov = heidrMode
                    && !ShouldSkip(__instance, player)
                    && (OwnerOf(Channel.Fov, state) == Owner.Heidr || sprintRelease);
                if (!ownsFov)
                {
                    // Arc, menu, explo, lit, build : Clear ce frame.
                    // Pas de fondu, et jamais SetTempFOV vers 0 (oscille).
                    debugFovAdd = 0f;
                    CameraUtil.ClearTempFov(__instance, ref appliedFov);
                    return;
                }

                CameraUtil.ApplyTempFov(__instance, fovTarget, ref appliedFov);
            }
        }

        [HarmonyPatch(typeof(CameraEffects), "UpdateDOF")]
        private static class UpdateDofPatch
        {
            private static void Postfix(CameraEffects __instance)
            {
                Player player = Player.m_localPlayer;
                GameCamera camera = GameCamera.instance;
                float weight = MysticDofWeight();
                debugDofW = weight;
                HeidrState state = ResolveState(player);
                bool ownsDof = !ShouldSkip(camera, player)
                    && OwnerOf(Channel.Dof, state) == Owner.Heidr
                    && weight >= 0.02f;
                if (ownsDof)
                {
                    ApplyMysticDof(__instance, player, weight);
                    return;
                }

                mysticDof.Restore(__instance);
            }
        }

        private static float MysticDofWeight()
        {
            float sway = ModConfig.HeidrSway != null ? Mathf.Clamp01(ModConfig.HeidrSway.Value) : 0f;
            float w = mysticBlend * sway * (1f - hipBlend);
            Player player = Player.m_localPlayer;
            Camera main = Camera.main;
            if (main && player)
            {
                w *= Mathf.Clamp01(Mathf.InverseLerp(1.2f, 3.5f, HeadDistance(player, main)));
            }

            return Mathf.Clamp01(w);
        }

        private static float HeadDistance(Player player, Camera camera)
        {
            if (!player || !camera)
            {
                return 4f;
            }

            Vector3 head = player.GetHeadPoint();
            if (head.sqrMagnitude < 0.01f)
            {
                head = player.transform.position + Vector3.up * 1.2f;
            }

            return Vector3.Distance(camera.transform.position, head);
        }

        private static void ApplyMysticDof(CameraEffects effects, Player player, float weight)
        {
            if (!effects || !player || !mysticDof.Capture(effects))
            {
                return;
            }

            Camera main = Camera.main;
            float dist = HeadDistance(player, main);
            float close = Mathf.Clamp01(Mathf.InverseLerp(3.5f, 1.2f, dist));
            float w = Mathf.Clamp01(weight);
            float focalSize = Mathf.Lerp(0.08f, 0.4f, close);
            mysticDof.Push(
                effects,
                Mathf.Lerp(mysticDof.SavedBlur, 1.2f + 2f * w, w),
                Mathf.Lerp(mysticDof.SavedFocalSize, focalSize, w),
                Mathf.Lerp(mysticDof.SavedAperture, 0.3f, w),
                null,
                dist,
                close < 0.5f);
        }

        private struct Contract
        {
            internal float size;
            internal float facing;
            internal float level;
            internal float viewX;
            internal float viewY;
        }

        private static Contract ExploreContract(float sign)
        {
            return new Contract
            {
                size = ModConfig.HeidrDistanceBoost.Value,
                facing = ModConfig.HeidrShoulderOffset.Value * sign,
                level = ModConfig.HeidrHeightOffset.Value,
                viewX = ModConfig.HeidrViewX.Value,
                viewY = ModConfig.HeidrViewY.Value
            };
        }

        private static Contract SprintContract(float sign, float room)
        {
            Contract contract = ExploreContract(sign);
            contract.size += ModConfig.HeidrSprintDistance.Value * room;
            contract.level += 0.08f * room;
            return contract;
        }

        // Pull-in = sujet plus grand. Facing réduit. ViewAt au centre.
        private static Contract CombatContract(float sign)
        {
            Contract contract = ExploreContract(sign);
            contract.size = Mathf.Max(-0.5f, contract.size - ModConfig.HeidrCombatZoom.Value);
            contract.facing *= 0.6f;
            contract.viewX = 0f;
            contract.viewY = 0.5f;
            return contract;
        }

        private static Contract LerpContract(Contract from, Contract to, float weight)
        {
            return new Contract
            {
                size = Mathf.Lerp(from.size, to.size, weight),
                facing = Mathf.Lerp(from.facing, to.facing, weight),
                level = Mathf.Lerp(from.level, to.level, weight),
                viewX = Mathf.Lerp(from.viewX, to.viewX, weight),
                viewY = Mathf.Lerp(from.viewY, to.viewY, weight)
            };
        }

        // Mètres à retirer du boom pour que l'ajout de FOV ne rétrécisse
        // pas le sujet une seconde fois. Échelle ∝ 1 / (distance * tan(fov/2)).
        private static float FovSizeCompensation(float distance, float baseFovDeg, float fovAddDeg)
        {
            if (fovAddDeg <= 0.01f || distance <= 0.05f)
            {
                return 0f;
            }

            float t0 = Mathf.Tan(baseFovDeg * 0.5f * Mathf.Deg2Rad);
            float t1 = Mathf.Tan((baseFovDeg + fovAddDeg) * 0.5f * Mathf.Deg2Rad);
            if (t1 <= 0.01f)
            {
                return 0f;
            }

            return Mathf.Max(0f, distance * (1f - t0 / t1));
        }

        private static float NearRadius()
        {
            Camera main = Camera.main;
            float near = main ? main.nearClipPlane : 0.3f;
            if (near < 0.05f || near > 2f)
            {
                near = 0.3f;
            }

            return Mathf.Max(0.2f, near * 1.15f);
        }

        private static bool Blocks(Player player, RaycastHit hit)
        {
            if (!hit.collider)
            {
                return false;
            }

            Transform root = hit.collider.transform.root;
            return !player || (root != player.transform && !hit.collider.GetComponentInParent<Player>());
        }

        private static float SphereClear(Player player, Vector3 origin, Vector3 direction, float maxDistance, float radius, int mask)
        {
            if (maxDistance <= radius + 0.02f || direction.sqrMagnitude < 0.001f)
            {
                return maxDistance;
            }

            Vector3 start = origin + direction * radius;
            float cast = maxDistance - radius;
            int count = Physics.SphereCastNonAlloc(start, radius, direction, legalHits, cast, mask, QueryTriggerInteraction.Ignore);
            float best = cast;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = legalHits[i];
                if (!Blocks(player, hit) || hit.distance >= best)
                {
                    continue;
                }

                best = hit.distance;
            }

            return best + radius;
        }

        // Pire rayon de la grille 3×3, centre inclus. Déficit en mètres
        // vers la tête. Pas de composante latérale : le push d'épaule
        // est le seul auteur du côté.
        private static float OcclusionDeficit(Player player, Vector3 proposed, Vector3 target, Vector3 right, Vector3 up, float radius, int mask)
        {
            float worst = 0f;
            for (int iy = -1; iy <= 1; iy++)
            {
                for (int ix = -1; ix <= 1; ix++)
                {
                    Vector3 from = proposed + right * (ix * radius) + up * (iy * radius);
                    Vector3 toTarget = target - from;
                    float rayLen = toTarget.magnitude;
                    if (rayLen < 0.2f)
                    {
                        continue;
                    }

                    Vector3 rayDir = toTarget / rayLen;
                    if (!Physics.Raycast(from, rayDir, out RaycastHit hit, rayLen, mask, QueryTriggerInteraction.Ignore) || !Blocks(player, hit))
                    {
                        continue;
                    }

                    float deficit = rayLen - hit.distance;
                    if (deficit > worst)
                    {
                        worst = deficit;
                    }
                }
            }

            return worst;
        }

        // Correction de pose. Le ressort Body ne la voit pas.
        // La sphère near-clip est appliquée en entier. Tau et grille ne
        // rajoutent que ce qu'elle n'a pas déjà couvert, lissé et borné.
        // Explo : push latéral si l'autre côté est libre, sinon ce pull.
        // Combat : pull-in seulement.
        private static Vector3 Legalize(Player player, GameCamera camera, HeidrState state, Vector3 origin, Vector3 right, Vector3 up, Vector3 back, float facing, float level, float size, float dt)
        {
            int mask = camera ? camera.m_blockCameraMask.value : Physics.DefaultRaycastLayers;
            float camDist = camera ? camera.m_distance : 4f;
            float radius = NearRadius();
            Vector3 proposed = origin + right * facing + up * level + back * (camDist + size);
            Vector3 toCam = proposed - origin;
            float want = toCam.magnitude;
            Vector3 dir = want > 0.05f ? toCam / want : back;
            float allowed = SphereClear(player, origin, dir, want, radius, mask);
            float hardPull = Mathf.Max(0f, want - allowed);
            debugHard = hardPull;

            float tauPull = 0f;
            if (debugTau < TauBrake && rearClosing > 0.25f)
            {
                tauPull = rearClosing * (TauBrake - debugTau);
            }

            float gridPull = OcclusionDeficit(player, proposed, origin, right, up, radius, mask);
            float sideNow = facing >= 0f ? Mathf.Min(rightClear, rightFar) : Mathf.Min(leftClear, leftFar);
            float sideOther = facing >= 0f ? Mathf.Min(leftClear, leftFar) : Mathf.Min(rightClear, rightFar);
            bool pushed = state != HeidrState.Combat && sideNow < EscapeRadius && sideOther > sideNow + 0.25f;
            float pushTarget = pushed ? Mathf.Min(0.35f, EscapeRadius - sideNow) : 0f;
            float softTarget = 0f;
            if (!pushed)
            {
                softTarget = Mathf.Max(tauPull, gridPull) - hardPull;
                if (softTarget < 0f)
                {
                    softTarget = 0f;
                }

                if (softTarget > SoftPullCap)
                {
                    softTarget = SoftPullCap;
                }
            }

            smPush = Mathf.SmoothDamp(smPush, pushTarget, ref velPush, SoftCorrSmooth, Mathf.Infinity, dt);
            smSoftPull = Mathf.SmoothDamp(smSoftPull, softTarget, ref velSoftPull, SoftCorrSmooth, Mathf.Infinity, dt);
            if (smPush < 0.0005f)
            {
                smPush = 0f;
            }

            if (smSoftPull < 0.0005f)
            {
                smSoftPull = 0f;
            }

            allowShoulder = smPush;
            float pushSign = facing >= 0f ? -1f : 1f;
            return -dir * hardPull + right * pushSign * smPush - dir * smSoftPull;
        }

        private static void StepPunch(float dt)
        {
            float remain = Mathf.Clamp(dt, 0f, 0.05f);
            float w2 = PunchOmega * PunchOmega;
            float damp = 2f * PunchZeta * PunchOmega;
            int guard = 0;
            while (remain > 0.0001f && guard++ < 8)
            {
                float h = Mathf.Min(remain, 1f / 90f);
                float acc = -w2 * punchPos - damp * punchVel;
                punchVel += acc * h;
                punchPos += punchVel * h;
                remain -= h;
            }

            punchPos = Mathf.Clamp(punchPos, -PunchLimit, PunchLimit);
            if (Mathf.Abs(punchPos) < 0.001f && Mathf.Abs(punchVel) < 0.02f)
            {
                punchPos = 0f;
                punchVel = 0f;
            }
        }

        private static float DampAsymmetric(float current, float target, float dt, float upSpeed, float downSpeed)
        {
            return Mathf.MoveTowards(current, target, dt * (target > current ? upSpeed : downSpeed));
        }

        private static void UpdateProbes(Player player, GameCamera camera, Vector3 origin, Vector3 future, Vector3 right, Vector3 forward, float dt, float horizon)
        {
            envProbeTimer -= dt;
            if (envProbeTimer > 0f)
            {
                return;
            }

            envProbeTimer = ProbeStep;
            int mask = camera ? camera.m_blockCameraMask.value : Physics.DefaultRaycastLayers;
            float radius = NearRadius();
            float rearMax = (camera ? camera.m_distance : 4f) + 3.5f;
            float rawRear = SphereClear(player, origin, -forward, rearMax, radius, mask);
            float rawRight = SphereClear(player, origin, right, 3f, radius, mask);
            float rawLeft = SphereClear(player, origin, -right, 3f, radius, mask);
            float rawFront = SphereClear(player, origin, forward, 4f, radius, mask);
            float camDist = camera ? camera.m_distance : 4f;
            Vector3 camEst = origin - forward * camDist + Vector3.up * smVer;
            float rawCeiling = SphereClear(player, camEst, Vector3.up, 2.5f, radius, mask);
            float rawRearFar = SphereClear(player, future, -forward, rearMax, radius, mask);
            float rawRightFar = SphereClear(player, future, right, 3f, radius, mask);
            float rawLeftFar = SphereClear(player, future, -right, 3f, radius, mask);
            Vector3 camEstFar = future - forward * camDist + Vector3.up * smVer;
            float rawCeilingFar = SphereClear(player, camEstFar, Vector3.up, 2.5f, radius, mask);

            float now = Time.time;
            rearClear = euroRear.Filter(rawRear, now, EuroMinCutoff, EuroBeta, EuroDcutoff);
            rightClear = euroRight.Filter(rawRight, now, EuroMinCutoff, EuroBeta, EuroDcutoff);
            leftClear = euroLeft.Filter(rawLeft, now, EuroMinCutoff, EuroBeta, EuroDcutoff);
            frontClear = euroFront.Filter(rawFront, now, EuroMinCutoff, EuroBeta, EuroDcutoff);
            ceilingClear = euroCeiling.Filter(rawCeiling, now, EuroMinCutoff, EuroBeta, EuroDcutoff);
            rearFar = rawRearFar;
            rightFar = rawRightFar;
            leftFar = rawLeftFar;
            ceilingFar = euroCeilingFar.Filter(rawCeilingFar, now, EuroMinCutoff, EuroBeta, EuroDcutoff);

            // Fermeture sur le brut. Le 1€ reste sur les décisions d'épaule,
            // il ne retarde pas le frein.
            float vSample = probesReady ? Mathf.Max(0f, (prevRawRear - rawRear) / ProbeStep) : 0f;
            float vPred = horizon > 0.05f ? Mathf.Max(0f, (rawRear - rawRearFar) / horizon) : 0f;
            prevRawRear = rawRear;
            rearClosing = Mathf.Max(vSample, vPred);
            rawGap = Mathf.Min(rawRear, rawRearFar);
            probesReady = true;
        }

        // Filtre 1€ (Casiez, Roussel, Vogel, CHI 2012) : passe-bas 1er
        // ordre dont la cutoff suit la vitesse du signal.
        private sealed class OneEuroFilter
        {
            private bool first = true;
            private float prevRaw;
            private float prevHat;
            private float prevDxHat;
            private float prevT;

            internal void Reset()
            {
                first = true;
                prevRaw = 0f;
                prevHat = 0f;
                prevDxHat = 0f;
            }

            internal float Filter(float x, float t, float minCutoff, float beta, float dcutoff)
            {
                float dt = first ? ProbeStep : Mathf.Max(0.001f, t - prevT);
                prevT = t;
                float dx = first ? 0f : (x - prevRaw) / dt;
                float edx = LowPass(dx, prevDxHat, dt, dcutoff);
                float cutoff = minCutoff + beta * Mathf.Abs(edx);
                float hat = LowPass(x, first ? x : prevHat, dt, cutoff);
                first = false;
                prevRaw = x;
                prevHat = hat;
                prevDxHat = edx;
                return hat;
            }

            private static float LowPass(float x, float prev, float dt, float cutoff)
            {
                float tau = 1f / (2f * Mathf.PI * Mathf.Max(0.01f, cutoff));
                float alpha = 1f / (1f + tau / dt);
                return alpha * x + (1f - alpha) * prev;
            }
        }

        private static bool WantOverlay()
        {
            return !GUIManager.IsHeadless()
                && ModConfig.CameraMode != null && ModConfig.CameraMode.Value == ShoulderCameraMode.Heidr
                && ModConfig.HeidrDebug != null && ModConfig.HeidrDebug.Value;
        }

        private static void SyncOverlay()
        {
            if (WantOverlay())
            {
                if (!overlay)
                {
                    GameObject go = new GameObject("HeidrDebugOverlay");
                    Object.DontDestroyOnLoad(go);
                    overlay = go.AddComponent<HeidrDebugOverlay>();
                }

                return;
            }

            if (overlay)
            {
                Object.Destroy(overlay.gameObject);
                overlay = null;
            }
        }

        private static void DrawDebug()
        {
            if (!WantOverlay() || overlay == null)
            {
                return;
            }

            if (Time.unscaledTime - overlayTextAt > 0.25f)
            {
                overlayTextAt = Time.unscaledTime;
                overlayText =
                    "Heidr [" + (debugActive ? debugState.ToString() : debugState + " (pause)") + "]\n" +
                    "lat " + OwnerOf(Channel.Lateral, debugState) + " | ver " + OwnerOf(Channel.Height, debugState) +
                    " | boom " + OwnerOf(Channel.Boom, debugState) + " | yawAuto " + OwnerOf(Channel.YawAuto, debugState) + "\n" +
                    "fov " + OwnerOf(Channel.Fov, debugState) + "(+" + debugFovAdd.ToString("F1") + ")" +
                    " run " + sprintWeight.ToString("F2") +
                    " | dof " + OwnerOf(Channel.Dof, debugState) + "(w" + debugDofW.ToString("F2") + ")" +
                    " | roll " + OwnerOf(Channel.Roll, debugState) + " | pitch " + OwnerOf(Channel.Pitch, debugState) + "\n" +
                    "tête (" + debugHeadUv.x.ToString("F2") + ", " + debugHeadUv.y.ToString("F2") + ") ancrage (" +
                    debugAnchor.x.ToString("F2") + ", " + debugAnchor.y.ToString("F2") + ")\n" +
                    "zone X " + debugZoneX + " Y " + debugZoneY +
                    " corr (" + debugLatCorr.ToString("F2") + ", " + debugVerCorr.ToString("F2") + ")\n" +
                    debugModule + " size " + debugSize.ToString("F2") + " facing " + debugFacing.ToString("F2") +
                    " level " + debugLevel.ToString("F2") + " tau " + debugTau.ToString("F2") + "\n" +
                    "sondes B " + rearClear.ToString("F1") + ">" + rearFar.ToString("F1") +
                    " D " + rightClear.ToString("F1") + ">" + rightFar.ToString("F1") +
                    " G " + leftClear.ToString("F1") + ">" + leftFar.ToString("F1") +
                    " P " + ceilingClear.ToString("F1") + ">" + ceilingFar.ToString("F1") + (ceilingBlocked ? " (bas)" : "") + "\n" +
                    "corr hard " + debugHard.ToString("F2") + " soft " + smSoftPull.ToString("F2") +
                    " push " + allowShoulder.ToString("F2") +
                    " corps " + debugBody.ToString("F2") +
                    " punch " + punchPos.ToString("F2") +
                    " int " + interiorBlend.ToString("F2") + " mys " + mysticBlend.ToString("F2") + "\n" +
                    "côté " + (targetSign > 0f ? "D" : "G") + " (" + rightClear.ToString("F1") + "/" + leftClear.ToString("F1") + ")" +
                    (pendingSide != 0f ? " pending " + (pendingSide > 0f ? "D" : "G") + " " + pendingTime.ToString("F2") + "s" : "") +
                    (Time.time < autoResumeAt ? " (manuel)" : "");
            }

            GUI.Box(new Rect(10f, 10f, 460f, 188f), overlayText);

            // Zones Composer autour de l'ancrage + position tête.
            float dead = ModConfig.HeidrDeadZone != null ? ModConfig.HeidrDeadZone.Value : 0.1f;
            float soft = ModConfig.HeidrSoftZone != null ? Mathf.Max(ModConfig.HeidrSoftZone.Value, dead) : 0.2f;
            Camera main = Camera.main;
            float aspect = main && main.aspect >= 0.1f ? main.aspect : 16f / 9f;
            DrawFrameRect(debugAnchor, dead / aspect, dead, Color.green);
            DrawFrameRect(debugAnchor, soft / aspect, soft, Color.yellow);
            Vector2 headPx = new Vector2(debugHeadUv.x * Screen.width, (1f - debugHeadUv.y) * Screen.height);
            DrawPixel(headPx, 5f, Color.red);
            Vector2 anchorPx = new Vector2(debugAnchor.x * Screen.width, (1f - debugAnchor.y) * Screen.height);
            DrawPixel(anchorPx, 3f, Color.white);
            DrawWorldGizmos(main);
        }

        // Sondes, sphère de fuite (tête future) et correction, projetées à l'écran.
        private static void DrawWorldGizmos(Camera cam)
        {
            if (!cam || debugOrigin.sqrMagnitude < 0.01f)
            {
                return;
            }

            DrawWorldSeg(cam, debugOrigin, debugOrigin + debugBack * Mathf.Min(rearClear, 8f), Color.cyan);
            DrawWorldSeg(cam, debugOrigin, debugOrigin + debugRight * Mathf.Min(rightClear, 3f), Color.green);
            DrawWorldSeg(cam, debugOrigin, debugOrigin - debugRight * Mathf.Min(leftClear, 3f), new Color(0.4f, 0.7f, 1f));
            DrawWorldSeg(cam, debugOrigin, debugOrigin + debugUp * Mathf.Min(ceilingClear, 2.5f), Color.yellow);
            DrawWorldSeg(cam, debugFuture, debugFuture + debugRight * Mathf.Min(rightFar, 3f), new Color(0.5f, 1f, 0.5f));
            DrawWorldSeg(cam, debugFuture, debugFuture - debugRight * Mathf.Min(leftFar, 3f), new Color(0.5f, 0.7f, 1f));
            if (debugCorr.sqrMagnitude > 0.0004f)
            {
                DrawWorldSeg(cam, debugOrigin, debugOrigin + debugCorr, Color.magenta);
            }

            Vector2 prev = Vector2.zero;
            bool has = false;
            const int segments = 12;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector3 point = debugFuture + (debugRight * Mathf.Cos(angle) + debugUp * Mathf.Sin(angle)) * EscapeRadius;
                if (!ToScreen(cam, point, out Vector2 px))
                {
                    has = false;
                    continue;
                }

                if (has)
                {
                    DrawLine(prev, px, new Color(1f, 0.45f, 0.1f));
                }

                prev = px;
                has = true;
            }
        }

        private static void DrawWorldSeg(Camera cam, Vector3 from, Vector3 to, Color color)
        {
            if (!ToScreen(cam, from, out Vector2 a) || !ToScreen(cam, to, out Vector2 b))
            {
                return;
            }

            DrawLine(a, b, color);
        }

        private static bool ToScreen(Camera cam, Vector3 world, out Vector2 pixel)
        {
            Vector3 screen = cam.WorldToScreenPoint(world);
            pixel = new Vector2(screen.x, Screen.height - screen.y);
            return screen.z > 0.1f;
        }

        private static void DrawFrameRect(Vector2 centerUv, float halfWuv, float halfHuv, Color color)
        {
            float x0 = (centerUv.x - halfWuv) * Screen.width;
            float x1 = (centerUv.x + halfWuv) * Screen.width;
            float y0 = (1f - centerUv.y - halfHuv) * Screen.height;
            float y1 = (1f - centerUv.y + halfHuv) * Screen.height;
            DrawLine(new Vector2(x0, y0), new Vector2(x1, y0), color);
            DrawLine(new Vector2(x1, y0), new Vector2(x1, y1), color);
            DrawLine(new Vector2(x1, y1), new Vector2(x0, y1), color);
            DrawLine(new Vector2(x0, y1), new Vector2(x0, y0), color);
        }

        private static void DrawLine(Vector2 from, Vector2 to, Color color)
        {
            EnsurePixel();
            Color saved = GUI.color;
            GUI.color = color;
            if (Mathf.Abs(to.x - from.x) >= Mathf.Abs(to.y - from.y))
            {
                float x0 = Mathf.Min(from.x, to.x);
                GUI.DrawTexture(new Rect(x0, from.y - 1f, Mathf.Abs(to.x - from.x), 2f), overlayPixel);
            }
            else
            {
                float y0 = Mathf.Min(from.y, to.y);
                GUI.DrawTexture(new Rect(from.x - 1f, y0, 2f, Mathf.Abs(to.y - from.y)), overlayPixel);
            }

            GUI.color = saved;
        }

        private static void DrawPixel(Vector2 center, float size, Color color)
        {
            EnsurePixel();
            Color saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), overlayPixel);
            GUI.color = saved;
        }

        private static void EnsurePixel()
        {
            if (!overlayPixel)
            {
                overlayPixel = new Texture2D(1, 1);
                overlayPixel.hideFlags = HideFlags.HideAndDontSave;
                overlayPixel.SetPixel(0, 0, Color.white);
                overlayPixel.Apply();
            }
        }

        internal sealed class HeidrDebugOverlay : MonoBehaviour
        {
            private void OnGUI()
            {
                DrawDebug();
            }
        }
    }
}
