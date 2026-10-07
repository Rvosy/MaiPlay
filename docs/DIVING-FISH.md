# 上传算法成绩到水鱼查分器

算法评估只生成紫谱（Master）和白谱（Re:Master）的成绩，也只上传这两个难度。`tools/diving_fish.py` 将 GameplayHarness 的报告转换为水鱼成绩，供评估账号查看达成率、DX 分数与 Rating。需要 Python 3.11 或更高版本，无第三方依赖。

使用专门的算法评估账号。批量接口会覆盖提交谱面的旧成绩，较低成绩也会覆盖；未提交的旧谱面仍然保留。每轮应使用同一曲目范围，保留本地报告比较退步、异常和覆盖率。B50 只反映其中最高的一部分成绩，不能代替整库比较。工具不会清空账号。

## 生成上传包

基线算法只运行难度 5 和 6，分别保存报告：

```powershell
dotnet run --project tests/MaiPlay.GameplayHarness -c Release -- --batch 5 .reference/charts/game-extracted artifacts/evaluation/master
dotnet run --project tests/MaiPlay.GameplayHarness -c Release -- --batch 6 .reference/charts/game-extracted artifacts/evaluation/remaster
```

评估 HumanPlanner 时，使用直接批量入口，只计算这套算法：

```powershell
dotnet run --project tests/MaiPlay.GameplayHarness -c Release -- --batch-human 5 .reference/charts/game-extracted artifacts/evaluation/human-master
dotnet run --project tests/MaiPlay.GameplayHarness -c Release -- --batch-human 6 .reference/charts/game-extracted artifacts/evaluation/human-remaster
```

输出同样是 `summary.json`，其中 `planner` 标明求解器。需要和基线对比时，再按[配置与动作规划](HUMAN-PLANNER.md)使用 `--compare-planners 5 all` 和 `--compare-planners 6 all`。不要运行其他难度后再丢弃结果。

两个难度分别生成上传包，再分别上传到同一评估账号。每次使用新的输出目录：

```powershell
python tools/diving_fish.py prepare artifacts/evaluation/master/summary.json artifacts/diving-fish/run-001-master
python tools/diving_fish.py prepare artifacts/evaluation/remaster/summary.json artifacts/diving-fish/run-001-remaster
```

对照报告必须明确选择求解器：

```powershell
python tools/diving_fish.py prepare artifacts/evaluation/master/comparison.json artifacts/diving-fish/run-001-master --side candidate
```

`prepare` 只读取公共曲库，不上传成绩。输出 `bundle.json` 和当次 `music-data.json`。离线复查可用 `--music-data` 指定保存的曲库。上传包记录报告路径、SHA-256、所选求解器、成绩和排除原因；原始报告需另行保留。

转换规则见 `tools/diving_fish.py::prepare`：

- 仅支持[官方转换曲库](OFFICIAL-CHARTS.md)的编号目录，通过歌曲编号获取水鱼的准确标题和 SD/DX 类型。标题以水鱼为准，例如编号 383 对应 `Link(CoF)`。
- 仅接收 Simai 难度 5、6，对应水鱼 `level_index` 3、4。其他难度列入 `excluded`；上传入口也会拒绝含其他难度的旧上传包。找不到歌曲、难度或音符总数不同的谱面列入 `excluded`，不猜测映射。音符总数相同仍不能证明两个版本的谱面完全一致。
- `partial` 的整曲实际成绩照常上传；求解异常和缺少难度不伪造为零分。排除项会缩小评估范围，需要逐轮对照。
- 全 CP 标记为 `app`；其他成绩暂不推断 FC/AP，`fc` 留空。单人算法没有联机同步成绩，`fs` 留空。这些空值也会覆盖账号的旧标记。

上传前查看 `bundle.json` 的曲目、数量和排除项。不要混用不同算法或不同轮次的报告。

## 上传与核对

在评估账号的“编辑个人资料”生成个人 `Import-Token`。它与已废弃的 `Developer-Token` 不同。工具支持隐藏输入，也可读取进程环境变量 `MAIPLAY_DIVING_FISH_IMPORT_TOKEN`；不要把 token 写入源码、命令参数或报告。

```powershell
python tools/diving_fish.py upload artifacts/diving-fish/run-001-master/bundle.json artifacts/diving-fish/run-001-master-receipt --account 评估账号用户名
python tools/diving_fish.py upload artifacts/diving-fish/run-001-remaster/bundle.json artifacts/diving-fish/run-001-remaster-receipt --account 评估账号用户名
```

工具先读取账号成绩并核对用户名，在新的回执目录保存 `before.json`，随后发送上传包并读回成绩。`submitted.json`、`response.json`、`after.json` 和 `verification.json` 保留本次请求内容与核对结果，不保存 token。回执含账号数据，应放在 Git 忽略的 `artifacts/` 下。

只有所有提交谱面的达成率、DX 分数、FC 和 FS 均通过读回检查，才输出已验证成功。网络失败不会自动重试；若上传后断线，远端可能已经更新，应先检查账号与回执。不要把 HTTP 成功或服务端“更新成功”直接当作所有谱面已经导入。

后续执行时需要指定评估报告、对照报告的 baseline/candidate、评估账号用户名及 Import-Token。未指定本轮范围时先确定报告，避免上传历史结果。

## 接口依据

2026-10-08 核对了[开发者文档](https://maimai.diving-fish.com/manual/docs/category/开发者文档)、[API 文档](https://maimai.diving-fish.com/manual/docs/developer/zh-api-document)和[服务端实现](https://github.com/Diving-Fish/maimaidx-prober/blob/main/database/routes/maimai.py)。

- `GET /music_data`：公共歌曲目录。
- `GET /player/records`：通过 `Import-Token` 读取所属账号的成绩。
- `POST /player/update_records`：通过 `Import-Token` 上传 JSON 数组，字段为 `title`、`type`、`level_index`、`achievements`、`dxScore`、`fc`、`fs`。匹配依据是准确标题、类型和难度，不能只发 `song_id`。

基础地址和实际字段以 `tools/diving_fish.py` 为准。本流程使用个人 Import-Token，不依赖 Developer-Token；后者的相关端点计划于 2027-01-01 停止服务。若以后接入多用户授权，再按 [OAuth 文档](https://maimai.diving-fish.com/manual/docs/developer/oauth-api-document)实现读写 scope。
