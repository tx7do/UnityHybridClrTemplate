# UnityHybridClrTemplate

Unity 6 + **HybridCLR 代码热更** + **Addressables 资源热更** 项目模板。

完整流程与架构说明见 [docs/HOTUPDATE_GUIDE.md](docs/HOTUPDATE_GUIDE.md)。

## 目录结构

- `Assets/Scripts/Main` — 主包代码：启动流程、HybridCLR 初始化、热更 DLL 下载与加载
- `Assets/Scripts/HotUpdate` — 热更新代码（`HotUpdate.asmdef`，构建为热更 DLL）
- `Assets/Scripts/Common` — 主包与热更程序集共享的工具代码
- `HybridCLRData/hybridclr_repo`、`HybridCLRData/il2cpp_plus_repo` — HybridCLR 构建依赖（git 子模块）

## 用模板新建项目

1. 从本模板创建仓库（GitHub `Use this template`，或 `git clone` 后按需重置历史），文件夹改成自己的项目名
2. 拉取 HybridCLR 子模块：`git submodule update --init --recursive`
3. 用 Unity **6000.3.19f1** 或更高版本打开项目
4. 菜单 `HybridCLR/Installer` → Install（在本机生成 `HybridCLRData/LocalIl2CppData-*`，该目录不入库）
5. 菜单 `HybridCLR/Generate/All` 一键执行必要的生成操作
6. `ProjectSettings` → Player → Product Name 改成自己的项目名
7. 按 [docs/HOTUPDATE_GUIDE.md](docs/HOTUPDATE_GUIDE.md) 初始化 Addressables 并验证「代码热更 + 资源热更」闭环

## HybridCLR 子模块

项目初始化教程：[快速上手](https://hybridclr.doc.code-philosophy.com/docs/beginner/quickstart)

git添加子模块

```bash
git submodule add https://github.com/focus-creative-games/hybridclr.git HybridCLRData/hybridclr_repo
git submodule add https://github.com/focus-creative-games/il2cpp_plus.git HybridCLRData/il2cpp_plus_repo
```

git更新子模块

```bash
git submodule update --init --recursive
```

## 打包流程

详细步骤见 [docs/HOTUPDATE_GUIDE.md](docs/HOTUPDATE_GUIDE.md)，核心链路：

- 运行菜单 `HybridCLR/Generate/All` 一键执行必要的生成操作
- `HybridCLRData/HotUpdateDlls` 下的热更新 dll 与 `HybridCLRData/AssembliesPostIl2CppStrip` 下的补充元数据 dll，随 Addressables/热更资源系统分发
- 启动时 `GameBootstrap` 自动完成：初始化 → 校验 → 加载热更 DLL → 远程加载场景
