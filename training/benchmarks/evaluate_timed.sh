#!/bin/sh
# Re-evaluate the historical V3 experiment using the archived V3 player.
set -eu
root=results/pilot-1000s
player=builds/archive-v3/GangFight.app
.venv/bin/python training/evaluation/evaluate_policy.py --player "$player" \
  --checkpoint "$root/128x2-a4/GangFightFighterV3/checkpoint.pt" \
  --rounds 40 --seed 9001 --output "$root/evaluation.json" > "$root/evaluation.log" 2>&1
.venv/bin/python training/evaluation/evaluate_policy.py --player "$player" \
  --checkpoint "$root/control-100k/GangFightFighterV3.pt" \
  --rounds 40 --seed 9001 --output "$root/control-100k/evaluation.json" > "$root/control-100k/evaluation.log" 2>&1
.venv/bin/python training/benchmarks/compare_timed.py
