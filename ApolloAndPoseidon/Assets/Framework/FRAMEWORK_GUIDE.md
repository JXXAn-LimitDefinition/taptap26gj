# Framework 使用说明文档

## 框架概述

Framework 是一个轻量级 Unity 游戏开发框架，提供单例管理、事件系统、UI 管理和音频管理等核心功能。采用模块化设计，支持快速开发和解耦。

### 核心模块

- **Singleton** - 单例模式基类
- **Event** - 事件驱动系统
- **UI** - UI 面板管理
- **Audio** - 音频管理

---

## 1. Singleton 单例模块

### 1.1 Singleton<T> - 普通 C# 单例

**适用场景**: 工具类、配置类、纯逻辑类（非 MonoBehaviour）

**特点**:
- 自动延迟初始化
- 线程安全
- 防止外部实例化

**使用示例**:

```csharp
using Framework.Singleton;

public class GameConfig : Singleton<GameConfig>
{
    public int PlayerMaxHealth = 100;
    public float MoveSpeed = 5f;
    
    protected GameConfig()
    {
        // 初始化逻辑
        Debug.Log("GameConfig 已加载");
    }
}

// 调用方式
int health = GameConfig.Instance.PlayerMaxHealth;
```

### 1.2 MonoSingleton<T> - MonoBehaviour 单例

**适用场景**: 需要挂载到 GameObject 的管理器类

**特点**:
- 自动创建 GameObjects
- 支持 DontDestroyOnLoad
- 单例唯一性保证

**使用示例**:

```csharp
using Framework.Singleton;
using UnityEngine;

public class GameManager : MonoSingleton<GameManager>
{
    protected override void OnInit()
    {
        SetDontDestroyOnLoad(true); // 跨场景保留
        Debug.Log("GameManager 已初始化");
    }
    
    public void RestartGame()
    {
        // 游戏重启逻辑
    }
}

// 调用方式
GameManager.Instance.RestartGame();
```

---

## 2. Event 事件系统

### 2.1 EventCenter - 事件中心

**设计模式**: 观察者模式

**特点**:
- 完全解耦的事件通信
- 支持参数传递
- 编辑器模式下可追踪监听器（调试用）

**⚠️ 重要规范**:
- **所有需要注册事件监听的类必须实现 `IEventListener` 接口**
- 必须在 `RegisterEvents()` 中注册监听器
- 必须在 `UnregisterEvents()` 中注销监听器
- 触发事件的类不需要实现接口

**核心方法**:

| 方法 | 说明 |
|------|------|
| `AddEventListener(string, Action<object>)` | 添加事件监听 |
| `RemoveEventListener(string, Action<object>)` | 移除事件监听 |
| `DispatchEvent(string, object)` | 触发事件 |

**使用示例**:

```csharp
using Framework.Event;
using Framework.Interface;

// 1. 定义事件名称（推荐在 EventConst 中定义）
public static class EventConst
{
    public const string OnPlayerDeath = "OnPlayerDeath";
    public const string OnScoreChanged = "OnScoreChanged";
}

// 2. 注册监听器（必须实现 IEventListener 接口）
public class AchievementSystem : MonoBehaviour, IEventListener
{
    private void Start()
    {
        RegisterEvents();
    }
    
    private void OnDestroy()
    {
        UnregisterEvents();
    }
    
    public void RegisterEvents()
    {
        EventCenter.AddEventListener(EventConst.OnPlayerDeath, OnPlayerDeathHandler);
    }
    
    public void UnregisterEvents()
    {
        EventCenter.RemoveEventListener(EventConst.OnPlayerDeath, OnPlayerDeathHandler);
    }
    
    private void OnPlayerDeathHandler(object data)
    {
        Debug.Log("玩家死亡，解锁成就");
    }
}

// 3. 触发事件（触发者无需实现接口）
public class Player : MonoBehaviour
{
    public void Die()
    {
        EventCenter.DispatchEvent(EventConst.OnPlayerDeath, "Player1");
    }
}
```

### 2.2 IEventListener - 事件监听器接口

**作用**: 规范化事件注册/注销流程

**使用示例**:

```csharp
using Framework.Event;
using Framework.Interface;
using UnityEngine;

public class MusicManager : MonoBehaviour, IEventListener
{
    void Start()
    {
        RegisterEvents();
    }

    void OnDestroy()
    {
        UnregisterEvents();
    }

    public void RegisterEvents()
    {
        EventCenter.AddEventListener("OnPlayMusic", PlayMusic);
    }

    public void UnregisterEvents()
    {
        EventCenter.RemoveEventListener("OnPlayMusic", PlayMusic);
    }

    private void PlayMusic(object data)
    {
        Debug.Log($"播放音乐: {data}");
    }
}
```

---

## 3. UI 模块

### 3.1 UIManager - UI 管理器

**继承**: `MonoSingleton<UIManager>`

**核心功能**:
- 面板缓存池（避免重复实例化）
- 面板配置管理
- 面板层级控制
- 支持数据传递

**配置步骤**:

1. **准备面板 Prefab**
   - 创建 UI 面板 Prefab
   - 脚本继承 `UIPanel`
   - 放置在 `Resources/UI/` 目录

2. **配置 UIManager**
   - 在场景中创建 Canvas
   - 添加 UIManager 组件
   - 在 `panelConfigs` 列表添加配置:
     - `panelName`: 面板类名
     - `prefab`: 面板 Prefab
     - `sortingOrder`: 层级（越大越靠前）

**使用示例**:

```csharp
using Framework.UI;

// 打开面板（无参数）
UIManager.Instance.OpenPanel<StartPanel>();

// 打开面板（带参数）
var userData = new UserInfo { userId = 1001, userName = "Player1" };
UIManager.Instance.OpenPanel<UserDetailPanel>(userData);

// 关闭面板
UIManager.Instance.ClosePanel("SettingPanel");

// 关闭所有面板
UIManager.Instance.CloseAllPanels();

// 销毁面板（从缓存移除）
UIManager.Instance.DestroyPanel("StartPanel");
```

### 3.2 UIPanel - 面板基类

**继承**: `MonoBehaviour, IEventListener`

**生命周期方法**:

| 方法 | 说明 |
|------|------|
| `OnOpen()` | 面板打开时调用 |
| `OnClose()` | 面板关闭时调用 |
| `ShowAnimation()` | 显示动画（可重写） |
| `HideAnimation()` | 隐藏动画（可重写） |
| `RegisterEvents()` | 注册事件监听 |
| `UnregisterEvents()` | 注销事件监听 |

**完整示例**:

```csharp
using Framework.UI;
using Framework.Event;
using UnityEngine;
using UnityEngine.UI;

public class UserDetailPanel : UIPanel
{
    [SerializeField] private Text nameText;
    [SerializeField] private Text idText;
    [SerializeField] private Button closeButton;

    public override void RegisterEvents()
    {
        EventCenter.AddEventListener("OnUserUpdate", OnUserUpdateHandler);
    }

    public override void UnregisterEvents()
    {
        EventCenter.RemoveEventListener("OnUserUpdate", OnUserUpdateHandler);
    }

    public override void OnOpen()
    {
        // 接收传递的数据
        if (Data is UserInfo userInfo)
        {
            nameText.text = userInfo.userName;
            idText.text = userInfo.userId.ToString();
        }
        
        // 绑定按钮事件
        closeButton.onClick.AddListener(OnCloseButtonClick);
    }

    public override void OnClose()
    {
        closeButton.onClick.RemoveListener(OnCloseButtonClick);
    }

    private void OnCloseButtonClick()
    {
        CloseSelf(); // 关闭当前面板
    }

    private void OnUserUpdateHandler(object data)
    {
        Debug.Log("用户数据更新");
    }

    // 自定义动画示例
    public override void ShowAnimation(System.Action onComplete = null)
    {
        // 淡入动画
        StartCoroutine(FadeIn(onComplete));
    }

    private System.Collections.IEnumerator FadeIn(System.Action onComplete)
    {
        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        float duration = 0.3f;
        float elapsed = 0;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = elapsed / duration;
            yield return null;
        }
        
        onComplete?.Invoke();
    }
}

// 数据结构定义
public class UserInfo
{
    public int userId;
    public string userName;
}
```

---

## 4. Audio 音频模块

### 4.1 AudioManager - 音频管理器

**继承**: `MonoSingleton<AudioManager>`

**音频类型**:
- `Bgm` - 背景音乐
- `Sfx` - 音效
- `Voice` - 语音

**核心功能**:
- 多音轨管理
- BGM 交叉淡化
- 音量控制

**使用示例**:

```csharp
using Framework.Audio;
using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    [SerializeField] private AudioClip bgmClip;
    [SerializeField] private AudioClip sfxClip;
    
    void Start()
    {
        // 播放 BGM
        AudioManager.Instance.PlayAudio(EAudioType.Bgm, bgmClip, 0.8f);
        
        // 切换 BGM（带淡入淡出）
        AudioManager.Instance.ChangeBgm(newBgmClip, fadeTime: 2.0f);
        
        // 播放音效
        AudioManager.Instance.PlayAudio(EAudioType.Sfx, sfxClip, 1.0f);
        
        // 设置音量
        AudioManager.Instance.AudioSettings(EAudioType.Bgm, 0.5f);
        
        // 停止播放
        AudioManager.Instance.StopAudio(EAudioType.Bgm);
        
        // 检查播放状态
        bool isPlaying = AudioManager.Instance.IsAudioPlaying(EAudioType.Bgm);
    }
}
```

---

## 5. 完整示例项目

### 5.1 创建游戏管理器

```csharp
using Framework.Singleton;
using Framework.Event;
using UnityEngine;

public class GameManager : MonoSingleton<GameManager>
{
    public int Score { get; private set; }
    
    protected override void OnInit()
    {
        SetDontDestroyOnLoad(true);
        Score = 0;
    }
    
    public void AddScore(int amount)
    {
        Score += amount;
        EventCenter.DispatchEvent("OnScoreChanged", Score);
    }
    
    public void GameOver()
    {
        EventCenter.DispatchEvent("OnGameOver", Score);
    }
}
```

### 5.2 创建 UI 面板

```csharp
using Framework.UI;
using Framework.Event;
using UnityEngine;
using UnityEngine.UI;

public class GameOverPanel : UIPanel
{
    [SerializeField] private Text scoreText;
    [SerializeField] private Button restartButton;
    
    public override void RegisterEvents()
    {
        // 可注册其他事件
    }
    
    public override void UnregisterEvents()
    {
        // 注销事件
    }
    
    public override void OnOpen()
    {
        // 显示得分
        if (Data is int score)
        {
            scoreText.text = $"最终得分: {score}";
        }
        
        restartButton.onClick.AddListener(OnRestartClick);
    }
    
    public override void OnClose()
    {
        restartButton.onClick.RemoveListener(OnRestartClick);
    }
    
    private void OnRestartClick()
    {
        GameManager.Instance.RestartGame();
        CloseSelf();
    }
}
```

### 5.3 触发游戏结束

```csharp
public class Player : MonoBehaviour
{
    public void Die()
    {
        int finalScore = GameManager.Instance.Score;
        UIManager.Instance.OpenPanel<GameOverPanel>(finalScore);
    }
}
```

### 5.4 监听分数变化（实现 IEventListener）

```csharp
using Framework.Event;
using Framework.Interface;
using UnityEngine;

// 必须实现 IEventListener 接口
public class ScoreDisplay : MonoBehaviour, IEventListener
{
    [SerializeField] private TMPro.TextMeshProUGUI scoreText;
    
    void Start()
    {
        // 注册事件监听
        RegisterEvents();
    }
    
    void OnDestroy()
    {
        // 注销事件监听（防止内存泄漏）
        UnregisterEvents();
    }
    
    public void RegisterEvents()
    {
        EventCenter.AddEventListener("OnScoreChanged", OnScoreChangedHandler);
    }
    
    public void UnregisterEvents()
    {
        EventCenter.RemoveEventListener("OnScoreChanged", OnScoreChangedHandler);
    }
    
    private void OnScoreChangedHandler(object data)
    {
        if (data is int score)
        {
            scoreText.text = $"得分: {score}";
        }
    }
}
```

---

## 6. 最佳实践

### 6.1 命名规范

- **事件名称**: 在 `EventConst` 中定义常量
- **管理器类**: 以 `Manager` 结尾（如 `GameManager`）
- **面板类**: 以 `Panel` 结尾（如 `StartPanel`）

### 6.2 单例使用原则

- **MonoSingleton**: 用于需要 MonoBehaviour 功能的管理器
- **Singleton**: 用于纯逻辑类、配置类、工具类

### 6.3 事件系统原则

- **强制规范**: 所有需要注册事件监听的类**必须**实现 `IEventListener` 接口
- 在 `Start()`/`Awake()` 中调用 `RegisterEvents()` 注册监听器
- 在 `OnDestroy()` 中调用 `UnregisterEvents()` 注销监听器（防止内存泄漏）
- 事件名称使用常量定义（在 `EventConst` 中定义，避免拼写错误）
- 触发事件的类不需要实现 `IEventListener` 接口

### 6.4 UI 管理原则

- 所有面板继承 `UIPanel`
- 在 `OnOpen` 中初始化，`OnClose` 中清理
- 使用 `CloseSelf()` 关闭面板（而不是直接销毁）
- 面板配置在 Inspector 中设置（便于维护）

### 6.5 音频管理原则

- BGM 使用 `ChangeBgm` 切换（支持淡入淡出）
- SFX 使用 `PlayAudio` 播放
- 音量设置保存在 `PlayerPrefs`

---

## 7. 常见问题

### Q1: 单例未初始化？

**原因**: 首次访问 `Instance` 时才会初始化

**解决**: 在游戏启动时显式调用 `Instance`

```csharp
void Awake()
{
    var manager = GameManager.Instance; // 强制初始化
}
```

### Q2: 事件监听器未触发？

**检查项**:
- 事件名称是否一致
- 是否已注册监听器
- 监听器是否被提前注销

### Q3: UI 面板无法打开？

**检查项**:
- UIManager 中是否配置面板
- `panelName` 是否与类名一致
- Prefab 是否正确引用

### Q4: BGM 切换无效果？

**原因**: BGM 片段相同且正在播放会跳过

**解决**: 先停止当前 BGM 或使用不同片段

### Q5: 为什么必须实现 IEventListener 接口？

**原因**: 强制规范化事件监听器的注册和注销流程

**好处**:
- 统一的代码风格
- 避免忘记注销监听器导致内存泄漏
- 便于团队协作和代码审查

**❌ 错误示例**:

```csharp
// 不推荐：没有实现 IEventListener 接口
public class BadExample : MonoBehaviour
{
    void Start()
    {
        EventCenter.AddEventListener("OnScoreChanged", OnScoreChanged);
    }
    
    void OnDestroy()
    {
        // 容易忘记注销！
        // EventCenter.RemoveEventListener("OnScoreChanged", OnScoreChanged);
    }
    
    private void OnScoreChanged(object data) { }
}
```

**✅ 正确示例**:

```csharp
// 推荐：实现 IEventListener 接口
public class GoodExample : MonoBehaviour, IEventListener
{
    void Start()
    {
        RegisterEvents();
    }
    
    void OnDestroy()
    {
        UnregisterEvents(); // 强制规范，不会遗漏
    }
    
    public void RegisterEvents()
    {
        EventCenter.AddEventListener("OnScoreChanged", OnScoreChanged);
    }
    
    public void UnregisterEvents()
    {
        EventCenter.RemoveEventListener("OnScoreChanged", OnScoreChanged);
    }
    
    private void OnScoreChanged(object data) { }
}
```

---

## 8. 扩展建议

### 8.1 添加对象池管理器

```csharp
public class ObjectPoolManager : MonoSingleton<ObjectPoolManager>
{
    // 实现对象池逻辑
}
```

### 8.2 添加存档管理器

```csharp
public class SaveManager : Singleton<SaveManager>
{
    // 实现存档逻辑
}
```

### 8.3 扩展事件系统

- 添加事件优先级
- 支持异步事件处理
- 添加事件调试工具

---

## 9. 项目集成

### 9.1 初始化流程

```csharp
public class GameInitializer : MonoBehaviour
{
    [SerializeField] private AudioClip startBgm;
    
    void Awake()
    {
        // 初始化所有管理器
        var gameManager = GameManager.Instance;
        var audioManager = AudioManager.Instance;
        var uiManager = UIManager.Instance;
    }
    
    void Start()
    {
        // 播放背景音乐
        AudioManager.Instance.PlayAudio(EAudioType.Bgm, startBgm);
        
        // 打开开始面板
        UIManager.Instance.OpenPanel<StartPanel>();
    }
}
```

### 9.2 场景切换处理

```csharp
public class SceneLoader : MonoBehaviour
{
    public void LoadScene(string sceneName)
    {
        UIManager.Instance.CloseAllPanels();
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }
}
```

---

## 10. 总结

Framework 提供了游戏开发的核心基础设施：

- **Singleton** - 统一的单例管理
- **Event** - 解耦的事件通信
- **UI** - 规范的 UI 管理
- **Audio** - 专业的音频控制

合理使用这些模块可以显著提高开发效率和代码质量。建议团队统一遵循本文档的规范和最佳实践。
