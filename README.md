# SAGE Unlocked 日冕定制版

红警 3 帧率解锁与单机开局自动视距。当前配置为 120 FPS、1.5 倍视距。进入对局后，扩展会按地图原生距离应用一次缩放，之后仍可用滚轮调整。联网及局域网对局沿用游戏原生视距。

**衍生版本说明（2026-11-22）**：本项目由 SAGE Unlocked 衍生，独立维护。于 2026-11-17 加入日冕单机开局自动视距，于 2026-11-22（北京时间）修复高帧率下帝国即时发电机的发光闪烁。保留上游署名和许可声明，本项目源码按 [GNU GPL 第 3 版](LICENSE)发布；上游文件声明的额外许可选项仍然适用。

这是基于 TheeHorse 的 SAGE Unlocked v1.9.5（`0f443f4`）的独立源码快照，包含自动视距扩展 1.1 和电厂发光修复。上游提交历史请参阅原项目。[上游版本](https://github.com/TheeHorse/SAGE-Unlocked/releases/tag/v1.9.5) · [上游使用说明](README.upstream.md) · [许可证](LICENSE)

帧率核心包含红警 3 1.12 和 1.13 的帝国即时发电机发光修复。高帧率下，粒子清理和着色器会延长同一段显示时间，消除相邻粒子之间的空帧。[上游问题 #29](https://github.com/TheeHorse/SAGE-Unlocked/issues/29)

| 想调整的部分 | 对应文件 |
| --- | --- |
| 帧率、特效计时、最大缩放范围 | [src/Unlocker.cs](src/Unlocker.cs) |
| 开局自动视距的时机和倍率 | [corona/AutoZoom.cs](corona/AutoZoom.cs)、[corona/StartupZoom.cs](corona/StartupZoom.cs) |
| 镜头诊断与本机配置示例 | [corona/TraceHooks.cs](corona/TraceHooks.cs)、[corona/SAGEUnlocked.ini](corona/SAGEUnlocked.ini) |
| 游戏加载器与上游安装器 | [dll/proxy.c](dll/proxy.c)、[installer/setup.iss](installer/setup.iss) |

## 构建

在 Windows 上从仓库根目录运行以下命令。脚本调用 32 位 .NET Framework 自带的 C# 编译器，将产物写入 `corona\build\`。

```powershell
.\corona\Build.ps1
```

| 构建产物 | 在红警 3 的 `Data` 目录中使用的文件名 |
| --- | --- |
| `SAGEAutoZoom.1.1.dll`，自动视距扩展 | `CnCFpsUnlocker.dll` |
| `CnCFpsUnlocker.dll`，帧率核心 | `CnCFpsUnlocker.original.dll` |

## 安装与回退

1. 退出游戏。首次安装先按上游说明装好 SAGE Unlocked 1.9.5，将 `Data\SAGEUnlocked.ini` 和原版 `Data\CnCFpsUnlocker.dll` 备份。保留该版本的 `d3d9.dll` 加载器。[上游 1.9.5 下载](https://github.com/TheeHorse/SAGE-Unlocked/releases/tag/v1.9.5)
2. 按上表安装构建产物。已安装自动视距 1.1 的玩家只需备份并替换 `Data\CnCFpsUnlocker.original.dll` 即可更新帧率核心。
3. 在已有 INI 中设置 `fps=120`、`zoom=1.5`、`autozoom=1`、`autozoomtrace=0`。可按需修改 FPS，30 至 240 之间取 15 的倍数。下次进图后，`SAGEAutoZoom.log` 中的 `startup camera applied` 会记录实际应用距离。

将 `autozoom` 改为 `0` 并重启游戏，即可停用开局缩放。排查粒子效果时，可以在 `skip` 列表中加入 `pfxglow`。仅回退帧率核心时，退出游戏并恢复备份的 `Data\CnCFpsUnlocker.original.dll`。完整回退时，将原版核心复制回 `Data\CnCFpsUnlocker.dll`。已有 1.1 安装记录的电脑也可运行下列脚本，它会核对安装记录中的两个 SHA-256 再恢复原版。

```powershell
.\corona\Restore.ps1 -InstallInfoPath 'X:\备份目录\install-info.json'
```

## 验证

先在仓库根目录创建 Python 环境，再运行验证。脚本会检查粒子清理计时和着色器修改，模拟开局镜头、联网跳过及手动滚轮，并检查核心委托和停用开关。

```powershell
py -m venv .venv
.\.venv\Scripts\python.exe -m pip install unicorn==2.1.4
.\corona\Build.ps1 -Verify -Python .\.venv\Scripts\python.exe
```

如需核对本地游戏版本，可在最后一条命令后加 `-GameFile 'X:\红警3\Data\ra3_1.12.game'`。这一步只读游戏文件。

1.1 版曾在红警 3 1.12、日冕 3.259 的卡巴纳和原始神庙单机遭遇战中实测，两局都在第 3 个逻辑帧将距离从 600 调到 900。第一局随后用滚轮拉近到约 827.48，第二局关闭详细诊断后，经过 17802 次镜头更新仍为 900。红警 3 1.13 已核对游戏文件特征，实机开局记录来自 1.12。[实机验证记录](corona/tests/live-verification-1.1.json)

### 日冕电厂发光对照（2026-11-22）

日冕 3.259，卡巴纳地图，帝国即时发电机建成后固定镜头。同一个构建只切换 `pfxglow`，同出生点及建造位置，各抓取 480 帧。修复开启与关闭的实测帧率分别为 115.8 FPS 和 115.6 FPS，上限均为 120 FPS。

三个发光环的小区域中（11×6 像素），相邻帧平均亮度变化超过约 5（0–255 标度）的区域计数合计从 280 降到 11；这些是区域跃变计数。正常发光仍在。两轮是独立对局，玩家颜色不同，指标只比较各轮内部相邻帧的亮度变化。结果适用于这一实测场景。
