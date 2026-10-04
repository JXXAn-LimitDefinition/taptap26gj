using UnityEngine;

namespace Framework.FrameworkUI
{
    [System.Serializable]
    public class UIPanelInfo
    {
        public string panelName;          // 面板唯一标识（建议用类名）
        public GameObject prefab;         // 面板 Prefab（放在 Resources/UI/ 下）
        public int sortingOrder = 0;      // Canvas 的 Sorting Order
        public bool isModal = false;      // 是否模态（点击背景关闭等，可扩展）
    }
}
//使用前的注意事项：
// 面板配置：需要在 Unity 编辑器中，给UIManager组件的panelConfigs列表添加面板配置：
// panelName：必须与面板类名一致（例如StartPanel）
// prefab：拖拽面板的 Prefab（需放在Resources/UI/目录或自定义加载路径）
// sortingOrder：设置面板层级（数值越大显示越靠上）
// 面板脚本规范：所有面板脚本必须继承UIPanel，并通过OnOpen、OnClose等生命周期方法处理逻辑。
// 单例模式：UIManager继承了MonoSingleton，确保全局唯一实例，直接通过UIManager.Instance调用即可。