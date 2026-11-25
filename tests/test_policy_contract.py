import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest


path = Path(__file__).resolve().parents[1] / 'training/evaluation/policy_contract.py'
spec = importlib.util.spec_from_file_location('policy_contract', path)
contract = importlib.util.module_from_spec(spec)
spec.loader.exec_module(contract)


class PolicyContractTest(unittest.TestCase):
    def test_rejects_v3_checkpoint_for_v4_player(self):
        weights = {'network_body._body_endoder.seq_layers.0.weight': SimpleNamespace(shape=(128, 86))}
        with self.assertRaisesRegex(ValueError, '86 observations.*526'):
            contract.validate_policy_input(weights, 526, 'GangFightFighterV4')

    def test_accepts_the_matching_input_contract(self):
        weights = {'network_body._body_endoder.seq_layers.0.weight': SimpleNamespace(shape=(128, 526))}
        contract.validate_policy_input(weights, 526, 'GangFightFighterV4')
