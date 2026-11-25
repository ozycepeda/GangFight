#!/bin/sh
# Run from the GangFight project root, after compiling MacTraining.
set -eu
player=builds/macOS-training/GangFight.app
root=${1:-results/benchmark-followup}
for processes in 1 2 4 8; do
  .venv/bin/python training/benchmarks/run.py --player "$player" \
    --output "$root/processes-$processes" --cases 128x2-a4 \
    --num-envs "$processes" --torch-threads 1
done
.venv/bin/python training/benchmarks/run.py --player builds/macOS/GangFight.app \
  --output "$root/baseline" --cases 128x2-a1 --torch-threads 4
.venv/bin/python training/benchmarks/run.py --player "$player" \
  --output "$root/optimized-control" --cases 128x2-a1 --torch-threads 4
.venv/bin/python training/benchmarks/run.py --player "$player" \
  --output "$root/single-thread-control" --cases 128x2-a1 --torch-threads 1
.venv/bin/python training/benchmarks/run.py --player "$player" \
  --output "$root/pilot" --cases 128x2-a4 --steps 100000 --torch-threads 1 --shaping
.venv/bin/python training/evaluation/evaluate_policy.py --player "$player" \
  --checkpoint "$root/pilot/128x2-a4/GangFightFighterV4/checkpoint.pt" \
  --rounds 100 --output "$root/evaluation.json"
