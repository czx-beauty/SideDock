# SideDock 0.2.0 · Windows 侧边网站工作台

## 安装和启动

运行 `SideDock-0.2.0-Setup.exe`，默认安装到当前用户的 `%LOCALAPPDATA%\Programs\SideDock`，不需要管理员权限。安装后从桌面或开始菜单启动。系统设置的应用列表中可卸载。

也可解压 `SideDock-0.2.0-win-x64.zip` 后运行 `SideDock.exe`，全部运行库须保留在同一目录。两种方式都是 Windows 10/11 x64 应用，包含 .NET 8 运行时。需要 Microsoft Edge WebView2 Runtime；缺失时从[微软官方页面](https://developer.microsoft.com/microsoft-edge/webview2/)安装。

升级前先在托盘右键菜单退出旧版，再覆盖 / 安装。原版数据仍保存在 `%LOCALAPPDATA%\SideDock`，不随程序目录移动；从 0.1 升级时自动迁移配置，旧版默认 Bing 搜索入口改为 Google，不删除其它入口。

## 功能与操作

1. **触边收回**：触边停留展开，鼠标移进面板后，再移回靠边的屏幕边缘，停留约 160ms 收起。若鼠标仍停在边缘，等展开完成后再向屏幕外推一次，也能通过相对鼠标位移收起；鼠标离开面板也会收起。刚唤出有 600ms 保护，防止同一次动作立即关闭。顶部 `●` 表示固定模式；点击变为 `○` 后启用自动收起。左右逻辑对称，触控板的原始位移支持依硬件而异。
2. **Google 和自定义搜索**：默认 Google。设置里填搜索引擎网址，如 `google.com`、`bing.com`、`duckduckgo.com`、`baidu.com`；自定义服务可写 `https://站点/search?term={query}`。地址栏输入关键词使用该引擎。Google 的可达性取决于本机网络。
3. **单个面板浏览**：普通新窗口链接在面板内打开，不再创建桌面网页窗口。同站页面继续留在原入口。OAuth 登录弹窗在该入口内临时显示，保留 opener 关系；登录页自行关闭会回到原页，或使用 `‹` 返回。不保证所有第三方认证服务接受嵌入式浏览器。
4. **开机自启**：设置勾选“登录 Windows 时自动启动”，保存。之后登录 Windows 会后台待命，触边唤出；不会启动时弹出主面板。取消勾选可关闭。便携版启用后请不要移动程序目录；Windows 启动应用设置也可能禁用该项。
5. **自动固定网站**：设置默认开启。打开 DeepSeek、YouTube 等新网站会创建侧栏入口，重复打开复用现有入口；DeepSeek 的常用子域和 YouTube / youtu.be 归为同一网站。其它网站按主机名归组，普通跨子域可能有各自入口。侧栏点击只切回当前实例，不刷新。网站图标可用时会缓存显示，暂时没有图标则显示名称首字。
6. **持久化和分发**：保存侧栏、最后 URL、历史、浏览器会话和可恢复位置；运行中切换保留网页实例。安装包可分享给其它 Windows x64 用户，每人数据独立。尚未配置独立下载站，也未做代码签名。
7. **贴近原版**：调研和设计依据见 `Slidepad评价与新版设计.md`，以侧边常驻、快速往返和保留上下文为优先。

浏览器退出后重开不是把整个进程内存封存。会话 URL / 网站数据可恢复，任意网站未保存的输入和实时流式内容不能保证重启后重现。

## 操作

- `Ctrl+Alt+Space` 展开 / 收起，`Esc` 收起。
- `Ctrl+L` 地址栏；`Ctrl+T` 添加；`Ctrl+R` 刷新；`Ctrl+H` 历史。
- 左侧 `◷` 查看历史、搜索标题或网址，双击打开。
- 右键网站图标编辑、上移或移除；顶部 `↗` 是主动在默认浏览器打开。
- 拖动边框调整宽度，关闭面板仅收起，托盘右键“退出”才结束进程。

## 数据和恢复

`%LOCALAPPDATA%\SideDock` 下包含 `settings.json`、上一版 `settings.json.bak`、`history.json`、`Browser` 和图标缓存。历史保留最近 10,000 条，每个入口保留最近 200 个后退地址。浏览器身份由 WebView2 保存，不读取你的 Chrome / Edge 主浏览器账号。

配置读取异常会尝试 `.bak`。便笺和登录仍在 Browser 中，备份数据请先退出程序再复制整个数据目录。卸载保留该目录，不自动删除个人记录。

## 构建

安装 .NET 8 或更新 SDK：

```powershell
dotnet publish .\source\SideDock.csproj -c Release -r win-x64 --self-contained true -o .\portable
```

NSIS 3.12 便携编译器执行 `source\installer\SideDock.nsi` 可生成安装包。自检：`SideDock.exe --smoke-test`；`--startup-check` 校验后台启动；`--online-smoke` 可附加外部 HTTPS 检查。自检写程序目录中的独立 `smoke-profile`，不触碰正式用户数据。
