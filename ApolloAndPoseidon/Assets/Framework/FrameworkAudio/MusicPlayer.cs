using System.Collections.Generic;
using Framework.FrameworkEvent;
using Framework.FrameworkSingleton;
using UnityEngine;

namespace Framework.FrameworkAudio
{
    /// <summary>
    /// 全局 BGM 播放器 — 把音乐片段拖入 Inspector 列表即用。
    /// 全局唯一 + 跨场景保留；播放走 AudioManager 的交叉淡化，支持顺序/随机/循环列表。
    /// </summary>
    public class MusicPlayer : MonoSingleton<MusicPlayer>
    {
        [Header("===== 曲目配置 =====")]
        [Tooltip("音乐片段列表 — 直接拖入即可")]
        public List<AudioClip> tracks = new List<AudioClip>();
        [Tooltip("启动时自动开始播放")]
        public bool playOnStart = true;
        [Tooltip("启动时从列表第几首开始（下标从 0 开始）")]
        public int startIndex = 0;
        [Tooltip("随机播放（顺序播放关闭后每次切歌随机选一首）")]
        public bool shuffle = false;
        [Tooltip("一首播完后自动切下一首（播放列表模式）；关闭则单曲循环")]
        public bool autoAdvance = true;
        [Tooltip("BGM 音量（0~1）")]
        public float bgmVolume = 1f;
        [Tooltip("切歌时的交叉淡化时长（秒）")]
        public float fadeTime = 1.5f;

        private int _currentIndex = -1;
        private float _elapsed;

        public bool IsPlaying { get; private set; }
        public int CurrentTrackIndex => _currentIndex;
        public AudioClip CurrentClip => _currentIndex >= 0 && _currentIndex < tracks.Count ? tracks[_currentIndex] : null;
        public string CurrentTrackName => CurrentClip != null ? CurrentClip.name : string.Empty;

        protected override void OnInit()
        {
            SetDontDestroyOnLoad(true);
        }

        void Start()
        {
            AudioManager.Instance.AudioSettings(EAudioType.Bgm, bgmVolume);
            if (playOnStart && tracks.Count > 0)
                Play(startIndex);
        }

        void Update()
        {
            if (!IsPlaying || CurrentClip == null)
                return;

            // BGM source 是 loop 的，无法靠 isPlaying 判断播完，用未缩放时间自行累计
            _elapsed += Time.unscaledDeltaTime;
            if (autoAdvance && _elapsed >= CurrentClip.length)
            {
                Next();
            }
        }

        /// <summary>播放指定下标曲目（越界时取模回绕）</summary>
        public void Play(int index)
        {
            if (tracks.Count == 0)
            {
                Debug.LogWarning("[MusicPlayer] tracks 为空，无法播放");
                return;
            }

            int target = index % tracks.Count;
            if (target < 0)
                target += tracks.Count;

            AudioClip clip = tracks[target];
            if (clip == null)
            {
                Debug.LogWarning($"[MusicPlayer] 第 {target} 首曲目为空，跳过");
                return;
            }

            _currentIndex = target;
            _elapsed = 0f;
            IsPlaying = true;
            AudioManager.Instance.ChangeBgm(clip, fadeTime, 0f);
        }

        /// <summary>随机播放一首</summary>
        public void PlayRandom()
        {
            if (tracks.Count == 0)
                return;
            int next = Random.Range(0, tracks.Count);
            if (next == _currentIndex && tracks.Count > 1)
                next = (next + 1) % tracks.Count;
            Play(next);
        }

        /// <summary>下一首（shuffle 时随机）</summary>
        public void Next()
        {
            if (tracks.Count == 0)
                return;
            if (shuffle)
            {
                PlayRandom();
                return;
            }
            Play(_currentIndex + 1);
        }

        /// <summary>上一首（shuffle 时随机）</summary>
        public void Previous()
        {
            if (tracks.Count == 0)
                return;
            if (shuffle)
            {
                PlayRandom();
                return;
            }
            int target = _currentIndex - 1;
            if (target < 0)
                target = tracks.Count - 1;
            Play(target);
        }

        /// <summary>停止播放</summary>
        public void Stop()
        {
            IsPlaying = false;
            _elapsed = 0f;
            AudioManager.Instance.StopAudio(EAudioType.Bgm);
        }

        /// <summary>暂停（当前曲目位置保留）</summary>
        public void Pause()
        {
            if (!IsPlaying)
                return;
            IsPlaying = false;
            AudioManager.Instance.AudioSources[EAudioType.Bgm].Pause();
        }

        /// <summary>恢复播放</summary>
        public void Resume()
        {
            if (IsPlaying || CurrentClip == null)
                return;
            IsPlaying = true;
            AudioManager.Instance.AudioSources[EAudioType.Bgm].UnPause();
        }

        /// <summary>设置 BGM 音量（0~1）</summary>
        public void SetVolume(float volume)
        {
            bgmVolume = Mathf.Clamp01(volume);
            AudioManager.Instance.AudioSettings(EAudioType.Bgm, bgmVolume);
        }
    }
}
