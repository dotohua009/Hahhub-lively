"""Write a readable preview report from the exported counter CSV."""
from pathlib import Path
from collections import defaultdict
import csv
import json
import sys


def write_report(folder, cpu, gpu):
    rows = list(csv.reader((folder / "preview-usage.csv").open(encoding="utf-8-sig", newline="")))[1:]
    info = json.loads((folder / "fixture.jsonl").read_text())
    effects = defaultdict(dict)
    for row in rows:
        effects[row[0]][row[1]] = row
    modes = ("空闲", "单键长按", "多键连按")
    lines = ["# hahhub 软件预览资源实测", "",
             f"Windows 本机，{cpu}（{info['logical']} 逻辑处理器），{gpu}，WPF 硬件渲染 Tier {info['tier']}。"
             f"应用选择 {info['rate']} FPS 档，预览区 {info['heroWidth']:.0f} × {info['heroHeight']:.0f} 个逻辑像素，显示缩放 {info['scale']:.0%}。"
             "IPI 84 使用已保存的校准布局，输入发往原生预览窗口。", "",
             "每格依次为进程 CPU 占整机百分比、进程 GPU 最忙引擎百分比、显卡整卡平均瓦数。每项取两轮均值，第二轮倒序。", "",
             "| 效果 | 空闲 | 单键长按 | 多键连按 |", "|---|---:|---:|---:|"]
    for effect, states in effects.items():
        cells = []
        for mode in modes:
            row = states[mode]
            power = f"{float(row[4]):.1f} W" if row[4] else "无功率读数"
            cells.append(f"{float(row[2]):.2f}% · {float(row[3]):.2f}% · {power}")
        lines.append("| " + " | ".join([effect, *cells]) + " |")
    lines += ["", "## 资源排名", ""]
    for mode in modes[1:]:
        state = [group[mode] for group in effects.values()]
        cpu_order = sorted(state, key=lambda r: -float(r[2]))
        gpu_order = sorted(state, key=lambda r: -float(r[3]))
        top = lambda items, col: "、".join(f"{r[0]} {float(r[col]):.2f}%" for r in items[:3])
        lines.append(f"- **{mode}**　CPU 前三为{top(cpu_order, 2)}。GPU 前三为{top(gpu_order, 3)}。")
    ticks = [float(states["多键连按"][11]) for states in effects.values()]
    parked = [name for name, states in effects.items() if int(states["单键长按"][12]) == 2]
    lines += ["", "## 输入与计量", "",
              "- 每轮先空闲 5.2 秒，再按住 A 键 12.5 秒。清空效果后连续输入 9 秒，A、S、D、F、H、J、K、L 轮流按下，每秒 8 次，每键保持约 250 毫秒。",
              "- CPU 使用 Windows ETW 线程调度记录，累加预览进程全部线程在各逻辑处理器上实际占用的时间，再除以场景时长和逻辑处理器数。调度跟踪丢失事件为 0。[Microsoft TraceEvent](https://github.com/microsoft/perfview/blob/main/documentation/TraceEvent/TraceEventProgrammersGuide.md)",
              "- GPU 读取 Windows 每进程硬件引擎累计忙时，每个有效区间取最忙引擎后按时长加权。功率来自 NVIDIA NVML，包含桌面合成及其他程序的显卡用电。此轮仅测软件预览，未测 CPU 封装瓦数、整机插座瓦数或键盘屏耗电。",
              f"- 11 项空闲时动画计时器均已停止。多键连按下，预览动画计时器实际每秒回调 {min(ticks):.1f}–{max(ticks):.1f} 次。60 FPS 是选择的档位。",
              "- " + "、".join(parked) + "在两轮单键长按末尾均自动停绘，上表长按均值包含其静止时间。" if parked else "",
              "", "[逐轮结果与采样数量](preview-usage.csv)"]
    if len(sys.argv) > 4:
        lines += ["", sys.argv[4]]
    (folder / "实测报告.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    write_report(Path(sys.argv[1]), sys.argv[2], sys.argv[3])
