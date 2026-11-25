"""Verify that current physics observations reach the existing V4 actor unchanged."""
from pathlib import Path
import json
import numpy as np
from mlagents.torch_utils import torch
from mlagents.trainers.policy.torch_policy import TorchPolicy
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel

root=Path(__file__).resolve().parent
project=root.parent.parent
torch.set_num_threads(1)
channel=EngineConfigurationChannel()
channel.set_configuration_parameters(time_scale=20,quality_level=0,target_frame_rate=-1)
env=UnityEnvironment(file_name=str(project/'builds/macOS-training/GangFight.app'),seed=41,no_graphics=True,base_port=61753,
                     side_channels=[channel,StatsSideChannel()],additional_args=['--gf-mode','passive','--gf-arenas','1','--gf-seed','41','--gf-fan','0'])
samples=[]
seen=[]
try:
    env.reset()
    name=next(iter(env.behavior_specs))
    spec=env.behavior_specs[name]
    assert spec.observation_specs[0].shape==(526,)
    assert list(spec.action_spec.discrete_branches)==[3,3,2,2,2,2,2,2]
    policy=TorchPolicy(41,spec,NetworkSettings(hidden_units=128,num_layers=2,normalize=False),SimpleActor,dict(conditional_sigma=False,tanh_squash=False))
    weights=torch.load(project/'results/observations-v4-integration/128x2-a4/GangFightFighterV4/checkpoint.pt',map_location='cpu')['Policy']
    policy.load_weights(weights)
    policy.actor.eval()
    hook=policy.actor.network_body._body_endoder.seq_layers[0].register_forward_pre_hook(lambda module,args: seen.append(args[0].detach().cpu().numpy().copy()))
    for step in range(120):
        decisions, terminal=env.get_steps(name)
        assert not len(terminal), f'Unexpected terminal at probe step {step}'
        if not len(decisions):
            env.step()
            continue
        current=decisions.obs[0]
        assert current.shape==(1,526) and np.isfinite(current).all() and np.abs(current).max()<=1.00001
        policy.get_action(decisions)
        np.testing.assert_array_equal(seen[-1],current)
        samples.append(current[0].copy())
        actions=np.zeros((1,8),dtype=np.int32)
        if 10<=step<30:
            actions[:,0]=2
        elif 30<=step<50:
            actions[:,0]=1
        if 60<=step<80:
            actions[:,6]=1
        if 85<=step<105:
            actions[:,7]=1
        if step==108:
            actions[:,3]=1
        env.set_actions(name,ActionTuple(discrete=actions))
        env.step()
    hook.remove()
finally:
    env.close()

values=np.asarray(samples)
bodies=values[:,86:176].reshape(-1,6,15)
changes={key:float(np.ptp(bodies[:,:,start:end],axis=0).max()) for key,start,end in [('relative_positions',0,3),('orientations',3,9),('linear_velocities',9,12),('angular_velocities',12,15)]}
assert all(v>.001 for v in changes.values()),changes
phases=set(int(x) for x in values[:,186:190].argmax(axis=1))
assert phases=={0,1,2,3},phases
result=dict(behavior=name,observations=526,actor_forward_passes=len(seen),inputs_equal_unity_observations=True,finite_and_normalized=True,changed_physics_channels=changes,headbutt_phases_seen=sorted(phases),action_branches=list(map(int,spec.action_spec.discrete_branches)))
(root/'live-input-verification.json').write_text(json.dumps(result,indent=2))
np.savez_compressed(root/'observations.npz',observations=values)
print(json.dumps(result,indent=2))
