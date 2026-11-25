"""Validate the vector input contract before loading a frozen MLP policy."""


def validate_policy_input(weights, observation_count, behavior):
    first = weights['network_body._body_endoder.seq_layers.0.weight']
    checkpoint_count = first.shape[1]
    if checkpoint_count != observation_count:
        raise ValueError(
            f'Checkpoint has {checkpoint_count} observations, but {behavior} requires '
            f'{observation_count}. Train a new policy for this observation contract; '
            'do not resume a V3 checkpoint with V4 inputs.'
        )
