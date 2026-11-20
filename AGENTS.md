# shiftgoods 项目协作

## Unity MCP

- 在 shiftgoods 工程中工作。Unity 版本以 ProjectSettings/ProjectVersion.txt 为准。
- 仓库通过 `Packages/manifest.json` 和 `Packages/packages-lock.json` 固定与 Shift 相同版本的 MCP Unity 插件。个人端口、路径和 AI 客户端连接保存在 Git 忽略的本机文件中。
- 插件首次打开会自动尝试使用默认端口 8090。若已有其他 Unity 工程使用该端口，或暂时无需启用 MCP，可在首次打开前在本机创建 `ProjectSettings/McpUnitySettings.json`，写入 `{"AutoStartServer": false}`。已有的本机设置可继续使用。
- 使用 MCP 时，在 `Tools > MCP Unity > Server Window` 为本工程选择本机可用端口，保持 `Allow Remote Connections` 关闭，再启动服务。同一机器同时打开多个 Unity 工程时，每个工程使用各自的端口。
- Codex 用户可以在窗口里选择 `Codex CLI (Project)`，将连接写入本工程的 `.codex/config.toml`，然后从受信任的工程目录打开 Codex 会话。确认 `MCP_UNITY_SETTINGS_PATH` 指向当前工作副本的 `ProjectSettings/McpUnitySettings.json`。其他客户端也各自指向当前工作副本。
- 提交前检查暂存列表，避免强制加入本机连接文件。共享接入说明放在本文件，端口、绝对路径和凭证留在个人配置中。
- 操作编辑器前先核对 MCP 指向 shiftgoods，并读取活动场景及 Console。当前会话若连着其他 Unity 工程，按本工程配置建立连接后再发送编辑命令。
- 优先使用 Unity MCP 读取、编辑和验证场景、GameObject、组件、Prefab 与材质。涉及本地脚本和其他项目文件时，按任务直接操作对应文件。
