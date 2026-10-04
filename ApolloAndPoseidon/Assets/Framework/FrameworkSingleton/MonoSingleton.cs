using UnityEngine;

namespace Framework.FrameworkSingleton
{
    /// <summary>
    /// Unity MonoBehaviour 单例基类。
    /// 自动创建 GameObject，支持 DontDestroyOnLoad。
    /// </summary>
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T _instance;
        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    // 尝试从场景中查找
                    _instance = FindObjectOfType<T>();

                    // 若未找到，则创建
                    if (_instance == null)
                    {
                        GameObject obj = new GameObject($"[{typeof(T).Name}]");
                        _instance = obj.AddComponent<T>();

                        // 子类可决定是否跨场景保留（默认不保留）
                        // 这里不自动调用 DontDestroyOnLoad，由子类控制
                    }
                }
                return _instance;
            }
        }

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this as T;
            OnInit(); // 子类初始化入口
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
        /// <summary>
        /// 子类重写此方法进行初始化（替代 Awake）
        /// </summary>
        protected abstract void OnInit();

        /// <summary>
        /// 设置是否在场景切换时保留（通常在 OnInit 中调用）
        /// </summary>
        protected void SetDontDestroyOnLoad(bool enable = true)
        {
            if (enable)
            {
                DontDestroyOnLoad(gameObject);
            }
        }
    }
}
//使用示例：
// public class AudioManager : MonoSingleton<AudioManager>
// {
//     protected override void OnInit()
//     {
//         SetDontDestroyOnLoad(true); // 跨场景保留
//         Debug.Log("AudioManager 已初始化");
//     }
//
//     public void PlaySound(string name) { /* 播放音效 */ }
// }