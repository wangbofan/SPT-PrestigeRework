# SPT-PrestigeRework

把 SPT 4.1.x 的 **转生（Prestige）** 从「重置角色、从头再来」改成 **等级奖励**：
达到指定等级就能直接领取该阶段的转生奖励，**角色不会被重置**，原版奖励内容一项不少。

另附一个可选客户端插件 **PrestigeSkipTransfer**，用来跳过转生界面里那两页没意义的「确认页 / 物品转移页」。

[English](#english) · [安装](#安装) · [配置](#配置) · [原理](#原理) · [从源码构建](#从源码构建)

---

## 特性

- **转生 1 / 2 / 3 / 4 的解锁条件改为角色等级 15 / 25 / 35 / 45**（可在 config.json 里改）。
- 原版那些又臭又长的条件（55 级、两个任务、力量/耐力/魅力、藏身处区域、2000 万卢布）默认全部去掉。
- 领取后：
  - 角色**不重置**、不清档、不进入角色重建流程；
  - 按原版规则发放该阶段的全部奖励：外观解锁（皮肤/手势/狗牌等）、技能点、物品邮件、额外每日任务；
  - 解锁对应的转生成就；
  - 写入 `PmcData.Prestige[阶段Id]` 与 `Info.PrestigeLevel`，客户端刷新存档后即可看到转生等级。
- **只发放刚达到的这一阶**，不会把之前几阶的奖励再补一遍。
- 每个阶段只能领一次，必须按 1 → 2 → 3 → 4 顺序领取。
- 客户端插件：点「转生」后直接发请求并播放转生动画，确认页、物品转移页都不再出现。

## 安装

### 方式 A：整合包（推荐）

下载 Release 里的整合包 zip，解压后把里面的 **`SPT_Runtime`** 和 **`BepInEx`** 两个文件夹
拖到**游戏根目录**（同时包含 `EscapeFromTarkov.exe` 和 `SPT_Runtime` 的那一层），提示合并时选「是」。

装好后的位置：

```
<游戏根目录>\SPT_Runtime\user\mods\PrestigeRework\PrestigeRework.dll
<游戏根目录>\BepInEx\plugins\PrestigeSkipTransfer\PrestigeSkipTransfer.dll
```

### 方式 B：只装服务端模组

把 `PrestigeRework` 文件夹放到 `SPT_Runtime\user\mods\PrestigeRework\`：

```
SPT_Runtime\user\mods\PrestigeRework\
    PrestigeRework.dll
    config\config.json
```

启动服务端后，控制台出现下面这行即表示生效：

```
[PrestigeRework] Loaded: prestige level requirements set to 15 / 25 / 35 / 45, character wipe disabled.
```

不装客户端插件也能正常用，只是转生界面还是原版那三页（确认页 → 物品转移页 → 动画），
物品转移页因为不需要转移任何东西，直接点「下一步」即可。

## 配置

### 服务端 `config\config.json`

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `Enabled` | `true` | 总开关。`false` 时完全恢复原版转生（会重置角色）。 |
| `RequiredLevels` | `[15, 25, 35, 45]` | 转生 1~4 各自需要的等级。某项缺失或 ≤0 时使用模板原值。 |
| `KeepOnlyLevelCondition` | `true` | `true`：只保留等级条件；`false`：保留原版全部条件，仅下调等级要求。 |
| `DisableItemTransfer` | `true` | 清空物品转移栏的筛选，让转移页无物可选（角色不重置，转移没有意义）。 |
| `GrantAchievements` | `true` | 是否发放转生成就。 |
| `RewardMailStorageSeconds` | `31536000` | 奖励物品邮件的保留时间（秒，默认 1 年）。 |

### 客户端 `BepInEx\config\com.wangbofan.prestigerework.skiptransfer.cfg`

首次运行后生成：

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `SkipConfirmation` | `true` | 跳过确认页：打开转生界面后直接发请求并播放动画。 |
| `SkipItemTransfer` | `true` | 跳过物品转移页（`SkipConfirmation` 为 true 时基本不会出现，只在请求失败回退时起作用）。 |

两个都设成 `false` 就是完全原版的转生界面。

## 原理

服务端：

1. `PrestigeReworkMod`（`IOnLoad`，优先级 200001）在数据库加载完成后、Web 服务器启动前，
   把 `TemplateTable.Prestige.Elements[i].Conditions` 改写成单条 `Level >= 15/25/35/45`，
   这样 `/client/prestige/list` 返回给客户端的条件就是纯等级条件，界面也只会显示等级要求。
2. `PrestigeObtainPatch` 用 Harmony **Prefix** 拦截 `PrestigeController.ObtainPrestige`：
   原版这里只是把请求塞进 `SptData.PendingPrestige`、把 `IsWiped` 置 `true`，等下次重建角色时才发奖；
   本模组改为**直接**给当前角色发奖、写入转生等级、保存存档，并返回 `Task.CompletedTask` 阻止原方法执行。
3. 发奖逻辑对齐原版 `PrestigeHelper.AddPrestigeRewardsToProfile` 的 switch，
   但只处理**刚达到的这一阶**，避免不清档时重复发奖。

客户端：

`ObtainPrestigeScreen` 是三个状态（0 确认页 / 1 物品转移 / 2 转生动画）。
插件 Patch 了 `ShowAnotherState(int)`：界面刚打开（`_currentSelectedStateIndex == -1`）要显示状态 0、
或从确认页点「下一步」要显示状态 1 时，改为调用原版的 `PrestigeTransferItemsState.SendItemsAccept()`
发送转生请求，成功后直接跳到状态 2。请求失败会自动退回原版界面，不会卡死、也不会重复发请求。

## 兼容性

- 服务端：SPT **4.1.2 及以上**（`~4.1.2`，即 >= 4.1.2 < 4.2.0），.NET 10。
  编译时引用的是 SPT 官方 NuGet 上的 4.1.2 公开引用程序集，因此 DLL 里记录的
  `SPTarkov.Server.Core` 版本是 **4.1.0.0**——比 4.1.6.0 更容易绑定，4.1.2 ~ 4.1.6 全都能加载。
  4.1.6 已实测；4.1.5 / 4.1.2 用的是同一套转生接口，但作者手头只有 4.1.6 服务端，其余版本**未实测**。
  如果哪个版本里 `PrestigeController.ObtainPrestige` 长得不一样，补丁会打不上，此时模组**会自我禁用**并打一条错误日志，
  转生条件保持原样（不会出现「界面显示能领、实际却重置角色」的情况）。
- 客户端：SPT 4.1.x 客户端 + BepInEx 5。插件只依赖 EFT 的 UI 类型（`EFT.UI.ObtainPrestigeScreen`），与 SPT 版本无关。
- 只改服务端模板和请求处理，不修改客户端资源；不依赖任何其他模组。

## 已知限制

- 客户端插件没有在自动化环境里验证过（Harmony 只能在 Unity 运行时里跑），
  首次进游戏请看一眼 `BepInEx\LogOutput.log` 是否出现 `patched EFT.UI.ObtainPrestigeScreen.ShowAnotherState`。
- 转生奖励里的物品通过系统邮件发放，注意邮箱别塞满。
- 物品转移栏里的东西**不会**被搬走（本来也不该搬），角色不重置，转移没有意义。

## 从源码构建

需要 .NET SDK 10。默认从 NuGet 拉 SPT 官方 4.1.2 引用程序集，**不需要本地装 SPT**；
想改成引用本地服务端（例如自己装了 4.1.6）就加 `-p:SptServerDir="D:\SPT\SPT_Runtime"`，
但注意这样做会把 DLL 里的 AssemblyRef 抬到本地版本，生成的包只能在该版本或更新的服务端加载。

```powershell
# 服务端模组（推荐：不传 SptServerDir，用 NuGet 上的 4.1.2 引用程序集）
dotnet build src/PrestigeRework/PrestigeRework.csproj -c Release

# 或者引用本地服务端（会抬高 AssemblyRef，别用来发版）
# dotnet build src/PrestigeRework/PrestigeRework.csproj -c Release -p:SptServerDir="D:\SPT\SPT_Runtime"

# 客户端插件
dotnet build src/PrestigeSkipTransfer/PrestigeSkipTransfer.csproj -c Release -p:GameDir="D:\SPT"

# 打包（生成 artifacts/ 下的整合包 zip）
./scripts/build.ps1 -GameDir "D:\SPT"
```

## English

SPT 4.1.x prestige rework: prestige 1-4 unlock at character level 15/25/35/45 and are claimed instantly —
**the character is never reset**, and all vanilla rewards are kept (cosmetics, skill points, item mail,
extra daily quests, achievement). Only the newly reached level is granted, so nothing is duplicated.

The optional BepInEx plugin `PrestigeSkipTransfer` skips the confirmation and item-transfer screens of
`EFT.UI.ObtainPrestigeScreen` and sends the request through the vanilla client code.

Install: drag `SPT_Runtime` and `BepInEx` from the release zip into your game root folder.
Server config: `SPT_Runtime/user/mods/PrestigeRework/config/config.json`.
Build: `dotnet build src/PrestigeRework/PrestigeRework.csproj -c Release -p:SptServerDir=...`

## 许可

[MIT](LICENSE) © wangbofan
