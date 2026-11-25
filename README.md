# GangFight

Combate físico en una plataforma para jugar con teclado y entrenar agentes de IA. El juego resuelve la locomoción y las articulaciones. La política decide qué controles usar para quedar como última participante en la arena.

## Preparar una máquina nueva

```sh
git clone https://github.com/ozycepeda/GangFight.git
cd GangFight
```

Instala Unity Hub y el Editor **6000.3.18f1**, añade esta carpeta como proyecto y espera a que Unity restaure los paquetes. Para entrenar en Apple Silicon, instala `uv` y reconstruye el entorno Python con las versiones probadas:

```sh
uv venv --python 3.10 .venv
uv pip sync --python .venv/bin/python training/benchmarks/native-lock.txt
```

Compila el jugador de entrenamiento desde el menú de Unity antes de ejecutar los comandos de entrenamiento. El repositorio incluye el código, escenas, configuración, pruebas y una copia de los resultados de IA guardados al 25 de noviembre de 2026; consulta [el inventario de resultados](results/README.md). Las compilaciones, cachés de Unity y entornos locales se generan en cada máquina.

## Jugar

Añade esta carpeta como proyecto en Unity Hub con Unity **6000.3.18f1** y abre **GangFight → Abrir arena jugable**. También puedes abrir directamente [Play.unity](Assets/GangFight/Scenes/Play.unity). Pulsa **▶ Play** en la barra superior y haz clic dentro de la pestaña **Game**. La plataforma y los luchadores aparecen al iniciar la partida. La escena comparte el mismo generador de arena y luchadores que el simulador de entrenamiento.

| Teclas | Acción |
|---|---|
| W / A / S / D | Arriba / izquierda / abajo / derecha |
| Espacio | Saltar |
| J / L | Tocar y soltar para golpear con la izquierda / derecha. Mantener para agarrar. |
| I | Alzar los brazos por encima de la cabeza mientras mantienes la tecla |
| K | Lanzar el cuerpo hacia delante para dar un cabezazo |
| , | Patear |
| R | Reiniciar la ronda |

El selector del primer rival permite jugar contra el muñeco o contra un bot. Para jugar con hasta tres rivales, inicia el ejecutable con `--gf-opponents edge,chase,passive`. Si importas una política de observaciones V4 como `Assets/GangFight/Resources/Policies/V4/Champion.onnx`, aparece la opción **Modelo**.

Mantén J y L para elevar al rival o al bloque de la arena tomándolo con ambas manos. WASD también orienta al luchador mientras lleva una presa. Para lanzarla, cambia de dirección con rapidez y suelta la última mano durante el giro. El multiplicador inicial es **1,2** sobre la velocidad que la presa lleva en la dirección tangencial del giro. Inicia el ejecutable con `--gf-throw-multiplier 1` para usar la suelta por inercia natural. En el Editor, el valor inicial se ajusta en `RunOptions.DefaultThrowMultiplier`.

La arena cuadrada mide aproximadamente **12,25 m por lado**. Cada luchador pesa 8,6 kg y sus piernas son un 30 % más cortas que en la revisión anterior. El giro responde más rápido y la carrera tiene un objetivo de 5,2 m/s. K da un cabezazo con más alcance e inclinación. En el aire, WASD orienta los ataques y la primera acción aérea aporta un impulso horizontal. Prueba una patada voladora con **D + Espacio + coma**.

La barra de cada luchador indica cuánto aturdimiento lleva acumulado. El primer KO de cada luchador dura 2 s. Cada KO siguiente suma 1 s y la cuenta vuelve a cero al empezar otra ronda. Cruzar el límite inferior elimina al luchador. Gana quien quede en la plataforma. Una caída simultánea de los últimos participantes produce un empate.

El menú **GangFight → Compilar** genera jugadores locales. Para macOS se configura ARM64. El destino Windows es x86-64. Instala el módulo de compilación correspondiente en Unity Hub.

## Observaciones físicas V4

Unity entrega directamente a la política el estado presente de los cuerpos y ataques. La interfaz `GangFightFighterV4` contiene **526 valores**, sin imágenes y sin necesitar una LSTM para reconstruir estos datos.

| Bloque | Valores |
| --- | ---: |
| Resumen del luchador, rivales, bordes y ventilador | 86 |
| Estado detallado del propio luchador | 110 |
| Estado detallado de hasta tres rivales | 330 |
| Total | 526 |

Cada bloque detallado tiene seis cuerpos en orden fijo: tronco, cabeza, brazo izquierdo, pierna izquierda, brazo derecho y pierna derecha. Cada cuerpo aporta posición relativa a su tronco (3), orientación como ejes frontal y superior (6), velocidad lineal (3) y angular (3). Los vectores se expresan en los ejes del observador. Posiciones se dividen entre 2 metros, velocidades lineales entre 16 m/s y angulares entre 24 rad/s; las componentes se acotan a [-1, 1]. No se modifican los cuerpos ni los cálculos físicos.

Los otros 20 valores corresponden a cuatro ataques: puño izquierdo, puño derecho, cabezazo y patada. Para cada uno se entrega una fase como cuatro indicadores (inactivo, preparación, impacto, recuperación) y el avance normalizado dentro de su duración. Los datos reflejan ataques ya iniciados, no controles pendientes ni decisiones futuras del rival. Los bloques de rivales inexistentes o eliminados se llenan con ceros.

Se mantienen las ocho ramas de controles y las decisiones a 25 Hz. Las entradas nuevas están disponibles tanto durante entrenamiento como al ejecutar un modelo en el juego. Usa modelos V4 en `Resources/Policies/V4/`; los modelos anteriores y sus resultados se conservan, pero sus 86 entradas no son compatibles. La comprobación `GANGFIGHT_OBSERVATIONS_V4_OK` valida normalización, ausencia de rivales, cambios de fase y reinicio por KO.

## Entrenamiento nativo en Apple Silicon

En un Mac con Apple Silicon usa el entrenador Python ARM64 y el jugador macOS ARM64. Prepara `.venv` con los pasos anteriores. `GangFight → Compilar → macOS para entrenamiento` genera la compilación sin cambiar la jugable. Desde línea de comandos utiliza `GangFight.Editor.BuildGangFight.MacTraining`.

```sh
GF_TORCH_THREADS=1 .venv/bin/python training/benchmarks/train_native.py \
  training/config/rapido.yaml --env=builds/macOS-training/GangFight.app \
  --num-envs=2 --no-graphics --seed=41 --run-id=passive-nativo-s41 \
  --results-dir=results --env-args --gf-mode passive --gf-arenas 4
```

Este perfil usa observaciones del estado, CPU y simulación acelerada. Mantiene la física a 50 Hz y las decisiones a 25 Hz por agente; no necesita imágenes. Para autojuego utiliza `autojuego.yaml` y `--gf-mode selfplay`. Guarda corridas distintas para cada arquitectura y semilla.

Consulta el [procedimiento reproducible](training/benchmarks/PROCEDIMIENTO.md) y el [análisis del primer experimento](training/benchmarks/informe.md). La [prueba de 1000 segundos](training/benchmarks/comparacion_1000s.md) compara las victorias y la supervivencia después de ampliar el entrenamiento. Los checkpoints y modelos quedan en `results/`; las redes del barrido son pruebas de rendimiento, no campeonas listas para jugar.

## Compilar y entrenar en Docker

En Unity Hub instala el módulo **Linux Build Support (Mono)** para Unity 6000.3.18f1. En el Editor elige **GangFight → Compilar → Linux para entrenamiento**. La salida es `builds/Linux/`, con el ejecutable y todos sus archivos de datos. Para automatizar la compilación en un Editor activado puedes invocar el método `GangFight.Editor.BuildGangFight.Linux`:

```sh
"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$PWD" \
  -buildTarget Linux64 -executeMethod GangFight.Editor.BuildGangFight.Linux \
  -logFile -
```

Desde la raíz de GangFight, construye la imagen para Linux AMD64. Docker Desktop en Apple Silicon ejecuta esa imagen bajo emulación. En Windows x86-64 se puede usar Docker Desktop con WSL2. Mantén instalado el Editor en el host y monta el directorio de resultados en el contenedor.

```sh
docker build --platform linux/amd64 -f training/docker/Dockerfile -t gangfight:local .
mkdir -p results
docker run --rm --platform linux/amd64 \
  --mount "type=bind,source=$PWD/results,target=/results" \
  --entrypoint /opt/gangfight/build/GangFight.x86_64 gangfight:local \
  -batchmode -nographics -logFile - --gf-mode smoke
```

La prueba del simulador debe terminar con `GANGFIGHT_SMOKE_OK`. Comprueba el espacio de acciones, las 526 observaciones, los impactos de cada ataque, los agarres, el combate aéreo, la recuperación tras un KO y el reglamento con dos arenas aisladas. El entrenador se arranca después con una sola instancia y el perfil de 100.000 decisiones.

```sh
docker run --rm --platform linux/amd64 \
  --mount "type=bind,source=$PWD/results,target=/results" \
  gangfight:local /opt/gangfight/config/prueba.yaml \
  --env=/opt/gangfight/build/GangFight.x86_64 --num-envs=1 --no-graphics \
  --seed=41 --run-id=prueba-s41 --results-dir=/results \
  --env-args --gf-mode passive
```

Los comandos están escritos para una shell POSIX. En PowerShell usa `New-Item -Force -ItemType Directory results`, sustituye las barras de continuación por el acento grave o pon cada orden en una sola línea. El directorio de resultados se puede mover entre equipos con la corrida detenida.

| Etapa | Archivo de entrenamiento | Argumentos del simulador |
|---|---|---|
| Expulsar al muñeco | `duelo.yaml` | `--gf-mode passive` |
| Experimentar con recompensa auxiliar | `duelo.yaml` | `--gf-mode passive --gf-shaping` |
| Duelo contra bot | `duelo.yaml` | `--gf-mode chase` o `--gf-mode edge` |
| Autojuego 1v1 | `autojuego.yaml` | `--gf-mode selfplay` |
| Todos contra todos | `duelo.yaml` | `--gf-mode ffa --gf-opponents edge,model:Champion,random` |
| Tamaño y ventilador variables | `arena_variable.yaml` | `--gf-mode ffa --gf-opponents edge,chase` |

`--env-args` va al final. Añade `--gf-arenas 2` a los argumentos del simulador para probar dos arenas por proceso. En un todos contra todos puedes alternar oponentes cada ronda con `--gf-opponent-pool edge,chase,model:Champion`. El entrenador inicia sus propios procesos con `--num-envs`. Pon `--resume` antes de `--env-args` con el mismo identificador para retomar un punto de control. Para cambiar de etapa, usa un identificador nuevo e indica `--initialize-from ID_ANTERIOR`. Las ramas, dimensiones y frecuencia de la interfaz deben coincidir. Para comprobar la reanudación, revisa que el contador supere el último paso guardado.

El ONNX final queda en `results/ID/GangFightFighterV4.onnx`. Copia los modelos de esta versión a `Assets/GangFight/Resources/Policies/V4/`. Reconstruye el ejecutable para usarlos en Docker como `model:Nombre`. Inicia una corrida nueva para observaciones V4: los checkpoints V3 de 86 entradas no se pueden reanudar con 526 entradas. Los controles y las repeticiones de combate conservan la versión 3.

En este Mac con Apple Silicon se compiló el simulador Linux x86-64, pero Mono falló al arrancarlo bajo emulación. La prueba de integración de combate V2 completó una corrida corta y exportó ONNX con el jugador macOS ARM64 sin gráficos y el entrenador en Docker, comunicados por un puerto publicado solo en `127.0.0.1`. [Incidencia de arranque de Mono en Docker AMD64 sobre Apple Silicon](https://github.com/mono/mono/issues/21423)

## Medir y comparar

El evaluador enfrenta bots y políticas congeladas. El primer rol es el evaluado. Para cada semilla, rota las posiciones iniciales. Guarda eventos, registros del jugador, resultados por ronda y un resumen con tasa de victorias e intervalo de Wilson. Usa rivales y semillas diferentes para seleccionar e informar resultados.

```sh
docker run --rm --platform linux/amd64 \
  --mount "type=bind,source=$PWD/results,target=/results" \
  --entrypoint python gangfight:local /opt/gangfight/evaluation/evaluate.py \
  --player /opt/gangfight/build/GangFight.x86_64 \
  --roles model:Champion,edge --rounds 200 --seed 9001 \
  --jobs 2 --output /results/eval-duelo
```

Repite las mediciones con 1, 2, 4 y 8 procesos usando la misma escena y hardware. Las métricas de TensorBoard incluyen decisiones por segundo de cada proceso, pasos físicos, duración de las rondas y cortes por tiempo. En autojuego el simulador cuenta las decisiones de ambos lados, incluida la política histórica. Para comparar la velocidad efectiva del aprendizaje usa el incremento de pasos del entrenador y el tiempo de reloj. Consulta TensorBoard desde el host con `tensorboard --logdir results`.

Para reproducir controles, inicia el jugador interactivo con `--gf-events ruta.jsonl --gf-record-commands`. Termina una ronda. Pasa esa grabación al jugador con `--gf-mode evaluate --gf-events replay.jsonl --gf-replay ruta.jsonl`. Repite con `--gf-replay-agent` y otra salida para aplicar la misma secuencia a través del decodificador de acciones de IA. Usa la misma compilación, semilla, tamaño y ventilador. Puedes elegir otra ronda grabada mediante `--gf-replay-round N`. Añade `--gf-record-commands` también al generar las dos repeticiones y compáralas con:

```sh
python3 training/evaluation/compare_replays.py replay.jsonl replay-agente.jsonl
```

## Pruebas sin Unity

Las pruebas de reglas y comandos compilan el mismo código que usa el juego. Solo requieren .NET 8.

```sh
dotnet run --project tests/Rules.csproj
python3 -m unittest discover -s tests
```

Consulta el [plan de implementación](plan_implementacion.md) para los objetivos del prototipo y los experimentos de entrenamiento.
