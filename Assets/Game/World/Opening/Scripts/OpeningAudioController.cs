using System.Collections;
using UnityEngine;

namespace NOCTURNE.Opening
{
    /// <summary>
    /// 开场音效。
    ///
    /// 只有四个声音：室内底噪、两声猫叫、被子摩擦、一次呼吸变化。
    /// 刻意不堆叠，恐怖感来自安静本身。
    ///
    /// 猫叫分两段 clip（第一声略闷、第二声略清晰），这是唯一的真实录音；
    /// 底噪/被子/呼吸是程序合成的，因为要的是"极轻"，现成素材普遍过戏剧化。
    /// </summary>
    [DisallowMultipleComponent]
    public class OpeningAudioController : MonoBehaviour
    {
        [Header("音源（场景中预先创建）")]
        public AudioSource ambienceSource;
        public AudioSource sfxSource;
        public AudioSource breathSource;

        [Header("片段")]
        public AudioClip ambienceClip;
        public AudioClip meowFirstClip;
        public AudioClip meowSecondClip;
        public AudioClip blanketClip;
        public AudioClip breathClip;

        [Header("音量")]
        [Range(0f, 1f)] public float ambienceVolume = 0.32f;
        [Range(0f, 1f)] public float meowVolume = 0.55f;
        [Range(0f, 1f)] public float blanketVolume = 0.45f;
        [Range(0f, 1f)] public float breathVolume = 0.38f;

        [Header("时序")]
        [Tooltip("两声猫叫之间的间隔")]
        public float meowInterval = 1.0f;

        [Tooltip("第一声猫叫相对第二声的音量比例（第一声更远更不确定）")]
        [Range(0.3f, 1f)] public float firstMeowVolumeScale = 0.8f;

        /// <summary>持续的室内底噪，从开场一直维持。</summary>
        public void PlayAmbience()
        {
            if (ambienceSource == null || ambienceClip == null) return;
            if (ambienceSource.isPlaying) return;

            ambienceSource.clip = ambienceClip;
            ambienceSource.loop = true;
            ambienceSource.playOnAwake = false;
            ambienceSource.volume = ambienceVolume;
            ambienceSource.Play();
        }

        public void StopAmbience()
        {
            if (ambienceSource != null && ambienceSource.isPlaying) ambienceSource.Stop();
        }

        /// <summary>两声猫叫：间隔约 1 秒，第二声更清晰但不突然变响。</summary>
        public IEnumerator MeowSequence()
        {
            PlayMeow(meowFirstClip, meowVolume * firstMeowVolumeScale, 0.96f);
            yield return new WaitForSeconds(meowInterval);
            PlayMeow(meowSecondClip, meowVolume, 1.02f);
        }

        void PlayMeow(AudioClip clip, float volume, float pitch)
        {
            if (sfxSource == null || clip == null) return;
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>被子/床铺摩擦——玩家在睡梦中动了一下。</summary>
        public void PlayBlanket(float delay = 0f)
        {
            if (sfxSource == null || blanketClip == null) return;
            if (delay <= 0f)
            {
                sfxSource.pitch = 1f;
                sfxSource.PlayOneShot(blanketClip, Mathf.Clamp01(blanketVolume));
            }
            else StartCoroutine(PlayBlanketDelayed(delay));
        }

        IEnumerator PlayBlanketDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            PlayBlanket(0f);
        }

        /// <summary>一次缓慢的呼吸（吸气 + 呼气），表现从睡眠里苏醒。</summary>
        public void PlayBreath()
        {
            if (breathSource == null || breathClip == null) return;
            breathSource.clip = breathClip;
            breathSource.loop = false;
            breathSource.volume = breathVolume;
            breathSource.pitch = 1f;
            breathSource.Play();
        }

        public void StopAll()
        {
            if (ambienceSource != null) ambienceSource.Stop();
            if (sfxSource != null) sfxSource.Stop();
            if (breathSource != null) breathSource.Stop();
        }

        /// <summary>运行时改底噪音量（起床后可以稍微抬起来）。</summary>
        public void SetAmbienceVolume(float v)
        {
            ambienceVolume = Mathf.Clamp01(v);
            if (ambienceSource != null) ambienceSource.volume = ambienceVolume;
        }
    }
}
