# 通用有限状态机（FSM）使用指南

## 📁 文件位置

```
Assets/Framework/FSM/
└── FSMBase.cs    # 通用FSM基类
```

## 🎯 架构说明

### 核心类

1. **FSMBase<TState>** - 通用FSM基类
   - 管理状态注册、切换、更新
   - 提供Start/Stop/Reset方法
   - 支持Update/FixedUpdate/LateUpdate

2. **IFSMState<TState>** - 状态接口
   - 定义状态生命周期方法
   - Enter/Exit/Update/FixedUpdate/LateUpdate

3. **FSMStateBase<TState>** - 状态基类（可选）
   - 提供默认实现
   - 简化状态创建

## 🚀 快速开始

### 步骤1：定义状态枚举

```csharp
public enum MyGameState
{
    Menu,
    Playing,
    Paused,
    GameOver
}
```

### 步骤2：创建状态类

```csharp
using Framework.FSM;

public class MenuState : FSMStateBase<MyGameState>
{
    public override void Enter()
    {
        base.Enter();
        Debug.Log("进入菜单界面");
        // 显示UI、播放音乐等
    }
    
    public override void Exit()
    {
        base.Exit();
        Debug.Log("退出菜单界面");
        // 隐藏UI、停止音乐等
    }
    
    public override void Update()
    {
        // 处理输入、动画等
        if (Input.GetKeyDown(KeyCode.Space))
        {
            fsm.ChangeState(MyGameState.Playing);
        }
    }
}
```

### 步骤3：创建FSM管理类

```csharp
using Framework.FSM;

public class GameManager : FSMBase<MyGameState>
{
    protected override void InitializeStates()
    {
        // 注册所有状态
        RegisterState(MyGameState.Menu, new MenuState());
        RegisterState(MyGameState.Playing, new PlayingState());
        RegisterState(MyGameState.Paused, new PausedState());
        RegisterState(MyGameState.GameOver, new GameOverState());
        
        // 启动FSM，设置初始状态
        Start(MyGameState.Menu);
    }
    
    // 可以添加自定义方法
    public void PauseGame()
    {
        if (IsInState(MyGameState.Playing))
        {
            ChangeState(MyGameState.Paused);
        }
    }
    
    public void ResumeGame()
    {
        if (IsInState(MyGameState.Paused))
        {
            ChangeState(MyGameState.Playing);
        }
    }
}
```

### 步骤4：在MonoBehaviour中使用

```csharp
public class GameController : MonoBehaviour
{
    private GameManager gameManager;
    
    void Awake()
    {
        gameManager = new GameManager();
    }
    
    void Update()
    {
        gameManager.Update();
    }
    
    void FixedUpdate()
    {
        gameManager.FixedUpdate();
    }
    
    void LateUpdate()
    {
        gameManager.LateUpdate();
    }
    
    void OnDestroy()
    {
        gameManager.Dispose();
    }
}
```

## 📋 完整示例：敌人AI

### 1. 定义状态

```csharp
public enum EnemyState
{
    Idle,
    Patrol,
    Chase,
    Attack,
    Dead
}
```

### 2. 创建状态类

```csharp
public class IdleState : FSMStateBase<EnemyState>
{
    private float idleTimer = 0f;
    private float idleDuration = 2f;
    
    public override void Enter()
    {
        base.Enter();
        idleTimer = 0f;
        // 播放待机动画
    }
    
    public override void Update()
    {
        idleTimer += Time.deltaTime;
        
        if (idleTimer >= idleDuration)
        {
            fsm.ChangeState(EnemyState.Patrol);
        }
        
        // 检测玩家
        if (DetectPlayer())
        {
            fsm.ChangeState(EnemyState.Chase);
        }
    }
    
    private bool DetectPlayer()
    {
        // 检测逻辑
        return false;
    }
}

public class ChaseState : FSMStateBase<EnemyState>
{
    public override void Enter()
    {
        base.Enter();
        // 播放追逐动画
    }
    
    public override void Update()
    {
        // 向玩家移动
        MoveTowardsPlayer();
        
        // 到达攻击范围
        if (IsInAttackRange())
        {
            fsm.ChangeState(EnemyState.Attack);
        }
        
        // 玩家逃脱
        if (!IsPlayerVisible())
        {
            fsm.ChangeState(EnemyState.Idle);
        }
    }
    
    private void MoveTowardsPlayer() { }
    private bool IsInAttackRange() { return false; }
    private bool IsPlayerVisible() { return false; }
}
```

### 3. 创建AI管理器

```csharp
public class EnemyAI : FSMBase<EnemyState>
{
    private Transform player;
    private NavMeshAgent agent;
    
    public EnemyAI(Transform playerTransform, NavMeshAgent navAgent)
    {
        this.player = playerTransform;
        this.agent = navAgent;
    }
    
    protected override void InitializeStates()
    {
        RegisterState(EnemyState.Idle, new IdleState());
        RegisterState(EnemyState.Patrol, new PatrolState());
        RegisterState(EnemyState.Chase, new ChaseState());
        RegisterState(EnemyState.Attack, new AttackState());
        RegisterState(EnemyState.Dead, new DeadState());
        
        Start(EnemyState.Idle);
    }
    
    protected override void OnReset()
    {
        // 重置AI状态
        agent.isStopped = false;
    }
}
```

## 🔧 高级用法

### 1. 带数据的状态

```csharp
public class BattleState : FSMStateBase<BattleStateType>
{
    private int roundNumber;
    private List<Unit> units;
    
    public BattleState(int round, List<Unit> battleUnits)
    {
        roundNumber = round;
        units = battleUnits;
    }
    
    public override void Enter()
    {
        base.Enter();
        Debug.Log($"开始第 {roundNumber} 回合");
        StartRound();
    }
}
```

### 2. 状态间通信

```csharp
public class PlayerStateMachine : FSMBase<PlayerState>
{
    public Vector3 LastPosition { get; private set; }
    public float Health { get; private set; }
    
    protected override void InitializeStates()
    {
        RegisterState(PlayerState.Idle, new IdleState(this));
        RegisterState(PlayerState.Running, new RunningState(this));
        Start(PlayerState.Idle);
    }
}

// 状态中访问FSM数据
public class RunningState : FSMStateBase<PlayerState>
{
    private PlayerStateMachine playerFSM;
    
    public RunningState(PlayerStateMachine fsm)
    {
        playerFSM = fsm;
    }
    
    public override void Update()
    {
        // 访问FSM的数据
        var lastPos = playerFSM.LastPosition;
        var health = playerFSM.Health;
    }
}
```

### 3. 条件转换

```csharp
public class GameStateMachine : FSMBase<GameState>
{
    protected override void InitializeStates()
    {
        RegisterState(GameState.Playing, new PlayingState());
        RegisterState(GameState.Paused, new PausedState());
        Start(GameState.Playing);
    }
    
    public void TryPause()
    {
        // 只有特定条件下才能暂停
        if (CanPause())
        {
            ChangeState(GameState.Paused);
        }
    }
    
    private bool CanPause()
    {
        // 检查是否允许暂停
        return !IsInCutscene() && !IsInDialogue();
    }
}
```

## 📊 API参考

### FSMBase<TState>

| 方法 | 说明 |
|------|------|
| `Start(TState initialState)` | 启动FSM，设置初始状态 |
| `Stop()` | 停止FSM |
| `ChangeState(TState newState)` | 切换到新状态 |
| `Update()` | 每帧更新 |
| `FixedUpdate()` | 固定更新 |
| `LateUpdate()` | Late更新 |
| `Reset()` | 重置FSM |
| `Dispose()` | 销毁FSM |
| `IsInState(TState state)` | 检查是否在指定状态 |
| `GetState(TState state)` | 获取状态实例 |
| `CurrentState` | 当前状态（只读） |
| `IsRunning` | 是否正在运行（只读） |

### IFSMState<TState>

| 方法 | 说明 |
|------|------|
| `Enter()` | 进入状态时调用 |
| `Exit()` | 退出状态时调用 |
| `Update()` | 每帧调用 |
| `FixedUpdate()` | 固定更新调用 |
| `LateUpdate()` | Late更新调用 |
| `SetFSM(FSMBase<TState> fsm)` | 设置FSM引用 |

## 💡 最佳实践

### ✅ 推荐做法

1. **每个状态一个类** - 保持职责单一
2. **使用枚举定义状态** - 类型安全
3. **在Enter/Exit中管理资源** - 避免内存泄漏
4. **继承FSMStateBase** - 减少重复代码
5. **使用RegisterState注册** - 统一管理

### ❌ 避免做法

1. **不要在状态中保存持久化数据** - 应该放在FSM中
2. **不要直接修改currentState** - 使用ChangeState
3. **不要在Update中频繁创建对象** - 性能问题
4. **不要忘记调用base方法** - 可能导致问题
5. **不要循环依赖** - FSM→State→FSM

## 🎓 设计模式

本FSM实现结合了以下设计模式：

1. **状态模式（State Pattern）** - 核心思想
2. **模板方法模式** - FSMBase定义流程
3. **泛型编程** - 支持任意状态类型
4. **接口隔离** - IFSMState明确契约

## 📝 注意事项

1. **线程安全** - 当前实现不是线程安全的
2. **异常处理** - 建议在状态方法中添加try-catch
3. **性能考虑** - 避免在Update中进行复杂计算
4. **内存管理** - 记得调用Dispose释放资源
5. **状态数量** - 建议不超过10个状态，过多考虑分层FSM

---

**版本**: 1.0.0  
**作者**: Framework Team  
**更新日期**: 2026-05-19
