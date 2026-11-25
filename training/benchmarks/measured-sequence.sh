#!/bin/sh
set -eu
PY=.venv/bin/python
PLAYER=builds/macOS-training/GangFight.app
$PY training/benchmarks/run.py --player builds/macOS/GangFight.app --output results/benchmark-reference-free --cases 128x2-a1 --torch-threads 4
$PY training/benchmarks/run.py --player "$PLAYER" --output results/benchmark-t4-free --cases 128x2-a1 --torch-threads 4
$PY training/benchmarks/run.py --player "$PLAYER" --output results/benchmark-t1-free --cases 128x2-a1 --torch-threads 1
$PY training/benchmarks/run.py --player "$PLAYER" --output results/benchmark-p2 --cases 128x2-a4 --num-envs 2 --torch-threads 1
$PY training/benchmarks/run.py --player "$PLAYER" --output results/benchmark-p4 --cases 128x2-a2 --num-envs 4 --torch-threads 1
$PY training/benchmarks/run.py --player "$PLAYER" --output results/benchmark-p8 --cases 128x2-a1 --num-envs 8 --torch-threads 1
$PY training/benchmarks/run.py --player "$PLAYER" --output results/pilot-100k --cases 128x2-a4 --steps 100000 --torch-threads 1 --shaping
$PY training/evaluation/evaluate_policy.py --player "$PLAYER" --checkpoint results/pilot-100k/128x2-a4/GangFightFighterV3/checkpoint.pt --rounds 40 --output results/pilot-100k/evaluation.json > results/pilot-100k/evaluation.log 2>&1
