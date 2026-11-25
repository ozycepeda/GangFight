# Capacidad de entrenamiento neuronal de GangFight

Experimento del 24 de noviembre de 2026, MacBook Air Apple M5, CPU de 10 núcleos y 32 GB de RAM. Unity 6000.3.18f1, ML-Agents 1.1.0 y PyTorch 2.1.2 ARM64 sobre CPU.

El equipo permite experimentar localmente con redes pequeñas y medianas. La configuración más rápida medida produjo **1594 decisiones de aprendizaje por segundo**, con 2 procesos y 4 arenas por proceso. A esa velocidad, 5 millones de decisiones requieren aproximadamente **52.3 min**. Este presupuesto mide cómputo; la cantidad necesaria para aprender a combatir sigue dependiendo de la exploración y las recompensas.

## Qué se optimizó y qué se conservó

Unity permite arrancar el jugador con `-batchmode -nographics`, sin inicializar el dispositivo gráfico. El proyecto ya evitaba mallas y materiales en ese modo. Se extendió la separación para omitir también los objetos decorativos vacíos de plataforma, ventilador y luchadores: aproximadamente 36 objetos por arena en un duelo. Se añadió una compilación macOS ARM64 específica para entrenamiento, un perfil acelerado y herramientas para medir y evaluar.

El entrenamiento usa 86 valores del estado, ocho ramas discretas de acción y ninguna imagen. Mantiene cuerpos, colisiones, articulaciones, ataques y agarres, paso físico de 0,02 s y una decisión cada dos pasos. Reducir los gráficos no priva a la red de observaciones. Reducir la precisión física o cambiar esa frecuencia sí podría alterar lo que aprende, por lo que no se hizo.

La referencia sin gráficos y con cuatro hilos dio 639 decisiones/s; la compilación optimizada dio 640. Una sola medición por variante no establece una mejora significativa por eliminar decoración. Con un hilo dio 638; tampoco aparece una ventaja clara de un hilo en este caso. El mayor beneficio medido viene de agrupar arenas y ajustar procesos.

Se instalaron Python y PyTorch ARM64 nativos. Se detuvieron dos contenedores antiguos de pruebas de humo de GangFight, atascados tras errores de Mono y consumiendo aproximadamente un núcleo cada uno. Las mediciones iniciales con esos procesos activos y la primera durante compilación se excluyen de las comparaciones finales. El ejecutable Linux x86-64 emulado no es la ruta recomendada para este equipo.

## Cómo se realizó la prueba

Se completaron 18 corridas y 517,792 decisiones solicitadas, incluidas las pruebas iniciales descartadas. Los barridos entrenan redes nuevas durante 24.576 decisiones, con semilla 41, PPO, buffer 4.096, minibatch 128 y tres épocas. Se descartan las primeras 4.096 decisiones al estimar la velocidad mediante TensorBoard. La duración total incluye arranque, exportación ONNX y cierre. Las corridas son secuenciales.

La escala temporal 100 permite correr más rápido que el reloj sin cambiar el tamaño del paso físico. Calidad gráfica cero, sin límite de cuadros, CPU y sin gráficos. El modo del barrido es duelo contra `passive`, plataforma de semitamaño 5 y sin ventilador. Es un límite práctico medido para esta carga; un combate prolongado con muchos contactos, cuatro luchadores o ventilador puede costar más.

Las comparaciones usan una semilla de entrenamiento. Son mediciones exploratorias de rendimiento, no una clasificación estadística de qué arquitectura juega mejor. Para una corrida de varias horas conviene presupuestar un 30–50% adicional y verificar 30 minutos sostenidos por temperatura y aplicaciones abiertas.

## Paralelismo y rendimiento

Todos los casos de esta tabla usan dos capas de 128 unidades. Los casos con varios procesos conservan ocho arenas en total para comparar cómo se reparten.

| Procesos | Arenas por proceso | Arenas totales | Decisiones/s | Tiempo total de prueba |
| --- | --- | --- | --- | --- |
| 1 | 1 | 1 | 638 | 42.7 s |
| 1 | 2 | 2 | 744 | 36.7 s |
| 1 | 4 | 4 | 1010 | 28.4 s |
| 1 | 8 | 8 | 1346 | 22.5 s |
| 2 | 4 | 8 | 1594 | 20.0 s |
| 4 | 2 | 8 | 1345 | 23.4 s |
| 8 | 1 | 8 | 995 | 31.1 s |

Recomendación inicial: 2 procesos con 4 arenas cada uno. Más procesos no garantizan mayor velocidad: añaden comunicación, procesos de Unity y competencia por CPU. Para reproducir cada escenario usa `--num-envs` y `--gf-arenas` como dimensiones independientes.

En el perfil de un proceso y ocho arenas, de unos 18,0 s dentro del ciclo principal, aproximadamente 15,8 s se registraron en el paso del entorno y 2,2 s en el avance del entrenador. Las actualizaciones de PPO ocuparon aproximadamente 1,6 s, incluidas dentro del segundo valor. Los temporizadores se anidan: no se suman padres con hijos. Esto apunta a simulación y comunicación como prioridades, antes de buscar una GPU para estas redes.

## Tamaños de red y tiempos estimados

Comparación con cuatro arenas, un proceso y un hilo de PyTorch. Los tiempos de la tabla extrapolan el rendimiento de cada red en esa misma configuración; no aplican a todas la velocidad máxima obtenida con otra distribución de arenas.

| Red oculta | Parámetros actor | Actor y crítico | Decisiones/s | 1 millón | 5 millones | 10 millones |
| --- | --- | --- | --- | --- | --- | --- |
| 2 capas de 64 | 10,898 | 20,691 | 1006 | 16.6 min | 1.38 h | 2.76 h |
| 2 capas de 128 | 29,970 | 57,747 | 1010 | 16.5 min | 1.38 h | 2.75 h |
| 2 capas de 256 | 92,690 | 181,011 | 987 | 16.9 min | 1.41 h | 2.81 h |
| 3 capas de 512 | 579,090 | 1,149,459 | 907 | 18.4 min | 1.53 h | 3.06 h |

Los parámetros distinguen el actor que se exporta para jugar del actor y crítico que se entrenan. No incluyen metadatos de ONNX, estados del optimizador ni memoria de Unity. Las redes pequeñas y medianas tienen velocidades similares. Empezaría con dos capas de 128 y probaría 256 si las evaluaciones muestran una limitación de representación. La red de tres capas de 512 añade costo y capacidad, pero este experimento no demuestra que la aproveche.

Aquí una iteración puede significar cosas distintas. Una decisión es una muestra de observación y acción. Con buffer de 4.096, un millón de decisiones permite aproximadamente 244 actualizaciones de PPO; cada una recorre el buffer tres veces y realiza alrededor de 96 minibatches. Por tanto, un millón corresponde a unas 23.438 actualizaciones por minibatch. Son aproximaciones: episodios y buffers pueden hacer que los límites no coincidan exactamente. Una corrida completa de 5 millones es un entrenamiento, no cinco millones de redes entrenadas.

Como presupuesto de búsqueda, probar tres semillas de cada una de las cuatro arquitecturas durante 5 millones de decisiones suma 12 corridas y 60 millones de muestras. Aplicando a cada tamaño su velocidad medida con cuatro arenas, serían 17.1 h de cómputo, o unas 22.2 h con 30% de margen. Usar dos procesos podría reducir ese tiempo, pero no se midió esa configuración para todos los tamaños. No incluye desarrollo ni evaluación.

## Piloto de aprendizaje y rivales disponibles

El piloto entrenó dos capas de 128 con cuatro arenas, recompensa auxiliar existente contra el muñeco y 100.000 decisiones solicitadas. Completó en 100.3 s, con 1043 decisiones/s después del calentamiento, y exportó un checkpoint y ONNX. Luego se evaluaron los pesos congelados, sin recompensa auxiliar ni actualizaciones, en 120 episodios con semilla base 9001, diferente de entrenamiento. Se utilizó el muestreo estocástico original de ML-Agents, cuatro arenas y posiciones que rotan por ronda.

| Rival | Victorias | Derrotas | Empates | Tiempo agotado | Victorias IC 95 por ciento |
| --- | --- | --- | --- | --- | --- |
| passive | 0 | 21 | 0 | 19 | 0.0% (0.0–8.8%) |
| chase | 16 | 24 | 0 | 0 | 40.0% (26.3–55.4%) |
| edge | 16 | 20 | 0 | 4 | 40.0% (26.3–55.4%) |

Comparación con acciones uniformes aleatorias en las mismas condiciones y semillas:

| Rival | Victorias de la red | Victorias con acciones aleatorias |
| --- | --- | --- |
| passive | 0/40 | 0/40 |
| chase | 16/40 | 13/40 |
| edge | 16/40 | 1/40 |

La diferencia frente a `edge` es prometedora: 40% para la red frente a 2,5% para acciones aleatorias. Frente a `chase`, 40% frente a 32,5% no muestra una mejora clara con esta muestra. El reglamento atribuyó al luchador neuronal 30 de las 32 expulsiones ganadoras contra bots; dos fueron caídas del rival sin crédito. Aun así, el piloto no consiguió expulsar al muñeco y no domina el objetivo básico. Esto orienta a trabajar exploración y acercamiento a un objetivo estático antes de aumentar la red.

Los intervalos de Wilson son descriptivos para esta muestra pequeña; no sustituyen repetir con múltiples semillas y al menos 200 partidas por rival para la evaluación final. Los cierres por tiempo se cuentan aparte y no como victoria.

Ya hay IA contra la que jugar y medir: `random` ejecuta controles aleatorios; `chase` persigue y ataca; `edge` intenta agarrar y expulsar, y evita el borde. Son bots de reglas y sirven como referencias estables. `passive` es el muñeco para la primera fase. Antes de este experimento no había una política neuronal V3 instalada en Resources. No se encontró en el proyecto una red externa compatible con estas 86 observaciones, ocho ramas y reglas V3. Un modelo de otro juego requeriría adaptar la interfaz y entrenar en este entorno.

El piloto está en `results/pilot-100k/128x2-a4/GangFightFighterV3.onnx`. Es un resultado de prueba, no un campeón validado. Para jugar contra una política elegida, el juego ya admite `Resources/Policies/V3/Champion.onnx` y la opción Modelo tras importarla y recompilar.

También se probó el autojuego con la configuración existente: 1217 pasos del entrenador/s, 24.8 s de duración total. Se utiliza el contador del entrenador; las decisiones del oponente histórico no deben contarse como muestras nuevas para estimar el aprendizaje.

## Estrategia recomendada y viabilidad del proyecto

La ruta recomendada es Unity compilado como simulador sin gráficos y PPO en Python nativo. Unity calcula la física y entrega el estado; PyTorch ajusta las redes; ONNX devuelve la política para jugar. El Editor queda para inspeccionar y depurar. El autojuego con rivales históricos es una etapa posterior al control básico y conviene mantener `threaded: false`, como en la configuración existente.

1. **Control y expulsión del muñeco:** empezar con 0,5–2 millones de decisiones por ensayo, dos capas de 128, y tres semillas. Revisar caídas propias, acercamiento, contactos y victorias. La recompensa auxiliar actual solo representa el desplazamiento del rival hacia el borde: si no aparecen contactos útiles, hace falta mejorar la exploración o un currículo de posiciones, no simplemente esperar más.
2. **Bots de reglas:** reservar inicialmente 2–5 millones por ensayo contra `chase` y `edge`, usando nuevas corridas inicializadas desde la fase anterior. Evaluar los pesos congelados en semillas aparte. El umbral propuesto para progresar es más de 80% frente al muñeco y después más de 60% frente a los bots, con al menos 200 partidas por rival; son objetivos, no resultados garantizados.
3. **Autojuego:** reservar una primera exploración de 5–20 millones, oponentes históricos y evaluaciones periódicas contra una colección fija. Evitar seleccionar solo por Elo de autojuego, que puede ocultar ciclos de estrategias.
4. **Generalización y juego real:** variar tamaño y ventilador con `arena_variable.yaml`, añadir todos contra todos y comprobar con el ejecutable jugable. El modo interactivo incorpora ventilador y bloque agarrable que no están en el duelo básico de rendimiento.

Estas cantidades son presupuestos iniciales para decidir cuánto probar, no estimaciones demostradas de convergencia para cada tamaño. Una red mayor no exige una cantidad fija de iteraciones adicional ni garantiza aprender con menos muestras. Si tras 0,5–1 millón no mejora la evaluación, inspeccionaría observaciones, exploración y recompensas antes de lanzar decenas de millones.

El equipo es suficiente para un prototipo y una búsqueda moderada. Una campaña de varios tamaños, semillas y autojuego cabe en días de cómputo local; conseguir un rival competitivo dependerá de iterar sobre el aprendizaje. No puede prometerse con esta prueba que una red fuerte estará lista después de una noche. Para un plazo de pocos días reduciría el alcance a duelo contra bots; con una o dos semanas incluiría autojuego y variación del entorno, dejando tiempo para ajustes y evaluación.

## Validación y reproducción

La compilación optimizada pasó `GANGFIGHT_COMBAT_OK` y `GANGFIGHT_SMOKE_OK`. También pasaron las pruebas compartidas de reglas y comandos en .NET y las tres pruebas Python existentes. Los modelos del experimento se exportaron; se comprueba su estructura con `onnx.checker`.

El procedimiento está en `PROCEDIMIENTO.md`; `run.py` ejecuta barridos y `evaluate_policy.py` evalúa checkpoints sin modificar pesos. `results/benchmark-analysis.json` consolida mediciones, exclusiones y extrapolaciones. Los archivos de TensorBoard, logs, temporizadores y ONNX se conservan en cada carpeta de resultados.

Fuentes oficiales: [modo sin gráficos de Unity](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/dedicated-server/desktop-headless-mode), [entrenamiento con ML-Agents](https://unity-technologies.github.io/ml-agents/Training-ML-Agents/) y [configuración de redes y autojuego](https://unity-technologies.github.io/ml-agents/Training-Configuration-File/). Las cifras de rendimiento y evaluación son mediciones locales de este experimento.
