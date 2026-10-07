# DX 达成率

播放时显示减分制达成率。含 Break 的谱面从 `101.0000%` 开始，没有 Break 从 `100.0000%` 开始。空谱面显示 `100.0000%`。

规则依据 MajdataPlay [`ObjectCounter.cs`](https://github.com/TeamMajdata/MajdataPlay/blob/dc19722d602f099131d93b37091624ad30150ad1/Assets/Scripts/Scenes/Game/ObjectCounter.cs) 的 `CountNoteSumAsync`、`UpdateNoteScoreCount`、`UpdateAccRate`，以及 `Achievement_101` 显示分支。对应版本为 `dc19722d602f099131d93b37091624ad30150ad1`。

## 计分

[`AchievementState`](../../src/MaiPlay.Core/AchievementState.cs) 分别累计基础分损失和 Break 额外分损失。Tap、Touch 的基础满分为 500，Hold、TouchHold 为 1000，Slide 为 1500。标记为 Break 的音符优先按 2500 计算，不再叠加原类型的分数。Slide 星头和轨迹已经是独立音符，各自按自己的类型和 Break 标记计分。

全谱基础分满分记为 `B`，基础分损失为 `L`。Break 数量乘以 100 得到额外分满分 `E`，额外分损失为 `X`。达成率为：

```text
100 × (B − L) / B + (E − X) / E
```

没有 Break 时，公式的第二项为 0。所有 Break 合计提供 1 个百分点，不是每颗 Break 各提供 1 个百分点。使用 `decimal` 计算，显示前向下截断至小数点后四位，保留 `%`。

普通音符的 Perfect 各档不扣基础分，Great 各档扣 20%，Good 扣 50%，Miss 和 TooFast 扣全部。Fast/Late 方向不改变减分；EX、EACH 标记不改变权重。传入的是已经确定的最终判定，本模块不决定 EX 的判定升级或命中窗口。

Break 的分档如下；表中的 2600 等是传统分值标识，DX 额外分按独立列计算。

| 判定 | 传统分值标识 | 基础分损失 | DX 额外分损失 |
|---|---:|---:|---:|
| Perfect（理想判定） | 2600 | 0 | 0 |
| Fast/Late Perfect 2nd | 2550 | 0 | 25 |
| Fast/Late Perfect 3rd | 2500 | 0 | 50 |
| Fast/Late Great | 2000 | 500 | 60 |
| Fast/Late Great 2nd | 1500 | 1000 | 60 |
| Fast/Late Great 3rd | 1250 | 1250 | 60 |
| Fast/Late Good | 1000 | 1500 | 70 |
| Miss / TooFast | 0 | 2500 | 100 |

例如，只有一颗 Break 的谱面判为 Perfect 2nd 后是 `100.7500%`，判为 Great 后是 `80.4000%`。三个普通 Tap 中 Miss 一颗后是 `66.6666%`，不会四舍五入为 `66.6667%`。

## 播放状态

[`PlaybackSession`](../../src/MaiPlay.Core/PlaybackSession.cs) 持有 `Achievement`。每个最终事件只计分一次，`Started` 不计分；Hold、TouchHold 和 Slide 在各自最终事件发生时更新达成率。重复推进同一时间或音频位置短暂回退不会重复扣分。

Auto Play 提交理想 Perfect，达成率保持初始值。算法模式接收区域或接触日志，按真实最终等级扣分；无输入的音符由各自状态机自然 Miss，全谱 Miss 时为 `0.0000%`。重新开始清空损失，恢复满达成率。

`AchievementState.Apply` 的调用约定是每个音符只提交一次最终判定，最终结算由游玩状态机和 `PlaybackSession` 保证。该模块只负责减分，不负责输入匹配和时间判定。

针对性验证位于 [`CoreAchievementTests`](../../tests/MaiPlay.Tests/CoreAchievementTests.cs)：类型权重、Break 优先级和额外 1%、全部 15 个等级、四位截断、空谱面、暂停与重开，以及本地可选的 18 个用户谱面在 Auto 和全 Miss 下的最终达成率。

## Rating 与 B50

[`RatingCalculator`](../../src/MaiPlay.Core/RatingCalculator.cs) 将最终达成率与谱面定数换算为整数 Rating：`floor(定数 × min(达成率, 100.5) × 分档系数 / 100)`。达成率先截断到四位小数。分档及 99.9999%、100.4999% 两个特殊边界参考 [PaperPig/MaimaiData 的 ConvertUtils](https://github.com/PaperPig/MaimaiData/blob/30ca1c3cce9069051d7d3a244b6623c0a650aae4/app/src/main/java/com/paperpig/maimaidata/utils/ConvertUtils.kt)，完整系数以代码为准。计算使用 decimal，避免二进制浮点将边界归入相邻档。

主界面的 Rating 随当前减分达成率更新，显示当前值和该定数在 100.5% 时的上限。谱面只提供 `12+` 等显示等级、没有数值定数时，不猜测定数或计算 Rating。

批量验证按选定难度计算 B50。同一次库扫描中版本编号最大的两个版本归入新曲，其余为旧曲；这两个编号必须从完整曲库确定，不能从失败重试子集推断。旧曲按 Rating 取前 35，新曲取前 15，缺额不跨组补齐。同一谱面身份只计一次，标准与 DX 的不同谱面路径分别保留。跨难度汇总时须保留难度编号作为谱面身份的一部分。

报告同时保存 B50、理论上限、全曲 Rating 合计、平均达成率和未通过音符。B50 封顶不代表所有谱面全 CP，错误不能因为没有进入 B50 而省略。缺少版本或定数的谱面单独计入排除数。失败重试子集的 B50 必须标明范围，不作为整库 B50。

`RatingTests` 验证分档边界、100.5% 封顶、四位截断、35/15 名额、谱面去重和新曲缺额。
