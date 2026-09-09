# Security Policy / 安全说明

## Reporting / 报告方式

普通缺陷可以提交 GitHub Issue。涉及安全的问题请先联系仓库维护者，并使用其 GitHub 个人资料中列出的非公开联系方式；不要在公开 Issue、截图或日志中包含 Microsoft 密码、验证码、OAuth 令牌、Cookie、私钥或服务器管理凭据。

Ordinary defects may be reported with a GitHub issue. For a security-sensitive report, contact the maintainer first through a non-public method listed on the maintainer's GitHub profile. Do not place Microsoft passwords, verification codes, OAuth tokens, cookies, private keys, or server-administration credentials in a public issue, screenshot, or log.

## Update trust boundary / 更新信任边界

《愚者》受管更新只接受内置公钥验证通过的 RSA/SHA-256 清单。下载文件还必须通过清单中记录的 SHA-256 校验。清单可以更新 Mod、配置、资源与 KubeJS 脚本，因此能够生成有效签名的人等同于受信任的软件发布者；签名私钥必须始终保存在仓库和 Web 目录之外。

The managed *The Fool* updater accepts only manifests that pass RSA/SHA-256 verification with the pinned public key. Every downloaded file must also match its manifest SHA-256. Because a signed manifest can update mods, configuration, resources, and KubeJS scripts, anyone able to create a valid signature is effectively a trusted software publisher. The signing private key must remain outside both this repository and the public web root.

The launcher creates recovery backups before replacing or removing managed files and fails closed on invalid signatures, incompatible Minecraft/loader versions, hash mismatches, or incomplete downloads. Backups are a recovery aid, not a substitute for independent world and client backups.
