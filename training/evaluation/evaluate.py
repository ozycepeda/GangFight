"""Run paired headless GangFight rounds and summarize event logs."""

import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from math import sqrt
from pathlib import Path
import subprocess


def read_events(path):
    with path.open(encoding="utf-8") as stream:
        return [json.loads(line) for line in stream if line.strip()]


def summarize_round(events, seed, seat):
    endings = [event for event in events if event["type"] in {"winner", "draw", "timeout"}]
    if len(endings) != 1:
        raise ValueError(f"Expected one round result, found {len(endings)}.")
    ending = endings[0]
    eliminations = [event for event in events if event["type"] == "elimination"]
    counts = Counter((event["slot"], event["type"]) for event in events)
    return {
        "seed": seed,
        "seat_offset": seat,
        "outcome": ending["type"],
        "winner": ending["slot"],
        "seconds": ending["elapsed_ticks"] / 50,
        "eliminations": [event["slot"] for event in eliminations],
        "learner_ejections": sum(event["slot"] != 0 and event["other"] == 0 for event in eliminations),
        "ambiguous_ejections": sum(event["other"] == -2 for event in eliminations),
        "learner_self_ejection": any(event["slot"] == 0 and event["other"] == -1 for event in eliminations),
        "learner_grips": counts[0, "grab_left"] + counts[0, "grab_right"],
        "learner_lifts": counts[0, "lift_with_grip"],
        "learner_punch_hits": counts[0, "punch_left_hit"] + counts[0, "punch_right_hit"],
        "learner_headbutt_hits": counts[0, "headbutt_hit"],
        "learner_kick_hits": counts[0, "kick_hit"],
        "learner_air_hits": sum(
            event["slot"] == 0 and event.get("airborne", False) and event["type"].endswith("_hit")
            for event in events
        ),
        "learner_knockouts": sum(event["type"] == "ko" and event.get("other") == 0 for event in events),
        "learner_recoveries": counts[0, "recovered"],
    }


def wilson(wins, games):
    z = 1.96
    mean = wins / games
    denominator = 1 + z * z / games
    midpoint = (mean + z * z / (2 * games)) / denominator
    radius = z * sqrt(mean * (1 - mean) / games + z * z / (4 * games * games)) / denominator
    return [max(0, midpoint - radius), min(1, midpoint + radius)]


def run_round(args, number):
    seed = args.seed + number // len(args.roles)
    seat = number % len(args.roles)
    events = args.output / f"round-{number:04d}.jsonl"
    log = events.with_suffix(".log")
    command = [
        str(args.player), "-batchmode", "-nographics", "-logFile", str(log),
        "--gf-mode", "evaluate", "--gf-roles", ",".join(args.roles),
        "--gf-seed", str(seed), "--gf-seat-offset", str(seat),
        "--gf-events", str(events), "--gf-fan", str(args.fan),
        "--gf-half-size", str(args.half_size), "--gf-timeout", str(args.timeout),
    ]
    if args.record:
        command.append("--gf-record-commands")
    try:
        subprocess.run(command, check=True, timeout=args.process_timeout)
        record = summarize_round(read_events(events), seed, seat)
    except (subprocess.SubprocessError, OSError, ValueError) as error:
        raise RuntimeError(f"Round {number}, seed {seed}, seat {seat}. See {log}.") from error
    return record


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--player", type=Path, required=True)
    cli.add_argument("--roles", type=lambda value: value.split(","), required=True,
                     help="Slot 0 is evaluated. Use passive, random, chase, edge or model:Name.")
    cli.add_argument("--output", type=Path, required=True)
    cli.add_argument("--rounds", type=int, default=200)
    cli.add_argument("--seed", type=int, default=9001)
    cli.add_argument("--jobs", type=int, default=1)
    cli.add_argument("--fan", type=float, default=0)
    cli.add_argument("--half-size", type=float, default=5 * sqrt(1.5))
    cli.add_argument("--timeout", type=float, default=60)
    cli.add_argument("--process-timeout", type=int, default=180)
    cli.add_argument("--record", action="store_true")
    args = cli.parse_args()
    if not 2 <= len(args.roles) <= 4 or args.rounds <= 0 or args.jobs <= 0:
        cli.error("Use two to four roles and positive values for --rounds and --jobs.")
    if args.rounds % len(args.roles):
        cli.error("--rounds must be divisible by the number of slots for paired evaluation.")
    args.player = args.player.resolve()
    args.output = args.output.resolve()
    if not args.player.is_file():
        cli.error(f"Player does not exist: {args.player}")
    args.output.mkdir(parents=True, exist_ok=False)
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        records = list(pool.map(lambda number: run_round(args, number), range(args.rounds)))

    wins = sum(record["winner"] == 0 for record in records)
    summary = {
        "roles": args.roles,
        "settings": {"fan": args.fan, "half_size": args.half_size, "timeout": args.timeout},
        "player_sha256": sha256(args.player),
        "rounds": args.rounds,
        "wins": wins,
        "losses": sum(record["winner"] > 0 for record in records),
        "draws": sum(record["outcome"] == "draw" for record in records),
        "timeouts": sum(record["outcome"] == "timeout" for record in records),
        "win_rate": wins / args.rounds,
        "wilson_95": wilson(wins, args.rounds),
        "ejection_rate": sum(record["learner_ejections"] > 0 for record in records) / args.rounds,
        "ambiguous_ejections": sum(record["ambiguous_ejections"] for record in records),
        "self_ejections": sum(record["learner_self_ejection"] for record in records),
        "mean_seconds": sum(record["seconds"] for record in records) / args.rounds,
    }
    (args.output / "rounds.json").write_text(json.dumps(records, indent=2) + "\n", encoding="utf-8")
    formatted = json.dumps(summary, indent=2)
    (args.output / "summary.json").write_text(formatted + "\n", encoding="utf-8")
    print(formatted)


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


if __name__ == "__main__":
    main()
