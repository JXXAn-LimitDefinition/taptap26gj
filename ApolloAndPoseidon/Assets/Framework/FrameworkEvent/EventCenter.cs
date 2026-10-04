using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Framework.FrameworkEvent
{
    public static class EventCenter
    {
        private static Dictionary<string, Action<object>> _eventMap = new Dictionary<string, Action<object>>();

        /// <summary>
        /// 类型化监听（值类型零装箱）。
        /// 每个事件名 → (参数类型 → 多播委托)。AddEventListener&lt;T&gt;/DispatchEvent&lt;T&gt; 走这里。
        /// </summary>
        private static Dictionary<string, Dictionary<Type, Delegate>> _typedEventMap =
            new Dictionary<string, Dictionary<Type, Delegate>>();
        
        #if UNITY_EDITOR
        private static Dictionary<string, List<ListenerInfo>> _listenerTracker = new Dictionary<string, List<ListenerInfo>>();
        #endif
        
        #if UNITY_EDITOR
        private struct ListenerInfo : IEquatable<ListenerInfo>
        {
            public readonly Action<object> Callback;
            public readonly string TypeName;
            public readonly string MethodName;

            public ListenerInfo(Action<object> callback)
            {
                Callback = callback;
                TypeName = callback.Target?.GetType().Name ?? "Unknown";
                MethodName = callback.Method.Name;
            }

            public override string ToString()
            {
                return $"{TypeName}.{MethodName}";
            }

            public bool Equals(ListenerInfo other)
            {
                return Equals(Callback, other.Callback) && TypeName == other.TypeName && MethodName == other.MethodName;
            }

            public override bool Equals(object obj)
            {
                return obj is ListenerInfo other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Callback, TypeName, MethodName);
            }
        }
        #endif

        /// <summary>
        /// 添加事件监听器
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="callback">回调函数</param>
        public static void AddEventListener(string eventName, Action<object> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null)
            {
                Debug.LogWarning($"EventCenter: Attempting to add listener with null event name or callback.");
                return;
            }

            _eventMap.TryAdd(eventName, null);

            // 添加到事件处理链
            _eventMap[eventName] += callback;

            #if UNITY_EDITOR
            // 编辑器模式下，同时添加到追踪器
            if (!_listenerTracker.ContainsKey(eventName))
            {
                _listenerTracker[eventName] = new List<ListenerInfo>();
            }

            var listenerInfo = new ListenerInfo(callback);
            if (!_listenerTracker[eventName].Contains(listenerInfo)) // 防止重复添加
            {
                _listenerTracker[eventName].Add(listenerInfo);
            }
            #endif
        }

        /// <summary>
        /// 移除事件监听器
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="callback">回调函数</param>
        public static void RemoveEventListener(string eventName, Action<object> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null)
            {
                Debug.LogWarning($"EventCenter: Attempting to remove listener with null event name or callback.");
                return;
            }

            if (_eventMap.ContainsKey(eventName))
            {
                _eventMap[eventName] -= callback;

                if (_eventMap[eventName] == null)
                {
                    _eventMap.Remove(eventName);
                }
            }

            #if UNITY_EDITOR
            // 编辑器模式下，同时从追踪器移除
            if (_listenerTracker.ContainsKey(eventName))
            {
                var listeners = _listenerTracker[eventName];
                listeners.RemoveAll(l => l.Callback == callback);

                if (listeners.Count == 0)
                {
                    _listenerTracker.Remove(eventName);
                }
            }
            #endif
        }

        /// <summary>
        /// 添加类型化事件监听器（值类型零装箱）。
        /// 与 object 通道互不影响：同一事件建议统一用类型化或 object 一种方式监听。
        /// </summary>
        /// <typeparam name="T">事件数据类型</typeparam>
        public static void AddEventListener<T>(string eventName, Action<T> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null)
            {
                Debug.LogWarning($"EventCenter: Attempting to add typed listener with null event name or callback.");
                return;
            }

            if (!_typedEventMap.TryGetValue(eventName, out var delegates))
            {
                delegates = new Dictionary<Type, Delegate>();
                _typedEventMap[eventName] = delegates;
            }

            Type type = typeof(T);
            if (delegates.TryGetValue(type, out var existing))
                delegates[type] = (Action<T>)existing + callback;
            else
                delegates[type] = callback;
        }

        /// <summary>
        /// 移除类型化事件监听器
        /// </summary>
        public static void RemoveEventListener<T>(string eventName, Action<T> callback)
        {
            if (string.IsNullOrEmpty(eventName) || callback == null)
            {
                Debug.LogWarning($"EventCenter: Attempting to remove typed listener with null event name or callback.");
                return;
            }

            if (_typedEventMap.TryGetValue(eventName, out var delegates) &&
                delegates.TryGetValue(typeof(T), out var existing))
            {
                var combined = (Action<T>)existing - callback;
                if (combined == null)
                {
                    delegates.Remove(typeof(T));
                    if (delegates.Count == 0)
                        _typedEventMap.Remove(eventName);
                }
                else
                {
                    delegates[typeof(T)] = combined;
                }
            }
        }

        /// <summary>
        /// 分发类型化事件 — 值类型零装箱，直接 Invoke。
        /// 若该事件注册了类型化监听则只走类型化通道；
        /// 否则回落到 object 通道，兼容旧的 Action&lt;object&gt; 监听。
        /// </summary>
        public static void DispatchEvent<T>(string eventName, T eventData)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.LogWarning($"EventCenter: Attempting to dispatch event with null name.");
                return;
            }

            if (_typedEventMap.TryGetValue(eventName, out var delegates))
            {
                if (delegates.TryGetValue(typeof(T), out var del))
                {
                    try
                    {
                        ((Action<T>)del)?.Invoke(eventData);
                        return;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Exception in event handler for '{eventName}': {e.Message}\n{e.StackTrace}");
                        return;
                    }
                }
            }

            // 兜底：无类型化监听时，走 object 通道（兼容旧监听）
            DispatchEvent(eventName, (object)eventData);
        }

        /// <summary>
        /// 分发事件
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="eventData">事件数据</param>
        public static void DispatchEvent(string eventName, object eventData = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.LogWarning($"EventCenter: Attempting to dispatch event with null name.");
                return;
            }

            if (_eventMap.ContainsKey(eventName) && _eventMap[eventName] != null)
            {
                try
                {
                    _eventMap[eventName]?.Invoke(eventData);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Exception in event handler for '{eventName}': {e.Message}\n{e.StackTrace}");
                }
            }
            else
            {
                // 可选：如果事件没有监听者，可以打印信息（调试用）
                // Debug.Log($"Event '{eventName}' dispatched but has no listeners.");
            }
        }

        // --- 编辑器专用方法 ---
        #if UNITY_EDITOR
        public static Dictionary<string, List<string>> GetListenerTrackerForEditor()
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var kvp in _listenerTracker)
            {
                result[kvp.Key] = kvp.Value.Select(li => li.ToString()).ToList();
            }
            return result;
        }

        public static List<string> GetListenersForEventForEditor(string eventName)
        {
            if (_listenerTracker.ContainsKey(eventName))
            {
                return _listenerTracker[eventName].Select(li => li.ToString()).ToList();
            }
            return new List<string>();
        }

        public static List<string> GetRegisteredEventsForEditor()
        {
            return _listenerTracker.Keys.ToList();
        }
        #endif
    }
}