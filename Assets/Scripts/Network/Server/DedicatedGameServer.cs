using System;
using UnityEngine;

namespace ShootingGame.Network.Server
{
    /// <summary>
    /// 纯逻辑 Unity 专用服务器入口。不渲染画面、不播音频，只跑网络+战斗逻辑。
    ///
    /// 启动方式：
    ///   1. Build Settings → Dedicated Server (Linux/Windows)
    ///   2. 命令行: Unity.exe -batchmode -nographics -logFile server.log
    ///   3. Editor 测试: 勾选 Inspector 的 Force Server Mode
    /// </summary>
    public class DedicatedGameServer : MonoBehaviour
    {
        [Header("服务器")]
        [SerializeField] private int _listenPort = 7777;
        [SerializeField] private bool _forceServerMode;

        public bool IsServer { get; private set; }

        private void Awake()
        {
            bool isDedicated =
#if UNITY_SERVER
                true;
#else
                Application.isBatchMode || _forceServerMode;
#endif

            if (!isDedicated) { Destroy(gameObject); return; }

            IsServer = true;

            // 纯服务器：关垂直同步、锁帧率、禁用音频
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            AudioListener.pause = true;
            // 禁用所有摄像机渲染
            foreach (var cam in FindObjectsOfType<Camera>(true))
                cam.enabled = false;

            Debug.Log($"[DedicatedServer] 服务器模式启动 port={_listenPort}");
        }

        private void Start()
        {
            if (!IsServer) return;

            // 1. 碰撞世界（CollisionWorldLoader 已在同一 GameObject 上，Awake 中加载完成）
            //    HostBattleServer.Awake 也会从 CollisionWorldLoader.Instance 读取

            // 2. UDP 传输
            var transport = new ServerTransport(_listenPort);
            transport.Start();

            // 3. 战斗服务器
            var serverGo = new GameObject("BattleServer");
            serverGo.transform.SetParent(transform);
            var battle = serverGo.AddComponent<HostBattleServer>();
            battle.StartServer(transport);

            Debug.Log($"[DedicatedServer] 就绪 port={_listenPort}");
        }
    }
}
