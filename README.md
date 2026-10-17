# Human Computer Use

一个 Windows 桌面视觉效果小工具：放大的鼠标、柔和的紫色光晕，以及顶部的蓝色提示条。

**Human is using your computer · Esc to cancel**

![顶部提示条](docs/banner.png)

软件由你亲自操作电脑，仅显示视觉效果。它不具备 AI 自动操作、远程控制或录屏功能。与 OpenAI、ChatGPT 或 Codex 无隶属或认可关系。

## 下载和使用

在本仓库的 **Releases** 页面下载 `Human-windows.zip`，解压后双击 `Human.exe`。发布包由 GitHub Actions 从对应版本源码编译。

| 操作 | 功能 |
| --- | --- |
| Esc，或点击 **Esc to cancel** | 退出并恢复原鼠标 |
| Ctrl + Alt + H | 暂停 / 恢复效果，暂停会恢复原鼠标 |
| Ctrl + Alt + B | 隐藏 / 显示顶部提示条 |
| Ctrl + Alt + Q | 退出 |
| 拖动提示条文字 | 调整位置 |
| 托盘 H 图标右键 | 打开操作菜单 |

支持 Windows 10 / 11 和 .NET Framework 4.x。无需安装或管理员权限。启动时常用系统鼠标会临时放大到约 150%，普通箭头使用黑色填充与白色轮廓。

![效果示意图](docs/preview.png)

## 运行时会做什么

- 创建置顶、鼠标可穿透的透明光晕窗口，每 16ms 检查鼠标位置。
- 显示状态提示条，注册 Ctrl + Alt 快捷键，通过只识别 Esc 的键盘钩子实现取消；不保存键盘输入。
- 临时替换 13 种常用系统鼠标。独立恢复进程保存启动前的图形和热点，在退出或主进程异常结束后恢复。
- 不写入持久鼠标设置，不添加开机启动，不安装服务，不读取个人文件，不截图、不联网、不上传数据。
- 正常运行有两个进程：界面进程和恢复进程，均使用同一个 `Human.exe`。

公开源码方便检查，不等于无安全问题。当前 EXE 没有 Authenticode 签名，Windows 可能显示未知发布者提示。扫描结果和构建来源证明也不构成无漏洞保证。更多细节见 [SECURITY.md](SECURITY.md)。

## 从源码编译

Windows 自带的 .NET Framework C# 编译器即可构建，无 NuGet 或其他第三方运行库依赖。在项目目录打开 PowerShell：

```powershell
.\build.ps1
```

生成 `dist/Human.exe`。可在本地编辑源码、查看完整构建脚本后自行编译。

## 测试

```powershell
.\test.ps1
```

默认测试不显示界面、不替换当前系统鼠标：检查透明度、原生图层上传、负坐标、状态文案、加粗字体、取消消息，以及多种 DPI 布局。

以下是**可选的交互会话测试**，会短暂放大当前鼠标，并恢复；还会启动并终止专用测试子进程，检查异常退出后的恢复：

```powershell
.\test.ps1 -CursorSession
```

测试报告位于 `test-results/`。默认 CI 运行离屏测试，不注入键盘按键。独占全屏覆盖、不同显示器缩放和所有 Windows 鼠标主题尚未完整验证。

## 核对下载文件的来源

发布文件附 `SHA256SUMS.txt`，可比较：

```powershell
Get-FileHash .\Human.exe -Algorithm SHA256
```

每次正式构建会记录源码提交和工作流编号，并生成 GitHub 构建来源证明。使用 GitHub CLI 验证：

```powershell
gh attestation verify .\Human.exe --repo lernicks0/human-computer-use
```

证明关联的是文件、源码提交和构建流程，不是安全认证。当前编译器输出不保证跨机器逐字节复现；自行编译的哈希可能不同。

## 许可证

[MIT](LICENSE)。
