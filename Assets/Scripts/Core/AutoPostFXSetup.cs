using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 自动确保场景有 Global Volume 并挂载卡通后处理 Profile。
/// 由 GameInitializer 创建（DontDestroyOnLoad，跨场景生效）。
/// 注意：会强制应用卡通 Profile（项目明确以卡通风格为目标）。
/// </summary>
public class AutoPostFXSetup : MonoBehaviour
{
    [SerializeField] private VolumeProfile volumeProfile;

    private void Awake()
    {
        // 加载卡通后处理 Profile（如果未指定）
        if (volumeProfile == null)
            volumeProfile = Resources.Load<VolumeProfile>("DefaultVolumeProfile");

        // 找到或创建全局 Volume
        var volume = FindFirstObjectByType<Volume>();
        if (volume == null)
        {
            var go = new GameObject("Global Volume (Cartoon)");
            volume = go.AddComponent<Volume>();
            go.transform.SetParent(transform, false);
        }

        volume.isGlobal = true;
        volume.weight = 1f;
        // 确保卡通 Profile 优先级高于场景中其它全局 Volume
        volume.priority = 100f;

        if (volumeProfile != null)
        {
            volume.profile = volumeProfile;
            Debug.Log($"[PostFX] 已挂载卡通后处理 Profile: {volumeProfile.name} (组件数: {volumeProfile.components.Count})");
        }
        else
        {
            Debug.LogWarning("[PostFX] 未找到 DefaultVolumeProfile，请先运行 菜单 > ShootingGame > 创建卡通后处理");
        }
    }
}
