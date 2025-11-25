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
        else Destroy(gameObject);

        // 初始化池.
        audioSources = new List<AudioSource>();
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

        AudioSource audioSource = GetAvailableAudioSource();
        audioSource.transform.position = position;
        audioSource.clip = clip;
        audioSource.Play();
    }
    
    private AudioSource GetAvailableAudioSource()
    {
        // 轮流复用池里的AudioSource
        AudioSource audioSource = audioSources[currentIndex];
        currentIndex = (currentIndex + 1) % poolSize;
        return audioSource;
    }
}