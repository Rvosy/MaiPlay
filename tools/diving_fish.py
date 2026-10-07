"""Prepare and upload GameplayHarness scores using the Diving-Fish API."""
import argparse
import getpass
import hashlib
import json
import math
import os
from pathlib import Path
import re
import urllib.request

BASE = "https://www.diving-fish.com/api/maimaidxprober"


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def request(endpoint, token=None, body=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Import-Token"] = token
    req = urllib.request.Request(BASE + endpoint, headers=headers,
                                 data=None if body is None else json.dumps(body).encode())
    with urllib.request.build_opener(NoRedirect).open(req, timeout=60) as response:
        result = json.load(response)
    if isinstance(result, dict) and result.get("status") == "error":
        raise ValueError(result.get("message", "API error"))
    return result


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False), encoding="utf-8")


def prepare(report, music, side=None):
    catalog = {int(song["id"]): song for song in music}
    records, excluded, seen = [], [], set()
    for chart in report["charts"]:
        path = chart["path"]
        metrics = chart[side] if side else chart
        difficulty = chart.get("difficulty", report["difficulty"])
        if difficulty not in (5, 6):
            excluded.append({"path": path, "difficulty": difficulty,
                             "reason": "仅上传 Master 和 Re:Master"})
            continue
        match = re.search(r"(?:^|/)(\d+)_([^/]+)/maidata\.txt$", path.replace("\\", "/"))
        song = catalog.get(int(match[1])) if match else None
        index = difficulty - 2
        reason = None
        if metrics["status"] not in ("all-cp", "partial"):
            reason = "未完成评估：" + metrics["status"]
        elif song is None:
            reason = "官方歌曲编号未在水鱼曲库中找到"
        elif not 0 <= index <= 4 or index >= len(song["charts"]):
            reason = "水鱼曲库中不存在该难度"
        elif chart["notes"] != sum(song["charts"][index]["notes"]):
            reason = "音符总数不同，需核对谱面版本"
        if reason:
            excluded.append({"path": path, "difficulty": difficulty, "reason": reason})
            continue
        achievement, dx = metrics["achievement"], metrics["dxScore"]
        if not isinstance(achievement, (int, float)) or not math.isfinite(achievement) or not 0 <= achievement <= 101:
            raise ValueError(f"Invalid achievement: {path}")
        if type(dx) is not int or not 0 <= dx <= chart["notes"] * 3:
            raise ValueError(f"Invalid DX score: {path}")
        key = (song["title"], song["type"], index)
        if key in seen:
            raise ValueError(f"Duplicate chart: {key}")
        seen.add(key)
        records.append({"title": song["title"], "type": song["type"], "level_index": index,
                        "achievements": achievement, "dxScore": dx,
                        "fc": "app" if metrics["status"] == "all-cp" else "", "fs": ""})
    return records, excluded


def verify(expected, actual):
    indexed = {(r["title"], r["type"], r["level_index"]): r for r in actual["records"]}
    mismatches = []
    for record in expected:
        key = (record["title"], record["type"], record["level_index"])
        remote = indexed.get(key)
        if remote is None or any(remote.get(k) != record[k] for k in ("dxScore", "fc", "fs")) or abs(remote["achievements"] - record["achievements"]) > 0.00005:
            mismatches.append(key)
    return mismatches


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prep = commands.add_parser("prepare")
    prep.add_argument("report")
    prep.add_argument("output")
    prep.add_argument("--side", choices=["baseline", "candidate"])
    prep.add_argument("--music-data", help="Use a saved music_data JSON instead of the public API")
    upload = commands.add_parser("upload")
    upload.add_argument("bundle")
    upload.add_argument("receipt", help="New directory for before/after records and verification")
    upload.add_argument("--account", required=True, help="Expected username of the evaluation account")
    args = parser.parse_args()
    if args.command == "prepare":
        report = read(args.report)
        if ("candidate" in report) != (args.side is not None):
            parser.error("comparison.json requires --side; summary.json must omit --side")
        music = read(args.music_data) if args.music_data else request("/music_data")
        records, excluded = prepare(report, music, args.side)
        output = Path(args.output)
        output.mkdir(parents=True, exist_ok=False)
        write(output / "music-data.json", music)
        write(output / "bundle.json", {"source": str(Path(args.report).resolve()),
              "sourceSha256": hashlib.sha256(Path(args.report).read_bytes()).hexdigest(),
              "side": args.side, "records": records, "excluded": excluded})
        print(f"Prepared {len(records)} records; excluded {len(excluded)}. {output / 'bundle.json'}")
        return 0 if records else 1
    bundle = read(args.bundle)
    if not bundle["records"]:
        raise ValueError("No records to upload")
    if any(record.get("level_index") not in (3, 4) for record in bundle["records"]):
        raise ValueError("Only Master and Re:Master records may be uploaded; prepare a new bundle")
    token = os.environ.get("MAIPLAY_DIVING_FISH_IMPORT_TOKEN") or getpass.getpass("Import-Token: ")
    before = request("/player/records", token)
    if before.get("username") != args.account:
        raise ValueError("Token account does not match --account; nothing uploaded")
    receipt = Path(args.receipt)
    receipt.mkdir(parents=True, exist_ok=False)
    write(receipt / "before.json", before)
    write(receipt / "submitted.json", bundle)
    response = request("/player/update_records", token, bundle["records"])
    write(receipt / "response.json", response)
    after = request("/player/records", token)
    write(receipt / "after.json", after)
    mismatches = verify(bundle["records"], after)
    write(receipt / "verification.json", {"submitted": len(bundle["records"]), "mismatches": mismatches})
    if mismatches:
        raise ValueError(f"Read-back mismatch for {len(mismatches)} charts; inspect receipt")
    print(f"Verified {len(bundle['records'])} uploaded records. Receipt: {receipt}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
