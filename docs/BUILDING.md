# 构建与自检

## 环境

- Windows x64。
- 本次验证的 .NET SDK 为 9.0.301，项目目标是 net8.0-windows；global.json 允许相同特性带内的补丁更新。其它 SDK 需另外验证。
- WebView2 Runtime；应用不包含该系统浏览器运行时。
- 可访问 NuGet 官方源；依赖版本记录在项目文件及 packages.lock.json。
- 安装器需要 NSIS 3.12。便携编译器从 https://nsis.sourceforge.io/Download 获取，不需要把编译器提交进仓库。

从仓库根目录：

```powershell
dotnet restore .\source\SideDock.csproj --locked-mode
dotnet build .\source\SideDock.csproj -c Release --no-restore
dotnet publish .\source\SideDock.csproj -c Release -r win-x64 --self-contained true -o .\portable
```

公开版 portable 输出需要携带项目及第三方许可材料；Build-Installer.ps1 会复制这些材料。若只分发 portable，可先执行安装器构建步骤，或自行复制根目录 LICENSE、NOTICE、THIRD_PARTY_NOTICES.md 及 third-party/。

```powershell
.\source\installer\Build-Installer.ps1 -MakeNSIS 'C:\Tools\NSIS\makensis.exe'
```

安装包写入 `dist/`。卸载文件清单由脚本生成，不需手工编辑。不要在待分发 portable 中保留测试输出。

```powershell
.\portable\SideDock.exe --smoke-test
.\portable\SideDock.exe --startup-check
```

启动后需等待进程退出，再查看 smoke-result.json / startup-result.json。自检使用程序目录中的独立 smoke-profile，不修改正式用户配置；本地测试站点使用 18643 端口。不要在真实用户数据目录运行测试或把测试结果目录打包。

`--online-smoke` 可配合 `--smoke-test` 检查外部网页；它依赖网络、网页行为和测试断言，不属于离线必需检查。

本次测试在本机 Windows 上执行，不代表所有 Windows 版本或真实站点登录均已验收。安装器 `/TESTMODE` 可隔离验证文件安装卸载，不创建正式快捷方式或注册项。
