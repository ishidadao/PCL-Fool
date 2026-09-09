# Privacy Notice / 隐私说明

Last updated / 更新日期：2026-09-09

## 中文

Plain Craft Launcher (PCL) 愚者玩家修改版本身不运营独立账号系统或用户数据库，也不会把玩家的 Microsoft 密码、验证码、OAuth 访问令牌或刷新令牌发送给《愚者》同步服务器。

运行启动器时可能发生以下数据处理：

- Microsoft 正版登录由 Microsoft、Xbox 与 Minecraft 的相关服务处理；必要的登录令牌由 PCL 保存在玩家自己的 Windows 环境中。
- 检查《愚者》客户端内容时，启动器会向清单地址和文件下载地址发起 HTTPS 请求。服务提供方可能记录 IP 地址、时间、请求路径、User-Agent 和常规错误信息。
- 直连多人服务器时，服务器会接收完成 Minecraft 连接所需的数据，并可能按照其自身配置记录玩家 UUID、游戏名、IP 地址、登录和游戏日志。
- 上游 PCL 带有可在“设置 → 其他 → 启动器”关闭的匿名错误上报功能；本修改版没有为账号或整合包同步新增独立遥测。
- 账号、服务器列表、整合包状态、备份、日志与崩溃报告主要保存在玩家本机。玩家可以通过 PCL 的账号管理和 Windows 文件管理删除这些本地数据。

Microsoft、GitHub、Minecraft 服务器以及清单/文件托管服务各自按照自己的隐私条款处理网络请求。本项目无法控制这些第三方服务的保留期限。

如需提出普通隐私问题，可使用仓库 Issue，但请删去个人信息。若问题必须包含令牌、IP、完整日志等敏感信息，请勿公开提交，先通过仓库维护者 GitHub 个人资料中提供的非公开联系方式联系维护者。

## English

Plain Craft Launcher (PCL) Fool Player Modification does not operate a separate account system or user database. It does not send a player's Microsoft password, verification code, OAuth access token, or refresh token to the *The Fool* synchronization server.

The following processing can occur while the launcher is used:

- Microsoft account authentication is handled by the relevant Microsoft, Xbox, and Minecraft services. Required authentication tokens are stored by PCL in the player's own Windows environment.
- When checking *The Fool* client content, the launcher makes HTTPS requests to the manifest and file hosts. Those providers may record the IP address, time, requested path, User-Agent, and ordinary error information.
- When directly connecting to a multiplayer server, that server receives the information required by the Minecraft protocol and may record the player's UUID, game name, IP address, login events, and gameplay logs according to its configuration.
- Upstream PCL includes optional anonymous error reporting that can be disabled under Settings → Other → Launcher. This modification adds no separate account or modpack-synchronization telemetry.
- Accounts, server entries, modpack state, backups, logs, and crash reports are primarily stored on the player's device. Players can remove local data through PCL account management and Windows file management.

Microsoft, GitHub, Minecraft servers, and the manifest/file hosting provider process network requests under their own privacy terms. This project does not control those third parties' retention periods.

Ordinary privacy questions may be opened as a repository issue after removing personal information. If a report must contain a token, IP address, or complete log, do not post it publicly; first contact the maintainer through a non-public method listed on the maintainer's GitHub profile.
