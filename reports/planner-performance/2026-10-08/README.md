# 2026-10-08 求解器性能验证

[性能对照](comparison.md)覆盖四张完整谱面、共 1,891 个音符。每版每张谱面测三次，比较耗时中位数；测量包括规划与整曲回放。两版均沿用 `HumanPlannerOptions` 的默认搜索参数，冻结版本的搜索宽度为 64、整曲核验候选数为 4。

## 改动

- `ContactMapper` 用位集合记录固定触区，以包围框排除不可能接触的区域，再执行原有圆与多边形相交判断。区域顺序、先释放后按下、同区多来源与 C 区合并规则保持原样。
- `HandReplay` 复用采样字典和来源字符串，直接插值接触位置与半径，减少逐帧创建的姿态和接触对象。显示姿态与实际输入共用接触插值计算。
- `HumanPlanner` 在一轮搜索中复用同一动作模板的时移帧，减少不同搜索状态之间的重复复制。

采样显示接触回放中的集合分配和触区几何检查是主要热点。搜索候选、剪枝规则、动作成本、时机反馈和判定窗口沿用原有行为。

## 测量范围

硬件为 AMD Ryzen 7 9700X，16 个逻辑处理器；Windows 11，.NET 10.0.5，Release 构建。每次启动先执行相同的小谱面预热，再计时求解一个完整样本。三轮顺序分别为原版→优化版、优化版→原版、原版→优化版。

测量期间本机同时运行其他曲库任务。绝对耗时会随负载变化；交替运行用于减小顺序偏差。结果只覆盖这四张谱面：单谱提速为 2.09～10.92 倍，中位数耗时合计提速 5.81 倍，不能据此断言每张谱面都能达到三倍。

两份性能程序集使用相同的 Core 源码快照，仅替换本次涉及的三个实现文件。四张谱面的所有运行都得到相同的完整输出指纹，均为全 CP、零缺口、零超速分段。指纹包含轨迹和逐音符结果，不只比较总分。原始记录和程序集 SHA-256 保存在 [before.json](before.json) 与 [after.json](after.json)。

## 回归验证

当前工作区完整测试：852 项通过，0 失败、0 跳过。新增用例覆盖所有触区、半径边界、多区域事件顺序、来源及通道独立性、接触偏移与半径插值。

当前集成版本另含 Slide 末点浮点误差合并修复。为核对实际集成结果，另行构建包含相同修复、仅移除本次三个实现文件优化的对照版。`HumanPlanner` 与 `NormalHandPlanner` 各跑四张谱面，优化前后的完整输出指纹全部一致，见 [integration.json](integration.json)。这组单次运行用于兼容性核对，不计入三轮性能统计。

```powershell
dotnet test tests/MaiPlay.Tests -c Release --artifacts-path artifacts/performance/final-tests --logger 'trx;LogFileName=final-regression.trx' --results-directory artifacts/performance/test-results
```

本机测试原始记录位于 `artifacts/performance/test-results/final-regression.trx`。未重新统计整库性能，也未测峰值内存；报告中的分配量是求解过程累计产生的托管对象字节数。

复测工具与步骤见[性能测量说明](../../../research/planner-performance/README.md)。
