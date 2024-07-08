/*
 * Copyright (C) 2024 Game4Freak.io
 * Your use of this mod indicates acceptance of the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
using Newtonsoft.Json;
using Rust;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Better Line Of Sight Teleport", "VisEntities", "1.0.0")]
    [Description(" ")]
    public class BetterLineOfSightTeleport : RustPlugin
    {
        #region Fields

        private static BetterLineOfSightTeleport _plugin;
        private static Configuration _config;
                
        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Entity Short Prefab Names")]
            public List<string> EntityShortPrefabNames { get; set; }

            [JsonProperty("Teleport Distance Limit")]
            public float TeleportDistanceLimit { get; set; }
            
            [JsonProperty("Entity Detection Radius")]
            public float EntityDetectionRadius { get; set; }

            [JsonProperty("Number Of Check Points")]
            public int NumberOfCheckPoints { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                EntityShortPrefabNames = new List<string>
                {
                    "bradleyapc",
                    "patrolhelicopter",
                    "supply_drop",
                    "minicopter.entity",
                    "scraptransporthelicopter",
                    "rhib",
                    "rowboat",
                    "testridablehorse",
                    "cargoshiptest"
                },
                TeleportDistanceLimit = 900f,
                EntityDetectionRadius = 40f,
                NumberOfCheckPoints = 15
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
        }

        private void Unload()
        {
            _config = null;
            _plugin = null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return null;

            if (arg.cmd.FullName != "global.teleportlos")
                return null;

            HandleTeleportCommand(player);
            return true;
        }

        private void HandleTeleportCommand(BasePlayer player)
        {
            Ray ray = player.eyes.HeadRay();
            float maximumDistance = _config.TeleportDistanceLimit;
            float detectionRadius = _config.EntityDetectionRadius;
            int intervalCount = _config.NumberOfCheckPoints;
            float intervalDistance = maximumDistance / intervalCount;

            Vector3 teleportPosition = ray.origin + ray.direction * maximumDistance;
            DrawUtil.Line(player, 5f, Color.red, ray.origin, teleportPosition);

            BaseEntity closestEntity = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i <= intervalCount; i++)
            {
                Vector3 checkPosition = ray.origin + ray.direction * (i * intervalDistance);
                DrawUtil.Sphere(player, 5f, Color.blue, checkPosition, detectionRadius);

                List<BaseEntity> entities = FindEntitiesOfType<BaseEntity>(checkPosition, detectionRadius, 1218652417, _config.EntityShortPrefabNames);
                foreach (BaseEntity entity in entities)
                {
                    float distance = Vector3.Distance(checkPosition, entity.transform.position);
                    if (distance < closestDistance)
                    {
                        closestEntity = entity;
                        closestDistance = distance;
                    }
                }

                Pool.FreeList(ref entities);

                if (closestEntity != null)
                {
                    teleportPosition = closestEntity.transform.position;
                    DrawUtil.Box(player, 5f, Color.green, teleportPosition, 1f);
                    break;
                }

                if (Physics.Raycast(checkPosition, ray.direction, out RaycastHit terrainHit, intervalDistance, Layers.Mask.Terrain | Layers.Mask.World))
                {
                    teleportPosition = terrainHit.point;
                    DrawUtil.Box(player, 5f, Color.green, teleportPosition, 1f);
                    break;
                }
            }

            player.Teleport(teleportPosition);
            DrawUtil.Text(player, 5f, Color.white, teleportPosition, $"Teleported to {teleportPosition}");
        }

        #endregion Oxide Hooks

        #region Helper Functions

        private static List<T> FindEntitiesOfType<T>(Vector3 position, float radius, LayerMask layer, List<string> entityPrefabs) where T : BaseEntity
        {
            int hits = Physics.OverlapSphereNonAlloc(position, radius, Vis.colBuffer, layer, QueryTriggerInteraction.Collide);
            List<T> entities = Pool.GetList<T>();

            for (int i = 0; i < hits; i++)
            {
                Collider collider = Vis.colBuffer[i];
                if (collider != null)
                {
                    BaseEntity entity = collider.ToBaseEntity();
                    if (entity != null && entity is T && entityPrefabs.Contains(entity.ShortPrefabName))
                    {
                        entities.Add(entity as T);
                    }
                }
                Vis.colBuffer[i] = null;
            }

            return entities;
        }

        #endregion Helper Functions

        #region Helper Classes

        public static class TerrainUtil
        {
            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask layer)
            {
                return Physics.Linecast(startPosition + new Vector3(0.0f, range, 0.0f), startPosition - new Vector3(0.0f, range, 0.0f), out raycastHit, layer);
            }

            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask layer, Transform ignoreTransform = null)
            {
                startPosition.y += 0.25f;
                range += 0.25f;
                raycastHit = default;

                RaycastHit hit;
                if (!GamePhysics.Trace(new Ray(startPosition, Vector3.down), 0f, out hit, range, layer, QueryTriggerInteraction.UseGlobal, null))
                    return false;

                if (ignoreTransform != null && hit.collider != null
                    && (hit.collider.transform == ignoreTransform || hit.collider.transform.IsChildOf(ignoreTransform)))
                {
                    return GetGroundInfo(startPosition - new Vector3(0f, 0.01f, 0f), out raycastHit, range, layer, ignoreTransform);
                }

                raycastHit = hit;
                return true;
            }
        }

        private static class DrawUtil
        {
            public static void Box(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.box", durationSeconds, color, position, radius);
            }

            public static void Sphere(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.sphere", durationSeconds, color, position, radius);
            }

            public static void Line(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition)
            {
                player.SendConsoleCommand("ddraw.line", durationSeconds, color, fromPosition, toPosition);
            }

            public static void Arrow(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition, float headSize)
            {
                player.SendConsoleCommand("ddraw.arrow", durationSeconds, color, fromPosition, toPosition, headSize);
            }

            public static void Text(BasePlayer player, float durationSeconds, Color color, Vector3 position, string text)
            {
                player.SendConsoleCommand("ddraw.text", durationSeconds, color, position, text);
            }
        }

        #endregion Helper Classes
    }
}