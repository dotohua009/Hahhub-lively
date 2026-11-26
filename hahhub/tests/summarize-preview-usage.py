"""Summarize the process CPU / GPU counters recorded by check-preview-usage.ps1."""
from bisect import bisect_left
import csv
import json
import sys
from collections import defaultdict
from pathlib import Path

TICKS = 10_000_000
NAMES = {
    "hahhan-glass": "碎屏", "hahhan-blackhole": "黑洞", "hahhub-ink": "泼墨",
    "hahhub-glitter-flow": "流麻", "hahhub-frost-fog": "霜雾", "hahhub-snowdrift": "风吹雪",
    "hahhub-frost": "冰刺", "hahhub-tendril": "触须", "hahhub-silk": "蛛群",
    "hahhub-fuse": "方块连爆", "hahhub-prism": "钻石切面",
}
MODES = {"idle": "空闲", "hold": "单键长按", "multi": "多键连按"}


def load_rows(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line]


def summarize_phase(phase, samples, logical):
    start, end = phase["start"], phase["end"]
    process_time = gpu_busy = watt_seconds = 0
    cpu_time = gpu_time = power_time = 0
    cpu_count = gpu_count = 0
    for prev, cur in zip(samples, samples[1:]):
        if start <= prev["at"] < cur["at"] <= end:
            if not prev["unlocked"] or not cur["unlocked"]:
                raise ValueError("Desktop inaccessible during " + str(phase))
            dt = (cur["at"] - prev["at"]) / TICKS
            cpu_time += dt
            process_time += cur["cpu"] - prev["cpu"]
            cpu_count += 1
            if prev["watts"] is not None and cur["watts"] is not None:
                power_time += dt
                watt_seconds += dt * (prev["watts"] + cur["watts"]) / 2
        if start <= prev["gpuTime"] < cur["gpuTime"] <= end:
            shared = prev["engines"].keys() & cur["engines"].keys()
            if not shared:
                continue
            busy = max(cur["engines"][k] - prev["engines"][k] for k in shared)
            elapsed = cur["gpuTime"] - prev["gpuTime"]
            if busy < 0 or busy > elapsed * 1.02:
                raise ValueError("Unexpected GPU running-time counter")
            gpu_time += elapsed / TICKS
            gpu_busy += busy / TICKS
            gpu_count += 1
    duration = (end - start) / TICKS
    if min(cpu_time, gpu_time) < duration * .50 or not phase["visible"]:
        raise ValueError("Insufficient visible CPU/GPU coverage: " + str(phase))
    if phase["mode"] == "idle" and (phase["timerTicks"] or phase["actions"]):
        raise ValueError("Idle animation still ticking: " + str(phase))
    if phase["mode"] != "idle" and not (phase["timerTicks"] and phase["actions"]):
        raise ValueError("Input did not animate: " + str(phase))
    return dict(pass_number=phase["pass"], cpu=100 * process_time / cpu_time / logical,
                gpu=100 * gpu_busy / gpu_time, watts=watt_seconds / power_time if power_time else None,
                seconds=duration, ticks=phase["timerTicks"], actions=phase["actions"],
                parked=not phase["timerStillOn"], cpu_intervals=cpu_count, gpu_intervals=gpu_count)


def avg(rows, key):
    return sum(r[key] for r in rows) / len(rows)


def precise_cpu(folder):
    begin = int((folder / "cpu-start.txt").read_text())
    end, lost = map(int, (folder / "cpu-end.txt").read_text().split(","))
    if lost:
        raise ValueError(f"Lost {lost} scheduler events")
    with (folder / "cpu-slices.csv").open() as handle:
        slices = sorted(tuple(map(int, line.split(","))) for line in handle)
    starts = [start for start, _ in slices]
    longest = max(stop - start for start, stop in slices)

    def percent(phase, cores):
        a, b = phase["start"], phase["end"]
        if not begin <= a < b <= end:
            raise ValueError("Missing scheduling coverage")
        left, right = bisect_left(starts, a - longest), bisect_left(starts, b)
        on_cpu = sum(max(0, min(b, stop) - max(a, start)) for start, stop in slices[left:right])
        return 100 * on_cpu / (b - a) / cores

    return percent


def read_run(folder, skip=()):
    info, = load_rows(folder / "fixture.jsonl")
    phases = load_rows(folder / "phases.jsonl")
    samples = load_rows(folder / "telemetry.jsonl")
    if not (folder / "complete").exists() or info["rate"] != 60 or info["tier"] < 1:
        raise ValueError("Incomplete measurement or hardware composition unavailable")
    grouped = defaultdict(list)
    cpu_percent = precise_cpu(folder)
    for phase in phases:
        if phase["effect"] in skip:
            continue
        row = summarize_phase(phase, samples, info["logical"])
        row["cpu"] = cpu_percent(phase, info["logical"])
        grouped[phase["effect"], phase["mode"]].append(row)
    return info, grouped, len(phases), len(samples)


def main(folder, updates):
    reruns = [read_run(path) for path in updates]
    replaced = {effect for other, _, _, _ in reruns for effect in other["ids"]}
    info, grouped, phase_count, sample_count = read_run(folder, replaced)
    for other, replacement, _, count in reruns:
        for key in ("logical", "tier", "heroWidth", "heroHeight", "scale", "rate", "resolution"):
            if other[key] != info[key]:
                raise ValueError("Retest fixture mismatch: " + key)
        if not set(other["ids"]) <= set(info["ids"]):
            raise ValueError("Retest has unknown effect")
        grouped.update(replacement)
        sample_count += count
    with (folder / "preview-usage.csv").open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["效果", "场景", "CPU占整机%", "GPU最忙引擎%", "显卡整卡W",
                         "轮1 CPU%", "轮2 CPU%", "轮1 GPU%", "轮2 GPU%", "轮1 整卡W", "轮2 整卡W",
                         "动画计时器回调/秒", "结束时停绘的轮数", "累计按下次数", "有效CPU区间数", "有效GPU区间数"])
        for effect in info["ids"]:
            for mode in MODES:
                rows = sorted(grouped[effect, mode], key=lambda r: r["pass_number"])
                if [r["pass_number"] for r in rows] != [1, 2]:
                    raise ValueError("Missing pass for " + effect + " / " + mode)
                watts = [r["watts"] for r in rows]
                writer.writerow([NAMES[effect], MODES[mode], round(avg(rows, "cpu"), 3), round(avg(rows, "gpu"), 3),
                                 round(avg(rows, "watts"), 2) if None not in watts else "",
                                 *[round(r[k], 3) if r[k] is not None else "" for k in ("cpu", "gpu", "watts") for r in rows],
                                 round(sum(r["ticks"] for r in rows) / sum(r["seconds"] for r in rows), 1),
                                 sum(r["parked"] for r in rows), sum(r["actions"] for r in rows),
                                 sum(r["cpu_intervals"] for r in rows), sum(r["gpu_intervals"] for r in rows)])
    print(f"{phase_count} final phases, {sample_count} recorded samples including retests, {len(grouped)} effect / state pairs")


if __name__ == "__main__":
    main(Path(sys.argv[1]), [Path(arg) for arg in sys.argv[2:]])
