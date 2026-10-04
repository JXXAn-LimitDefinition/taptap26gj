using System.Collections.Generic;
using Framework.FrameworkSingleton;
using UnityEngine;

namespace Framework.FrameworkUI
{
    public class UIManager : MonoSingleton<UIManager>
    {

        [SerializeField] private Canvas mainCanvas; // 主画布
        [SerializeField] private Transform panelRoot; // 所有面板的父物体

        // 面板缓存池（避免重复 Instantiate）
        private Dictionary<string, GameObject> _loadedPanels = new Dictionary<string, GameObject>();

        // 配置表（可在 Inspector 中配置）
        [SerializeField] private List<UIPanelInfo> panelConfigs;

        protected override void OnInit()
        {
            //这个是设置自己在场景切换时是否被销毁
            SetDontDestroyOnLoad(false);
            //这里设置为会被销毁是为了每个场景可以有不同的UIManager配置
        }
        
        private void Start()
        {
            if (!mainCanvas) mainCanvas = GetComponent<Canvas>();
            if (!panelRoot) panelRoot = transform;
        }

        // 通过类型名打开面板（支持传参）
        public T OpenPanel<T>(object data = null) where T : UIPanel
        {
            string panelName = typeof(T).Name;
            return OpenPanel(panelName, data) as T;
        }

        // 通过名字打开面板
        public UIPanel OpenPanel(string panelName, object data = null)
        {
            // 1. 查找配置
            var config = panelConfigs.Find(p => p.panelName == panelName);
            if (config == null)
            {
                Debug.LogError($"UIPanel config not found: {panelName}");
                return null;
            }

            // 2. 加载或获取面板实例
            if (!_loadedPanels.TryGetValue(panelName, out var panelObj))
            {
                // 从 Resources 加载（你也可以替换为 Addressables 或其他加载方式）
                panelObj = Instantiate(config.prefab, panelRoot);
                panelObj.name = panelName;
                _loadedPanels[panelName] = panelObj;

                // 设置 Canvas Layer
                var canvas = panelObj.GetComponent<Canvas>();
                if (canvas != null)
                    canvas.sortingOrder = config.sortingOrder;
            }

            panelObj.SetActive(true);
            var panel = panelObj.GetComponent<UIPanel>();
            panel.Data = data;
            panel.OnOpen();

            // 可选：播放显示动画
            panel.ShowAnimation();

            return panel;
        }

        // 关闭面板
        public void ClosePanel(string panelName)
        {
            if (_loadedPanels.TryGetValue(panelName, out GameObject panelObj))
            {
                var panel = panelObj.GetComponent<UIPanel>();
                panel.HideAnimation(() =>
                {
                    panel.OnClose();
                    panelObj.SetActive(false);
                });
            }
        }

        public void ClosePanel<T>()
        {
            string name = typeof(T).Name;
            ClosePanel(name);
        }

        // 关闭所有面板
        public void CloseAllPanels()
        {
            foreach (var pair in _loadedPanels)
            {
                if (pair.Value != null && pair.Value.activeSelf)
                {
                    var panel = pair.Value.GetComponent<UIPanel>();
                    panel.OnClose();
                    pair.Value.SetActive(false);
                }
            }
        }

        // 销毁面板（从缓存中移除）
        public void DestroyPanel(string panelName)
        {
            if (_loadedPanels.TryGetValue(panelName, out GameObject panelObj))
            {
                ClosePanel(panelName);
                Destroy(panelObj);
                _loadedPanels.Remove(panelName);
            }
        }
        
        // 获取已加载的面板实例
        public T GetPanel<T>() where T : UIPanel
        {
            string panelName = typeof(T).Name;
            if (_loadedPanels.TryGetValue(panelName, out GameObject panelObj))
            {
                var panel = panelObj.GetComponent<T>();
                return panel;
            }
            return null;
        }
        
        // 通过名字获取面板
        public UIPanel GetPanel(string panelName)
        {
            if (_loadedPanels.TryGetValue(panelName, out GameObject panelObj))
            {
                var panel = panelObj.GetComponent<UIPanel>();
                return panel;
            }
            return null;
        }
        
    }
}

//使用示例:
// 打开开始面板（无需传参）
//UIManager.Instance.OpenPanel<StartPanel>();

// 定义传递的数据结构（示例）
// public class UserInfo
// {
//     public int userId;
//     public string userName;
// }
//
// // 在其他脚本中打开面板并传参
// var userData = new UserInfo { userId = 1001, userName = "Player1" };
// UIManager.Instance.OpenPanel<UserDetailPanel>(userData);
//
// // 在目标面板（UserDetailPanel）中接收数据
// public class UserDetailPanel : UIPanel
// {
//     public override void OnOpen()
//     {
//         base.OnOpen();
//         // 从Data属性获取传递的数据
//         if (Data is UserInfo userInfo)
//         {
//             Debug.Log($"打开用户详情：ID={userInfo.userId}, 名称={userInfo.userName}");
//             // 在这里更新UI显示
//         }
//     }
// }
//关闭面板示例:
// public class SettingPanel : UIPanel
// {
//     // 绑定到"关闭"按钮的点击事件
//     public void OnCloseButtonClick()
//     {
//         // 调用基类的CloseSelf()方法关闭当前面板
//         CloseSelf();
//     }
// }
// 在其他脚本中关闭指定面板（例如关闭设置面板）
//UIManager.Instance.ClosePanel("SettingPanel");
// 关闭当前打开的所有面板
//UIManager.Instance.CloseAllPanels();
//销毁面板（会先关闭面板，再从缓存和场景中移除）
//UIManager.Instance.DestroyPanel("StartPanel");
