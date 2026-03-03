/*
 * Copyright (C) 2026 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
using Newtonsoft.Json;
using Rust;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Better Line Of Sight Teleport", "VisEntities", "1.1.0")]
    [Description("A better and smarter version of the teleportlos command.")]
    public class BetterLineOfSightTeleport : RustPlugin
    {
        #region Fields

        private static BetterLineOfSightTeleport _plugin;
        private static Configuration _config;
        private const int LAYER_GROUND = Layers.Mask.World | Layers.Mask.Terrain | Layers.Mask.Construction;
        private const float DEBUG_DURATION = 10f;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Maximum Teleport Distance (meters)")]
            public float MaximumTeleportDistance { get; set; }

            [JsonProperty("Prioritized Entities (short prefab names)")]
            public List<string> PrioritizedEntities { get; set; }

            [JsonProperty("Entity Detection Radius (meters)")]
            public float EntityDetectionRadius { get; set; }

            [JsonProperty("Teleport Checkpoints (higher = more precise)")]
            public int TeleportCheckpoints { get; set; }

            [JsonProperty("Enable Debug Mode")]
            public bool EnableDebugMode { get; set; }
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

            if (string.Compare(_config.Version, "1.1.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                MaximumTeleportDistance = 900f,
                PrioritizedEntities = new List<string>
                {
                    "bradleyapc",
                    "patrolhelicopter",
                    "supply_drop",
                    "minicopter.entity",
                    "scraptransporthelicopter",
                    "rhib",
                    "rowboat",
                    "cargoshiptest",
                    "ch47scientists.entity",
                    "playerboat"
                },
                EntityDetectionRadius = 40f,
                TeleportCheckpoints = 15,
                EnableDebugMode = false
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
            if (player == null || !player.IsAdmin)
                return null;

            if (arg.cmd.FullName != "global.teleportlos")
                return null;

            Teleport(player);
            return true;
        }

        #endregion Oxide Hooks

        #region Custom Teleportation

        private void Teleport(BasePlayer player)
        {
            Ray ray = player.eyes.HeadRay();
            float maxDistance = _config.MaximumTeleportDistance;
            float entityDetectionRadius = _config.EntityDetectionRadius;
            int checkpoints = _config.TeleportCheckpoints;
            float checkpointSpacing = maxDistance / checkpoints;

            Vector3 teleportPosition = ray.origin + ray.direction * maxDistance;

            if (_config.EnableDebugMode)
                DrawUtil.Arrow(player, DEBUG_DURATION, Color.black, ray.origin, teleportPosition, 5.0f);

            BaseEntity closestEntity = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i <= checkpoints; i++)
            {
                Vector3 checkPosition = ray.origin + ray.direction * (i * checkpointSpacing);

                if (_config.EnableDebugMode)
                    DrawUtil.Sphere(player, DEBUG_DURATION, Color.black, checkPosition, entityDetectionRadius);

                if (i != 0)
                {
                    List<BaseEntity> entities = FindEntitiesOfType<BaseEntity>(checkPosition, entityDetectionRadius, 1218652417, _config.PrioritizedEntities);
                    foreach (BaseEntity entity in entities)
                    {
                        float distance = Vector3.Distance(checkPosition, entity.transform.position);
                        if (distance < closestDistance)
                        {
                            closestEntity = entity;
                            closestDistance = distance;
                        }
                    }

                    Pool.FreeUnmanaged(ref entities);

                    if (closestEntity != null)
                    {
                        teleportPosition = closestEntity.transform.position;

                        if (_config.EnableDebugMode)
                        {
                            DrawUtil.Box(player, DEBUG_DURATION, Color.green, teleportPosition, 1f);
                            DrawUtil.Text(player, DEBUG_DURATION, Color.white, teleportPosition, closestEntity.ShortPrefabName);
                        }

                        break;
                    }
                }

                if (Physics.Raycast(checkPosition, ray.direction, out RaycastHit raycastHit, checkpointSpacing, LAYER_GROUND))
                {
                    teleportPosition = raycastHit.point;

                    if (_config.EnableDebugMode)
                        DrawUtil.Box(player, DEBUG_DURATION, Color.green, teleportPosition, 1f);

                    break;
                }
            }

            WaterLevel.WaterInfo waterInfo = WaterLevel.GetWaterInfo(teleportPosition, true, true);
            if (waterInfo.isValid && teleportPosition.y < waterInfo.surfaceLevel)
            {
                teleportPosition.y = waterInfo.surfaceLevel;

                if (_config.EnableDebugMode)
                    DrawUtil.Box(player, DEBUG_DURATION, Color.cyan, teleportPosition, 1f);
            }

            player.Teleport(teleportPosition);
        }

        #endregion Custom Teleportation

        #region Helper Functions

        private static List<T> FindEntitiesOfType<T>(Vector3 position, float radius, LayerMask layer, List<string> entityPrefabs) where T : BaseEntity
        {
            int hits = Physics.OverlapSphereNonAlloc(position, radius, Vis.colBuffer, layer, QueryTriggerInteraction.Collide);
            List<T> entities = Pool.Get<List<T>>();

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