# Medir el entrenamiento de GangFight

El experimento mide decisiones que realmente recibe PPO por segundo, incluyendo recolección de experiencia y actualizaciones. No equivale a cuadros por segundo ni demuestra convergencia.

Usa Python 3.10 ARM64 en macOS. El entorno `.venv` de este proyecto ya está preparado. El archivo `native-lock.txt` conserva todas las versiones utilizadas. ML-Agents 1.1.0 requiere una excepción de gRPC para esta plataforma: se probó 1.68.1, fuera del límite declarado por ML-Agents (`<=1.48.2`). La comunicación y exportación se verificaron en este experimento; conserva el entorno fijado para reproducirlo. `setuptools` permanece en 75.8.0 porque ML-Agents utiliza `pkg_resources`.

Para reconstruir el entorno con uv:

```sh
uv venv --python 3.10 .venv
uv pip sync --python .venv/bin/python training/benchmarks/native-lock.txt
```

Compila desde Unity con `GangFight.Editor.BuildGangFight.MacTraining`, que genera `builds/macOS-training/GangFight.app` y deja la escena Training como primera escena. Los argumentos `--gf-mode passive` permiten también entrenar con el ejecutable jugable.

```sh
.venv/bin/python training/benchmarks/run.py \
  --player builds/macOS-training/GangFight.app \
  --output results/mi-benchmark --torch-threads 1
```

La configuración más rápida del primer barrido fue dos procesos con cuatro arenas por proceso; `README.md` incluye el comando de entrenamiento.

Cada caso usa una red nueva y semilla 41. `128x2-a4` significa dos capas ocultas de 128 unidades y cuatro arenas dentro de un proceso. `--num-envs N` añade procesos independientes; cada proceso contiene el número de arenas del caso. Para comparar procesos, ejecuta por separado con salidas distintas.

El perfil fija CPU, escala temporal 100, calidad cero, sin límite de cuadros y sin gráficos. Mantiene el paso físico de 0,02 segundos, una decisión cada dos pasos físicos, 526 observaciones V4 y las ocho ramas discretas de acciones. No reduce la precisión de articulaciones ni elimina objetos de colisión.

La medición descarta los primeros 4096 pasos y calcula la pendiente entre resúmenes de TensorBoard. `elapsed_seconds` incluye arranque, exportación y cierre; úsalo para presupuestar muchas corridas pequeñas. `steps_per_second` estima una corrida sostenida. Los registros completos, checkpoints, ONNX y temporizadores quedan junto a `summary.json`. Los contadores de tiempo de ML-Agents se anidan: no se deben sumar padre e hijos.

Para un piloto más largo y con recompensa auxiliar:

```sh
.venv/bin/python training/benchmarks/run.py \
  --player builds/macOS-training/GangFight.app \
  --output results/mi-piloto --cases 128x2-a4 \
  --steps 100000 --torch-threads 1 --shaping
```

La recompensa auxiliar ya existe en el juego y solo se permite contra `passive`. Debe retirarse al pasar a bots y autojuego. Compara las victorias en evaluación, no el valor de recompensas de fases diferentes.

Las mediciones son secuenciales. Evita compilar, ejecutar otras corridas o evaluar partidas mientras haces el barrido. Antes de extrapolar a una noche completa, repite el caso elegido durante al menos 30 minutos: temperatura, aplicaciones abiertas y duración de episodios pueden modificar el rendimiento.

Para resolver desde los requisitos mínimos en lugar del archivo completo de versiones:

```sh
uv pip install --python .venv/bin/python \
  --override training/benchmarks/overrides-native.txt \
  -r training/benchmarks/requirements-native.txt
```

Los informes de los primeros barridos y de 1000 segundos describen la interfaz histórica V3 de 86 entradas. Se conservan sin alterar sus resultados. Las configuraciones actuales entrenan V4; utiliza identificadores nuevos y no reanudes checkpoints V3.
