# CS2-NaiWa 流動奶蛙

流動奶蛙：每回合在地圖隨機角落放一個奶蛙立牌，玩家對它按 E 會聽到奶蛙的笑聲。

![遊戲中的奶蛙立牌](20261018132108_1.jpg)

## 功能

- 每回合開始時，在地圖上隨機選一個位置生成奶蛙立牌（`func_breakable`）。
- 位置取自地圖的 nav：只選玩家走得到、夠大、夠平的地板，並避開出生點和最近幾回合出現過的位置。
- 支援官方地圖和工作坊地圖。插件會自動掃描工作坊資料夾、建立索引，收藏更新後不用手動處理。
- 可以設定 health，或設成無法破壞。
- 對奶蛙按 E 會播放笑聲（每位玩家有冷卻時間）。
- 管理員可以在遊戲中新增固定位置、排除不好的位置，或停用某張地圖。

## 安裝

1. 用 `dotnet build -c Release` 編譯，把 `bin/Release/net10.0/` 的內容複製到 `addons/counterstrikesharp/plugins/NaiWa/`。
2. 把 [assets/content/](assets/content/) 複製到 addon 的 content 資料夾，用 Workshop Tools 編譯以下檔案，再更新伺服器使用的工作坊 addon：
   - `models/naiwa/naiwa_standee.vmdl`
   - `soundevents/naiwa.vsndevts`
   - `sounds/naiwa/naiwa.mp3`
3. 需要 CounterStrikeSharp 1.0.376 以上（.NET 10），以及 Clientprefs 插件（`shared/ClientprefsApi`）。
   笑聲音量讀取玩家的 `Volume` cookie，和其他音效插件共用同一個設定。

## 設定

設定檔在 `configs/plugins/NaiWa/NaiWa.json`：

| 設定 | 預設值 | 說明 |
| --- | --- | --- |
| `MapPrefixes` | `["de_", "cs_"]` | 只在這些前綴的地圖出現 |
| `DefaultVolume` | `0.4` | 玩家沒有設定 `Volume` cookie，或 Clientprefs 無法使用時的音量 |
| `Health` | `0` | 0 表示無法破壞 |
| `BreakableMaterial` | `1` | func_breakable 材質（1 = 木頭） |
| `UseDistance` | `120` | 按 E 的有效距離 |
| `UseCooldown` | `1.5` | 每位玩家按 E 的冷卻秒數 |
| `EnableHintCommand` / `HintCooldown` | `true` / `10` | 是否開放玩家用 `!naiwa`，以及冷卻秒數 |
| `AnnounceSpawn` / `AnnounceBreak` | `true` | 生成、被打壞時是否公告 |
| `MinDistanceFromSpawn` | `600` | 與出生點的最短距離 |
| `RecentAvoidCount` / `RecentMinDistance` | `5` / `800` | 避開最近幾次出現的位置 |
| `ManualSpotChance` | `0.3` | 地圖有手動位置時，使用手動位置的機率 |
| `MinAreaSize` | `2304` | nav area 的最小面積（平方單位） |
| `MaxAreaHeightDelta` | `12` | nav area 允許的最大高低差 |
| `EdgeShrink` | `0.6` | 隨機點往 area 中心靠攏的比例，避免貼牆 |
| `ExtraWorkshopPaths` | `[]` | 額外的 `workshop/content/730` 路徑 |

## 玩家指令

| 指令 | 說明 |
| --- | --- |
| `!naiwa` | 提示奶蛙在你的哪個方向、距離幾公尺、在樓上或樓下（有冷卻時間） |

## 管理員指令（`@css/generic`）

| 指令 | 說明 |
| --- | --- |
| `css_naiwa_glow` | 開關奶蛙發光，隔著牆也看得到（測試用，所有玩家都看得到） |
| `css_naiwa_info` | 顯示目前地圖的 nav 狀態、可用 area 數和奶蛙位置 |
| `css_naiwa_move` | 立刻把奶蛙移到新位置 |
| `css_naiwa_goto` | 傳送到奶蛙面前 |
| `css_naiwa_add` | 把自己站的位置加為固定位置，奶蛙會面向你看的方向 |
| `css_naiwa_ban [半徑]` | 排除奶蛙目前位置附近的範圍（預設 256）並移動奶蛙 |
| `css_naiwa_toggle` | 啟用或停用目前地圖 |
| `css_naiwa_clear` | 清除目前地圖的固定位置和排除範圍 |
| `css_naiwa_rescan` | 重建工作坊地圖索引並重新讀取 nav |

每張地圖的修改存在 `configs/plugins/NaiWa/maps/<地圖>.json`。

## 開發工具

- [tools/make_standee.py](tools/make_standee.py)：用角色圖片產生立牌模型的原始檔（需要 numpy、opencv-python）。
- [tools/NavProbe](tools/NavProbe)：離線測試 nav 讀取和選點。
  用法：`dotnet run -- <官方 maps 資料夾> <workshop/content/730> <輸出資料夾> <地圖>...`
