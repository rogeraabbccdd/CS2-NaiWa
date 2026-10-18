# CS2-NaiWa 流動奶蛙

流動奶蛙：每回合在地圖隨機角落放一個奶蛙立牌，玩家對它按 E 會聽到奶蛙的笑聲。

![遊戲中的奶蛙立牌](20261018132108_1.jpg)

## 功能

- 每回合奶蛙會出現在地圖上的隨機位置，而且一定是玩家走得到的地方
- 對奶蛙按 E，會聽到奶蛙的笑聲
- 找不到奶蛙？輸入 `!naiwa`，看看它在哪個方向
- 可以讓奶蛙被打壞，也可以設成打不壞
- 官方地圖和工作坊地圖都能用
- 管理員可以在遊戲中調整奶蛙會出現的位置

## 需求

- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) v1.0.376 以上
- [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager)：讓伺服器載入奶蛙的模型和音效，玩家進伺服器時也會自動下載
- [Clientprefs](https://github.com/Cruze03/Clientprefs) v1.0.7 以上（選用）：讓每位玩家用自己的音量聽笑聲

## 安裝

1. 從 [Releases](../../releases) 下載 `CS2-NaiWa-<版本>.zip`，解壓縮到伺服器的 `game/csgo/`。
2. 準備奶蛙的模型和音效：
   1. 下載 `CS2-NaiWa-addon-<版本>.zip`，解壓縮到你的 addon 資料夾 `content/csgo_addons/<addon 名稱>/`。
   2. 用 CS2 Workshop Tools 編譯 `models/naiwa/naiwa_standee.vmdl`、`soundevents/naiwa.vsndevts` 和 `sounds/naiwa/naiwa.mp3`。
   3. 把 addon 上傳到工作坊。
3. 用 MultiAddonManager 載入這個 addon：把它的工作坊 ID 加進 `cfg/multiaddonmanager/multiaddonmanager.cfg` 的 `mm_extra_addons`。如果你已經有放自訂內容的 addon，也可以直接把奶蛙放進同一個 addon。
4. 重新啟動伺服器。

## 設定

第一次啟動後，設定檔會出現在 `addons/counterstrikesharp/configs/plugins/NaiWa/NaiWa.json`。

| 設定 | 預設值 | 說明 |
| --- | --- | --- |
| `MapPrefixes` | `["de_", "cs_"]` | 奶蛙只會出現在這些開頭的地圖 |
| `Health` | `0` | 奶蛙的血量，`0` 代表打不壞 |
| `BreakableMaterial` | `1` | 奶蛙被打壞時的碎片材質，`1` 是木頭 |
| `DefaultVolume` | `0.4` | 笑聲的預設音量 |
| `UseDistance` | `120` | 離奶蛙多近才能按 E |
| `UseCooldown` | `1.5` | 每位玩家按 E 的冷卻秒數 |
| `EnableHintCommand` | `true` | 是否開放玩家使用 `!naiwa` |
| `HintCooldown` | `10` | `!naiwa` 的冷卻秒數 |
| `AnnounceSpawn` | `true` | 奶蛙出現時是否在聊天室公告 |
| `AnnounceBreak` | `true` | 奶蛙被打壞時是否公告是誰打的 |
| `MinDistanceFromSpawn` | `600` | 奶蛙離出生點至少要多遠 |
| `RecentAvoidCount` | `5` | 避開最近幾回合出現過的位置 |
| `RecentMinDistance` | `800` | 跟最近出現過的位置至少要距離多遠 |
| `ManualSpotChance` | `0.3` | 地圖有管理員加的固定位置時，使用固定位置的機率 |
| `MinAreaSize` | `2304` | 太小塊的地板不會放奶蛙，數字越大越挑 |
| `MaxAreaHeightDelta` | `12` | 太斜的地板不會放奶蛙，數字越小越挑 |
| `EdgeShrink` | `0.6` | 數字越小，奶蛙越會放在地板中間、離牆越遠 |
| `ExtraWorkshopPaths` | `[]` | 工作坊地圖抓不到時，可以在這裡補上 `steamapps/workshop/content/730` 的路徑 |

### 音量

有安裝 Clientprefs 時，笑聲會使用玩家的 `Volume` 設定。其他使用同一個 `Volume` 設定的音效插件，玩家只要調一次就好。沒有安裝 Clientprefs，或玩家沒調過音量時，使用 `DefaultVolume`。

## 指令

### 玩家

| 指令 | 說明 |
| --- | --- |
| `!naiwa` | 告訴你奶蛙在哪個方向、大約幾公尺遠、在樓上還是樓下 |

### 管理員（需要 `@css/generic` 權限）

| 指令 | 說明 |
| --- | --- |
| `css_naiwa_info` | 顯示奶蛙在這張地圖的狀態和位置 |
| `css_naiwa_goto` | 傳送到奶蛙面前 |
| `css_naiwa_move` | 馬上把奶蛙換到新位置 |
| `css_naiwa_glow` | 讓奶蛙發光，隔著牆也看得到，方便測試。所有玩家都看得到，測完記得關掉 |
| `css_naiwa_add` | 把你站的位置加為奶蛙的固定位置，奶蛙會面向你看的方向 |
| `css_naiwa_ban [半徑]` | 奶蛙卡在奇怪的地方時用，之後不會再出現在那一帶（預設半徑 256） |
| `css_naiwa_toggle` | 在這張地圖開關奶蛙 |
| `css_naiwa_clear` | 清掉這張地圖的固定位置和排除範圍 |
| `css_naiwa_rescan` | 工作坊地圖抓不到 nav 時，重新掃描一次 |

固定位置和排除範圍會存在 `configs/plugins/NaiWa/maps/<地圖名稱>.json`。

## 開發

需要 .NET 10 SDK。

```
dotnet build CS2-NaiWa.csproj -c Release
```

推送 `v` 開頭的 tag（例如 `v1.0.0`）會自動發布 release，tag 要和 `src/NaiWaPlugin.cs` 裡的 `ModuleVersion` 一致。

`tools/` 裡有兩個輔助工具：

- `make_standee.py`：用角色圖片產生立牌模型
- `NavProbe`：在電腦上直接測試地圖的 nav 讀取和選點
