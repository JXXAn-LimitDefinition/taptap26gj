namespace Framework.FrameworkSingleton
{
    /// <summary>
    /// 普通 C# 单例基类（非 MonoBehaviour）。
    /// 适用于工具类、配置类、纯逻辑类。
    /// </summary>
    public abstract class Singleton<T> where T : Singleton<T>, new()
    {
        private static T _instance;
        public static T Instance
        {
            get { return _instance ??= new T(); }
        }
        protected Singleton()
        {
            // 防止外部实例化
        }
        
    }
}
//使用示例:
// public class GameConfig : Singleton<GameConfig>
// {
//     public int PlayerMaxHealth = 100;
//     public float MoveSpeed = 5f;
//
//     protected override void OnInit()
//     {
//       
//         Debug.Log("GameConfig 已加载");
//     }
// }