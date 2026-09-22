using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    internal static class CinematicIdleCamera
    {
        private enum MoveType
        {
            Orbit,
            Horizontal,
            Vertical,
            Mix
        }

        private struct Move
        {
            public MoveType Type;
            public float Duration;
            public float YawStart;
            public float YawEnd;
            public float Pitch;
            public float PitchEnd;
            public Vector3 StartLocal;
            public Vector3 EndLocal;
            public bool UseSpherical;
        }

        private const float MinPitch = 8f;
        private const float MaxPitch = 42f;
        private const float ZoomDistance = 1.7f;
        private const float LookHeight = 1.2f;

        private static float idleTime;
        private static int lastTickFrame = -1;
        private static int ignoreInputUntilFrame = -1;
        private static readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        private static Move currentMove;
        private static float moveTime;
        private static Vector3 currentPos;
        private static Quaternion currentRot = Quaternion.identity;
        private static Transform subjectTransform;
        private static Character subjectCharacter;
        private static float subjectLookHeight = LookHeight;
        private static readonly List<Character> characterBuffer = new List<Character>();
        private static readonly List<SubjectCandidate> subjectBuffer = new List<SubjectCandidate>();

        private struct SubjectCandidate
        {
            public Transform Transform;
            public Character Character;
            public float LookHeight;
        }
        private static FieldInfo dofField;
        private static readonly Dictionary<string, FieldInfo> dofPropFields = new Dictionary<string, FieldInfo>();
        private static bool dofSaved;
        private static bool dofWasEnabled;
        private static bool dofWasAutoFocus;
        private static bool dofWasForced;
        private static object dofComponent;
        private static float savedBlur;
        private static float savedFocalSize;
        private static float savedAperture;
        private static float savedFocalLength;
        private static object savedFocalTransform;

        internal static bool IsActive { get; private set; }

        internal static void Tick()
        {
            if (Time.frameCount == lastTickFrame)
            {
                return;
            }

            lastTickFrame = Time.frameCount;

            if (!ModConfig.CinematicIdleEnabled.Value)
            {
                Stop(resetIdle: true);
                return;
            }

            Player player = Util.AlivePlayer();
            if (!player || Util.IsMenuBlocking() || MagicBush.IsActive)
            {
                Stop(resetIdle: true);
                return;
            }

            if (HasPlayerInput())
            {
                Stop(resetIdle: true);
                return;
            }

            idleTime += Time.unscaledDeltaTime;
            float delay = ModConfig.CinematicIdleDelay.Value;
            if (!IsActive && idleTime >= delay)
            {
                Start(player);
            }

            if (IsActive)
            {
                moveTime += Time.unscaledDeltaTime;
                if (moveTime >= currentMove.Duration)
                {
                    NextMove(player);
                }
            }
        }

        internal static bool TryOverride(GameCamera camera, Player player, float dt, ref Vector3 pos, ref Quaternion rot)
        {
            Tick();
            if (!IsActive || !player)
            {
                return false;
            }

            EvaluateCamera(player, camera, dt, out pos, out rot);
            return true;
        }

        private static void Start(Player player)
        {
            IsActive = true;
            PickSubject(player);
            GenerateNextMove();
            SnapToMove(player);
            Jotunn.Logger.LogInfo("Caméra cinématique idle");
        }

        internal static void ForceStart()
        {
            Player player = Util.AlivePlayer();
            if (!player)
            {
                return;
            }

            ignoreInputUntilFrame = Time.frameCount + 2;
            idleTime = ModConfig.CinematicIdleDelay.Value;
            if (!IsActive)
            {
                Start(player);
            }
        }

        private static void NextMove(Player player)
        {
            PickSubject(player);
            GenerateNextMove();
            SnapToMove(player);
        }

        private static void PickSubject(Player player)
        {
            if (Random.value < 0.5f)
            {
                SetPlayerSubject(player);
                return;
            }

            CollectNearbySubjects(player);
            if (subjectBuffer.Count == 0)
            {
                SetPlayerSubject(player);
                return;
            }

            SubjectCandidate pick = subjectBuffer[Random.Range(0, subjectBuffer.Count)];
            subjectTransform = pick.Transform;
            subjectCharacter = pick.Character;
            subjectLookHeight = pick.LookHeight;
        }

        private static void SetPlayerSubject(Player player)
        {
            subjectTransform = player.transform;
            subjectCharacter = player;
            subjectLookHeight = LookHeight;
        }

        private static void CollectNearbySubjects(Player player)
        {
            subjectBuffer.Clear();
            Vector3 origin = player.transform.position;

            characterBuffer.Clear();
            Character.GetCharactersInRange(origin, ModConfig.CinematicSubjectRadius.Value, characterBuffer);
            foreach (Character character in characterBuffer)
            {
                if (!character || character.IsPlayer() || character.IsDead())
                {
                    continue;
                }

                subjectBuffer.Add(new SubjectCandidate
                {
                    Transform = character.transform,
                    Character = character,
                    LookHeight = 1.1f
                });
            }
        }

        private static void GenerateNextMove()
        {
            int kind = Random.Range(0, 4);
            switch (kind)
            {
                case 0:
                    currentMove = MakeOrbit();
                    break;
                case 1:
                    currentMove = MakeHorizontal();
                    break;
                case 2:
                    currentMove = MakeVertical();
                    break;
                default:
                    currentMove = MakeMix();
                    break;
            }
        }

        private static float Sign()
        {
            return Random.value < 0.5f ? 1f : -1f;
        }

        private static Move MakeOrbit()
        {
            float yaw0 = Random.Range(0f, 360f);
            float span = Random.Range(55f, 95f) * Sign();
            return new Move
            {
                Type = MoveType.Orbit,
                Duration = Random.Range(16f, 22f),
                YawStart = yaw0,
                YawEnd = yaw0 + span,
                Pitch = Random.Range(12f, 28f),
                UseSpherical = true
            };
        }

        private static Move MakeHorizontal()
        {
            float y = Random.Range(0.28f, 0.55f);
            float z = Random.value < 0.35f ? Random.Range(1.35f, 1.65f) : Random.Range(-1.65f, -1.35f);
            float x = Random.Range(1.5f, 1.95f) * Sign();
            return new Move
            {
                Type = MoveType.Horizontal,
                Duration = Random.Range(14f, 19f),
                StartLocal = new Vector3(x, y, z),
                EndLocal = new Vector3(-x, y, z)
            };
        }

        private static Move MakeVertical()
        {
            float x = Random.Range(0.85f, 1.6f) * Sign();
            float z = Random.Range(-1.65f, -1.35f);
            float yLow = Random.Range(0.18f, 0.32f);
            float yHigh = Random.Range(0.85f, 1.08f);
            bool up = Random.value < 0.5f;
            return new Move
            {
                Type = MoveType.Vertical,
                Duration = Random.Range(13f, 18f),
                StartLocal = new Vector3(x, up ? yLow : yHigh, z),
                EndLocal = new Vector3(x, up ? yHigh : yLow, z)
            };
        }

        private static Move MakeMix()
        {
            int combo = Random.Range(0, 3);
            if (combo == 0)
            {
                float yaw0 = Random.Range(0f, 360f);
                float span = Random.Range(40f, 75f) * Sign();
                float pitchA = Random.Range(10f, 18f);
                float pitchB = Random.Range(24f, 38f);
                if (Random.value < 0.5f)
                {
                    float tmp = pitchA;
                    pitchA = pitchB;
                    pitchB = tmp;
                }

                return new Move
                {
                    Type = MoveType.Mix,
                    Duration = Random.Range(15f, 21f),
                    YawStart = yaw0,
                    YawEnd = yaw0 + span,
                    Pitch = pitchA,
                    PitchEnd = pitchB,
                    UseSpherical = true
                };
            }

            if (combo == 1)
            {
                float z = Random.Range(-1.6f, -1.35f);
                float x = Random.Range(1.4f, 1.85f) * Sign();
                return new Move
                {
                    Type = MoveType.Mix,
                    Duration = Random.Range(15f, 20f),
                    StartLocal = new Vector3(x, Random.Range(0.2f, 0.4f), z),
                    EndLocal = new Vector3(-x, Random.Range(0.75f, 1.05f), z)
                };
            }

            float y = Random.Range(0.3f, 0.55f);
            float x2 = Random.Range(1.4f, 1.8f) * Sign();
            return new Move
            {
                Type = MoveType.Mix,
                Duration = Random.Range(15f, 20f),
                StartLocal = new Vector3(-x2, y, Random.Range(-1.55f, -1.2f)),
                EndLocal = new Vector3(x2, y, Random.Range(-1.7f, -1.4f))
            };
        }

        private static void SnapToMove(Player player)
        {
            moveTime = 0f;
            Vector3 lookAt = GetLookAt(player);
            currentPos = EvaluateMove(player, lookAt, currentMove, 0f);
            ClampPitch(lookAt, ref currentPos);
            Vector3 lookDir = lookAt - currentPos;
            currentRot = lookDir.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
                : player.transform.rotation;
        }

        private static void Stop(bool resetIdle)
        {
            if (IsActive)
            {
                RestoreDof();
            }

            IsActive = false;
            dofComponent = null;
            if (resetIdle)
            {
                idleTime = 0f;
            }
        }

        private static void EvaluateCamera(Player player, GameCamera camera, float dt, out Vector3 pos, out Quaternion rot)
        {
            Vector3 lookAt = GetLookAt(player);
            Move move = currentMove;
            float t = move.Duration > 0f ? Mathf.Clamp01(moveTime / move.Duration) : 1f;
            Vector3 targetPos = EvaluateMove(player, lookAt, move, t);
            ClampPitch(lookAt, ref targetPos);
            AvoidCollision(player, camera, lookAt, ref targetPos);

            currentPos = targetPos;
            Vector3 lookDir = lookAt - currentPos;
            if (lookDir.sqrMagnitude < 0.001f)
            {
                lookDir = player.transform.forward;
            }

            currentRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            pos = currentPos;
            rot = currentRot;
        }

        private static Vector3 EvaluateMove(Player player, Vector3 lookAt, Move move, float t)
        {
            if (move.UseSpherical || move.Type == MoveType.Orbit)
            {
                float yaw = GetFacing(player).eulerAngles.y + Mathf.Lerp(move.YawStart, move.YawEnd, t);
                float pitchEnd = move.Type == MoveType.Mix ? move.PitchEnd : move.Pitch;
                float pitch = Mathf.Clamp(Mathf.Lerp(move.Pitch, pitchEnd, t), MinPitch, MaxPitch);
                Quaternion orbitRot = Quaternion.Euler(pitch, yaw, 0f);
                return lookAt - orbitRot * Vector3.forward * ZoomDistance;
            }

            Vector3 local = Vector3.Lerp(move.StartLocal, move.EndLocal, t);
            return lookAt + GetFacing(player) * local;
        }

        private static Quaternion GetFacing(Player player)
        {
            Transform t = subjectTransform ? subjectTransform : player.transform;
            return Quaternion.Euler(0f, t.eulerAngles.y, 0f);
        }

        private static void ClampPitch(Vector3 lookAt, ref Vector3 pos)
        {
            Vector3 toCam = pos - lookAt;
            float horiz = new Vector2(toCam.x, toCam.z).magnitude;
            if (horiz < 0.35f)
            {
                horiz = 0.35f;
                Vector3 flat = new Vector3(toCam.x, 0f, toCam.z);
                if (flat.sqrMagnitude < 0.001f)
                {
                    flat = Vector3.back;
                }

                flat.Normalize();
                pos = lookAt + flat * horiz + Vector3.up * toCam.y;
                toCam = pos - lookAt;
            }

            float maxH = horiz * Mathf.Tan(MaxPitch * Mathf.Deg2Rad);
            float minH = horiz * Mathf.Tan(MinPitch * Mathf.Deg2Rad);
            float y = Mathf.Clamp(toCam.y, minH, maxH);
            pos = lookAt + new Vector3(toCam.x, 0f, toCam.z).normalized * horiz + Vector3.up * y;
        }

        private static Vector3 GetLookAt(Player player)
        {
            if (subjectCharacter)
            {
                Vector3 head = subjectCharacter.GetHeadPoint();
                if (head.sqrMagnitude > 0.01f)
                {
                    return head;
                }

                return subjectCharacter.GetCenterPoint();
            }

            if (subjectTransform)
            {
                return subjectTransform.position + Vector3.up * subjectLookHeight;
            }

            return player.transform.position + Vector3.up * LookHeight;
        }

        private static void AvoidCollision(Player player, GameCamera camera, Vector3 lookAt, ref Vector3 pos)
        {
            Vector3 delta = pos - lookAt;
            float dist = delta.magnitude;
            if (dist < 0.9f)
            {
                return;
            }

            Vector3 dir = delta / dist;
            float skip = 0.65f;
            int mask = camera ? camera.m_blockCameraMask.value : Physics.DefaultRaycastLayers;
            int hitCount = Physics.SphereCastNonAlloc(
                lookAt + dir * skip,
                0.1f,
                dir,
                hitBuffer,
                dist - skip,
                mask,
                QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (!hit.collider || IsIgnoredHit(player, hit.collider))
                {
                    continue;
                }

                if (hit.distance < best)
                {
                    best = hit.distance;
                }
            }

            if (best < float.MaxValue)
            {
                float allowed = Mathf.Max(0.95f, skip + best - 0.25f);
                pos = lookAt + dir * allowed;
            }
        }

        private static bool IsIgnoredHit(Player player, Collider collider)
        {
            Transform root = collider.transform.root;
            if (root == player.transform || collider.GetComponentInParent<Player>())
            {
                return true;
            }

            if (subjectTransform && (root == subjectTransform.root || collider.transform.IsChildOf(subjectTransform)))
            {
                return true;
            }

            return subjectCharacter && collider.GetComponentInParent<Character>() == subjectCharacter;
        }

        private static void ApplyCinematicDof(CameraEffects effects, Player player)
        {
            if (!effects)
            {
                return;
            }

            object dof = GetDof(effects);
            if (dof == null)
            {
                return;
            }

            if (!dofSaved)
            {
                dofWasEnabled = dof is Behaviour dofBehaviour && dofBehaviour.enabled;
                dofWasAutoFocus = effects.m_dofAutoFocus;
                dofWasForced = effects.m_forceDof;
                savedBlur = GetDofFloat(dof, "maxBlurSize");
                savedFocalSize = GetDofFloat(dof, "focalSize");
                savedAperture = GetDofFloat(dof, "aperture");
                savedFocalLength = GetDofFloat(dof, "focalLength");
                savedFocalTransform = GetDofValue(dof, "focalTransform");
                dofSaved = true;
            }

            effects.SetDof(true);
            effects.m_forceDof = true;
            effects.m_dofAutoFocus = false;
            SetDofFloat(dof, "maxBlurSize", 5.2f);
            SetDofFloat(dof, "focalSize", 0.06f);
            SetDofFloat(dof, "aperture", 0.22f);
            SetDofValue(dof, "focalTransform", subjectTransform ? subjectTransform : player.transform);
            SetDofFloat(dof, "focalLength", Vector3.Distance(currentPos, GetLookAt(player)));
            SetDofBool(dof, "highResolution", true);
            SetDofBool(dof, "nearBlur", true);
        }

        private static void RestoreDof()
        {
            CameraEffects effects = CameraEffects.instance;
            if (!dofSaved || !effects)
            {
                dofSaved = false;
                return;
            }

            object dof = GetDof(effects);
            effects.m_forceDof = dofWasForced;
            effects.m_dofAutoFocus = dofWasAutoFocus;
            effects.SetDof(dofWasEnabled);
            if (dof != null)
            {
                SetDofFloat(dof, "maxBlurSize", savedBlur);
                SetDofFloat(dof, "focalSize", savedFocalSize);
                SetDofFloat(dof, "aperture", savedAperture);
                SetDofFloat(dof, "focalLength", savedFocalLength);
                SetDofValue(dof, "focalTransform", savedFocalTransform);
            }

            dofSaved = false;
        }

        private static object GetDof(CameraEffects effects)
        {
            if (dofComponent is Behaviour alive && alive)
            {
                return dofComponent;
            }

            dofComponent = null;
            if (dofField == null)
            {
                dofField = typeof(CameraEffects).GetField("m_dof");
            }

            dofComponent = dofField != null ? dofField.GetValue(effects) : null;
            return dofComponent;
        }

        private static float GetDofFloat(object dof, string name)
        {
            object value = GetDofValue(dof, name);
            return value is float f ? f : 0f;
        }

        private static object GetDofValue(object dof, string name)
        {
            FieldInfo field = DofPropField(dof, name);
            return field != null ? field.GetValue(dof) : null;
        }

        private static FieldInfo DofPropField(object dof, string name)
        {
            string key = dof.GetType().FullName + "#" + name;
            FieldInfo field;
            if (!dofPropFields.TryGetValue(key, out field))
            {
                field = dof.GetType().GetField(name);
                dofPropFields[key] = field;
            }

            return field;
        }

        private static void SetDofFloat(object dof, string name, float value)
        {
            SetDofValue(dof, name, value);
        }

        private static void SetDofBool(object dof, string name, bool value)
        {
            SetDofValue(dof, name, value);
        }

        private static void SetDofValue(object dof, string name, object value)
        {
            FieldInfo field = DofPropField(dof, name);
            if (field != null)
            {
                field.SetValue(dof, value);
            }
        }

        private static bool HasPlayerInput()
        {
            if (Time.frameCount <= ignoreInputUntilFrame)
            {
                return false;
            }

            Vector2 mouse = ZInput.GetMouseDelta();
            if (mouse.sqrMagnitude > 4f)
            {
                return true;
            }

            if (Mathf.Abs(ZInput.GetMouseScrollWheel()) > 0.01f)
            {
                return true;
            }

            if (ZInput.GetMouseButton(0) || ZInput.GetMouseButton(1) || ZInput.GetMouseButton(2))
            {
                return true;
            }

            if (ZInput.GetJoyLeftStick().sqrMagnitude > 0.05f || ZInput.GetJoyRightStick().sqrMagnitude > 0.05f)
            {
                return true;
            }

            if (ZInput.GetJoyLTrigger() > 0.25f || ZInput.GetJoyRTrigger() > 0.25f)
            {
                return true;
            }

            if (Input.anyKeyDown)
            {
                return true;
            }

            return ZInput.GetButton("Forward") || ZInput.GetButton("Backward") || ZInput.GetButton("Left") ||
                   ZInput.GetButton("Right") || ZInput.GetButton("Jump") || ZInput.GetButton("Attack") ||
                   ZInput.GetButton("SecondAttack") || ZInput.GetButton("Block") || ZInput.GetButton("Use") ||
                   ZInput.GetButton("Hide") || ZInput.GetButton("Crouch") || ZInput.GetButton("Run") ||
                   ZInput.GetButton("Dodge") || ZInput.GetButtonDown("Inventory") || ZInput.GetButtonDown("Map");
        }

        [HarmonyPatch(typeof(GameCamera), "GetCameraPosition")]
        private static class GetCameraPositionPatch
        {
            private static void Postfix(GameCamera __instance, float dt, ref Vector3 pos, ref Quaternion rot)
            {
                Player player = Player.m_localPlayer;
                TryOverride(__instance, player, dt, ref pos, ref rot);
            }
        }

        [HarmonyPatch(typeof(CameraEffects), "UpdateDOF")]
        private static class UpdateDofPatch
        {
            private static void Postfix(CameraEffects __instance)
            {
                Player player = Player.m_localPlayer;
                if (IsActive && player)
                {
                    ApplyCinematicDof(__instance, player);
                }
            }
        }
    }
}
