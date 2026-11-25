FROM python:3.10.12-slim-bookworm@sha256:4d440b214e447deddc0a94de23a3d97d28dfafdf125a8b4bb8073381510c9ee2 AS trainer
ENV PYTHONUNBUFFERED=1 PIP_DISABLE_PIP_VERSION_CHECK=1
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates libatomic1 libgl1 libglib2.0-0 libx11-6 libxext6 libxrandr2 libxcursor1 libxi6 \
    && rm -rf /var/lib/apt/lists/*
COPY training/docker/requirements.txt /tmp/requirements.txt
RUN --mount=type=cache,target=/root/.cache/pip \
    python -m pip install -r /tmp/requirements.txt && python -m pip check
WORKDIR /opt/gangfight
COPY training/config/ config/
COPY training/evaluation/ evaluation/
RUN mkdir -p /results

FROM trainer AS gangfight
COPY builds/Linux/ build/
RUN test -f build/GangFight.x86_64 && chmod +x build/GangFight.x86_64
ENTRYPOINT ["mlagents-learn"]
CMD ["/opt/gangfight/config/duelo.yaml", "--env=/opt/gangfight/build/GangFight.x86_64", "--num-envs=1", "--no-graphics", "--seed=41", "--run-id=duelo-s41", "--results-dir=/results", "--env-args", "--gf-mode", "passive"]
