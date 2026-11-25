"""Stop ML-Agents at a safe step boundary after a wall-clock learning budget."""
import json
import os
from pathlib import Path
import time

from mlagents.trainers.learn import main
from mlagents.trainers.trainer_controller import TrainerController
from mlagents.torch_utils import torch

torch.set_num_threads(int(os.environ.get('GF_TORCH_THREADS', '1')))
budget = float(os.environ['GF_TRAIN_SECONDS'])
if budget <= 0:
    raise ValueError('GF_TRAIN_SECONDS must be positive')
original_advance = TrainerController.advance
started = None
measurement = dict(requested_learning_seconds=budget)


def timed_advance(self, env_manager):
    global started
    now = time.perf_counter()
    if started is None:
        started = now
        measurement['started_unix_seconds'] = time.time()
    elapsed = now - started
    if elapsed >= budget:
        measurement.update(learning_seconds=elapsed, steps={name: int(trainer.get_step) for name, trainer in self.trainers.items()}, stop_reason='wall_clock_budget')
        raise KeyboardInterrupt
    return original_advance(self, env_manager)


if __name__ == '__main__':
    TrainerController.advance = timed_advance
    process_started = time.perf_counter()
    try:
        main()
    finally:
        measurement['total_seconds_including_export'] = time.perf_counter() - process_started
        Path(os.environ['GF_TIMING_FILE']).write_text(json.dumps(measurement, indent=2))
