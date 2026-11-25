#!/bin/sh
# Usage from project root: sh training/benchmarks/run_timed.sh results/new-1000s
set -eu
root=${1:?Choose a new results directory}
if test -e "$root"; then
  echo 'The output directory already exists; choose a new one.' >&2
  exit 1
fi
mkdir -p "$root"
root=$(cd "$root" && pwd)
GF_TRAIN_SECONDS=1000 GF_TIMING_FILE="$root/timing.json" GF_TORCH_THREADS=1 \
OMP_NUM_THREADS=1 MKL_NUM_THREADS=1 VECLIB_MAXIMUM_THREADS=1 \
.venv/bin/python training/benchmarks/train_timed.py training/config/prueba_1000s.yaml \
  --env=builds/macOS-training/GangFight.app --run-id=128x2-a4 \
  --results-dir="$root" --seed=41 --num-envs=1 --torch-device=cpu --no-graphics \
  --env-args --gf-mode passive --gf-arenas 4 --gf-seed 41 --gf-shaping \
  > "$root/train.log" 2>&1
.venv/bin/python training/evaluation/evaluate_policy.py \
  --player builds/macOS-training/GangFight.app \
  --checkpoint "$root/128x2-a4/GangFightFighterV4/checkpoint.pt" \
  --rounds 40 --seed 9001 --output "$root/evaluation.json" \
  > "$root/evaluation.log" 2>&1
