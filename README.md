# FG_Tool — 烽火光猫助手

联通 HG6371F（烽火 FiberHome 系从猫）超管密码与 telnet 一键获取工具。

对应文章：[联通 HG6371F 光猫超密和 telnet 获取：从不做认证的出厂接口，一路拿到 root shell](https://xiaozou123.cn/china-unicom-hg6371f-unauth-factory-api-root-shell/)

---

## 它做什么

把文章里那条手搓的链路压成了几个按钮：

| 按钮 | 背后做的事 |
|---|---|
| **探测设备 & MAC** | 探 80 端口，从本机 ARP 表读网关 MAC，按 MAC 算出 `/fh_tool/api` 的 AES key/iv |
| **一键开启 telnet + 取超管** | 依次调 `GetDevInfo`、`GetAdminAccount`、`TelnetEnable` |
| **凭据区** | 按 MAC 后 6 位算出 telnet 登录口令（`Fh@` + MAC 后 6 位）和 su 提权口令（`hg2x0` + MAC 后 6 位），带复制按钮 |
| **守护：掉线自动重开** | 每 30 秒探一次 23 端口，掉了就自动重调 `TelnetEnable` |

## 怎么用

1. 连上光猫的 WiFi
2. 取到 `FG_Tool.exe`：从 [Releases](https://github.com/dellzou/FG_Tool/releases/latest) 下载，或者直接点仓库里的 [`FG_Tool.exe`](./FG_Tool.exe)
3. 双击运行（地址默认 `192.168.2.1`）
4. 点「探测设备 & MAC」
5. 点「一键开启 telnet + 取超管」，三组凭据自动填进框里
6. 用任意终端软件（Windows telnet 客户端 / MobaXterm / PuTTY）按框里凭据登录，再 `su` 提权

运行环境：Windows 10/11，依赖系统自带 .NET Framework 4.x，不需要装 Python 或其他运行时。

## 限制

- 只对烽火这一套有效（HG6371F / HG5145D 这类从猫），因为它依赖 `/fh_tool/api` 接口和「MAC 派生密钥」规则，换牌子、换固件都不成立。
- 超管栏能否读到取决于固件里有没有 `GetAdminAccount`。没有会提示无响应，此时直接试出厂默认的 `CUAdmin / CUAdmin`。
- 不改固件、不刷机，但确实会连设备、会开 telnet。

## 从源码编译

不依赖 Visual Studio，用系统自带的 C# 编译器：

```powershell
powershell -ExecutionPolicy Bypass -File .\build_exe.ps1
```

产物是当前目录下的 `FG_Tool.exe`。

> **关于源码的说明**：原始 `FgTool.cs` 已遗失，仓库里的 `FgTool.cs` 是由 v1.0 的 `FG_Tool.exe`（SHA256 `bc25c48a2d487eb07a44b69b0de7083c5dd80c6dc497b8facf9a5387ba33a393`）反编译还原、人工整理而来，行为与 UI 与 v1.0 一致。反编译产物未经原作者逐行复核，发现差异请开 issue。

## 免责声明

本工具只做文章里那些接口调用，用于**自有设备**的取证与研究。请勿用于任何你无权操作的设备或网络。使用后果由使用者自行承担。

## License

MIT
