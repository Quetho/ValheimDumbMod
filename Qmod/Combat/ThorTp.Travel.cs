using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace Qmod
{
    internal static partial class ThorTp
    {
        private const float AnimLead = 0.45f;

        private const float DistantRange = 48f;

        private const float GuardianSuppressSeconds = 6f;

        private const float LandLift = 0.5f;

        private const float ScanRadius = 0.38f;

        private const float ScanHeight = 1.8f;

        private const float ScanMaxDeltaY = 4f;

        private const float InteriorY = 3000f;

        private static readonly float[] ScanDistances = { 1.1f, 1.5f, 2.0f, 2.6f };

        private static readonly float[] ScanInteriorDistances = { 0.8f, 1.15f, 1.5f, 2.0f };

        private static readonly int[] ScanBesideAngles = { 90, -90, 135, -135, 45, -45, 180, 0 };

        private static readonly int[] ScanForwardAngles = { 0, 45, -45, 90, -90, 135, -135, 180 };

        private static readonly Collider[] ScanHits = new Collider[24];

        private struct Bolt
        {
            public float Delay;
            public float Radius;
            public bool Center;
        }

        private static readonly Bolt[] SourceStorm =
        {
            new Bolt { Delay = 0.00f, Radius = 0.10f, Center = true },
            new Bolt { Delay = 0.23f, Radius = 0.58f },
            new Bolt { Delay = 0.09f, Radius = 0.42f },
            new Bolt { Delay = 0.34f, Radius = 0.16f, Center = true },
            new Bolt { Delay = 0.06f, Radius = 0.72f },
            new Bolt { Delay = 0.05f, Radius = 0.64f },
            new Bolt { Delay = 0.07f, Radius = 0.50f },
            new Bolt { Delay = 0.29f, Radius = 0.18f, Center = true }
        };

        private static readonly Bolt[] DestStorm =
        {
            new Bolt { Delay = 0.16f, Radius = 0.22f, Center = true },
            new Bolt { Delay = 0.21f, Radius = 0.62f },
            new Bolt { Delay = 0.09f, Radius = 0.44f },
            new Bolt { Delay = 0.31f, Radius = 0.20f, Center = true },
            new Bolt { Delay = 0.07f, Radius = 0.56f },
            new Bolt { Delay = 0.08f, Radius = 0.48f }
        };

        private static readonly Bolt[] LandStorm =
        {
            new Bolt { Delay = 0.00f, Radius = 0.12f, Center = true },
            new Bolt { Delay = 0.11f, Radius = 0.46f },
            new Bolt { Delay = 0.07f, Radius = 0.38f },
            new Bolt { Delay = 0.26f, Radius = 0.16f, Center = true }
        };

        private static bool traveling;

        private static float suppressGuardianUntil;

        internal static bool IsTraveling => traveling;

        private static void Travel(int index)
        {
            if (traveling)
            {
                return;
            }

            List<TpPoint> points = LoadCurrentPoints();
            if (index < 0 || index >= points.Count)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (!CanTravel(player))
            {
                return;
            }

            TpPoint point = points[index];
            Close();
            if (Qmod.Instance)
            {
                Qmod.Instance.StartCoroutine(TravelTo(new Vector3(point.x, point.y, point.z), point.yaw));
            }
        }

        private static IEnumerator TravelBeside(ZDOID id, Vector3 around, float theirYaw, Vector3 dest, float yaw)
        {
            yield return TravelTo(dest, yaw);

            Player player = Player.m_localPlayer;
            if (!player || player.IsDead())
            {
                yield break;
            }

            float waited = 0f;
            while (waited < 2f)
            {
                player = Player.m_localPlayer;
                if (!player || player.IsDead())
                {
                    yield break;
                }

                if (!player.IsTeleporting())
                {
                    break;
                }

                waited += Time.deltaTime;
                yield return null;
            }

            yield return new WaitForSeconds(0.2f);
            player = Player.m_localPlayer;
            if (!player || player.IsDead())
            {
                yield break;
            }

            Vector3 live;
            float liveYaw;
            if (TryGetPlayerWorldPos(id, out live, out liveYaw))
            {
                around = live;
                theirYaw = liveYaw;
            }

            if (Vector3.Distance(player.transform.position, around) < 2.8f)
            {
                yield break;
            }

            Vector3 dest2;
            float yaw2;
            if (!FindClearLanding(around, theirYaw, false, out dest2, out yaw2))
            {
                FallbackBeside(around, theirYaw, out dest2, out yaw2);
            }

            dest2.y += LandLift;
            Quaternion rot = Quaternion.Euler(0f, yaw2, 0f);
            if (Vector3.Distance(player.transform.position, dest2) > DistantRange)
            {
                player.m_teleportCooldown = 2f;
                player.TeleportTo(dest2, rot, true);
                yield break;
            }

            player.transform.SetPositionAndRotation(dest2, rot);
            Rigidbody body = player.m_body;
            if (body)
            {
                body.position = dest2;
                body.rotation = rot;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private static void FallbackBeside(Vector3 around, float theirYaw, out Vector3 dest, out float yaw)
        {
            Vector3 right = Quaternion.Euler(0f, theirYaw, 0f) * Vector3.right;
            dest = around + right * 1.1f;
            SnapToSupport(around.y, ref dest);
            Vector3 look = around - dest;
            look.y = 0f;
            yaw = look.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(look).eulerAngles.y
                : theirYaw + 180f;
        }

        private static bool FindClearLanding(Vector3 around, float aroundYaw, bool preferForward, out Vector3 dest, out float yaw)
        {
            dest = around;
            yaw = aroundYaw;
            int[] angles = preferForward ? ScanForwardAngles : ScanBesideAngles;
            float[] distances = around.y > InteriorY ? ScanInteriorDistances : ScanDistances;
            Vector3 forward = Util.FlatForward(aroundYaw);
            for (int d = 0; d < distances.Length; d++)
            {
                float distance = distances[d];
                for (int a = 0; a < angles.Length; a++)
                {
                    Vector3 offset = Quaternion.Euler(0f, angles[a], 0f) * forward * distance;
                    Vector3 candidate = around + offset;
                    SnapToSupport(around.y, ref candidate);
                    if (!IsLandingClear(candidate, around))
                    {
                        continue;
                    }

                    Vector3 look = around - candidate;
                    look.y = 0f;
                    dest = candidate;
                    yaw = look.sqrMagnitude > 0.0001f
                        ? Quaternion.LookRotation(look).eulerAngles.y
                        : aroundYaw + 180f;
                    return true;
                }
            }

            return false;
        }

        private static void SnapToSupport(float referenceY, ref Vector3 dest)
        {
            dest.y = referenceY;
            if (!ZoneSystem.instance)
            {
                return;
            }

            Vector3 probe = dest;
            probe.y = referenceY;
            float height;
            if (ZoneSystem.instance.FindFloor(probe, out height)
                && height > referenceY - ScanMaxDeltaY
                && height < referenceY + 1.6f)
            {
                dest.y = height;
                return;
            }

            if (referenceY > InteriorY)
            {
                return;
            }

            height = ZoneSystem.instance.GetSolidHeight(dest);
            if (height > referenceY - ScanMaxDeltaY && height < referenceY + ScanMaxDeltaY + 2f)
            {
                dest.y = height;
            }
        }

        private static bool IsLandingClear(Vector3 dest, Vector3 around)
        {
            Player local = Player.m_localPlayer;
            float radius = local ? Mathf.Max(0.28f, local.GetRadius()) : ScanRadius;
            float height = local ? Mathf.Max(1.4f, local.GetHeight()) : ScanHeight;
            Vector3 bottom = dest + Vector3.up * (radius + 0.1f);
            Vector3 top = dest + Vector3.up * Mathf.Max(radius + 0.2f, height - radius);
            int mask = SolidMask();
            int hits = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, ScanHits, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider hit = ScanHits[i];
                if (!hit || hit.isTrigger)
                {
                    continue;
                }

                Transform root = hit.transform;
                if (local && (root == local.transform || root.IsChildOf(local.transform)))
                {
                    continue;
                }

                if (hit.GetComponentInParent<Character>())
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static int SolidMask()
        {
            if (Character.s_blockedRayMask != 0)
            {
                return Character.s_blockedRayMask;
            }

            if (ZoneSystem.instance)
            {
                return ZoneSystem.instance.m_solidRayMask;
            }

            return LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "blocker", "vehicle");
        }

        private static bool CanTravel(Player player)
        {
            if (!Util.CanAct(player))
            {
                return false;
            }

            if (MagicBush.IsActive)
            {
                Util.NotifyPlayer(player, "Descends du nuage d'abord");
                return false;
            }

            if (player.InBed() || player.IsAttached() || player.IsAttachedToShip())
            {
                return false;
            }

            return true;
        }

        private static IEnumerator TravelTo(Vector3 dest, float yaw)
        {
            traveling = true;
            Player player = Player.m_localPlayer;
            if (!CanTravel(player))
            {
                traveling = false;
                yield break;
            }

            dest.y += LandLift;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            suppressGuardianUntil = Time.unscaledTime + GuardianSuppressSeconds;
            if (player.m_zanim)
            {
                player.m_zanim.SetTrigger("gpower");
            }

            yield return new WaitForSeconds(AnimLead);

            if (Qmod.Instance)
            {
                Qmod.Instance.StartCoroutine(PlayBeats(DestStorm, dest, null));
            }

            yield return PlayBeats(SourceStorm, Vector3.zero, player);

            player = Player.m_localPlayer;
            if (!CanTravel(player))
            {
                traveling = false;
                yield break;
            }

            bool distant = Vector3.Distance(player.transform.position, dest) > DistantRange;
            player.m_teleportCooldown = 2f;
            player.TeleportTo(dest, rot, distant);

            float waited = 0f;
            while (waited < 8f)
            {
                player = Player.m_localPlayer;
                if (!player || player.IsDead())
                {
                    traveling = false;
                    yield break;
                }

                if (!player.IsTeleporting())
                {
                    break;
                }

                waited += Time.deltaTime;
                yield return null;
            }

            yield return new WaitForSeconds(0.06f);
            player = Player.m_localPlayer;
            if (player && !player.IsDead())
            {
                yield return PlayBeats(LandStorm, dest, player);
            }

            traveling = false;
        }

        private static IEnumerator PlayBeats(Bolt[] beats, Vector3 origin, Player follow)
        {
            for (int i = 0; i < beats.Length; i++)
            {
                Bolt beat = beats[i];
                float wait = beat.Delay * UnityEngine.Random.Range(0.86f, 1.16f);
                if (wait > 0.001f)
                {
                    yield return new WaitForSeconds(wait);
                }

                Vector3 pos = origin;
                if (follow)
                {
                    pos = follow.transform.position;
                }

                ThorLightning.Play(Scatter(pos, beat.Radius, beat.Center));
            }
        }

        private static Vector3 Scatter(Vector3 origin, float radius, bool center)
        {
            float span = center ? radius * 0.35f : radius;
            float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = span * Mathf.Sqrt(UnityEngine.Random.Range(0f, 1f));
            origin.x += Mathf.Cos(angle) * distance;
            origin.z += Mathf.Sin(angle) * distance;
            return origin;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ActivateGuardianPower))]
        private static class ActivateGuardianPowerPatch
        {
            private static bool Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || Time.unscaledTime >= suppressGuardianUntil)
                {
                    return true;
                }

                return false;
            }
        }
    }
}
