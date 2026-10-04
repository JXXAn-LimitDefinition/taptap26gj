using Framework.FrameworkInterface;
using UnityEngine;

namespace Framework.FrameworkUI
{
    public abstract class UIPanel : MonoBehaviour, IEventListener
    {

        // 面板打开时传入的数据
        public object Data { get; set; }

        // 面板生命周期
        public virtual void OnOpen()
        {
            RegisterEvents();
        }
        
        public virtual void OnClose()
        {
            UnregisterEvents();
        }

        // 可选：动画接口（子类可 override 实现淡入、缩放等）
        public virtual void ShowAnimation(System.Action onComplete = null)
        {
            onComplete?.Invoke();
        }

        public virtual void HideAnimation(System.Action onComplete = null)
        {
            onComplete?.Invoke();
        }
  
        /// <summary>
        /// 快速关闭自己的方法，直接在子类里面调用即可关闭面板
        /// </summary>
        protected void CloseSelf()
        {
            UIManager.Instance.ClosePanel(GetType().Name);
        }
        
        /// <summary>
        /// 设置所有的监听,会在面板打开时调用
        /// </summary>
        public abstract void RegisterEvents();
        /// <summary>
        /// 移除所有的监听,会在面板关闭后调用
        /// </summary>
        public abstract void UnregisterEvents();

    }
    //示例:
    //见 \UI\EndPanel.cs
}