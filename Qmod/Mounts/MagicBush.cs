using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Qmod
{
    internal static class MagicBush
    {
        private enum Phase
        {
            None,
            Incoming,
            Riding,
            Outgoing
        }

        private const string FallbackPrefab = "Bush01";
        private const string SitAnimation = "attach_chair";
        private const float SitHeight = 0.28f;
        private const float MinClearance = 1.15f;
        private const float RideHeight = 2.3f;
        private const float HoverBand = 14f;
        private const float IncomingDuration = 0.85f;
        private const float OutgoingDuration = 1.15f;
        private static readonly int SlowFallHash = "SlowFall".GetStableHashCode();

        private static Phase phase;
        private static GameObject root;
        private static Transform attach;
        private static Vector3 velocity;
        private static Vector3 outgoingVel;
        private static float yaw;
        private static float bank;
        private static float prevYaw;
        private static float phaseTime;
        private static bool grantingFall;
        private static bool savedInterpolationSet;
        private static RigidbodyInterpolation savedInterpolation;
        private static bool slowFallGranted;
        private static float slowFallGrantedAt;

        internal static bool IsActive => phase != Phase.None;

        internal static bool IsRiding => phase == Phase.Riding;

        internal static bool IsThrusting { get; private set; }

        private static bool IsRidingCharacter(Character character)
        {
            return phase == Phase.Riding && character && character == Player.m_localPlayer;
        }

        internal static void Toggle()
        {
            if (phase == Phase.Riding || phase == Phase.Incoming)
            {
                Dismount(grantSlowFall: phase == Phase.Riding);
                return;
            }

            if (phase == Phase.Outgoing)
            {
                return;
            }

            TrySummon();
        }

        internal static void Tick(Player player)
        {
            TickSlowFallCleanup(player);
            if (phase == Phase.None)
            {
                return;
            }

            if (!ModConfig.IsOdin() || !ModConfig.MagicBushEnabled.Value || !Util.CanAct(player))
            {
                Dismount(grantSlowFall: false);
                DestroyBush();
                return;
            }

            if (!root)
            {
                phase = Phase.None;
                return;
            }

            float dt = Time.deltaTime;
            phaseTime += dt;

            switch (phase)
            {
                case Phase.Incoming:
                    TickIncoming(player, dt);
                    break;
                case Phase.Riding:
                    TickRiding(player, dt);
                    break;
                case Phase.Outgoing:
                    TickOutgoing(dt);
                    break;
            }
        }

        private static void TrySummon()
        {
            Player player = Player.m_localPlayer;
            if (!ModConfig.IsOdin())
            {
                Util.NotifyPlayer(player, "Odin ne répond pas");
                return;
            }

            if (ModConfig.MagicBushEnabled == null || !ModConfig.MagicBushEnabled.Value)
            {
                return;
            }

            if (!CanSummon(player))
            {
                return;
            }

            if (!CreateBush(player))
            {
                return;
            }

            phase = Phase.Incoming;
            phaseTime = 0f;
            velocity = Vector3.zero;
            Util.NotifyCenter("Buisson magique !");

            Jotunn.Logger.LogInfo("Buisson magique appelé");
        }

        private static bool CanSummon(Player player)
        {
            if (!Util.CanAct(player) || player.InBed() ||
                player.InPlaceMode() || player.IsAttached() || player.IsRiding() ||
                player.IsAttachedToShip() || player.InInterior() || player.IsDebugFlying())
            {
                return false;
            }

            return ZNetScene.instance;
        }

        private static bool CreateBush(Player player)
        {
            DestroyBush();
            GameObject prefab = ResolvePrefab();
            if (!prefab)
            {
                Jotunn.Logger.LogWarning("Prefab buisson magique introuvable");
                return false;
            }

            root = new GameObject("QmodMagicBush");
            Vector3 spawn = player.transform.position
                            + player.transform.right * 9f
                            + player.transform.forward * -2f
                            + Vector3.up * 3.5f;
            root.transform.position = spawn;
            root.transform.rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);

            GameObject seat = new GameObject("attach");
            attach = seat.transform;
            attach.SetParent(root.transform, false);
            attach.localPosition = new Vector3(0f, SitHeight, 0f);

            bool prev = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            GameObject clone;
            try
            {
                clone = Object.Instantiate(prefab, root.transform);
            }
            finally
            {
                ZNetView.m_forceDisableInit = prev;
            }

            clone.name = "visual";
            Transform visual = clone.transform;
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one * ModConfig.MagicBushScale.Value;
            StripNetworkAndCollision(clone);
            return true;
        }

        private static GameObject ResolvePrefab()
        {
            string name = ModConfig.MagicBushPrefab != null ? ModConfig.MagicBushPrefab.Value : FallbackPrefab;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = FallbackPrefab;
            }

            GameObject prefab;
            if (Util.TryGetPrefab(name, out prefab))
            {
                return prefab;
            }

            Util.TryGetPrefab(FallbackPrefab, out prefab);
            return prefab;
        }

        private static void StripNetworkAndCollision(GameObject clone)
        {
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
            {
                Object.Destroy(collider);
            }

            foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.Destroy(body);
            }

            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour)
                {
                    continue;
                }

                Object.Destroy(behaviour);
            }

            foreach (ZNetView view in clone.GetComponentsInChildren<ZNetView>(true))
            {
                Object.Destroy(view);
            }
        }

        private static void TickIncoming(Player player, float dt)
        {
            Vector3 target = player.transform.position + Vector3.up * 0.05f;
            root.transform.position = Vector3.Lerp(root.transform.position, target, 1f - Mathf.Exp(-6.5f * dt));
            Vector3 toPlayer = player.transform.position - root.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.05f)
            {
                root.transform.rotation = Quaternion.Slerp(
                    root.transform.rotation,
                    Quaternion.LookRotation(toPlayer.normalized, Vector3.up),
                    1f - Mathf.Exp(-8f * dt));
            }

            if ((root.transform.position - target).sqrMagnitude < 0.35f || phaseTime > IncomingDuration)
            {
                Mount(player);
            }
        }

        private static void Mount(Player player)
        {
            if (!player || !attach)
            {
                Dismount(grantSlowFall: false);
                DestroyBush();
                return;
            }

            player.SetCrouch(false);
            player.AttachStart(attach, null, false, false, false, SitAnimation, Vector3.zero, null);
            FreezePlayerBody(player);
            phase = Phase.Riding;
            phaseTime = 0f;
            StartFlightAttitude(player);
            SnapPlayer(player);
        }

        private static void TickRiding(Player player, float dt)
        {
            if (!player.IsAttached() || player.GetAttachPoint() != attach)
            {
                Dismount(grantSlowFall: true);
                return;
            }

            Fly(dt);
            SnapPlayer(player);
        }

        private static void StartFlightAttitude(Player player)
        {
            yaw = player.transform.eulerAngles.y;
            if (GameCamera.instance)
            {
                yaw = GameCamera.instance.transform.eulerAngles.y;
            }

            prevYaw = yaw;
            bank = 0f;
            velocity = Vector3.zero;
            IsThrusting = false;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static void Fly(float dt)
        {
            dt = Mathf.Max(dt, 0.0001f);
            float cruise = ModConfig.MagicBushSpeed.Value;
            float maxSpeed = Mathf.Max(cruise, ModConfig.MagicBushSprintSpeed.Value);
            float response = ModConfig.MagicBushAcceleration.Value;

            Transform cam = GameCamera.instance ? GameCamera.instance.transform : root.transform;
            Vector3 look = cam.forward;
            Vector3 flat = look;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.001f)
            {
                flat = new Vector3(root.transform.forward.x, 0f, root.transform.forward.z);
            }

            flat.Normalize();

            IsThrusting = ModInput.Held(ModConfig.MagicBushThrust) || ZInput.GetJoyLTrigger() > 0.35f;
            bool braking = ModInput.Held(ModConfig.MagicBushBrake);
            float climb = 0f;
            if (ModInput.Held(ModConfig.MagicBushStrafeUp) || ZInput.GetButton("JoyJump"))
            {
                climb += 1f;
            }

            if (ModInput.Held(ModConfig.MagicBushStrafeDown) || ZInput.GetButton("JoyCrouch"))
            {
                climb -= 1f;
            }

            GetFloor(root.transform.position, out float floor, out float ceiling);
            float clearance = root.transform.position.y - floor;
            bool takeOff = IsThrusting && look.y > 0.18f;
            bool hover = clearance < HoverBand && climb <= 0f && !takeOff;

            Vector3 wish = Vector3.zero;
            if (IsThrusting && !braking)
            {
                float target = hover ? Mathf.Max(cruise, 18f) : maxSpeed;
                wish = (hover ? flat : look.normalized) * target;
                if (hover)
                {
                    wish.y = 0f;
                }
            }

            if (climb > 0f)
            {
                wish.y = Mathf.Max(wish.y, maxSpeed * 0.55f);
            }
            else if (climb < 0f)
            {
                wish.y = Mathf.Min(wish.y, -cruise * 0.7f);
            }
            else if (hover)
            {
                float hold = floor + RideHeight;
                wish.y = (hold - root.transform.position.y) * 3.4f;
                wish.y = Mathf.Clamp(wish.y, -8f, 8f);
            }

            if (braking)
            {
                wish.x = 0f;
                wish.z = 0f;
                if (!hover && climb == 0f)
                {
                    wish.y = 0f;
                }
            }

            velocity = Vector3.Lerp(velocity, wish, 1f - Mathf.Exp(-response * dt));

            Vector3 pos = root.transform.position + velocity * dt;
            if (pos.y < floor)
            {
                pos.y = floor;
                velocity.y = Mathf.Max(0f, velocity.y);
            }
            else if (pos.y > ceiling)
            {
                pos.y = ceiling;
                velocity.y = Mathf.Min(0f, velocity.y);
            }

            root.transform.position = pos;

            float lookYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            yaw = Mathf.LerpAngle(yaw, lookYaw, 1f - Mathf.Exp(-9f * dt));
            float yawRate = Mathf.DeltaAngle(prevYaw, yaw) / dt;
            prevYaw = yaw;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            float targetBank = Mathf.Clamp(-yawRate * 0.22f, -34f, 34f);
            targetBank *= Mathf.Clamp01(speed / 8f);
            bank = Mathf.LerpAngle(bank, targetBank, 1f - Mathf.Exp(-7f * dt));
            float pitch = Mathf.Clamp(-velocity.y * 0.85f, -16f, 18f);
            root.transform.rotation = Quaternion.Euler(pitch, yaw, bank);
        }

        private static void GetFloor(Vector3 pos, out float floor, out float ceiling)
        {
            floor = MinClearance;
            if (ZoneSystem.instance)
            {
                float ground = ZoneSystem.instance.GetGroundHeight(pos);
                floor = Mathf.Max(ground, ZoneSystem.instance.m_waterLevel) + MinClearance;
            }

            ceiling = Mathf.Max(floor + 4f, ModConfig.MagicBushMaxAltitude.Value);
        }

        private static void SnapPlayer(Player player)
        {
            if (!player || !attach)
            {
                return;
            }

            Vector3 pos = attach.position;
            Quaternion rot = attach.rotation;
            player.transform.SetPositionAndRotation(pos, rot);
            Rigidbody body = player.m_body;
            if (!body)
            {
                return;
            }

            body.position = pos;
            body.rotation = rot;
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
        }

        private static void FreezePlayerBody(Player player)
        {
            Rigidbody body = player && player.m_body ? player.m_body : null;
            if (!body)
            {
                return;
            }

            if (!savedInterpolationSet)
            {
                savedInterpolation = body.interpolation;
                savedInterpolationSet = true;
            }

            body.interpolation = RigidbodyInterpolation.None;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private static void RestorePlayerBody(Player player)
        {
            Rigidbody body = player && player.m_body ? player.m_body : null;
            if (body)
            {
                if (savedInterpolationSet)
                {
                    body.interpolation = savedInterpolation;
                }

                body.useGravity = true;
            }

            savedInterpolationSet = false;
        }

        private static void Dismount(bool grantSlowFall)
        {
            if (phase == Phase.None || phase == Phase.Outgoing)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (grantSlowFall && player && ShouldGrantSlowFall(player))
            {
                GrantSlowFall(player);
            }

            RestorePlayerBody(player);
            IsThrusting = false;
            grantingFall = true;
            if (player && player.IsAttached() && player.GetAttachPoint() == attach)
            {
                player.AttachStop();
            }

            grantingFall = false;
            if (!root)
            {
                phase = Phase.None;
                return;
            }

            phase = Phase.Outgoing;
            phaseTime = 0f;
            Vector3 away = velocity.sqrMagnitude > 1f ? velocity.normalized : root.transform.forward;
            outgoingVel = away * 16f + Vector3.up * 9f;
            velocity = Vector3.zero;
        }

        private static void TickOutgoing(float dt)
        {
            outgoingVel += Vector3.up * 7f * dt;
            root.transform.position += outgoingVel * dt;
            root.transform.rotation *= Quaternion.Euler(0f, 70f * dt, 0f);
            if (phaseTime >= OutgoingDuration)
            {
                DestroyBush();
            }
        }

        private static bool ShouldGrantSlowFall(Player player)
        {
            if (!ZoneSystem.instance)
            {
                return false;
            }

            float ground = ZoneSystem.instance.GetGroundHeight(player.transform.position);
            return player.transform.position.y - ground > 5f;
        }

        private static void GrantSlowFall(Player player)
        {
            SEMan seman = player.GetSEMan();
            if (seman == null || seman.HaveStatusEffect(SlowFallHash))
            {
                return;
            }

            seman.AddStatusEffect(SlowFallHash, true, 0, 0f, 0);
            slowFallGranted = true;
            slowFallGrantedAt = Time.time;
        }

        private static void TickSlowFallCleanup(Player player)
        {
            if (!slowFallGranted)
            {
                return;
            }

            if (!player || player.IsDead() || Time.time - slowFallGrantedAt > 60f)
            {
                ClearSlowFall(player);
                return;
            }

            // Laisse le contact sol se stabiliser apres AttachStop avant d'autoriser le retrait.
            if (Time.time - slowFallGrantedAt < 0.5f)
            {
                return;
            }

            if (player.IsOnGround() || player.IsSwimming())
            {
                ClearSlowFall(player);
            }
        }

        private static void ClearSlowFall(Player player)
        {
            slowFallGranted = false;
            if (!player)
            {
                return;
            }

            // Ne retire pas le buff d'une cape equipee : il ne nous appartient pas.
            if (HasSlowFallEquipment(player))
            {
                return;
            }

            SEMan seman = player.GetSEMan();
            if (seman != null && seman.HaveStatusEffect(SlowFallHash))
            {
                seman.RemoveStatusEffect(SlowFallHash, true);
            }
        }

        private static bool HasSlowFallEquipment(Player player)
        {
            Inventory inventory = player.GetInventory();
            if (inventory == null)
            {
                return false;
            }

            List<ItemDrop.ItemData> equipped = inventory.GetEquippedItems();
            if (equipped == null)
            {
                return false;
            }

            foreach (ItemDrop.ItemData item in equipped)
            {
                StatusEffect equipEffect = item != null && item.m_shared != null ? item.m_shared.m_equipStatusEffect : null;
                if (equipEffect != null && equipEffect.name == "SlowFall")
                {
                    return true;
                }
            }

            return false;
        }

        private static void DestroyBush()
        {
            phase = Phase.None;
            velocity = Vector3.zero;
            IsThrusting = false;
            attach = null;
            if (root)
            {
                Object.Destroy(root);
                root = null;
            }
        }

        [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
        [HarmonyPriority(Priority.First)]
        private static class GameCameraLateUpdatePatch
        {
            private static void Prefix()
            {
                Player player = Player.m_localPlayer;
                if (player)
                {
                    Tick(player);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateHover))]
        private static class UpdateHoverPatch
        {
            private static bool Prefix(Player __instance)
            {
                return !IsRidingCharacter(__instance);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateWalking))]
        private static class UpdateWalkingPatch
        {
            private static bool Prefix(Character __instance)
            {
                return !IsRidingCharacter(__instance);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateGroundContact))]
        private static class UpdateGroundContactPatch
        {
            private static bool Prefix(Character __instance)
            {
                return !IsRidingCharacter(__instance);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
        private static class JumpPatch
        {
            private static bool Prefix(Character __instance)
            {
                return !IsRidingCharacter(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class SetControlsPatch
        {
            private static void Prefix(
                Player __instance,
                ref Vector3 movedir,
                ref bool attack,
                ref bool attackHold,
                ref bool secondaryAttack,
                ref bool secondaryAttackHold,
                ref bool block,
                ref bool blockHold,
                ref bool jump,
                ref bool crouch)
            {
                if (phase != Phase.Riding || __instance != Player.m_localPlayer)
                {
                    return;
                }

                movedir = Vector3.zero;
                attack = false;
                attackHold = false;
                secondaryAttack = false;
                secondaryAttackHold = false;
                block = false;
                blockHold = false;
                jump = false;
                crouch = false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
        private static class AttachStopPatch
        {
            private static void Postfix(Player __instance)
            {
                if (grantingFall || phase != Phase.Riding || __instance != Player.m_localPlayer)
                {
                    return;
                }

                Dismount(grantSlowFall: true);
            }
        }
    }
}
