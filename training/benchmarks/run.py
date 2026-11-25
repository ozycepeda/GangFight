"""Reproducible native PPO throughput sweep. Never overwrites a run."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import yaml
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--player', type=Path, required=True)
    cli.add_argument('--output', type=Path, required=True)
    cli.add_argument('--cases', default='128x2-a1,128x2-a2,128x2-a4,128x2-a8,64x2-a4,256x2-a4,512x3-a4')
    cli.add_argument('--steps', type=int, default=24576)
    cli.add_argument('--mode', default='passive')
    cli.add_argument('--graphics', action='store_true')
    cli.add_argument('--num-envs', type=int, default=1)
    cli.add_argument('--seed', type=int, default=41)
    cli.add_argument('--shaping', action='store_true')
    cli.add_argument('--torch-threads', type=int, default=4)
    cli.add_argument('--process-timeout', type=float, default=3600)
    args = cli.parse_args()
    if args.steps <= 4096 or args.num_envs < 1 or args.torch_threads < 1:
        cli.error('Use more than 4096 steps and positive process/thread counts.')
    if args.shaping and args.mode != 'passive':
        cli.error('--shaping requires --mode passive.')
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=False)
    base = yaml.safe_load(Path('training/config/autojuego.yaml' if args.mode == 'selfplay' else 'training/config/prueba.yaml').read_text())
    behavior_name = next(iter(base['behaviors']))
    results = []
    for case in args.cases.split(','):
        dims, arenas = case.split('-a')
        units, layers = map(int, dims.split('x'))
        config = yaml.safe_load(yaml.safe_dump(base))
        behavior = config['behaviors'][behavior_name]
        behavior['network_settings'].update(hidden_units=units, num_layers=layers)
        behavior.update(max_steps=args.steps, summary_freq=2048, checkpoint_interval=args.steps, keep_checkpoints=1)
        config['engine_settings'] = dict(time_scale=100, target_frame_rate=-1, quality_level=0, capture_frame_rate=0)
        config['torch_settings'] = dict(device='cpu')
        cfg = args.output / (case + '.yaml')
        cfg.write_text(yaml.safe_dump(config, sort_keys=False))
        command = [sys.executable, str(Path(__file__).with_name('train_native.py').resolve()), str(cfg), '--env='+str(args.player.resolve()), '--run-id='+case, '--results-dir='+str(args.output), '--seed='+str(args.seed), '--num-envs='+str(args.num_envs), '--torch-device=cpu']
        if not args.graphics:
            command += ['--no-graphics']
        command += ['--env-args', '--gf-mode', args.mode, '--gf-arenas', arenas, '--gf-seed', str(args.seed)]
        if args.shaping:
            command.append('--gf-shaping')
        started = time.perf_counter()
        with (args.output / (case+'.log')).open('w') as log:
            run = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, env={**os.environ, 'GF_TORCH_THREADS':str(args.torch_threads), 'OMP_NUM_THREADS':'1', 'MKL_NUM_THREADS':'1', 'VECLIB_MAXIMUM_THREADS':'1'}, timeout=args.process_timeout)
        elapsed = time.perf_counter() - started
        record = dict(case=case, behavior=behavior_name, num_envs=args.num_envs, torch_threads=args.torch_threads, seed=args.seed, shaping=args.shaping, units=units, layers=layers, arenas=int(arenas), mode=args.mode, graphics=args.graphics, elapsed_seconds=elapsed, returncode=run.returncode, steps_requested=args.steps)
        if run.returncode == 0:
            acc = EventAccumulator(str(args.output / case / behavior_name)).Reload()
            points = acc.Scalars('Policy/Entropy')
            if len(points) > 2:
                start, end = points[1], points[-1]
                record.update(measured_steps=end.step-start.step, measured_seconds=end.wall_time-start.wall_time, steps_per_second=(end.step-start.step)/(end.wall_time-start.wall_time), final_step=end.step)
            record['scalars'] = {tag: [dict(step=p.step, value=p.value, wall_time=p.wall_time) for p in acc.Scalars(tag)] for tag in acc.Tags()['scalars']}
            record['onnx'] = str(args.output / case / (behavior_name + '.onnx'))
        results.append(record)
        (args.output / 'summary.json').write_text(json.dumps(results, indent=2))
        print(json.dumps({k:v for k,v in record.items() if k != 'scalars'}), flush=True)
        if run.returncode:
            raise RuntimeError('Training failed; see '+str(args.output/(case+'.log')))


if __name__ == '__main__':
    main()
