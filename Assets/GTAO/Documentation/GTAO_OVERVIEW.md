# GTAO_OVERVIEW — 论文与本项目实现的对照

> 论文：**Jorge Jimenez, Xian-Chun Wu, Angelo Pesce, Adrian Jarabo —
> "Practical Realtime Strategies for Accurate Indirect Occlusion"**
> (Technical Memo ATVI-TR-16-01, Activision Blizzard / Universidad de Zaragoza)
>
> 对照实现：本仓库 `Assets/GTAO/`（Unity 2022.3 / URP 14 移植版），
> 参考实现：`Assets/Legacy_BuiltIn_GTAO/`（原内置管线版本）。

本文档的目标是：**把论文里 GTAO 的那套推导，与本项目 shader 里的每一段代码一一对上**，
并提炼出这套实现的「心法」（mental model）。读完本文，你应该能指着
`GTAO_Common.hlsl` 说出每一行对应论文的哪个公式、为什么这么写。

---

## 约定（notation）

| 符号 | 含义 |
|---|---|
| `x` | 着色点 |
| `n` | `x` 处的表面法线 |
| `o` | 着色点指向相机的方向（view vector），`o = normalize(-P)` |
| `ωi` | 入射方向（半球积分变量） |
| `V(x, ωi) ∈ {0,1}` | **二值**可见性：被遮挡为 0，否则为 1 |
| `A(x) ∈ [0,1]` | 归一化的余弦加权可见性（论文称 ambient occlusion term，见下方重要说明） |
| `Â(x)` | 屏幕空间估计出来的 `A(x)` |
| `θ` | `n` 与 `o` 的夹角（本实现里是**带符号**的投影法线夹角 `n`） |
| `γ1(φ), γ2(φ)` | 沿切片方向 `φ` 两侧找到的最大地平线角（horizon angle） |
| `ρ` | 反照率 albedo |
| `b` | bent normal（弯曲法线） |

> **⚠️ 一个必须记住的坑：论文里的 `A(x)` 是「可见性 / accessibility」，不是「阴影量」。**
> `V=1` 处处成立时 `A(x)=1`（完全敞开、完全受光）；完全被挡住时 `A(x)=0`。
> 本项目 shader 里那个变量名叫 `occlusion`、以及 G-buffer 里的 `ao`，
> **其实就是这个 `A(x)`（1 = 敞开）**——所以合成时是 `sceneColor * ao`，
> debug 视图里白色代表没有遮蔽。名字叫 occlusion，语义却是 accessibility。

---

## 0. 一页心法（mental model）

如果把论文的 GTAO 压缩成一条链：

```
渲染方程
  └─ 假设：无限远均匀白光 + 纯吸收表面 + 漫反射        →  AO 积分 (Eq.2)
        └─ 把可见性 V 改成二值，不做任何经验衰减       →  可以解析积分
              └─ 换参考系：地平线相对「视线 o」而非「法线 n」  →  只看一个角 γ (Eq.5,6)
                    └─ 每个切片方向 φ：找到两侧最大地平线角 γ1,γ2
                          └─ 内积分 a‹ 在 γ1,γ2 上可闭式求解     →  Eq.7
                                └─ 投影法线修正（n 不在切片平面内） →  Eq.8
                                      └─ Monte Carlo 采样 φ 方向
                                            └─ 空间双边滤波 + 时域重投影  →  用 1 个方向/帧换 ~96 个方向
                                                  └─ 多弹射补偿（albedo ↔ AO 的立方拟合）→ Eq.10
                                                        └─ GTSO：可见性锥 ∩ 高光锥 → 反射遮蔽   → Eq.18-20
```

一句话总结：**GTAO 的本质，是「把半球上的可见性积分，换成沿切片方向求 max 角度、再做一次解析弧长积分」；
而实时能跑的关键，是「用空间 + 时间把稀疏采样重新拼成稠密采样」。**

---

## 1. 从渲染方程到 AO 积分（论文 §2，Eq.1–2）

**Eq.1（反射方程）**

```
Lr(x, ωo) = ∫_{H²} Li(x, ωi) fr(x, ωi, ωo) ⟨n, ωi⟩₊ dωi
```

**Eq.2（AO 的三条假设）**：i) 无限远均匀白光；ii) 周围表面纯吸收（不反弹）；iii) 着色点漫反射。

```
Lr(x, ωo) = Li · ρ(x)/π · ∫_{H²} V(x, ωi) ⟨n, ωi⟩₊ dωi  =  Li · ρ(x)/π · A(x)
A(x) = 归一化余弦加权可见性 ∈ [0,1]
```

**对应到本项目**：这就是整个效果的物理地基。`GTAO_Common.hlsl` 顶部对坐标系的说明
（"forward = +Z"）以及 `GTAO_GetViewPosition / GTAO_GetViewNormal` 两个重建函数，
就是在为这个积分准备 `n`、`o`、`P`。

**关键分歧点（本项目最有教育意义的一处）**：论文特意强调——
**它们不使用「obscurance（带距离衰减的可见性）」，`V` 是纯二值的。**
正因为 `V` 是二值函数，内积分才能解析求解。参考实现在 `GTAO_Common.cginc` 里
没有任何 `1/(1+d)` 之类的 ad-hoc 衰减，本 URP 移植同样如此。

---

## 2. GTAO 的三个关键改造（论文 §4，Eq.3/5/6）

### 2.1 HBAO 的原始形式（Eq.3，作为背景）

Bavoil 等人把可见性按**法线 n** 为轴参数化：

```
Â(x) = 1/π ∫₀^π ∫_{-π/2}^{π/2} V(ω, γ) |sin ω| dω dγ
```

### 2.2 改造一：参考系从「法线」换成「视线」（Eq.5）

论文的核心改动：**地平线相对视线 `o` 来测量**（参考 Fig.2）。

```
Â(x) = 1/π ∫₀^π ∫_{γ1(φ)}^{γ2(φ)} max(cos(θ − γ), 0) · |sin θ| dθ dφ      (Eq.5)
```

收益：`n` 只在「投影」时出现一次；积分核里 `cos(θ−γ)` 结构简单，后面的解析积分因此成立，
整个 shader 变成 **memory bound**（ALU 便宜）。

> 对应 `GTAO()` 里：
> ```hlsl
> half3 viewDir = normalize(-positionVS);   // o
> ...
> half2 h = half2(dot(ds, viewDir), dot(dt, viewDir)) * dsdtLength;  // ⟨ŝ, o⟩
> ```
> `h` 就是采样方向与**视线**的夹角余弦（而不是与法线的夹角）。

### 2.3 改造二：地平线 = 两侧采样的最大夹角（Eq.6）

沿切片方向 `t̂(φ)` 和 `-t̂(φ)` 在 n×n 邻域里扫描，取与视线夹角的最大值：

```
γ1(φ) = arccos( max_{s<n/2} ⟨ŝ, o⟩ )      ŝ = (s − x) / |s − x|        (Eq.6)
γ2(φ) 同理（另一侧）
```

> 对应 `GTAO()` 内层循环：
> ```hlsl
> half3 ds = GTAO_GetViewPosition(uv + uvOffset) - positionVS;
> half3 dt = GTAO_GetViewPosition(uv - uvOffset) - positionVS;
> half2 dsdtLength = rsqrt(half2(dot(ds, ds), dot(dt, dt)));   // 1/|s-x|
> half2 h = half2(dot(ds, viewDir), dot(dt, viewDir)) * dsdtLength;  // ⟨ŝ, o⟩
> horizon.xy = (h > horizon.xy) ? lerp(h, horizon.xy, falloff)
>                               : lerp(h, horizon.xy, thickness);   // 取 max（带启发式）
> ```
> 循环结束后 `horizon = acos(clamp(horizon,-1,1))` 就得到 `γ1, γ2`。
> 只维护「最大角」而不维护距离，正是论文说的「只需保留最大值」——
> 这是它能在 GCN 上用一条 `rsqrt`（四分之一速率指令）完成的原因。

---

## 3. 解析内积分（论文 §4，Eq.7）

给定 `γ1, γ2`，内积分 `a‹` 有闭式解：

```
a‹ = 1/4 ( −cos(2γ1 − θ) + cos θ + 2γ1 sin θ )
   + 1/4 ( −cos(2γ2 − θ) + cos θ + 2γ2 sin θ )                            (Eq.7)
```

> 对应 `GTAO_IntegrateArc_CosWeight`，**逐符号完全一致**：
> ```hlsl
> half GTAO_IntegrateArc_CosWeight(half2 h, half n)   // h=(γ1,γ2), n=θ
> {
>     half2 arc = -cos(2 * h - n) + cos(n) + 2 * h * sin(n);
>     return 0.25 * (arc.x + arc.y);                  // 对应两个 1/4 项
> }
> ```
> 论文指出推导后只剩 **2 个 cos + 1 个 sin + 3 个 acos**，本实现正是如此。

> 论文 **Appendix A / Eq.22–23** 还给了「不做余弦加权（uniform weight）」的变体：
> ```hlsl
> half GTAO_IntegrateArc_UniformWeight(half2 h) { half2 a = 1 - cos(h); return a.x + a.y; }
> ```
> 该变体在本项目的 `GTAO_Common.hlsl` 里保留作对照，但**实际走的是余弦加权的 Eq.7**，
> 因为只有它才与 ground truth 一致，也才能接多弹射拟合（Eq.10）。

---

## 4. 投影法线修正（论文 §4，Eq.8）

`n` 一般不在切片平面 `P = span(t̂(φ), o)` 内，直接代入会高估。做法是：
把 `n` 投影到 `P` 得到 `n_P`，用 `n_P` 与 `o` 的夹角当 `θ`，再按投影法线的长度补偿。

```
Â(x) = 1/π ∫₀^π |n̂x| · a‹(φ) dφ        （|n̂x| = 投影法线长度 = projLength）   (Eq.8)
```

> 对应 `GTAO()` 里：
> ```hlsl
> planeNormal    = normalize(cross(sliceDir, viewDir));            // P 的法线
> projectedNormal = normalVS - planeNormal * dot(normalVS, planeNormal);
> projLength     = length(projectedNormal);                        // |n̂x|
> cos_n = clamp(dot(normalize(projectedNormal), viewDir), -1, 1);  // 投影后与 o 的夹角
> n     = -sign(dot(projectedNormal, tangent)) * acos(cos_n);      // 带符号的 θ
> ...
> occlusion += projLength * GTAO_IntegrateArc_CosWeight(horizon, n);   // Eq.8 的乘加
> ```
> 注意本实现把 `θ` 取了符号（用 `tangent` 判断在视线的哪一侧），这是论文思路的自然实现细节。

**外层的 Monte Carlo**：论文对 `φ` 做随机采样，本项目用 `directions` 个均匀切片
（`i * π / directions`），最后 `occlusion / numDirections` 就是 Eq.8 里 `1/π ∫ dφ` 的估计量。
绕圈的角度还叠加了：

```hlsl
angle = (i + noiseDirection + _GTAO_TemporalDirection) * (PI / numDirections);
```

其中 `noiseDirection`（`GTAO_Noise`，interleaved gradient noise）做**逐像素去相关**，
`_GTAO_TemporalDirection` 做**逐帧旋转抖动**（见 §6）。

---

## 5. 厚度启发式：屏幕空间看不见「厚度」（论文 §4.1，Eq.9）

深度缓冲无法判断遮挡物的厚度，树叶 / 树枝这类薄片会投出过量的 AO。
论文的修正（假定物体厚度 ≈ 其屏幕宽度）写成一个「在搜索过程中更新地平线」的规则：

```
γ = max(γs, γ)              if cos(γs) ≥ cos(γs−1)
    blend(γs−1, γs)         otherwise        （γ0 = θ）                    (Eq.9)
```

> 对应内层循环的分支：
> ```hlsl
> horizon.xy = (h > horizon.xy)
>     ? lerp(h, horizon.xy, falloff)      // 距离衰减：近处取新地平线，远处保留旧值
>     : lerp(h, horizon.xy, thickness);   // 薄遮挡物混合
> ```
> 两件事在这里合并：
> * `falloff = saturate(dsdt * (2 / radius²))` —— 呼应论文 §4.1 的
>   **conservative attenuation**：只在近场保真，远场衰减到 0（远场交给烘焙 GI）；
> * `thickness` —— 呼应 Eq.9 的薄遮挡物修正（本实现默认 1，即关闭该修正）。
> 论文还强调这个启发式对普通墙角**不产生偏差**，本实现保留了这一性质。

---

## 6. 时空复用：把 1 个方向放大成 ~96 个方向（论文 §4.1）

论文的实现要点：

* AO 在**半分辨率**计算再上采样；
* 每像素只采样 **1 个方向**，用 **4×4 的双边重建滤波**在空间上补齐；
* 时域上交替 **6 个旋转角**并做重投影，用 **指数累积缓冲**（exponential accumulation buffer）；
* 合计 `4 × 4 × 6 = 96` 个有效方向/像素。

> 对应本项目的 `GTAORenderPass.cs`：

| 论文机制 | 本项目实现 |
|---|---|
| 6 个交替旋转 | `s_TemporalRotations = { 60, 300, 180, 240, 120, 0 }` |
| 子像素抖动 | `s_SpatialOffsets = { 0, 0.5, 0.25, 0.75 }` |
| 逐像素方向去相关 | `GTAO_Noise()` 的 interleaved gradient noise |
| 空间双边重建 | `GTAO_BilateralBlur`（可分离 X/Y 两次 pass，`GTAO_CrossBilateralWeight` 按深度拒绝跨边缘样本）—— 对应论文的"bilateral reconstruction filter" |
| 时域重投影 + 累积 | `GTAO_Temporal_frag`：用 `_MotionVectorTexture` 重投影历史帧，再做 **variance clipping**（3×3 均值 ± k·标准差）代替指数累积，`k = _GTAO_TemporalScale`，混合权重 `_GTAO_TemporalResponse` |
| 半分辨率 | 本项目为清晰起见在**全分辨率**计算，质量项由 `directions/steps` 控制（可自行加 1/2 分辨率 RT 复现论文方案） |

```hlsl
// GTAO_Temporal_frag —— 重投影 + AABB(variance) clipping
float2 velocity = SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_PointClamp, uv).rg;
GTAO_ResolveAABB(uv, _GTAO_TemporalScale, minColor, maxColor, current);
history = clamp(GTAO_SampleHistory(uv - velocity), minColor, maxColor);
historyWeight = saturate(_GTAO_TemporalResponse * (1 - length(velocity) * 8));   // 运动越快越不信历史
return lerp(current, history, historyWeight).rg;
```

> 这说明本实现忠实保留了论文的**核心采样思想**（1 方向 + 空间重建 + 时域累积），
> 只是把「指数累积」换成了业界更常用的「variance clipping」，以更好地抑制 ghosting。

---

## 7. 逼近近场全局光照 / 多弹射（论文 §5，Eq.10）

AO 假设周围表面纯吸收，于是墙角会**过暗**——因为丢掉了近场互相反弹的光。
论文的做法：做 7 组 albedo 的蒙特卡洛实验，发现 **AO 与近场 GI 之间存在一个可由
albedo 参数化的立方映射**，于是用拟合出的多项式把能量加回来：

```
G(A, ρ) = a(ρ)·A³ − b(ρ)·A² + c(ρ)·A
a(ρ) = 2.0404ρ − 0.3324
b(ρ) = 4.7951ρ − 0.6417
c(ρ) = 2.7552ρ + 0.6903                                              (Eq.10)
```

> 对应本项目 `GTAO_MultiBounce`（注意工程实现用的是论文系数的经典近似值）：
> ```hlsl
> half3 GTAO_MultiBounce(half ao, half3 albedo)
> {
>     half3 a = 2.0   * albedo - 0.33;   //  ≈ 2.0404ρ − 0.3324
>     half3 b = -4.8  * albedo + 0.64;   //  ≈ (−4.7951ρ + 0.6417)
>     half3 c = 2.75  * albedo + 0.69;   //  ≈ 2.7552ρ + 0.6903
>     return max(ao, ((ao * a + b) * ao + c) * ao);   // 三次多项式 + 不小于 ao
> }
> ```
> 调用点在 `GTAO_Composite_frag`：用 `_GTAO_MULTI_BOUNCE` 关键字控制，读取 **deferred `_GBuffer0`
> 的 albedo**（所以本项目要求 Deferred 路径）。这正是论文说的"基于已算好的信息、无需光传输模拟"。

---

## 8. GTSO：镜面遮蔽（论文 §6，Eq.17–26）

AO 是「漫反射版」的可见性；论文把它推广到任意 BRDF，得到 GTSO。
本项目保留了这个函数（`GTAO_ReflectionOcclusion`）作为**学习 / 扩展点**（当前未接入合成）。

### 8.1 可见性锥的孔径来自 AO（Eq.17–18）

把可见性近似成绕 **bent normal `b`** 的一个锥，锥半角 `v`。假定可见性均匀，
则可用 AO 反解出锥角：

```
Â(x) = 1 − cos²(v(x))          →        cos(v(x)) = sqrt(1 − Â(x))        (Eq.17, Eq.18)
```

> 本实现里 **bent normal 的长度**天然编码了"敞开程度"：
> 完全敞开时 `|b| = 1`，完全遮挡时 `|b| → 0`。这正是 `GTAO()` 末尾
> `bentNormal = normalize(normalize(bentNormal) - viewDir * 0.5);` 的产物。
> 这也是为什么本项目的 debug 模式里专门有 `BentNormal` 视图——它既是调试输出，
> 又是 GTSO 可见性锥的输入。

### 8.2 高光锥的孔径来自粗糙度（Eq.26）

```
cos(αs) = 2^(−3.32193 · r²)          （r = roughness）                       (Eq.26)
```

### 8.3 反射遮蔽 = 两个球面锥的交集占高光锥的比例（Eq.19, Eq.20）

```
S(x, o) = Ωi / Ωs                    （Ωi = 交集立体角，Ωs = 高光锥立体角）   (Eq.19)
```

> 对应 `GTAO_ReflectionOcclusion`：
> ```hlsl
> half arc0 = max(roughness, 0.1) * PI;                 // 高光锥 αs（Eq.26 的线性近似）
> half arc1 = length(bentNormal) * PI * OcclusionStrength; // 可见性锥 v（由 |b| 近似 Eq.18）
> half angleBetween = acos(dot(bentNormal, reflectionVector) / max(length(bentNormal), 0.001)); // β
> half intersection = smoothstep(0, 1, 1 - saturate(
>         (angleBetween - abs(arc0 - arc1)) / (arc0 + arc1 - abs(arc0 - arc1))));                // Eq.19 的锥交近似
> return lerp(0, intersection, saturate((arc1 - 0.1) / 0.2));
> ```
> 论文 Appendix B 指出锥–锥交集有解析解 [OS07, Maz12]，这里的 `ApproximateConeConeIntersection`
> 是 Activision 代码里对该解析解的近似实现。

**为什么本项目没有真正接上它**：Built-in 管线里有全局的 `_CameraReflectionsTexture` 可以做
`reflection * GTRO`；URP 没有以同样的全局名暴露延迟反射缓冲，所以移植时保留了公式但未接入合成
（详见 `Documentation/README.md` 第 8 节，扩展方式也写在那里）。

---

## 9. 公式 ↔ 代码 总对照表

| 论文 | 内容 | 本项目代码 |
|---|---|---|
| Eq.1–2 | 渲染方程 → AO 积分、二值可见性 | `GTAO_Common.hlsl` 文件头说明 + 无 ad-hoc 衰减 |
| Eq.3 | HBAO 以法线为轴的参数化 | 背景，未直接使用 |
| Eq.5 | 换到「视线参考系」的 AO 积分 | `GTAO()`：`viewDir = normalize(-positionVS)` |
| Eq.6 | 沿 φ 求两侧最大地平线角 | `GTAO()` 内层循环 `horizon = acos(...)` |
| Eq.7 | 解析内积分（余弦加权） | `GTAO_IntegrateArc_CosWeight` |
| Eq.8 | 投影法线长度修正 + MC over φ | `projLength * GTAO_IntegrateArc_CosWeight(...)`, `occlusion/numDirections` |
| Eq.9 | 厚度启发式 / 保守衰减 | 循环里的 `falloff` 与 `thickness` 分支 |
| Eq.10 | 多弹射立方拟合 | `GTAO_MultiBounce`（`GTAO_Composite_frag` 调用） |
| Eq.17–18 | 可见性锥孔径 ← AO / bent normal | `GTAO()` 的 bent normal 输出、`_GTAO_BentNormalTexture` |
| Eq.19–20 | GTSO = 锥交集比例 | `GTAO_ReflectionOcclusion`（保留、未接入） |
| Eq.22–23 | 均匀加权（Appendix A） | `GTAO_IntegrateArc_UniformWeight`（保留、未使用） |
| Eq.26 | 高光锥孔径 ← roughness | `GTAO_ReflectionOcclusion` 的 `arc0` |
| §4.1 | 半分辨率 + 4×4 空间重建 + 6 旋转 + 时域累积 | `GTAORenderPass.cs` 的 `s_TemporalRotations`、`s_SpatialOffsets`、`GTAO_BilateralBlur`、`GTAO_Temporal_frag` |

---

## 10. 与论文的差异（有意为之）

| 论文 | 本项目 | 原因 |
|---|---|---|
| 半分辨率 AO + 上采样 | 全分辨率 | 移植优先清晰 / 可学习，质量可调 |
| 指数累积缓冲（时域） | variance clipping（AABB）+ 历史混合 | 更好抑制 ghosting，业界更通用 |
| 1 方向/像素 + 4×4 重建（96 有效样本） | `directions` 个切片 + 可分离 8-tap 双边 | 保留思想，参数更直观 |
| 反射遮蔽 GTSO 接入合成 | 保留公式、未接入 | URP 不暴露等价的反射缓冲全局纹理 |
| 自带 `_CameraReflectionsTexture`（Built-in） | 使用 `_GBuffer0/1/2` + `_CameraNormalsTexture` | URP 的数据源与调度方式不同 |

**注意**：所有这些差异都只发生在「数据从哪来 / 调度怎么排」这一层；
`GTAO_Common.hlsl` 里的**数学（Eq.5–10）与论文逐条对应**，这是移植时刻意守住的底线。

---

## 11. 核心要义（TL;DR）

1. **A 是 accessibility，不是阴影。** 论文与本项目的 `A ∈ [0,1]`，1 = 完全敞开；
   shader 里叫 `occlusion` / `ao` 的变量就是它，所以合成用乘法、debug 白色 = 无遮蔽。

2. **二值可见性 + 无经验衰减，是能解析积分的根本原因。**
   正因如此才有 Eq.7 的闭式内积分，shader 才变成 memory bound。

3. **换到视线参考系，是简化的关键。**
   地平线相对 `o` 而非 `n` 测量，使核心只剩 2 个 cos + 1 个 sin + 3 个 acos。

4. **地平线搜索只保留最大角**（Eq.6），投影法线长度做修正（Eq.8）。

5. **实时性的真正来源是时空复用**，不是单像素采样数：
   1 方向/帧 × 空间双边 × 时域重投影 ≈ 96 有效方向（论文 §4.1）。

6. **多弹射是对 AO 能量损失的物理化补偿**（Eq.10），
   依赖 albedo，所以本项目选 Deferred 路径读取 `_GBuffer0`。

7. **bent normal 是 AO 的"副产品"，却是 GTSO 的"主料"**（Eq.18）；
   本项目把它单独输出、单独 debug，为将来接入反射遮蔽留好接口。

8. **移植到 URP 只换了"接口"，没换"数学"。**
   `CameraEvent → ScriptableRenderPass`、`_CameraGBufferTexture* → _GBuffer*/_CameraNormalsTexture`、
   `CommandBuffer.Blit → Blitter + RTHandle`，而 `GTAO_Common.hlsl` 中的公式与论文一一对应。

---

## 参考

* Jimenez et al., *Practical Realtime Strategies for Accurate Indirect Occlusion*, 2016.
  <https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion.pdf>
* Bavoil et al., *Image-space Horizon-based Ambient Occlusion*, SIGGRAPH 2008 Talks.
* Timonen, *Line-sweep Ambient Obscurance*, CGF 2013.
* 项目内文件：`Assets/GTAO/Shaders/GTAO_Common.hlsl`、`GTAO_Passes.hlsl`、
  `Assets/GTAO/Runtime/GTAORenderPass.cs`、`Assets/GTAO/Documentation/README.md`。
