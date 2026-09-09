# Plain Craft Launcher (PCL) 愚者玩家修改版

[中文](#中文说明) · [English](#english)

> This is an independent, third-party modification of Plain Craft Launcher (PCL). It is not developed, approved, or supported by the PCL developers or by the creators of *The Fool* modpack.
>
> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.

## 中文说明

### 项目用途

这是一个面向小型 Minecraft Java Edition 玩家群体的 PCL 二次开发项目。它保留 PCL 的常规单人游戏与多人服务器启动能力，并为管理员指定的《愚者》服务器增加受控的客户端同步流程。

主要功能：

- 在启动器中选择单人游戏，或保存并选择任意多人服务器地址；
- 每条服务器记录可单独选择是否提供自动更新；未选择时只执行普通直连；
- 选择自动更新时，从玩家输入的服务器主机获取签名发现文档，由服务器返回清单地址和实际联机地址；
- 仅在发现文档和清单都通过内置公钥验证、且玩家首次明确确认后执行同步；
- 校验由服务器管理员签名的清单、Minecraft/Forge 版本以及每个受管文件的 SHA-256；
- 更新 `mods`、`config`、`defaultconfigs`、`kubejs` 等受管内容前创建恢复备份；
- 清单、签名或下载校验失败时停止启动，避免带着不完整的客户端进入服务器。

同步协议和服务端生成工具分别见 [docs/fool-manifest-v1.md](docs/fool-manifest-v1.md) 与 [FoolServerTools/README.md](FoolServerTools/README.md)。

### Microsoft 登录状态

本修改版使用 Microsoft OAuth 设备代码流程登录。Azure 应用注册名称为 `PCL-Fool`，Application (Client) ID 为 `fceae039-821d-4a70-9fc0-1b5de3f90992`。Client ID 是公开标识符，不是客户端密码；构建时由 GitHub Actions 的 `CLIENT_ID` 配置注入。

目前该应用正在申请 Minecraft Java Services API 审核。在 Microsoft/Minecraft 批准 AppID 之前，Microsoft 账号本身可能已成功登录，但后续 Minecraft Services 请求仍会被拒绝。这个限制不是用户密码、电脑、代理或 IP 地址造成的，也不会通过反复登录解决。

本项目不会要求玩家把 Microsoft 密码、验证码、访问令牌或刷新令牌交给服务器管理员。

### 隐私与安全

本修改版没有新增独立的账号系统或用户数据库。Microsoft 登录凭据由启动器保存在玩家本机，并只用于相应的 Microsoft/Xbox/Minecraft 登录流程；《愚者》同步服务不接收这些凭据。访问清单、下载文件或连接 Minecraft 服务器时，服务器及其网络服务商仍会像普通网络服务一样看到 IP 地址和基础访问日志。

完整说明见 [PRIVACY.md](PRIVACY.md)，安全问题报告方式见 [SECURITY.md](SECURITY.md)。请勿在公开 Issue 中粘贴密码、令牌、私钥或完整登录日志。

### 构建

项目沿用 PCL 上游的 Visual Basic/.NET Framework 工程结构，可使用 Visual Studio 2022 与 MSBuild 构建。仓库内的 GitHub Actions 工作流会分别生成 Debug 和 Release 构建产物。开源构建需要提供自己的公开 OAuth Client ID；仓库不包含客户端密码、签名私钥或服务器管理凭据。

### 上游、署名与许可

本项目是基于 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL) 的重度使用与独立二次创作。PCL 原作者为龙腾猫跃；请通过[爱发电](https://meloong.com/afd/a/LTCat)支持原作者。PCL 的下载、帮助和社区信息请以其[官方仓库](https://github.com/Meloong-Git/PCL)为准。

本仓库继续使用根目录的 [LICENCE](LICENCE) 作为分发许可与合理使用指南。二进制分发必须能够对应到公开的完整修改源码，并继续满足该许可的名称、署名、正版购买劝导和界面等要求。

问题与建议可通过本仓库 [Issues](https://github.com/ishidadao/PCL-Fool/issues) 提交；涉及安全或隐私的数据请先阅读 [SECURITY.md](SECURITY.md)。

## English

### Purpose

This repository contains an independent PCL modification for a small Minecraft Java Edition player community. It retains ordinary single-player launching and saved multiplayer server connections while adding an administrator-controlled client synchronization flow for one designated *The Fool* server.

Key behavior:

- players can select single-player or save and select a multiplayer server in the launcher;
- each saved server can independently be marked as providing automatic updates; unmarked servers use ordinary direct connection only;
- for a marked server, the launcher derives a discovery endpoint from the player-entered host, and the signed discovery document returns both the manifest URL and actual Minecraft connection address;
- synchronization runs only after both discovery and manifest signatures pass verification and the player explicitly confirms the first binding;
- the launcher verifies an administrator-signed manifest, the Minecraft/Forge versions, and the SHA-256 of every managed file;
- managed content such as `mods`, `config`, `defaultconfigs`, and `kubejs` is backed up before replacement or removal;
- invalid signatures, incompatible versions, and failed downloads stop the launch instead of leaving a partially updated client.

See [docs/fool-manifest-v1.md](docs/fool-manifest-v1.md) for the protocol and [FoolServerTools/README.md](FoolServerTools/README.md) for the server-side publishing tool.

### Microsoft authentication status

The launcher uses the Microsoft OAuth device-code flow. Its Azure app registration is named `PCL-Fool`, with Application (Client) ID `fceae039-821d-4a70-9fc0-1b5de3f90992`. A Client ID is a public identifier, not a client secret, and is injected by the `CLIENT_ID` GitHub Actions configuration during builds.

The application is currently being submitted for Minecraft Java Services API review. Until Microsoft/Minecraft approves the AppID, Microsoft account authentication may complete successfully while the subsequent Minecraft Services request is denied. Repeated login attempts or changes to a user's password, computer, proxy, or IP address do not resolve an unapproved AppID.

Players are never asked to provide a Microsoft password, verification code, access token, or refresh token to the server administrator.

### Privacy and security

This modification does not add a separate account service or user database. Microsoft authentication credentials remain on the player's device and are used only in the corresponding Microsoft/Xbox/Minecraft authentication flow; they are not sent to the *The Fool* synchronization service. As with any network service, the manifest host, file host, and Minecraft server can observe IP addresses and basic request logs when contacted.

See [PRIVACY.md](PRIVACY.md) for details and [SECURITY.md](SECURITY.md) for security reporting. Do not post passwords, tokens, private keys, or complete authentication logs in a public issue.

### Building

The project retains PCL's Visual Basic/.NET Framework project structure and can be built with Visual Studio 2022 and MSBuild. The included GitHub Actions workflow produces Debug and Release artifacts. Open-source builds must provide their own public OAuth Client ID. This repository does not contain client secrets, signing private keys, or server-administration credentials.

### Upstream, attribution, and licence

This is a substantial-use, independent derivative of [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL). PCL was created by 龙腾猫跃 (LTCat); please support the original developer through [Afdian](https://meloong.com/afd/a/LTCat). Refer to the [upstream repository](https://github.com/Meloong-Git/PCL) for official PCL downloads, help, and community information.

The repository retains [LICENCE](LICENCE), the PCL limited distribution licence and fair-use guide. Any binary distribution must remain traceable to its complete public modified source and continue to satisfy the naming, attribution, genuine-purchase encouragement, and user-interface requirements in that licence.

Use this repository's [Issues](https://github.com/ishidadao/PCL-Fool/issues) for ordinary questions. Read [SECURITY.md](SECURITY.md) before sharing security- or privacy-sensitive details.
