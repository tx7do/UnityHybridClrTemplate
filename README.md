# UnityHybridClrTemplate

Unity 6 + **HybridCLR 代码热更** + **Addressables 资源热更** 的**容器式**项目模板：
同一套代码同时支持「单游戏」与「多游戏矩阵」两种形态，切换方式只是改游戏清单？

——不，是**改清单内容**：`games.json` 里 1 条游戏记录 = 单游戏项目（启动自动进入），N 条 = 矩阵项目（显示游戏大厅）。

完整流程与架构说明见 [docs/HOTUPDATE_GUIDE.md](docs/HOTUPDATE_GUIDE.md)。

## 核心模型

```
主包（基本不变）
└─ GameBootstrap（容器）
   ① HybridCLR 初始化（AOT 补充元数据）
   ② 获取 games.json：远程 → 失败回退 StreamingAssets 内置
   ③ 全局最低 App 版本门禁
   ④ Addressables 初始化
   ⑤ 分形态：
      ├─ 1 个游戏  → 自动进入（单游戏体验，玩家无感知大厅）
      └─ N 个游戏  → 游戏大厅 → 点击后按需下载该游戏的 DLL + 资源 → 进入
```

每个子游戏独立拥有：版本号、DLL 依赖列表（含公共程序集）、入口类/方法、入口场景、资源 label。
改一个子游戏只重下该子游戏的 DLL，其它子游戏不受影响。

## 目录结构

- `Assets/HotUpdate/games.json` — **游戏清单定义**（开发者维护，声明有哪些游戏、各自的依赖/入口/场景）
- `Assets/Scripts/Main/Bootstrap` — `GameBootstrap`：容器流程编排 + 失败策略
- `Assets/Scripts/Main/Games` — `GameRegistry`（清单数据结构）+ `GameUpdateManager`（按游戏版本/下载）
- `Assets/Scripts/Main/UI` — `PatchUI`（下载进度/重试）+ `GameHallUI`（游戏大厅，均运行时构建）
- `Assets/Scripts/Main/Config` — `HotUpdateConfig` 部署配置（服务器地址、最低 App 版本）
- `Assets/Scripts/HotUpdate` — 热更新代码（可扩展为多个游戏程序集）
- `Assets/Scripts/Common` — 主包与热更程序集共享的工具代码
- `Assets/Editor` — 打包工具链（菜单 `HotUpdate/`）
- `Assets/Resources/HotUpdateConfig.asset` — 部署配置资产（切 CDN 只改这一处）
- `Assets/Tests` — EditMode 单元测试 + PlayMode 端到端冒烟测试
- `tools/serve_hotupdate.py` — 本地热更测试服务器（代码 + 资源）

## 用模板新建项目

1. 从本模板创建仓库（GitHub `Use this template`，或 `git clone` 后按需重置历史），文件夹改成自己的项目名
2. 用 Unity **6000.3.19f1** 或更高版本打开项目
3. 菜单 `HybridCLR/Installer` → Install（自动下载与本机 Unity 版本匹配的 hybridclr/il2cpp_plus 并安装到本地）
4. 菜单 `HybridCLR/Generate/All` 一键执行必要的生成操作
5. `ProjectSettings` → Player → Product Name 改成自己的项目名
6. 按 [docs/HOTUPDATE_GUIDE.md](docs/HOTUPDATE_GUIDE.md) 初始化 Addressables 并验证「代码热更 + 资源热更」闭环

## 单游戏 / 多游戏怎么选

| | 单游戏项目 | 多游戏矩阵 |
|---|---|---|
| `games.json` | 1 条记录 | N 条记录 |
| 启动行为 | 自动进入该游戏 | 显示游戏大厅 |
| 下载行为 | 启动时下载该游戏 | 点进哪个游戏下载哪个 |
| 工程结构 | 一个热更程序集（模板默认） | 建议拆公共程序集 + 每游戏一个程序集 |
| 代码改动 | 无 | 无（清单加记录 + 新 asmdef + 新 Asmdef 的资源 label） |

两种形态可以在同一份工程里验证：改 `games.json` 条目数 → 重新打包 → 重启客户端即可。

## HybridCLR 版本说明

- 本模板锁定 `com.code-philosophy.hybridclr#v9.0.1`，对应 **Unity 6000.3.x**。v9 起安装器自动管理 hybridclr/il2cpp_plus 源码版本（下载到 `Library/HybridCLR/`），不再使用 git 子模块。
- **HybridCLR 版本必须与 Unity 版本严格匹配**：il2cpp 补丁按 Unity 小版本维护（如 `v6000.3.x-9.0.1`）。版本不匹配时编辑器一切正常，但真机会在热更初始化阶段直接崩溃（global-metadata 版本断言失败），且日志几乎没有线索。
- 升级 Unity 版本时：同步升级 `Packages/manifest.json` 中 hybridclr_unity 包的版本标签，然后重跑 `HybridCLR/Installer` 与 `HybridCLR/Generate/All`，并重新出包验证。

## 打包流程

详细步骤见 [docs/HOTUPDATE_GUIDE.md](docs/HOTUPDATE_GUIDE.md)，核心链路：

- 维护 `Assets/HotUpdate/games.json`（声明游戏清单）
- 运行菜单 `HotUpdate/1. Build HotUpdate Package (一键打包)` 生成代码热更包
  （内部执行 Generate/All → 编译 DLL → 拷贝 AOT 元数据 → 生成服务器输出与 `games.json` 注册表）
- 运行菜单 `HotUpdate/Addressables/2. Build Addressables (一键打包资源)` 生成资源热更包
  （按游戏标记 label → 构建 → 拷贝到服务器目录）
- `python tools/serve_hotupdate.py` 启动本地服务器（默认 8080，客户端已固定走 `127.0.0.1` 并绕过系统代理）
- 启动客户端：`GameBootstrap` 按 `games.json` 的形态自动走「进入单游戏」或「显示大厅」

## 版本门禁

`HotUpdateConfig.minAppVersion` 打包时写入 `games.json`：App 版本低于它的旧主包会被强制更新提示拦截
（防「旧主包 + 新热更 DLL」不兼容）。

## 单元测试

`Window → General → Test Runner → Run All`：EditMode 覆盖清单序列化契约、版本门禁、MD5 工具；
PlayMode 冒烟测试会真实跑通完整闭环（需先启动 `tools/serve_hotupdate.py`）。
