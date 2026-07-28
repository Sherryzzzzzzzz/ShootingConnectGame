using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ShootingGame.Shared.Simulation;

namespace ShootingGame.Server
{
    /// <summary>
    /// Reads spawn point configuration from a JSON file.
    /// </summary>
    public class SpawnPointConfig
    {
        public List<SpawnPointEntry> SpawnPoints { get; set; } = new List<SpawnPointEntry>();

        public class SpawnPointEntry
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public float Yaw { get; set; }
            public int TeamId { get; set; }
        }

        /// <summary>
        /// Load spawn points from a JSON file.
        /// </summary>
        public static List<SpawnPoint> Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[SpawnPointConfig] File not found: {filePath}, using defaults");
                return null;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<SpawnPointConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (config?.SpawnPoints == null || config.SpawnPoints.Count == 0)
                {
                    Console.WriteLine("[SpawnPointConfig] Config file is empty, using defaults");
                    return null;
                }

                var spawnPoints = new List<SpawnPoint>();
                foreach (var entry in config.SpawnPoints)
                {
                    spawnPoints.Add(new SpawnPoint(
                        new ShootingGame.Shared.Math.Vec3(entry.X, entry.Y, entry.Z),
                        entry.Yaw,
                        entry.TeamId));
                }

                Console.WriteLine($"[SpawnPointConfig] Loaded {spawnPoints.Count} spawn points from {filePath}:");
                foreach (var sp in spawnPoints)
                {
                    Console.WriteLine($"  Team{sp.TeamId}: ({sp.Position.x:F2}, {sp.Position.y:F2}, {sp.Position.z:F2}) yaw={sp.Yaw}");
                }

                return spawnPoints;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpawnPointConfig] Error reading {filePath}: {ex.Message}");
                return null;
            }
        }
    }
}
