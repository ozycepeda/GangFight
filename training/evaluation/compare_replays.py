"""Compare direct and agent-decoded replays from a common recorded round."""

import argparse
from math import dist
from pathlib import Path

from evaluate import read_events


def commands(events):
    return {
        (event["elapsed_ticks"], event["slot"]): event
        for event in events if event["type"] == "command"
    }


def comparable_events(events):
    return [
        (event["type"], event["elapsed_ticks"], event["slot"], event.get("other", -1))
        for event in events if event["type"] not in {"command", "layout"}
    ]


def compare(direct, agent, tolerance):
    base = commands(direct)
    other = commands(agent)
    if not base or base.keys() != other.keys():
        raise ValueError("The replays have different command ticks or fighters.")
    if comparable_events(direct) != comparable_events(agent):
        raise ValueError("The replays have different combat events.")
    worst = 0.0
    for where, event in base.items():
        replay = other[where]
        if any(event[key] != replay[key] for key in ("x", "z", "buttons")):
            raise ValueError(f"Different command at tick and slot {where}.")
        delta = dist(event["position"], replay["position"])
        if delta > tolerance:
            raise ValueError(f"Position differs by {delta:.4f} m at tick and slot {where}.")
        worst = max(worst, delta)
    return len(base), worst


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("direct", type=Path)
    cli.add_argument("agent", type=Path)
    cli.add_argument("--tolerance", type=float, default=.03)
    args = cli.parse_args()
    if args.tolerance < 0:
        cli.error("Tolerance must be nonnegative.")
    count, worst = compare(read_events(args.direct), read_events(args.agent), args.tolerance)
    print(f"{count} commands, maximum position difference {worst:.4f} m")


if __name__ == "__main__":
    main()
