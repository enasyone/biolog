#!/usr/bin/env python3
"""Repeatable named HTTP latency probe; synthetic requests only."""
import argparse, json, platform, statistics, time
from pathlib import Path
from urllib.request import Request, urlopen

def measure(name, url, samples, warmups):
    values = []
    for index in range(samples + warmups):
        started = time.perf_counter_ns()
        with urlopen(Request(url, headers={"User-Agent": "BIO-OPS-007"}), timeout=10) as response:
            response.read()
            if response.status != 200:
                raise RuntimeError(f"{name}: HTTP {response.status}")
        elapsed = (time.perf_counter_ns() - started) / 1_000_000
        if index >= warmups:
            values.append(round(elapsed, 3))
    ordered = sorted(values)
    percentile = lambda p: ordered[min(len(ordered) - 1, max(0, int((len(ordered) - 1) * p)))]
    return {"operation": name, "metric": "HTTP GET round-trip to a live local endpoint",
            "limitations": "single-client loopback on one laptop; not a production network, concurrency, or load profile",
            "url": url, "unit": "ms", "samples": len(values),
            "median_ms": round(statistics.median(values), 3), "p95_ms": percentile(.95),
            "p99_ms": percentile(.99), "min_ms": ordered[0], "max_ms": ordered[-1],
            "raw_ms": values}

def validate(path):
    data = json.loads(Path(path).read_text())
    for key in ("api", "ui"):
        item = data[key]
        assert item["samples"] >= 20 and item["unit"] == "ms"
        assert item["operation"] and item["metric"] and item["limitations"] and item["url"]
        assert len(item["raw_ms"]) == item["samples"]
        assert 0 <= item["median_ms"] <= item["p95_ms"] <= item["max_ms"]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--api-url")
    parser.add_argument("--ui-url")
    parser.add_argument("--samples", type=int, default=31)
    parser.add_argument("--warmups", type=int, default=5)
    parser.add_argument("--output")
    parser.add_argument("--validate")
    args = parser.parse_args()
    if args.validate:
        validate(args.validate); print("latency evidence valid"); return
    if not (args.api_url and args.ui_url and args.output):
        parser.error("measurement requires --api-url, --ui-url and --output")
    result = {"schema_version": 2, "recorded_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
              "classification": "SYNTHETIC", "environment": {"platform": platform.platform(),
              "python": platform.python_version(), "method": "5 warmups then sequential HTTP GET samples"},
              "command": f"latency-probe.py --api-url {args.api_url} --ui-url {args.ui_url} --samples {args.samples}",
              "api": measure("GET API health readiness", args.api_url, args.samples, args.warmups),
              "ui": measure("GET approved Replace Card page", args.ui_url, args.samples, args.warmups)}
    Path(args.output).write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))

if __name__ == "__main__": main()
