"""比较同一组谱面的耗时，并核对完整输出指纹。"""

import json
from pathlib import Path
from statistics import median
import sys


def compare(before_path, after_path, output_path):
    before = json.loads(Path(before_path).read_text(encoding="utf-8-sig"))
    after = json.loads(Path(after_path).read_text(encoding="utf-8-sig"))
    if before["assemblyHash"] == after["assemblyHash"]:
        raise ValueError("两个报告来自同一程序集，请检查是否复用了增量构建产物。")
    for field in ("planner", "currentRuleVersion", "framework"):
        if before[field] != after[field]:
            raise ValueError(f"测量条件不同：{field}")
    key = lambda chart: (chart["path"], chart["difficulty"])
    old = {key(chart): chart for chart in before["charts"]}
    new = {key(chart): chart for chart in after["charts"]}
    if not old or old.keys() != new.keys():
        raise ValueError("两份报告的样本清单不同或为空。")
    lines = ["# 求解器性能对照", "", "每张谱面取多次运行的中位数。耗时包含规划和整曲核验，不含谱面解析与报告序列化。", "",
             "| 谱面 | 难度 | 音符数 | 原版 / 秒 | 优化版 / 秒 | 提速 | 分配量下降 | 输出 |",
             "|---|---:|---:|---:|---:|---:|---:|---|"]
    total_old = total_new = allocation_old = allocation_new = 0
    for identity, a in old.items():
        b = new[identity]
        if a["chartSha256"] != b["chartSha256"]:
            raise ValueError(f"谱面内容不同：{identity}")
        fingerprints = {run["fingerprint"] for chart in (a, b) for run in chart["runs"]}
        if len(fingerprints) != 1:
            raise ValueError(f"轨迹、输入、判定或诊断不一致：{identity}")
        ta, tb = (median(run["seconds"] for run in chart["runs"]) for chart in (a, b))
        aa, ab = (median(run["allocatedBytes"] for run in chart["runs"]) for chart in (a, b))
        total_old += ta
        total_new += tb
        allocation_old += aa
        allocation_new += ab
        title = a["title"].replace("|", "\\|")
        lines.append(f"| {title} | {a['difficulty']} | {a['notes']} | {ta:.3f} | {tb:.3f} | {ta / tb:.2f}× | {1 - ab / aa:.1%} | 一致 |")
    lines += ["", f"各谱面中位数合计：{total_old:.3f} → {total_new:.3f} 秒，提速 **{total_old / total_new:.2f} 倍**。",
              f"分配量中位数合计下降 **{1 - allocation_new / allocation_old:.1%}**。分配量是累计托管分配，不是峰值内存。", "",
              "完整输出指纹包含手部轨迹、输入事件、逐音符判定、缺口、动作诊断、达成率和 DX 分数。", "",
              f"求解器：`{before['planner']}`；运行时：`{before['framework']}`；规则：`{before['currentRuleVersion']}`。", "",
              f"原版程序集 SHA-256：`{before['assemblyHash']}`。", "",
              f"优化版程序集 SHA-256：`{after['assemblyHash']}`。", "",
              f"原始测量：[before.json]({Path(before_path).name})、[after.json]({Path(after_path).name})。", ""]
    if before.get("measurement"):
        lines += [before["measurement"], ""]
    Path(output_path).write_text("\n".join(lines), encoding="utf-8")
    print(f"{len(old)} 张谱面输出一致，合计提速 {total_old / total_new:.2f} 倍。")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("用法：compare.py before.json after.json 输出.md")
    compare(*sys.argv[1:])
