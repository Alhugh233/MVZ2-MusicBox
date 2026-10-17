语言：[English](README.md) | 简体中文

# MusicBox

![Vibe Coding](https://img.shields.io/badge/Vibe%20Coding-100%25-blueviolet)

面向 **MVZ2 (Minecraft VS Zombies 2) / MVZ2Expand** 的 BepInEx 6 (IL2CPP) 音乐扩展 Mod。

它允许玩家添加自定义音乐包、让音乐室恒常可见，并可按场景自定义关卡 / 地图背景音乐。

> **从 v1.2 起，本 Mod 运行时依赖 [LanguagePackEnhance](https://github.com/Alhugh233/MVZ2-LPE)（LPE）。** v1.1 及更早版本可独立运行。

> **从 v1.3 起，本 Mod 适配 MVZ2 0.7.X 底层。** 对 EXPAND 等改版的支持将在其源码更新后于后续版本恢复。

## 目录

- [概述](#概述)
- [Vibe Coding](#vibe-coding)
- [功能](#功能)
- [安装](#安装)
- [使用](#使用)
- [本地化](#本地化)
- [数据格式](#数据格式)
- [从源码构建](#从源码构建)
- [兼容性](#兼容性)
- [已知限制](#已知限制)
- [开发说明](#开发说明)
- [致谢](#致谢)
- [许可](#许可)

## 概述

MusicBox 通过 BepInEx 在运行时注入游戏，提供：

- 玩家自定义音乐包（打包方式类似语言包）；
- 音乐室恒常可用，忽略进度解锁；
- 在游戏「插件」界面管理音乐包；
- 在音乐室中按场景配置战斗 / 地图背景音乐。

## Vibe Coding

本项目采用 **Vibe Coding** 开发：代码与文档主要由向 AI 助手提问生成，部分代码为 AI 生成。请在使用前自行审查与测试。

## 功能

- **自定义音乐包**：放入 `music_packs/`；文件夹或 `.zip` 均可，音频支持 `.ogg`（Vorbis/Opus）、`.wav`（PCM / IEEE float）与 `.mp3`。
- **音乐室恒常可用**：绕过进度解锁（只读检查，不修改存档）。
- **音乐包管理**：在「插件」界面导入 / 导出 / 删除 / 排序。
- **自定义战斗音乐**：
  - 章节（主线）战斗音乐，含梦境 / 梦魇等变体；
  - Boss 关卡的分阶段音乐；
  - 章节地图 BGM，按地图预设区分（如梦境 / 梦魇地图）。
- **双轨支持**：地图 BGM 可先播放主轨，再接着副轨循环。
- **异步解码 + 磁盘缓存**：曲目在后台线程解码并缓存到磁盘（PCM），后续启动可完全跳过 OGG 解码。
- **动态场景解析**：章节 / Boss 关卡 / 地图预设运行时从游戏数据枚举，且只显示当前存档已解锁的章节。

## 安装

1. 确认游戏已安装 **BepInEx 6 (IL2CPP)**。
2. 安装 **[LanguagePackEnhance](https://github.com/Alhugh233/MVZ2-LPE)**（硬依赖）。MusicBox 的自身界面文本与自定义曲目元数据都经由它做本地化。
3. 把以下 5 个 DLL 复制到 `BepInEx/plugins/`（同一目录）：

   - `MusicBox.dll`
   - `NVorbis.dll`
   - `Concentus.dll`
   - `Concentus.Oggfile.dll`
   - `NLayer.dll`

4. 启动游戏。日志出现 `Plugin com.mvz2.musicbox is loaded!` 即安装成功。

## 使用

### 1. 添加自定义音乐包

把音乐包放到：

```
<游戏数据目录>/music_packs/
```

每个音乐包是**一个文件夹**或**一个 `.zip`**（允许再嵌套一层文件夹）。格式见[数据格式](#数据格式)。

下次启动会自动加载；也可以在「插件」界面导入 zip。

### 2. 在「插件」界面管理音乐包

从主菜单打开「插件」界面，会出现一个「音乐包」按钮（外观复用语言包按钮）。在那里可以：

- 导入 `.zip` 音乐包；
- 导出或删除音乐包（删除会先确认）；
- 上移 / 下移（只影响自定义曲目在音乐室中的相对顺序）。

### 3. 配置战斗 / 地图 BGM

进入**音乐室**，点击地图选项风格的按钮打开配置：

1. 选择章节；
2. 选择要配置的项：章节音乐（各变体）/ 地图音乐（各预设）/ 各 Boss 阶段；
3. 选择原曲或自定义曲目。

每个槽位只能选「该场景的原始音乐」或「一个自定义曲目」，不能选其它场景的内置音乐。

## 本地化

MusicBox 接入了 **[LanguagePackEnhance](https://github.com/Alhugh233/MVZ2-LPE)**（LPE）。有两类文本被本地化：

1. **MusicBox 自身界面文本**（按钮、表头、提示、弹窗）：以内嵌 LPE 语言包（`en-US`）随 Mod 提供。第三方语言包（优先级高于 Mod 自带）可以翻译 / 覆盖，因此翻译者只需提供一个包含 Mod 原文的语言包即可。

2. **自定义曲目元数据**（音乐室与 MusicBox 自己页面里显示的名称 / 描述 / 作者 / 来源 / 原曲）：来自每个音乐包自带的 `languages/<lang>.json`（见[数据格式](#数据格式)）。MusicBox 在加载时把它们注册给 LPE，游戏使用与内置曲目相同的 `music.*` 本地化 context 进行查找。缺译文时回退原文。

各处查找优先级：**导入的第三方语言包 > Mod 自带语言包 > 游戏内置 > 原文**。

> 不熟悉语言包制作？参考官方模板与教程：**Cuerzor/MVZ2LanguagePack** — <https://github.com/Cuerzor/MVZ2LanguagePack>。

## 数据格式

### 音乐包

```
music_packs/<包名>/      或 <包名>.zip
    pack.json                 { name, author, description, dataVersion }
    songs.json                { "songs": [ { ... } ] }
    *.ogg / *.wav / *.mp3
```

`pack.json`：

```json
{
    "name": "包名",
    "author": "作者",
    "description": "描述",
    "dataVersion": 1
}
```

`songs.json`：

```json
{
    "songs": [
        {
            "id": "song_1",
            "name": "曲目名",
            "file": "song_1.ogg",
            "subFile": "song_1_sub.ogg",
            "source": "来源",
            "origin": "原曲",
            "author": "作者",
            "description": "评测文本，可多段。\n\n空行分段。"
        }
    ]
}
```

字段说明：

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `id` | 是 | 包内唯一；缺失 / 重复会**整包拒绝** |
| `name` | 否 | 音乐室显示名；允许重复 |
| `file` | 是 | 主轨文件：`.ogg` / `.wav` / `.mp3` |
| `subFile` | 否 | 副轨文件：`.ogg` / `.wav` / `.mp3` |
| `source` / `origin` / `author` / `description` | 否 | 音乐室信息与评测文本 |

### languages/&lt;lang&gt;.json（可选）

每个音乐包可以为自己的曲目提供翻译，**每种语言一个文件**，放在 `languages/` 目录下：

```
music_packs/<包名>/
    pack.json
    songs.json
    languages/
        en-US.json
        ja-JP.json
    *.ogg / *.wav / *.mp3
```

```json
{
    "song_1": {
        "name": "曲目名（译文）",
        "description": "评测文本（译文）",
        "author": "作者",
        "source": "来源",
        "origin": "原曲"
    }
}
```

- key 是 `songs.json` 里的曲目 `id`；每个字段都可选（只翻译列出的字段）。
- `songs.json` 里的中文即原文；`languages/<lang>.json` 提供译文。
- `zh-Hans` 可省略（回退原文）。
- 这些译文通过 LPE 的 `music.name` / `music.description` / `music.author` / `music.source` / `music.origin` context 注册。

### battle_music.json

由 Mod 界面自动读写（存放于 `music_packs/battle_music.json`），玩家一般无需手动编辑：

```json
{
    "areas":  { "mvz2:dream_level": "musicbox:xxx", "mvz2:nightmare_level": "musicbox:yyy" },
    "maps":   { "mvz2:dream_map": "musicbox:zzz" },
    "stages": { "mvz2:halloween_11": ["", "musicbox:yyy"] }
}
```

- `areas`：章节战斗音乐，按**原版战斗曲目 ID** 为键；
- `maps`：地图 BGM，按**地图预设音乐 ID** 为键；
- `stages`：Boss 关卡各阶段；数组下标为阶段，空字符串回退原曲；
- 缺失 / 为空 = 使用原曲。

## 从源码构建

需要 `.NET SDK`（目标 `net6.0`）。

1. 把目标版本的游戏 **interop DLL** 放入仓库的 `interop/` 目录（该目录已在 `.gitignore` 中），或用 `-p:InteropDir=` 指定。

2. 编译所用的 `LanguagePackEnhance.API.dll` 位于 `libs/`（来自 [MVZ2-LPEAPI](https://github.com/Alhugh233/MVZ2-LPEAPI)，API v1.1.0，已随仓库提供）。如需更新，替换为新版或通过 `-p:LpeDir=` 指定。

3. 构建：

```bash
dotnet build
# 或指定 interop 目录
dotnet build -p:InteropDir=/path/to/interop_mvz2
```

产物在 `bin/Debug/net6.0/`。部署时把 `MusicBox.dll`、`NVorbis.dll`、`Concentus.dll`、`Concentus.Oggfile.dll`、`NLayer.dll` 一起放好。

### 依赖

- `BepInEx.Unity.IL2CPP`、`BepInEx.PluginInfoProps`
- `NVorbis`（Vorbis 解码）
- `Concentus.Oggfile` / `Concentus`（Opus 解码）
- `NLayer`（MP3 解码）
- **[LanguagePackEnhance](https://github.com/Alhugh233/MVZ2-LPE)**（运行时硬依赖）。编译期从 `libs/` 引用瘦 API `LanguagePackEnhance.API.dll`（[MVZ2-LPEAPI](https://github.com/Alhugh233/MVZ2-LPEAPI)）。

## 兼容性

- 目标框架：`net6.0`（BepInEx 6 IL2CPP / CoreCLR）。
- 插件 GUID：`com.mvz2.musicbox`。
- 依赖 **LanguagePackEnhance**（`com.mvz2.languagepackenhance`）。
- 面向 **MVZ2 0.7.X** 适配（interop：0.7.X 的 interop 集）。EXPAND 等改版将在其源码更新到 0.7.X 底层后恢复支持。
- 已在实机上针对 MVZ2 0.7.X 验证。

## 已知限制

- MusicBox 自身界面文本通过 LanguagePackEnhance 本地化（随 Mod 内嵌 `en-US`）；自定义曲目元数据只有在音乐包提供 `languages/<lang>.json`（或有第三方语言包翻译了 `music.*` context）时才本地化。
- Boss 阶段曲表为静态维护；游戏新增 Boss 时需更新该表。
- 在非音乐室场景（战斗 / 地图），所需自定义曲目会在播放前**同步**解码；全新安装、磁盘缓存未热时，首次可能短暂卡顿。音乐室为后台解码，就绪后会刷新显示的曲目信息（如 `??:??` → 实际时长）。
- 尚未支持音乐包图标（`pack.png`）。
- 战斗 / Boss 音乐的双轨仍沿用游戏原有逻辑（大波次交叉淡入淡出），不适用「先主后副」顺序。

## 开发说明

### 目录结构

```
MusicBox/
  Plugin.cs                     Mod 入口：Harmony、注入 MusicBoxBehaviour、注册内嵌语言包
  MusicBoxBehaviour.cs          注入的 MonoBehaviour（备用启动驱动）
  Core/MusicCore.cs             核心：MiniJson、注册表、音乐包加载/校验/解码 + 缓存清理
  Core/Loc.cs                   本地化辅助（LPE Translate / 游戏 _p）
  Core/MusicLocalization.cs     读取各音乐包的 languages/<lang>.json 并注册给 LPE
  Resources/MusicBox.lang.json  内嵌 LPE 语言包（en-US），用于 MusicBox 自身界面文本
  Patches/MusicHooks.cs         音乐相关 Harmony 补丁
  Patches/StartupLoadPatch.cs   启动时加载音乐包与战斗配置
  Addons/MusicPacksPage.cs      「插件」界面里的「音乐包」按钮 + 管理页
  Battle/BattleMusic.cs         战斗/地图 BGM 配置数据 + 动态枚举 + 运行时解析
  Battle/BattleMusicPage.cs     音乐室配置按钮 + 自绘对话框
  libs/LanguagePackEnhance.API.dll  编译期引用（随仓库提供）
```

### 主要逻辑

- **启动**：`Plugin.Load` 应用 Harmony 并注册注入类型；`Patches/StartupLoadPatch.cs` 在 `MainManager.Initialize` 后加载音乐包与战斗配置。
- **音乐包**：`MusicPackLoader.Reload` 负责扫描 / 解析 / 校验（整包拒绝）并注册 `MusicMeta` 与音频文件路径。音频在后台线程**异步**解码（OGG → PCM，带以源文件大小 + mtime 为键的持久 PCM 缓存），AudioClip 由主线程的 pump（`MusicManager.Update`）创建。非音乐室场景（战斗 / 地图覆盖）会**同步**解码所需曲目以保证播放前就绪。
- **本地化**：MusicBox 依赖 **LanguagePackEnhance**。自身界面文本以内嵌 LPE 语言包提供，通过 `Loc.T`（`LanguagePackEnhanceApi.Translate`）查找；自定义曲目元数据从各音乐包的 `languages/<lang>.json` 读取并注册给 LPE（`LanguagePackEnhanceApi.RegisterTranslations`），再由游戏自身的 `music.*` context 解析。所有注入都插在第三方语言包之下、内置语言包之上。
- **缓存清理**：每次 `Reload` 时 `PruneCache` 会删除孤儿缓存 —— 不再对应任何已加载 clip 的 PCM 缓存文件，以及已被删除包的解压 `.zip` 目录。
- **游戏集成**：补丁 `ResourceManager.GetAllMusicID/GetMusicMeta/GetMusicClip`，把自定义曲目带进音乐室；`LogicSaveExt.IsMusicRoomUnlocked` 恒返回 `true` 以保持音乐室可见。
- **战斗 / 地图 BGM**：`BattleMusic` 运行时枚举章节 / Boss 关卡 / 地图预设，并为当前场景解析自定义曲目。覆盖时**不改变正在播放的 ID**，而是在 `MusicManager.Play`/`SetPlayingMusic` 调用期间把该 ID 的 meta 临时重定向到自定义曲目，从而保留游戏自身逻辑（如梦境/梦魇切换）。
- **双轨**：对地图 BGM，解码时把主、副轨的 PCM 拼接成一个循环 AudioClip。

### Harmony 补丁列表

| 目标 | 类型 | 作用 |
| --- | --- | --- |
| `LogicSaveExt.IsMusicRoomUnlocked` | Prefix | 保持音乐室可见 |
| `ResourceManager.GetAllMusicID` | Postfix | 追加自定义曲目 |
| `ResourceManager.GetMusicMeta` | Prefix | 自定义 meta + 战斗重定向 |
| `ResourceManager.GetMusicClip` | Prefix | 自定义 clip（惰性解码） |
| `MusicRoomController.Display` | Postfix | 预解码曲目 |
| `MusicRoomController.Awake` | Postfix | 安装音乐室配置按钮与音乐包按钮 |
| `AddonsController.Awake` | Postfix | 安装「音乐包」按钮 |
| `LanguagePacksController.Awake` | Prefix | 克隆音乐包页时跳过被克隆的控制器 |
| `LanguagePacksController.OnButtonClickCallback` / `OnPackItemToggledCallback` | Prefix | 在音乐包页上抑制语言包控制器 |
| `LanguagePacksController.Display` | Prefix | 打开语言包页时退出「音乐模式」 |
| `MainManager.Initialize` | Postfix | 启动加载 |
| `MusicManager.Play` / `SetPlayingMusic` | Prefix + Postfix | 战斗/地图 BGM 重定向 |

### 平台注意事项

目标运行在 Android 的 BepInEx CoreCLR 上，JIT 某些框架程序集会立刻以 `SIGABRT`（`libc++abi: terminating`）中止。开发时避免：

- 不要使用 `System.Text.Json`（改用自带的 `MiniJson`）；
- 不要使用 CoreCLR 的 `System.IO.Compression`（改用 interop 的 `Il2CppSystem.IO.Compression`）；
- 不要使用 `System.Uri` / `System.Private.Uri`（手动拼 `file://` 字符串）；
- 尽量避开 `System.Linq` / `System.Globalization`。

其它：

- 该构建下 Unity 的 `UnityWebRequestMultimedia` / `DownloadHandlerAudioClip` 不可用（音频下载 icall 缺失），音频必须手动解码；
- 不要在启动期解码音频（会生成静音的 AudioClip）；
- 「克隆 UI 实例复用外观」时：文本上的 `TextMeshProUGUITranslator` 在启用时会覆盖你设置的文本（先禁用它）；克隆出来的控制器会在 `Awake` 里订阅事件（首次激活时用标志跳过）。

## 致谢

- **MVZ2（原作）** 作者 Cuerzor — <https://github.com/Cuerzor/MinecraftVsZombies2Unity>
- **MVZ2Expand** 作者 Tr-DESE107 — <https://github.com/Tr-DESE107/MinecraftVsZombies2_EXPAND>
- **LanguagePackEnhance** 作者 Alhugh233 — Mod 本地化框架。
- 框架：BepInEx。
- 解码器：NVorbis（Vorbis）、Concentus（Opus）、NLayer（MP3）。

## 许可

版权所有 (C) 2026 Alhugh233。

本项目采用 **GNU 通用公共许可证 v3.0** 授权，完整许可证文本原样收录于 [LICENSE](LICENSE)。

本项目**直接基于两个开源游戏 MVZ2 与 MVZ2Expand**（均为 GPL-3.0）。它是一个通过 BepInEx / Harmony 与游戏互操作的运行时 Mod，不重新分发游戏的程序集。

第三方组件（NVorbis、Concentus、Concentus.Oggfile、NLayer）及其许可声明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
