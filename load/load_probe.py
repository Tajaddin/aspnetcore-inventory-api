"""HTTP load probe for the inventory API.

Seeds products, then drives concurrent GET /api/products requests and reports
throughput (req/s) and latency p50/p95/p99. Standard-library only.

    python load/load_probe.py --base http://localhost:5080 --requests 5000 --concurrency 50
"""
from __future__ import annotations

import argparse
import json
import statistics
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor


def _post(base: str, path: str, body: dict) -> int:
    data = json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=data, method="POST")
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return resp.status
    except urllib.error.HTTPError as e:
        return e.code


def _timed_get(base: str, path: str) -> float:
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(base + path, timeout=10) as resp:
            resp.read()
            return (time.perf_counter() - t0) * 1000 if resp.status == 200 else -1.0
    except urllib.error.HTTPError:
        return -1.0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://localhost:5080")
    ap.add_argument("--requests", type=int, default=5000)
    ap.add_argument("--concurrency", type=int, default=50)
    ap.add_argument("--out", default="load/results.json")
    args = ap.parse_args()

    cats = ["Electronics", "Apparel", "Home", "Food", "Other"]
    for i in range(50):
        _post(args.base, "/api/products", {
            "name": f"Product {i}",
            "sku": f"SKU-{i}",
            "price": 9.99 + i,
            "stockQuantity": 100,
            "category": cats[i % len(cats)],
        })

    for _ in range(100):
        _timed_get(args.base, "/api/products?size=20")

    latencies: list[float] = []
    t0 = time.perf_counter()
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        futures = [pool.submit(_timed_get, args.base, "/api/products?size=20") for _ in range(args.requests)]
        for f in futures:
            ms = f.result()
            if ms >= 0:
                latencies.append(ms)
    elapsed = time.perf_counter() - t0

    latencies.sort()
    n = len(latencies)
    summary = {
        "endpoint": "GET /api/products?size=20 (paginated, SQLite demo provider)",
        "requests_ok": n,
        "requests_total": args.requests,
        "concurrency": args.concurrency,
        "wall_seconds": round(elapsed, 3),
        "throughput_rps": round(n / elapsed, 1) if elapsed else 0.0,
        "latency_ms": {
            "p50": round(latencies[int(0.50 * n)], 2),
            "p95": round(latencies[int(0.95 * n)], 2),
            "p99": round(latencies[int(0.99 * n)], 2),
            "max": round(latencies[-1], 2),
            "mean": round(statistics.mean(latencies), 2),
        },
    }
    with open(args.out, "w") as fh:
        json.dump(summary, fh, indent=2)
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
