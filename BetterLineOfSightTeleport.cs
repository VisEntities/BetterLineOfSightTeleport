/*
 * Copyright (C) 2024 Game4Freak.io
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
    [Info("Better Line Of Sight Teleport", "VisEntities", "1.0.0")]
    [Description("Improves the teleportation mechanism of the native teleportlos command.")]
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

            [JsonProperty("Teleport Distance Limit")]
            public float TeleportDistanceLimit { get; set; }

            [JsonProperty("Entity Short Prefab Names To Prioritize")]
            public List<string> EntityShortPrefabNamesToPrioritize { get; set; }
            
            [JsonProperty("Radius For Detecting Nearby Entities")]
            public float RadiusForDetectingNearbyEntities { get; set; }

            [JsonProperty("Number Of Check Points During Teleportation")]
            public int NumberOfCheckPointsDuringTeleportation { get; set; }

            [JsonProperty("Enable Debug")]
            public bool EnableDebug { get; set; }
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
                TeleportDistanceLimit = 900f,
                EntityShortPrefabNamesToPrioritize = new List<string>
                {
                    "bradleyapc",
                    "patrolhelicopter",
                    "supply_drop",
                    "minicopter.entity",
                    "scraptransporthelicopter",
                    "rhib",
                    "rowboat",
                    "cargoshiptest",
                    "ch47scientists.entity"
                },
                RadiusForDetectingNearbyEntities = 40f,
                NumberOfCheckPointsDuringTeleportation = 15,
                EnableDebug = false
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
            float maximumDistance = _config.TeleportDistanceLimit;
            float entityDetectionRadius = _config.RadiusForDetectingNearbyEntities;
            int numberOfTeleportCheckpoints = _config.NumberOfCheckPointsDuringTeleportation;
            float distanceBetweenCheckpoints = maximumDistance / numberOfTeleportCheckpoints;

            Vector3 initialTeleportPosition = ray.origin + ray.direction * maximumDistance;

            if (_config.EnableDebug)
                DrawUtil.Arrow(player, DEBUG_DURATION, Color.black, ray.origin, initialTeleportPosition, 5.0f);

            BaseEntity closestEntity = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i <= numberOfTeleportCheckpoints; i++)
            {
                Vector3 checkPosition = ray.origin + ray.direction * (i * distanceBetweenCheckpoints);

                if (_config.EnableDebug)
                    DrawUtil.Sphere(player, DEBUG_DURATION, Color.black, checkPosition, entityDetectionRadius);

                if (i != 0)
                {
                    // It has to be this layer; otherwise some entities like the patrol helicopter won't be detected for whatever reason.
                    List<BaseEntity> entities = FindEntitiesOfType<BaseEntity>(checkPosition, entityDetectionRadius, 1218652417, _config.EntityShortPrefabNamesToPrioritize);
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
                        initialTeleportPosition = closestEntity.transform.position;

                        if (_config.EnableDebug)
                        {
                            DrawUtil.Box(player, DEBUG_DURATION, Color.green, initialTeleportPosition, 1f);
                            DrawUtil.Text(player, DEBUG_DURATION, Color.white, initialTeleportPosition, closestEntity.ShortPrefabName);
                        }

                        break;
                    }
                }

                if (Physics.Raycast(checkPosition, ray.direction, out RaycastHit raycastHit, distanceBetweenCheckpoints, LAYER_GROUND))
                {
                    initialTeleportPosition = raycastHit.point;

                    if (_config.EnableDebug)
                        DrawUtil.Box(player, DEBUG_DURATION, Color.green, initialTeleportPosition, 1f);

                    break;
                }
            }

            player.Teleport(initialTeleportPosition);
        }

        #endregion Custom Teleportation

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