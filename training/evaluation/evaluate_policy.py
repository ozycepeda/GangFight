"""Evaluate a frozen PPO checkpoint with a matching Unity observation contract."""
import argparse
from collections import Counter
import json
from pathlib import Path
import time
import numpy as np

from mlagents.torch_utils import torch
from mlagents.trainers.policy.torch_policy import TorchPolicy
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel
from evaluate import wilson
from policy_contract import validate_policy_input


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--player', required=True)
    source = cli.add_mutually_exclusive_group(required=True)
    source.add_argument('--checkpoint', type=Path)
    source.add_argument('--random-actions', action='store_true')
    cli.add_argument('--units', type=int, default=128)
    cli.add_argument('--layers', type=int, default=2)
    cli.add_argument('--opponents', default='passive,chase,edge')
    cli.add_argument('--rounds', type=int, default=100)
    cli.add_argument('--seed', type=int, default=9001)
    cli.add_argument('--output', type=Path, required=True)
    args = cli.parse_args()
    if args.rounds < 1:
        cli.error('--rounds must be positive')
    args.output = args.output.resolve()
    if args.output.exists():
        cli.error('--output already exists')
    torch.set_num_threads(1)
    weights = torch.load(args.checkpoint, map_location='cpu')['Policy'] if args.checkpoint else None
    args.output.parent.mkdir(parents=True, exist_ok=True)
    results=[]
    for rival in args.opponents.split(','):
        torch.manual_seed(args.seed)
        np.random.seed(args.seed)
        channel=EngineConfigurationChannel()
        channel.set_configuration_parameters(time_scale=100, quality_level=0, target_frame_rate=-1)
        env=UnityEnvironment(file_name=str(Path(args.player).resolve()), seed=args.seed, no_graphics=True,
            side_channels=[channel, StatsSideChannel()], additional_args=['--gf-mode',rival,'--gf-arenas','4','--gf-seed',str(args.seed), '--gf-events', str(args.output.with_suffix(''))+'-'+rival+'.jsonl'])
        started=time.perf_counter()
        try:
            env.reset()
            name=next(iter(env.behavior_specs))
            observation_count = sum(int(np.prod(obs.shape)) for obs in env.behavior_specs[name].observation_specs)
            if weights is not None:
                validate_policy_input(weights, observation_count, name)
            policy=TorchPolicy(args.seed, env.behavior_specs[name], NetworkSettings(hidden_units=args.units,num_layers=args.layers),SimpleActor,dict(conditional_sigma=False,tanh_squash=False))
            if weights is not None:
                policy.load_weights(weights)
            policy.actor.eval()
            outcomes=[]
            decisions=0
            while len(outcomes)<args.rounds:
                active, ended=env.get_steps(name)
                for reward, interrupted in zip(ended.reward,ended.interrupted):
                    outcomes.append('timeout' if interrupted else 'win' if reward>0 else 'loss' if reward<0 else 'draw')
                if len(outcomes)>=args.rounds:
                    break
                if len(active):
                    action = policy.get_action(active).action if weights is not None else env.behavior_specs[name].action_spec.random_action(len(active))
                    env.set_actions(name, action)
                    decisions+=len(active)
                if decisions>args.rounds*3000+10000:
                    raise RuntimeError('Evaluation exceeded expected decision budget')
                env.step()
            counts=Counter(outcomes[:args.rounds])
            record=dict(policy=str(args.checkpoint) if args.checkpoint else 'uniform random actions', behavior=name, observation_count=observation_count, opponent=rival,rounds=args.rounds,seed=args.seed,arenas=4,sampling='seeded stochastic ML-Agents policy; frozen weights' if weights is not None else 'seeded uniform random discrete actions',outcomes=dict(counts),win_rate=counts['win']/args.rounds,wilson_95=wilson(counts['win'],args.rounds),seconds=time.perf_counter()-started)
            results.append(record)
            print(json.dumps(record),flush=True)
        finally:
            env.close()
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(results,indent=2))


if __name__=='__main__':
    main()
