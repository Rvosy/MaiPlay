# 第二版求解器性能验证

本次对照以第一版性能优化为基线，重点测量实际导入的 Panopticon Master 14.4（900 音符）和 Re:Master 14.3（909 音符），另取第一轮的四张谱面检查其他配置。[完整数据](comparison.md)来自每版每谱三次交替运行的中位数。

Panopticon 紫谱从 8.756 秒降到 5.343 秒，减少 39.0%；白谱从 5.531 秒降到 3.930 秒，减少 28.9%。六张谱面的中位数合计从 21.415 秒降到 15.556 秒，提速 1.38 倍。轻量谱面的收益较小，不能把 Panopticon 的提速比例套用于整库。

## 实现

本版修改 `HumanPlanner` 和 `PlaybackSession`：

- `HumanPlanner.PreparedSearch` 在一次求解内复用需求编译、动作校准、配置识别及动作模板，时机反馈的后续搜索继续使用这份准备结果。
- `HumanPlanner.Plan` 跳过与当前最优方案完全相同的时机请求。Panopticon 紫谱原本执行五轮搜索，其中一轮重复已有请求；现在执行四轮，其余反馈候选照常搜索和核验。
- `HumanPlanner.Action.Frames` 延后展开接近轨迹。剪枝仍使用相同的时位、分手和成本信息，后续衔接及实际回放需要时才生成帧。
- `HumanPlanner.Prune` 按候选数量预分配去重集合，避免扩容时反复复制较大的状态键。排序、去重取舍和各类保留候选的顺序沿用原逻辑。
- `PlaybackSession` 从首个音符窗口或首条输入之前的逻辑采样点开始，省去没有输入、没有可判定音符的前导空跑。绝对时间戳、240 Hz 采样相位和提前输入的持续状态保持原样。

搜索宽度和整曲核验候选数保持 `HumanPlannerOptions` 默认配置。判定窗口、运动模型、候选打法、轨迹采样密度均未调整。

## 验证

六张谱面、每版每谱三次运行的完整输出指纹全部一致，包括轨迹、输入事件、逐音符判定、缺口、动作诊断及成绩。Panopticon 的原有结果保持为紫谱 CP 899/900、1 个缺口；白谱 CP 861/909、47 个缺口。本次只优化时间，没有修复这些缺口。

当前工作区完整测试通过 **1,105 项**，0 失败、0 跳过。新增回归用例确认延后开始采样时，窗口前已经按住的输入不会变成新的点击，Reset 后重放保持相同结果与输入历史。

```powershell
dotnet test tests/MaiPlay.Tests -c Release --artifacts-path artifacts/performance-v2/final-tests --logger 'trx;LogFileName=final-regression.trx' --results-directory artifacts/performance-v2/test-results
```

测试原始记录：`artifacts/performance-v2/test-results/final-regression.trx`。性能原始记录为 [before.json](before.json) 和 [after.json](after.json)，包含谱面哈希、程序集哈希、各次耗时与输出指纹。

## 桌面构建

第二版可执行文件：`artifacts/preview-20261008-performance-v2/MaiPlay.exe`。性能测试程序引用该桌面包中的 `MaiPlay.Core.dll`，二者 SHA-256 均为 `73EC30C862826EAFF471F1DE87392CE10903BAD5F0AABAB59BA3D4756C8E6762`。

```powershell
dotnet build src/MaiPlay -t:Rebuild -c Release --artifacts-path artifacts/performance-v2/app-build -o artifacts/preview-20261008-performance-v2
```

运行模式选择「配置与动作规划（试验）」后，「求解手顺」通过 `MainWindow.GeneratePlanAsync` 调用此程序集的 `HumanPlanner.Plan`。已打开的旧进程不会自动加载新程序集，需要从第二版入口重新启动。本轮没有通过桌面自动化点击按钮，验证范围为调用链、桌面包程序集和完整测试。

## 测量条件

AMD Ryzen 7 9700X，16 个逻辑处理器，Windows 11，.NET 10.0.5，Release 构建。每次用独立进程完成相同的小谱面预热，再计时完整目标谱面的规划与核验；解析和报告序列化不计入耗时。三轮顺序为第一版→第二版、第二版→第一版、第一版→第二版。

测量期间本机存在其他任务负载。这里使用本轮成对测量，不与上一轮不同负载下的单次秒数直接相除。整库耗时与峰值内存尚未测量；报告中的分配量为累计托管分配，六谱合计下降 45.4%。

测量工具见[性能测量说明](../../../research/planner-performance/README.md)。
