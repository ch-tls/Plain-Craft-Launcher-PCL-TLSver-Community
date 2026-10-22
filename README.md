# Plain Craft Launcher (PCL) - TLSver 社区修改版（非官方）

本项目是第三方基于 [Plain Craft Launcher](https://github.com/Meloong-Git/PCL) 制作的免费、非官方修改版，与 PCL 官方及开发团队没有隶属关系。

## 主要改动

- 恢复 Minecraft 游戏下载与安装功能。
- 支持在下载页配置自己的 CurseForge API Key。
- 保留 Modrinth、CurseForge 资源搜索与自定义文件下载。
- 支持 Microsoft 授权码登录，并提供设备代码登录作为兼容方式。
- 保持官方蓝色主题，不提供赞助门槛、赞助解锁或收费功能。

Minecraft 正版登录需要 Client ID 通过 Minecraft Services 的 AppID 审核；审核通过前，登录可能返回 `Invalid app registration`。

## 构建

需要 Windows、Visual Studio 2022、.NET Framework 4.8 开发工具和 .NET SDK。

```powershell
git submodule update --init
dotnet restore "Plain Craft Launcher 2.sln"
```

随后使用 Visual Studio 打开解决方案并构建，或使用本机 MSBuild 构建主项目。编译产物、运行日志和本地发布压缩包不会提交到仓库。

## 许可与致谢

PCL 原项目及其相关权利归原作者所有。使用、修改和分发前请阅读 [LICENCE](LICENCE)。

- 原作者：龙腾猫跃
- 官方项目：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)
