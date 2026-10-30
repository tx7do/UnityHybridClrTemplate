# 热更新完整使用指南

本项目使用 **HybridCLR** 实现代码热更新，**Addressables** 实现资源热更新，
并在两者之上实现**容器模型**：同一套代码支持单游戏与多游戏矩阵两种形态。

---

## 架构总览

```
┌─ SampleScene (Bootstrap场景，打进主包) ──────────────────────────────┐
│  GameBootstrap（容器）+ PatchUI（进度/重试）                           │
│    ① HybridCLR 初始化（加载 AOT 补充元数据）                           │
│    ② 获取 games.json：远程 → 失败回退 StreamingAssets 内置             │
│    ③ 全局最低 App 版本门禁                                             │
│    ④ Addressables 初始化                                              │
│    ⑤ 分形态                                                            │
│       ├ games 仅 1 条 → 自动进入（单游戏体验）                         │
│       └ games N 条    → GameHallUI 大厅 → 点击游戏时按需下载并进入      │
└────────────────────────────────────────────────────────────────────────┘
                              ↓ 进入单个游戏
┌─ EnterGameRoutine ───────────────────────────────────────────────────┐
│  1. 按游戏对比版本（本地 Data/games/{id}/state.json vs 注册表）        │
│  2. 缺/旧 → 下载该游戏的 DLL（逐个 MD5 校验）                          │
│  3. Assembly.Load（跨游戏去重，同名程序集只加载一次）                   │
│  4. 反射调用该游戏入口（entryClass.entryMethod）                       │
│  5. 按该游戏 label 解析资源位置并下载（LoadResourceLocations + Download）│
│  6. Addressables.LoadSceneAsync(该游戏入口场景)                        │
└──────────────────────────────────────────────────────────────────────┘
                              ↓
┌─ 子游戏场景 (Addressable远程资源，如 games/demo/main) ────────────────┐
│  DemoSceneLoader → InstantiateAsync("games/demo/cube")               │
│    → 预制体上挂热更程序集的 DemoCubeController                         │
└──────────────────────────────────────────────────────────────────────┘
```

### 单游戏与矩阵的统一

没有任何"模式开关"。**清单条目数决定形态**：

| | 单游戏 | 多游戏矩阵 |
|---|---|---|
| `games.json` | 1 条 | N 条 |
| 启动 | 自动进入 | 显示大厅 |
| 版本粒度 | 该游戏整体 | 每游戏独立版本（改一个只下一个） |

单游戏就是"N=1 的矩阵"，走的完全同一条代码路径。切换形态 = 改清单 + 重新打包，主包代码零改动。

### 三份 games.json 各自的角色

| 文件 | 谁写 | 内容 | 用途 |
|------|------|------|------|
| `Assets/HotUpdate/games.json` | 开发者手写 | 只要 id/name/dlls/入口/scene/label | **定义**（随工程管理，无版本/MD5） |
| `ServerOutput/games.json` | 打包工具生成 | 定义 + 整体版本 + 每游戏版本 + 每 DLL 的 MD5/大小 | **发布**（服务器提供） |
| `StreamingAssets/games.json` | 打包工具生成 | 与服务器一致，但对应内置 DLL | **兜底**（离线/首次安装） |

运行时优先拉远程注册表；拉不到时用内置清单 + 内置 DLL，离线也能进内置版本。

### 部署配置（HotUpdateConfig）

`Assets/Resources/HotUpdateConfig.asset`（菜单 `HotUpdate/0. HotUpdate Config` 打开）：

| 字段 | 默认值 | 消费方 | 说明 |
|------|--------|--------|------|
| `serverBaseUrl` | `http://127.0.0.1:8080` | `GameUpdateManager`；编辑器 Addressables Setup | 代码与资源热更共用的服务器根地址 |
| `requestTimeoutSeconds` | `30` | HttpClient | 请求超时 |
| `minAppVersion` | 空 | 打包工具 → games.json | 最低 App 版本门禁 |

每个游戏的入口/依赖/场景不在这个资产里——在 `Assets/HotUpdate/games.json`。

### 失败策略

| 阶段 | 失败时行为 |
|------|-----------|
| 获取远程注册表 | 回退 StreamingAssets 内置清单（离线可玩内置版本） |
| 拉不到清单且无内置 | 阻断 + 重试按钮 |
| App 版本低于 `minAppVersion` | **阻断**：提示更新 App（无重试） |
| 某游戏 DLL 下载/校验失败 | 阻断 + 重试按钮（重试只重跑该游戏） |
| 该游戏入口执行失败 | 阻断 + 重试（游戏逻辑不可用，不同于整体降级） |
| 资源预下载失败 | 记录日志，场景加载时按需拉取 |
| 场景加载失败 | 阻断 + 重试按钮 |

### 退出子游戏、返回大厅

容器对象是 `DontDestroyOnLoad` 的：子游戏场景以 Single 模式加载后 Bootstrap 场景被卸载，
但容器（GameBootstrap / PatchUI / GameHallUI）继续存活，因此游戏运行期间仍能响应返回请求。

**热更侧的调用方式**（`Common` 已被 HotUpdate 程序集引用）：

```csharp
using Common.GameModule;

GameRuntime.RequestReturnToHall();   // 返回 false = 没有容器接管
```

**容器侧的处理顺序**（`GameBootstrap.ReturnToHallRoutine`）：

1. 调用该游戏的退出方法（`games.json` 的 `exitMethod`，缺省约定 `"Exit"`；未声明则跳过）
2. 卸载该游戏场景并释放句柄
   - Unity 不允许卸载「最后一个已加载场景」：先 `SceneManager.CreateScene` 建空场景承托再卸载
   - `Addressables.UnloadSceneAsync` 的 `autoReleaseHandle` **默认是 true**，不要对返回句柄再 Release
3. 重跑容器流程：矩阵形态回到大厅（状态刷新），单游戏形态重新进入该游戏

示例：`DemoCubeController` 里按 Esc 触发返回；`Hello.Exit()` 演示退出方法约定。

---

## 首次环境准备（一次性）

### 1. 安装 HybridCLR

打开 Unity 后执行菜单 `HybridCLR/Installer...` → Install。
安装器会自动下载与本机 Unity 版本匹配的 hybridclr/il2cpp_plus 源码（v9 起无需 git 子模块）。
**HybridCLR 版本必须与 Unity 版本严格匹配**，否则真机会在热更初始化阶段崩溃。

### 2. 确认包

- `HybridCLR`（代码热更）
- `Addressables 2.9.1`（资源热更）
- `Test Framework`（跑单元测试）

### 3. Player Settings

`File/Build Settings` → Scripting Backend 改为 **IL2CPP**。

### 4. 初始化 Addressables（仅需一次）

```
HotUpdate / Addressables / 0. Setup Addressables Settings
```

自动创建 Settings、配置 Profile（Remote.LoadPath = `<serverBaseUrl>/Addressables/[BuildTarget]`）、
创建默认 Group 并配置为远程打包。

---

## 日常打包流程

### 步骤 0：维护游戏清单

编辑 `Assets/HotUpdate/games.json`：

```json
{
    "games": [
        {
            "id": "demo",
            "name": "Demo 小游戏",
            "dlls": ["HotUpdate.dll"],
            "entryAssembly": "HotUpdate",
            "entryClass": "HotUpdate.Hello",
            "entryMethod": "Run",
            "scene": "games/demo/main",
            "label": "games/demo"
        }
    ]
}
```

- `dlls`：该游戏依赖的全部热更程序集（含公共程序集），**按依赖序**排列
- `dlls` 中的程序集必须已加入 HybridCLR Settings 的 `hotUpdateAssemblyDefinitions`
- `scene` / `label`：必须与该游戏资源的 Addressable 地址/label 对应

### 步骤 1：代码热更打包

```
HotUpdate / 1. Build HotUpdate Package (一键打包)
```

产出：`HybridCLRData/ServerOutput/`（games.json + 全部被引用的热更 DLL）。

> 打包工具只发布 `games.json` 里声明的 DLL。HybridCLR 编译输出目录里还有主包等程序集，不会混入。
> 一键打包会把 DLL 与内置 games.json 也拷进 `Assets/StreamingAssets/`（内置兜底版本）；
> 只想更新服务器而不重新出主包时，执行 `HotUpdate/6. Generate Server Output` 即可。

### 步骤 2：资源热更打包

```
HotUpdate / Addressables / 2. Build Addressables (一键打包资源)
```

自动将 DemoCube/MainMenu 标记为 Addressable（地址 `games/demo/*`，label `games/demo`）→ 构建 →
拷贝到 `HybridCLRData/ServerOutput/Addressables/[平台]/`。

### 步骤 3：启动服务器

```bash
cd <项目根目录>
python3 tools/serve_hotupdate.py
```

### 步骤 4：运行测试

Play 场景，PatchUI 显示进度；单游戏形态日志示例：

```
[GameBootstrap] ① 初始化 HybridCLR
[GameBootstrap] ② 获取游戏注册表
[GameUpdateManager] 远程注册表: 1 个游戏 (version=20261030180442)
[GameBootstrap] ⑤ 单游戏形态，自动进入
[GameBootstrap] [demo] 发现新版本 20261030180442，开始下载
[GameUpdateManager] MD5 校验通过: HotUpdate.dll
[GameBootstrap] [demo] 资源就绪
[DemoSceneLoader] DemoCube 实例化成功（热更代码+资源协同验证）
[DemoCube] 热更组件启动 @ DemoCube(Clone)，旋转速度=90°/s
```

矩阵形态则显示"游戏大厅"（每个游戏一行，标注 待下载/可更新/已就绪），点击后进入。

---

## 验证热更新

### 验证代码热更

1. 修改 `Hello.cs` 的日志文本
2. 执行 `HotUpdate/1. Build HotUpdate Package`
3. 重启 Python 服务器
4. 启动客户端 → 自动下载新 DLL → 显示新文本

### 验证资源热更

1. 修改 `DemoCube.prefab` 的颜色/大小/旋转参数（或 `DemoCubeController.cs` 的 `rotateSpeed`）
2. 执行 `HotUpdate/Addressables/2. Build Addressables`
3. 重启 Python 服务器
4. 启动客户端 → 自动下载新资源 → 观察新效果

### 验证矩阵形态

1. 在 `Assets/HotUpdate/games.json` 里复制 demo 条目改成 `id: "second"`（可复用同一套 DLL/场景）
2. 重新执行 `HotUpdate/1. Build HotUpdate Package`（或只 `6. Generate Server Output`）
3. 启动客户端 → 应显示「游戏大厅」而不是自动进入
4. 删回一条 → 重新打包 → 又变回自动进入

### 模拟首次安装

菜单 `HotUpdate/8. Clear Local HotUpdate Cache (清本地缓存)`：
删除 `persistentDataPath/Data/`（含 `Data/games/{id}/` 各游戏的 DLL 与状态）并清空 AssetBundle 缓存。

---

## 新增一个子游戏（矩阵扩编）

1. **建程序集**：新建 asmdef（如 `Hot.Game.Ddz`），写入该游戏代码，入口类提供静态方法
2. **注册热更程序集**：`ProjectSettings/HybridCLRSettings.asset`（菜单 HybridCLR/Settings）→
   `hotUpdateAssemblyDefinitions` 追加该 asmdef
3. **资源打 label**：该游戏的场景/预制体标记 Addressable，地址用 `games/ddz/*` 约定，
   label 设为 `games/ddz`（可照抄 `AddressablesBuildEditor.MarkAssetsAsAddressable` 的写法）
4. **加清单记录**：`Assets/HotUpdate/games.json` 追加一条（dlls 若依赖公共程序集，把它排前面）
5. **打包**：一键代码打包 + Addressables 打包
6. 大厅自动出现新游戏，点击按需下载

公共程序集（如 UI/网络/音频框架）应单独做一个 asmdef 并在每个游戏的 `dlls` 里排第一位：
跨游戏去重加载，只下载一次（首次进入任一游戏时）。

---

## 菜单命令一览

### 配置与代码热更新

| 菜单 | 说明 |
|------|------|
| `HotUpdate/0. HotUpdate Config (部署配置)` | 打开/创建 HotUpdateConfig 资产 |
| `HotUpdate/1. Build HotUpdate Package` | 一键代码打包 |
| `HotUpdate/2-5. ...` | 分步代码打包（高级） |
| `HotUpdate/6. Generate Server Output` | 仅重新生成服务器输出（含 games.json） |
| `HotUpdate/7. Open Server Output Folder` | 打开服务器输出目录 |
| `HotUpdate/8. Clear Local HotUpdate Cache` | 清本地缓存，模拟首次安装 |

### 资源热更新

| 菜单 | 说明 |
|------|------|
| `HotUpdate/Addressables/0. Setup Addressables Settings` | **首次初始化**（仅1次） |
| `HotUpdate/Addressables/1. Check Addressables Status` | 检查配置状态 |
| `HotUpdate/Addressables/2. Build Addressables` | **一键资源打包** |
| `HotUpdate/Addressables/3. Mark Assets as Addressable` | 仅标记资源 |
| `HotUpdate/Addressables/4. Clean Build Cache` | 清理构建缓存 |

## 单元测试

`Window → General → Test Runner → Run All`：

**EditMode（随处可跑）**
- `GameRegistryTests` — games.json 序列化契约（含旧格式/缺字段兼容）
- `VersionUtilsTests` — 版本比较与门禁语义（数字段比较、空值不拦截）
- `CryptoUtilsTests` — MD5 输出格式（下载校验依赖 32 位小写 hex）

**PlayMode（端到端冒烟，需先打包并启动服务器）**
- `HotUpdateFlowSmokeTests` — 清空本地缓存后加载 Bootstrap 场景，断言单游戏形态
  自动进入：注册表获取 → 按游戏下载（state.json 落盘）→ 进入 MainMenu 且 DemoCube 挂着热更组件

命令行运行：
```
Unity -batchmode -nographics -projectPath <项目> -runTests -testPlatform PlayMode -testResults <xml>
```

> 注意：PlayMode 测试的程序集必须用 `defineConstraints: ["UNITY_INCLUDE_TESTS"]`
> （而不是限定 Editor 平台），否则会被 HybridCLR 的 player 目标裁剪编译拦截，或测试发现不到。

## IL2CPP 真机路径验证（重要）

Editor 与 PlayMode 测试都跑在 Mono 下，且 `HotUpdateLoader.EnsureLoaded` 在 `#if UNITY_EDITOR`
下直接放行（编辑器里程序集由 Unity 自动加载）。因此 **player 侧的"读已下载 DLL → 去重 →
Assembly.Load(bytes)"这条路径只能靠出包验证**，HybridCLR 的 AOT 元数据机制也只在 IL2CPP 下生效。

出 Windows IL2CPP 开发包并跑一遍：

```
# 1. 打包（代码 + 资源，已在上面步骤完成）
# 2. 出包（Standalone 的 scriptingBackend 必须是 IL2CPP）
Unity -batchmode -quit -nographics -projectPath <项目> ^
      -executeMethod <你的出包脚本方法> -logFile build.log

# 3. 启动本地服务器后运行 exe，日志在
#    %USERPROFILE%\AppData\LocalLow\<Company>\<Product>\Player.log
```

玩家侧日志里应出现这几条（Editor 下不会有的）：

```
[HotUpdateLoader] 读取已下载的 DLL: ...\Data\games\demo\dlls\HotUpdate.dll
[HotUpdateLoader] 已加载程序集: HotUpdate
Hello, World
```

Android 额外验证 StreamingAssets 读取路径（AOT 元数据 + 内置 DLL 走 UnityWebRequest）。

---

## 文件结构说明

```
项目根/
├── Assets/
│   ├── Editor/
│   │   ├── HotUpdateBuildEditor.cs       ← 代码打包 + games.json 注册表生成
│   │   ├── HotUpdateCacheMenu.cs         ← 清本地缓存（模拟首次安装）
│   │   ├── AddressablesSetupEditor.cs    ← Addressables初始化
│   │   └── AddressablesBuildEditor.cs    ← Addressables打包（按游戏打 label）
│   ├── HotUpdate/
│   │   └── games.json                    ← 游戏清单定义（开发者维护）
│   ├── AddressableAssets/
│   │   ├── Prefabs/DemoCube.prefab       ← demo 资源(label: games/demo)
│   │   └── Scenes/
│   │       ├── SampleScene.unity         ← Bootstrap场景(打进主包)
│   │       └── MainMenu.unity            ← demo 入口场景(address: games/demo/main)
│   ├── Resources/HotUpdateConfig.asset   ← 部署配置
│   ├── Scripts/
│   │   ├── Main/
│   │   │   ├── Bootstrap/GameBootstrap.cs   ← 容器流程 + 失败策略
│   │   │   ├── Games/                       ← GameRegistry + GameUpdateManager
│   │   │   ├── UI/                          ← PatchUI + GameHallUI + UiBuilder
│   │   │   ├── Config/HotUpdateConfig.cs
│   │   │   ├── HybridCLR/HybridClrManager.cs
│   │   │   ├── HotUpdateLoader.cs           ← 按游戏加载程序集 + 入口调用
│   │   │   └── DemoSceneLoader.cs
│   │   ├── HotUpdate/                       ← 热更代码（可扩展多程序集）
│   │   └── Common/Utilities/                ← CryptoUtils / VersionUtils
│   ├── StreamingAssets/                     ← (打包生成)
│   │   ├── AOT/                             ← AOT补充元数据
│   │   ├── *.dll                            ← 内置热更DLL（兜底版本）
│   │   └── games.json                       ← 内置注册表（兜底）
│   └── Tests/                               ← Editor / PlayMode 测试
├── ServerData/                              ← Addressables构建输出(打包生成)
├── HybridCLRData/ServerOutput/              ← Python serve此目录
│   ├── games.json                           ← 游戏注册表（含版本/MD5）
│   ├── *.dll                                ← 全部热更DLL（平铺）
│   └── Addressables/[平台]/                 ← 资源热更产物
├── tools/serve_hotupdate.py
└── docs/HOTUPDATE_GUIDE.md
```

---

## 关键配置说明

### Addressables Profile（由 Setup 脚本自动配置）

| 变量 | 值 | 说明 |
|------|-----|------|
| `Remote.BuildPath` | `ServerData/[BuildTarget]` | 构建输出目录 |
| `Remote.LoadPath` | `<serverBaseUrl>/Addressables/[BuildTarget]` | 运行时加载URL |

### demo 游戏的地址映射

| 资源 | 地址 | label | 用途 |
|------|------|-------|------|
| `DemoCube.prefab` | `games/demo/cube` | `games/demo` | DemoSceneLoader 实例化 |
| `MainMenu.unity` | `games/demo/main` | `games/demo` | 入口场景（games.json 的 scene 字段） |

### 切换到正式 CDN

**只改一处**：`HotUpdate/0. HotUpdate Config` → `serverBaseUrl` 改为 `https://your-cdn.com`，然后：

1. 代码热更：运行时直接生效，无需重编主包
2. 资源热更：URL 烘焙在 catalog 里，需重跑 `HotUpdate/Addressables/0. Setup` + `2. Build Addressables`
3. 将 `HybridCLRData/ServerOutput/` 下所有文件上传到对应 CDN 路径

---

## 常见问题

### Q: Editor 下 Addressables 报 "catalog not found"

首次必须执行 `HotUpdate/Addressables/2. Build Addressables` 生成 catalog。

### Q: Build Addressables 报 "Settings not found"

先执行 `HotUpdate/Addressables/0. Setup Addressables Settings`。

### Q: 增量构建后资源没更新

执行 `HotUpdate/Addressables/4. Clean Build Cache` 后重新打包。

### Q: 打包报 "游戏 xxx 引用的 DLL 不在 HybridCLR Settings 的热更程序集中"

`games.json` 里的 dlls 必须是 HybridCLR Settings 声明的热更程序集。
在 `HybridCLR/Settings` 的 `hotUpdateAssemblyDefinitions` 里添加对应 asmdef。

### Q: 真机无法连接服务器

- 确认 Python 服务器在运行
- 真机使用电脑的局域网 IP，并同步修改 `HotUpdateConfig.serverBaseUrl`（改完重跑 Addressables Setup + Build）
- 客户端不走系统代理（`UseProxy=false`），本地测试不会被 Clash 等代理劫持

### Q: 运行时提示「需要更新 App」

触发了最低版本门禁：games.json 的 `minAppVersion` 高于当前 App 版本（Player Settings → Version）。
属预期行为；调试时可清空 `minAppVersion` 重新打包。

### Q: DemoCube 加载后看不到

- 检查摄像机位置和朝向（MainMenu 的 Main Camera 在原点）
- DemoCube 实例化在 z=5 位置，摄像机应该能看到

### Q: 进入第二个子游戏后，公共程序集的改动没生效

共享程序集在本次会话内只加载一次（跨游戏去重）。公共程序集的更新需要重启应用生效——
这是刻意设计：避免会话中途替换已引用类型（会导致类型身份不一致）。

### Q: MD5 校验能防篡改吗

不能——MD5 只保证下载完整性（防传输损坏），不防恶意篡改。正式环境请使用 HTTPS CDN 保证传输安全；
需要更强保证时，可给 games.json 增加签名字段（对 DLL 内容做 RSA/Ed25519 签名，主包内置公钥校验），
扩展点在 `GameUpdateManager.DownloadGame` 的 MD5 校验处。

### Q: 热更组件挂不上 / Missing 脚本

热更 MonoBehavour 不能被**主包**资源直接引用（序列化会丢脚本）。
正确示范：预制体放进 Addressables（加载晚于热更 DLL 加载），组件脚本在热更程序集中随资源实例化生效。
主包预制体需要热更逻辑时，用主包占位组件 + 运行时接口转发到热更程序集。
