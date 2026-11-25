import importlib.util
from pathlib import Path
import unittest


path = Path(__file__).resolve().parents[1] / "training" / "evaluation" / "evaluate.py"
spec = importlib.util.spec_from_file_location("evaluate", path)
evaluate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evaluate)


class EvaluationTest(unittest.TestCase):
    def test_winner_and_attributed_elimination(self):
        events = [
            {"type": "grab_left", "slot": 0},
            {"type": "lift_with_grip", "slot": 0},
            {"type": "elimination", "slot": 1, "other": 0},
            {"type": "winner", "slot": 0, "elapsed_ticks": 50},
        ]
        record = evaluate.summarize_round(events, 101, 1)
        self.assertEqual(record["winner"], 0)
        self.assertEqual(record["learner_ejections"], 1)
        self.assertEqual(record["learner_grips"], 1)
        self.assertEqual(record["learner_lifts"], 1)
        self.assertEqual(record["seconds"], 1)

    def test_timeout_remains_distinct_from_self_ejection(self):
        events = [
            {"type": "elimination", "slot": 0, "other": -1},
            {"type": "timeout", "slot": -1, "elapsed_ticks": 3000},
        ]
        record = evaluate.summarize_round(events, 101, 0)
        self.assertTrue(record["learner_self_ejection"])
        self.assertEqual(record["outcome"], "timeout")
        self.assertEqual(record["winner"], -1)

    def test_wilson_interval(self):
        low, high = evaluate.wilson(50, 100)
        self.assertAlmostEqual(low, 1 - high)
        self.assertLess(low, .5)
        self.assertGreater(high, .5)


if __name__ == "__main__":
    unittest.main()
