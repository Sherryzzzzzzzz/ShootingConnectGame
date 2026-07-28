using System.Collections.Generic;

namespace ShootingGame.Shared.Hero
{
    /// <summary>
    /// 枪械注册表（双端共用）。键为枪械 ID（GunConfig SO 资产名）。
    /// 服务器在启动时从 guns.json 加载；客户端可直接从 GunConfig SO 构建。
    /// </summary>
    public static class GunRegistry
    {
        private static Dictionary<string, GunConfigData> _guns = new Dictionary<string, GunConfigData>();
        private static bool _initialized;

        /// <summary>默认枪械 ID（配置缺失时的回退）</summary>
        public const string DefaultGunId = "Rifle_SemiAuto";

        public static void Initialize(Dictionary<string, GunConfigData> guns)
        {
            _guns = guns ?? new Dictionary<string, GunConfigData>();
            if (_guns.Count == 0)
                InitFallback();
            _initialized = true;
        }

        public static GunConfigData GetGun(string id)
        {
            if (!_initialized) Initialize(null);
            if (id != null && _guns.TryGetValue(id, out var gun))
                return gun;
            // 回退默认枪
            if (_guns.TryGetValue(DefaultGunId, out var def))
                return def;
            foreach (var kv in _guns)
                return kv.Value;
            return null;
        }

        public static bool TryGetGun(string id, out GunConfigData gun)
        {
            if (!_initialized) Initialize(null);
            gun = null;
            return id != null && _guns.TryGetValue(id, out gun);
        }

        public static IReadOnlyDictionary<string, GunConfigData> All
        {
            get { if (!_initialized) Initialize(null); return _guns; }
        }

        /// <summary>无配置时的兜底（数值与 GameConstants 历史默认值一致）</summary>
        private static void InitFallback()
        {
            _guns[DefaultGunId] = new GunConfigData
            {
                Id = DefaultGunId,
                GunName = "半自动步枪",
                FireMode = FireMode.Single,
                Bullet = BulletType.Projectile,
                FireRate = 0.15f,
                Damage = 25,
                ClipSize = 30,
                ReloadTime = 2.0f,
                Range = 200f,
                BulletSpeed = 100f,
            };
        }
    }
}
