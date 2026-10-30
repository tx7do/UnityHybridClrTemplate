# UnityHybridClrTemplate

Unity 6 + **HybridCLR 代码热更** + **Addressables 资源热更** 项目模板。

完整流程与架构说明见 [docs/HOTUPDATE_GUIDE.md](docs/HOTUPDATE_GUIDE.md)。

## 目录结构

- `Assets/Scripts/Main` — 主包代码：启动流程、HybridCLR 初始化、热更 DLL 下载与加载
- `Assets/Scripts/HotUpdate` — 热更新代码（`HotUpdate.asmdef`，构建为热更 DLL）
- `Assets/Scripts/Common` — 主包与热更程序集共享的工具代码
- `Assets/Editor` — 打包工具链：代码热更打包、Addressables 初始化与资源打包（菜单 `HotUpdate/`）
- `tools/serve_hotupdate.py` — 本地热更测试服务器（同时服务代码热更与资源热更）

## 用模板新建项目

1. 从本模板创建仓库（GitHub `Use this template`，或 `git clone` 后按需重置历史），文件夹改成自己的项目名
2. 用 Unity **6000.3.19f1** 或更高版本打开项目
3. 菜单 `HybridCLR/Installer` → Install（自动下载与本机 Unity 版本匹配的 hybridclr/il2cpp_plus 并安装到本地，无需手动管理源码）
4. 菜单 `HybridCLR/Generate/All` 一键执行必要的生成操作
5. `ProjectSettings` → Player → Product Name 改成自己的项目名
6. 按 [docs/HOTUPDATE_GUIDE.md](docs/HOTUPDATE_GUIDE.md) 初始化 Addressables 并验证「代码热更 + 资源热更」闭环

## HybridCLR 版本说明

- 本模板锁定 `com.code-philosophy.hybridclr#v9.0.1`，对应 **Unity 6000.3.x**。v9 起安装器自动管理 hybridclr/il2cpp_plus 源码版本（下载到 `Library/HybridCLR/`），不再使用 git 子模块。
- **HybridCLR 版本必须与 Unity 版本严格匹配**：il2cpp 补丁按 Unity 小版本维护（如 `v6000.3.x-9.0.1`）。版本不匹配时编辑器一切正常，但真机会在热更初始化阶段直接崩溃（global-metadata 版本断言失败），且日志几乎没有线索。
- 升级 Unity 版本时：同步升级 `Packages/manifest.json` 中 hybridclr_unity 包的版本标签，然后重跑 `HybridCLR/Installer` 与 `HybridCLR/Generate/All`，并重新出包验证。

## 打包流程

详细步骤见 [docs/HOTUPDATE_GUIDE.md](docs/HOTUPDATE_GUIDE.md)，核心链路：

- 运行菜单 `HotUpdate/1. Build HotUpdate Package (一键打包)` 生成代码热更包（内部执行 Generate/All → 编译 DLL → 拷贝 AOT 元数据 → 生成服务器输出）
- 运行菜单 `HotUpdate/Addressables/2. Build Addressables (一键打包资源)` 生成资源热更包（标记 DemoCube/MainMenu → 构建 → 拷贝到服务器目录）
- `python tools/serve_hotupdate.py` 启动本地服务器（默认 8080，客户端已固定走 `127.0.0.1` 并绕过系统代理）
- 启动客户端，`GameBootstrap` 自动完成六步流程：初始化 → 代码更新 → 执行热更入口 → Addressables 初始化 → 资源更新 → 远程加载场景
