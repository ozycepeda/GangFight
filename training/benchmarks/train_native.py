"""ML-Agents entry point with an explicit PyTorch CPU thread count."""
import os
from mlagents.trainers.learn import main
from mlagents.torch_utils import torch

torch.set_num_threads(int(os.environ.get('GF_TORCH_THREADS', '1')))
if __name__ == '__main__':
    main()
