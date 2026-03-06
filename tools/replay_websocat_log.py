#!/usr/bin/env python3
"""Quick analyzer for JellyFish websocat logs.

Usage:
  python tools/replay_websocat_log.py /path/to/log.txt
"""

from __future__ import annotations

import json
import sys
from collections import Counter


def load_lines(path: str):
    with open(path, "r", encoding="utf-8") as f:
        for raw in f:
            line = raw.strip()
            if not line.startswith("{"):
                continue
            try:
                yield json.loads(line)
            except json.JSONDecodeError:
                continue


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: python tools/replay_websocat_log.py <websocat_log>")
        return 1

    path = sys.argv[1]
    zones = Counter()
    files = Counter()
    speeds = Counter()
    brightness = Counter()
    led_events = Counter()
    run_pattern_events = 0

    for msg in load_lines(path):
        if msg.get("cmd") != "fromCtlr":
            continue

        if "ledPower" in msg:
            led_events[str(bool(msg["ledPower"]))] += 1

        run = msg.get("runPattern")
        if not isinstance(run, dict):
            continue

        run_pattern_events += 1
        zone = run.get("id")
        if zone:
            zones[zone] += 1

        file_name = run.get("file")
        if file_name:
            files[file_name] += 1

        data = run.get("data")
        if data:
            try:
                data_obj = json.loads(data)
                run_data = data_obj.get("runData", {})
                if "speed" in run_data:
                    speeds[str(run_data["speed"])] += 1
                if "brightness" in run_data:
                    brightness[str(run_data["brightness"])] += 1
            except Exception:
                pass

    print("runPattern events:", run_pattern_events)
    print("zones:", dict(zones))
    print("files:", dict(files))
    print("speed values:", dict(speeds))
    print("brightness values:", dict(brightness))
    print("ledPower events:", dict(led_events))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
