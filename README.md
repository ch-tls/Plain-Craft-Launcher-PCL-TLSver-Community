# Plain Craft Launcher (PCL) - TLSver 社区修改版（非官方）

本项目是第三方基于 [Plain Craft Launcher](https://github.com/Meloong-Git/PCL) 制作的免费、非官方修改版，与 PCL 官方及开发团队没有隶属关系。

> 当前状态：第一开发版，尚未正式发布。详细开发进度见项目清单。

## 主要改动

### 客户端与下载

- 恢复 Minecraft 游戏下载与安装功能。
- 支持在下载页配置自己的 CurseForge API Key（掩码输入、本地加密保存，只会发送给 CurseForge API）。
- 保留 Modrinth、CurseForge 资源搜索与自定义文件下载。

### Microsoft 正版登录

- 支持 Microsoft 授权码 + PKCE 登录，使用 `localhost:2350` 本地回调，并提供设备代码登录作为兼容方式。
- 已打通 Microsoft → Xbox Live → XSTS → Minecraft Services 完整认证链，Refresh Token 使用 v2 端点刷新。
- 内置公开的 Microsoft Client ID，不包含也不分发 Client Secret。

### 服务器核心与下载

- 支持 Vanilla、Paper、Purpur、Fabric、Forge、NeoForge 服务端核心下载与安装。
- 支持 MSLMC v4 镜像核心下载，包括同时支持插件与 Mod 的混合服务器（请求串行 + 列表缓存）。
- 统一服务器目录 `PCL\Servers\<服务器名称>`，自动检测候选核心 JAR，支持多核心下拉选择。
- 下载时按来源提供的 SHA-1 / SHA-256 / MD5 校验文件完整性。

### 服务器插件

- `下载 → 服务器插件` 支持从多个来源搜索并下载插件：
  - Modrinth 官方接口；
  - 麦块开放 API（Modrinth / CurseForge / Spigot 三个来源），感谢 [麦块社区](https://bbs.minekuai.com/open-api) 提供接口。
- 按目标服务器的 Minecraft 版本与核心类型筛选兼容文件，下载后可直接放入服务器的 `plugins` 目录。
- Spigot 来源需要登录网页下载，选择时会直接打开资源页面。

### 服务器启动与管理

- 启动页新增「服务器启动与管理」：内存、端口、Java、JVM 参数、服务端参数、本地 Mod 与插件管理。
- 支持扫描本机 Java，并按服务器 Minecraft 版本推荐 Java 主版本。
- 内置服务器文件浏览器与编辑器（黑底彩色语法高亮，支持 YAML / JSON / properties），可直接修改 `server.properties`、`bukkit.yml` 等配置。
- EULA 由用户在软件内明确确认，不自动接受、不提前生成。

### 界面

- 保持官方蓝色主题，标题栏显示 `PCL-TLSver` 文本标识。
- 个性化设置中不再提供主题与隐藏主题选项。
- 不提供赞助解锁、收费功能或启动器自动更新。

## 使用

由于是未正式发布的开发版，目前需要通过源码自行构建。构建完成后运行 `Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe`。

常用入口：

| 功能 | 位置 |
| --- | --- |
| 客户端下载 | 下载 → 原版游戏 |
| 服务器核心下载 | 下载 → 服务器核心 |
| 服务器插件 | 下载 → 服务器插件 |
| CurseForge Key | 下载 → CurseForge Key |
| 服务器启动与管理 | 启动 → 服务器启动与管理 |

## 构建

需要 Windows、Visual Studio 2022 或更高版本、.NET Framework 4.8 开发工具和 .NET SDK。

```powershell
git submodule update --init
dotnet restore "Plain Craft Launcher 2.sln"
```

随后使用 Visual Studio 打开解决方案并构建，或使用本机 MSBuild 构建主项目：

```powershell
msbuild "Plain Craft Launcher 2.sln" /t:Rebuild /p:Configuration=Release
```

编译产物、运行日志和本地发布压缩包不会提交到仓库。

## 已知限制

- Minecraft 正版登录依赖 Minecraft Services 的 AppID 审核；若审核未通过，登录可能返回 `Invalid app registration`。
- 插件下载暂不自动解析依赖，也不做插件更新管理。
- 启动器不会接管已运行的服务器进程，也不内置服务器控制台。

## 许可与致谢

PCL 原项目及其相关权利归原作者所有。使用、修改和分发前请阅读 [LICENCE](LICENCE)。

- 原作者：龙腾猫跃
- 官方项目：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)

服务端下载与插件检索用到的第三方服务：

- [BMCLAPI](https://bmclapi.bangbang93.com)（bangbang93）：镜像源与 Forge 安装工具。
- [MSL 开服器 / MSLMC](https://www.mslmc.cn)：服务端镜像源，本服务由 MSL 开服器提供。
- [麦块社区开放 API](https://bbs.minekuai.com/open-api)：插件检索与下载直链。
- [Modrinth](https://modrinth.com)、[CurseForge](https://www.curseforge.com)、[PaperMC](https://papermc.io)、[Purpur](https://purpurmc.org)：游戏服务端与插件资源。

请勿重新分发 Minecraft、Paper、Purpur 等服务端文件，也请勿把本项目的修改推送到官方 PCL 仓库。
