using System;
using System.Collections.Generic;
using UnityEngine;

namespace Framework.FSM
{
    /// <summary>
    /// 通用有限状态机基类
    /// 可用于任何需要状态管理的系统
    /// </summary>
    /// <typeparam name="TState">状态枚举类型</typeparam>
    public abstract class FSMBase<TState> where TState : Enum
    {
        protected Dictionary<TState, IFSMState<TState>> states;
        protected IFSMState<TState> currentState;
        protected TState currentStateType;
        
        /// <summary>
        /// 当前状态
        /// </summary>
        public TState CurrentState => currentStateType;
        
        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning { get; private set; } = false;
        
        public FSMBase()
        {
            states = new Dictionary<TState, IFSMState<TState>>();
            InitializeStates();
        }
        
        /// <summary>
        /// 初始化所有状态（子类实现）
        /// </summary>
        protected abstract void InitializeStates();
        
        /// <summary>
        /// 注册状态
        /// </summary>
        protected void RegisterState(TState stateType, IFSMState<TState> state)
        {
            if (states.ContainsKey(stateType))
            {
                Debug.LogWarning($"[FSM] 状态 {stateType} 已存在，将被覆盖");
            }
            
            states[stateType] = state;
            state.SetFSM(this);
            
            Debug.Log($"[FSM] 注册状态: {stateType}");
        }
        
        /// <summary>
        /// 切换状态
        /// </summary>
        public virtual void ChangeState(TState newState)
        {
            if (currentState != null && EqualityComparer<TState>.Default.Equals(currentStateType, newState))
            {
                Debug.LogWarning($"[FSM] 已经在状态 {newState} 中");
                return;
            }
            
            if (!states.ContainsKey(newState))
            {
                Debug.LogError($"[FSM] 状态 {newState} 未注册！");
                return;
            }
            
            // 退出当前状态
            if (currentState != null)
            {
                currentState.Exit();
            }
            
            // 切换到新状态
            currentStateType = newState;
            currentState = states[newState];
            
            Debug.Log($"[FSM] 状态切换: {newState}");
            
            // 进入新状态
            currentState.Enter();
        }
        
        /// <summary>
        /// 更新状态
        /// </summary>
        public void Update()
        {
            if (!IsRunning || currentState == null) return;
            currentState.Update();
        }
        
        /// <summary>
        /// 固定更新
        /// </summary>
        public void FixedUpdate()
        {
            if (!IsRunning || currentState == null) return;
            currentState.FixedUpdate();
        }
        
        /// <summary>
        ///  late更新
        /// </summary>
        public void LateUpdate()
        {
            if (!IsRunning || currentState == null) return;
            currentState.LateUpdate();
        }
        
        /// <summary>
        /// 启动FSM
        /// </summary>
        public void Start(TState initialState)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[FSM] FSM已经在运行中");
                return;
            }
            
            IsRunning = true;
            ChangeState(initialState);
            Debug.Log($"[FSM] FSM启动，初始状态: {initialState}");
        }
        
        /// <summary>
        /// 停止FSM
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;
            
            IsRunning = false;
            
            if (currentState != null)
            {
                currentState.Exit();
                currentState = null;
            }
            
            Debug.Log("[FSM] FSM已停止");
        }
        
        /// <summary>
        /// 重置FSM
        /// </summary>
        public virtual void Reset()
        {
            Stop();
            OnReset();
            Debug.Log("[FSM] FSM已重置");
        }
        
        /// <summary>
        /// 重置时的回调（子类可重写）
        /// </summary>
        protected virtual void OnReset()
        {
        }
        
        /// <summary>
        /// 获取状态实例
        /// </summary>
        public IFSMState<TState> GetState(TState stateType)
        {
            return states.TryGetValue(stateType, out var state) ? state : null;
        }
        
        /// <summary>
        /// 检查是否在指定状态
        /// </summary>
        public bool IsInState(TState stateType)
        {
            return EqualityComparer<TState>.Default.Equals(currentStateType, stateType);
        }
        
        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            Stop();
            states.Clear();
            Debug.Log("[FSM] FSM已销毁");
        }
    }
    
    /// <summary>
    /// FSM状态接口
    /// </summary>
    /// <typeparam name="TState">状态枚举类型</typeparam>
    public interface IFSMState<TState> where TState : Enum
    {
        /// <summary>
        /// 进入状态
        /// </summary>
        void Enter();
        
        /// <summary>
        /// 退出状态
        /// </summary>
        void Exit();
        
        /// <summary>
        /// 每帧更新
        /// </summary>
        void Update();
        
        /// <summary>
        /// 固定更新
        /// </summary>
        void FixedUpdate();
        
        /// <summary>
        /// Late更新
        /// </summary>
        void LateUpdate();
        
        /// <summary>
        /// 设置FSM引用
        /// </summary>
        void SetFSM(FSMBase<TState> fsm);
    }
    
    /// <summary>
    /// FSM状态基类（可选继承）
    /// </summary>
    /// <typeparam name="TState">状态枚举类型</typeparam>
    public abstract class FSMStateBase<TState> : IFSMState<TState> where TState : Enum
    {
        protected FSMBase<TState> fsm;
        
        public virtual void Enter()
        {
            Debug.Log($"[FSMState] 进入状态: {GetType().Name}");
        }
        
        public virtual void Exit()
        {
            Debug.Log($"[FSMState] 退出状态: {GetType().Name}");
        }
        
        public virtual void Update()
        {
        }
        
        public virtual void FixedUpdate()
        {
        }
        
        public virtual void LateUpdate()
        {
        }
        
        public void SetFSM(FSMBase<TState> fsm)
        {
            this.fsm = fsm;
        }
    }
}
