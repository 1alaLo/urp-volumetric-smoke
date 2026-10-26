# 仿 CS2 体积烟雾弹（Unity URP）

一个在 Unity URP 下实现的**真体积烟雾** Demo：解析式球体 ray-march + 3D 噪声密度场 +
主光散射 / 自阴影 + 基于深度图的正确遮挡裁剪。附带一个可以直接开玩的第一人称场景。

不是 billboard、不是贴图序列帧，是对密度场做沿视线的光学厚度积分。

![preview](docs/preview.png)

## 环境要求

| 项 | 版本 |
| --- | --- |
| Unity | 6000.5.6f1（Unity 6.1 及以上均可） |
| URP | 17.5.0 |
| Input System | 1.20.0 |

URP Asset（`Assets/Settings/PC_RPAsset.asset`）里的 **Depth Texture 必须开启**，
体积积分靠它把射线裁剪到不透明表面处。

## 快速开始

1. 用 Unity Hub 打开本工程目录
2. 打开场景 `Assets/SmokeVolumetric/Scenes/SmokeDemo.unity`
3. 点 **Play**

一开始会锁定鼠标；按 `Esc` 释放，点击画面重新锁定。

## 操作

| 输入 | 作用 |
| --- | --- |
| `WASD` | 移动 |
| `Shift` | 冲刺 |
| `Space` | 跳 |
| 鼠标 | 视角 |
| **左键** | 投掷烟雾弹（1.55s 引信） |
| **右键** | 脚下快速投放（0.9s 引信） |
| `C` | 清除所有烟 |
| `R` | 回到出生点 |
| `Esc` | 释放/锁定鼠标 |

场景里放了几组掩体（矮墙、箱子、塔楼、建筑），用来观察烟和几何体的正确遮挡关系：
烟只在几何体**后面**的部分被绘制，贴在墙前、绕到墙后都会正确分层。

## 效果参数

材质在 `Assets/SmokeVolumetric/Resources/SmokeVolume.mat`（运行时由 `Resources.Load` 读取）。

| 参数 | 当前值 | 含义 |
| --- | --- | --- |
| `_Density` | 2.4 | 整体密度。最直观的"烟有多浓" |
| `_Absorption` | 1.45 | 消光系数，和 `_Density` 相乘得到 sigmaT |
| `_NoiseScale` | 1.05 | 噪声频率，越大团块越碎 |
| `_EdgeSoftness` | 0.42 | 轮廓软硬，越小边缘越硬 |
| `_StepCount` | 56 | 单条射线步数，性能/质量的主要开关 |
| `_ShadowStrength` | 1.05 | 自阴影强度 |
| `_AmbientStrength` | 1.25 | 环境光贡献 |
| `_PhaseG` | 0.42 | HG 相函数前向散射强度 |

调参时的经验：`_Density` 和 `_Absorption` 的乘积决定光学厚度；这个乘积偏大时，
射线在靠近相机的一小段内就衰减到 0，烟里会变成一块均匀的灰板（没有层次）；
偏小时又能看穿烟。当前值是在"6.4m 直径仍然全黑、内部积分深度约 2m"之间取的平衡。

## 生命周期

`SmokeVolume` 上的公开字段可以按需改：

| 字段 | 值 |
| --- | --- |
| `maxRadius` | 3.2 m |
| `lifetime` | 18 s |
| `expandTime` | 1.25 s |
| `fadeTime` | 3.5 s |

爆开 → 1.25s 内膨胀到最大半径 → 缓慢上浮 → 最后 3.5s 由 `_Fade` 驱动**侵蚀消散**
（密度场被逐渐掏空成丝状残烟），而不是整体变透明。

## 实现要点

**渲染**（`Shaders/SmokeVolume.shader`）

- 用球体网格做代理，`Cull Front` 保证相机进入烟内部时仍然有像素参与绘制
- `ZTest Always` + `ZWrite Off`，真正的遮挡交给深度纹理判断
- 片元里解析求交得到 `[t0, t1]`，再用 `_CameraDepthTexture` 换算的视线距离裁剪 `t1`
- 沿射线等距采样密度场，累积 `T *= exp(-sigma*step)`，输出 **预乘 alpha**
  （`Blend One OneMinusSrcAlpha`）
- 光照：主光 + Henyey-Greenstein 相函数 + 3 次朝光源的密度采样做自阴影 +
  多次散射近似 + 球谐环境光
- 交错梯度噪声抖动采样起点，消除层状切片

**密度场**（`Scripts/SmokeNoise.cs`）

- 64³ 可平铺 R8 3D 纹理，三个八度的 value noise，**纯 C# 运行时生成**，
  仓库里不需要任何二进制噪声资源
- 密度 = 内实核心 + 噪声外鞘；`_Age` 驱动涡流 warp 和上浮，
  所以烟雾在掉帧的机器上也是连续演化的

**交互**（`Scripts/FpsPlayer.cs`、`Scripts/SmokeGrenade.cs`）

- `CharacterController` 第一人称，带冲刺、跳跃、走路镜头晃动
- 烟雾弹是刚体抛射物，弹跳后落地，引信到点后向下 raycast 找地面再生成烟团
- Input System 直接轮询 `Keyboard.current` / `Mouse.current`，不需要 `.inputactions` 资源

## 目录结构

```
Assets/SmokeVolumetric/
├── Shaders/SmokeVolume.shader   # 体积 ray-march
├── Scripts/
│   ├── SmokeNoise.cs            # 程序化 3D 噪声
│   ├── SmokeVolume.cs           # 单个烟团：生命周期 + 参数推送
│   ├── SmokeGrenade.cs          # 投掷物 + 引信 + 生成烟团
│   ├── FpsPlayer.cs             # 第一人称控制
│   ├── DemoHud.cs               # IMGUI 准星与状态
│   └── SmokeDemoBootstrap.cs    # 后台运行设置
├── Editor/SmokeDemoBuilder.cs   # 用代码生成整个演示场景
├── Resources/SmokeVolume.mat
├── Scenes/SmokeDemo.unity
├── Textures/SmokeDemo_Grid.png
└── Materials/                   # 场景用的 Lit 材质
```

## 关于场景生成

演示场景不是手搭的，而是由 `Editor/SmokeDemoBuilder.cs` 用代码生成（40×40 场地、
掩体、光照、天空盒、玩家、HUD），这样仓库里没有易碎的手工引用。

开发期通过 Unity 的 `com.unity.pipeline` 实验包暴露了两个命令：

```
unity command smoke_demo_build   # 重建并保存 SmokeDemo.unity
unity command smoke_preview      # 在 Scene 视图交叉点生成一朵预览烟
```

**如果你不想保留这个实验包依赖**：删掉 `Editor/SmokeDemoBuilder.cs` 顶部的
`using Unity.Pipeline.Commands;` 和文件里两处 `[CliCommand(...)]` 即可。
场景文件已经提交，运行时完全不需要这个包。

## 性能

1280×720 下：看向烟外约 300+ fps，整个屏幕被烟填满时约 70–80 fps
（测试机为桌面独显）。想更快就把 `_StepCount` 从 56 降到 32，视觉差异很小；
烟雾的渲染开销几乎全部来自步数 × 每步 4 次 3D 纹理采样（1 次密度 + 3 次自阴影）。

## 许可

本项目仅用于学习和演示，与 Valve / Counter-Strike 无关。
