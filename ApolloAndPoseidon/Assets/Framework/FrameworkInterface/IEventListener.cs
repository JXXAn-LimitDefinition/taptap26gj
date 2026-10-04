namespace Framework.FrameworkInterface
{
    /// <summary>
    /// 定义事件监听器的接口。
    /// 实现此接口的类应负责注册和注销其自身对事件中心的监听。
    /// </summary>
    public interface IEventListener
    {
        /// <summary>
        /// 注册事件监听器。
        /// 通常在对象初始化或启用时调用。
        /// </summary>
        void RegisterEvents();

        /// <summary>
        /// 注销事件监听器。
        /// 通常在对象销毁或禁用时调用，以防止内存泄漏。
        /// </summary>
        void UnregisterEvents();
    }
    
    //示例:
    // public class MusicManager : MonoBehaviour, IEventListener // 实现接口
    // {
    //     void Start()
    //     {
    //         // 在 Start 中调用注册
    //         RegisterEvents();
    //     }
    //
    //     void OnDestroy()
    //     {
    //         // 在 OnDestroy 中调用注销
    //         UnregisterEvents();
    //     }
    //
    //     // 实现接口方法
    //     public void RegisterEvents()
    //     {
    //         EventCenter.AddEventListener("OnPlayMusic", PlayMusic);
    //         Debug.Log("MusicManager subscribed to OnPlayMusic via IEventListener");
    //     }
    //
    //     public void UnregisterEvents()
    //     {
    //         EventCenter.RemoveEventListener("OnPlayMusic", PlayMusic);
    //         Debug.Log("MusicManager unsubscribed from OnPlayMusic via IEventListener");
    //     }
    //
    //     private void PlayMusic(object data)
    //     {
    //         Debug.Log($"MusicManager: Playing music - {data}");
    //     }
    // }
    
    
}