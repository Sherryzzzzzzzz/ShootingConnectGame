using System.Collections.Generic;
using UnityEngine;

public class AudioPoolManager : MonoBehaviour
{
    public static AudioPoolManager Instance;

    [Tooltip("预制的AudioSource对象")]
    public AudioSource audioSourcePrefab;

    [Tooltip("音源池大小")]
    public int poolSize = 10;

    private List<AudioSource> audioSources;
    private int currentIndex = 0;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        audioSources = new List<AudioSource>();

        if (audioSourcePrefab == null)
        {
            // 没有预制体时，直接创建带 AudioSource 的 GameObject 作为池元素
            Debug.Log("[AudioPoolManager] audioSourcePrefab 未赋值，将自动创建默认 AudioSource 池。如需 3D 空间音效，请在 Inspector 中赋值。");
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject($"AudioSource_{i}");
                go.transform.SetParent(transform);
                var audio = go.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 1f; // 3D
                audio.minDistance = 1f;
                audio.maxDistance = 50f;
                audioSources.Add(audio);
            }
            Debug.Log($"[AudioPoolManager] 创建了 {audioSources.Count} 个默认 AudioSource（回退方案）");
            return;
        }

        for (int i = 0; i < poolSize; i++)
        {
            AudioSource newAudio = Instantiate(audioSourcePrefab, transform);
            newAudio.playOnAwake = false;
            audioSources.Add(newAudio);
        }
    }

    public void PlaySound(AudioClip clip, Vector3 position)
    {
        if (clip == null) return;

        // 有池时用池播放（3D空间音效）
        if (audioSources.Count > 0)
        {
            AudioSource audioSource = GetAvailableAudioSource();
            if (audioSource != null)
            {
                audioSource.transform.position = position;
                audioSource.clip = clip;
                audioSource.Play();
                return;
            }
        }

        // 回退：Unity 临时 AudioSource（自动销毁）
        AudioSource.PlayClipAtPoint(clip, position);
    }

    private AudioSource GetAvailableAudioSource()
    {
        if (audioSources.Count == 0) return null;
        // 轮流复用池里的AudioSource
        AudioSource audioSource = audioSources[currentIndex];
        currentIndex = (currentIndex + 1) % audioSources.Count;
        return audioSource;
    }
}