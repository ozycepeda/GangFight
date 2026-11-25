# Resultados de IA conservados

Copia de los experimentos y verificaciones disponibles al 25 de noviembre de 2026. Incluye configuraciones, métricas, eventos de TensorBoard, registros, checkpoints de PyTorch (`.pt`) y políticas exportadas (`.onnx`).

| Ruta | Contenido |
| --- | --- |
| `benchmark-*` | Barridos de arquitectura, procesos, arenas y velocidad de entrenamiento; análisis y hashes del código medido. |
| `pilot-100k/` | Piloto de 100.000 pasos y evaluaciones contra rivales. |
| `pilot-1000s/` | Piloto de 1000 segundos y comparación con el control de 100.000 pasos. |
| `observations-v4-integration/` | Entrenamiento de integración con 526 observaciones V4, checkpoints y ONNX. |
| `physics-observations-check-2026-11-25/` | Verificación de entradas físicas V4, observaciones capturadas y comprobaciones del simulador. |
| `validation/` y `onnx-validation.json` | Evidencia de validación del simulador y de exportación de modelos. |

Los modelos históricos V3 tienen 86 entradas y no son compatibles con la interfaz actual de 526 entradas. Los modelos son experimentales; los informes de `training/benchmarks/` explican sus resultados y límites. El juego no instala automáticamente ninguna de estas políticas como campeona. Para probar un ONNX V4, sigue las instrucciones del README principal.

Los resultados existentes se añadieron expresamente a Git. `.gitignore` mantiene excluidas las nuevas salidas de `results/`; para conservar una corrida nueva, revisa sus archivos y añádela con `git add -f results/ID`. Los archivos ya versionados siguen registrando sus cambios normalmente.
