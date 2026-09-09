# Plain Craft Launcher (PCL) 愚者玩家修改版

这是第三方基于 PCL 独立制作的玩家修改版，与 PCL 官方及《愚者》制作组无隶属关系。

源码仓库：https://github.com/ishidadao/PCL-Fool

本仓库已按 PCL 分发许可公开。任何后续编译版本都必须能够对应到本仓库中公开的完整修改源码。

PCL 上游源码：https://github.com/Meloong-Git/PCL

当前 MVP 在玩家选择“此服务器提供自动更新”时，从输入的服务器主机获取签名发现文档；服务器返回清单地址与实际联机地址。PCL 验证发现文档及清单、检查 Minecraft 与 Forge 版本、按 SHA-256 同步受管客户端文件、备份被替换或删除的文件，并在成功后连接签名返回的地址。未选择自动更新的服务器只进行普通直连。

上游项目及原作者信息见仓库的 `README.md`、`LICENCE` 与启动器“关于与鸣谢”页面。分发编译版本时必须同时公开与该版本对应的完整修改源码，并继续遵守 `LICENCE` 中的合理使用指南及其所指向的《PCL 分发有限许可》。

服务器端发布工具与格式说明位于 `FoolServerTools/` 和 `docs/`。
