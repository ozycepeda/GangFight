# Comparación de entrenamiento de 100 y 1000 segundos

La nueva corrida entrenó desde cero durante 1000.000 segundos y alcanzó 1,104,244 decisiones. El reloj empieza al entrar al ciclo de aprendizaje, después de conectar Unity, y termina entre pasos completos; la exportación se mide aparte.

Se mantuvieron dos capas de 128 unidades, semilla 41, PPO, CPU nativa con un hilo, un proceso con cuatro arenas, duelo contra `passive` y recompensa auxiliar. No se reanudó el piloto anterior.

Se amplió `max_steps` de 100000 a 2000000 para que el límite efectivo fuera el reloj. Como PPO utiliza ese horizonte para reducir la tasa de aprendizaje, la comparación con el piloto anterior combina más tiempo y un descenso más lento de esa tasa. El control intermedio de la nueva corrida permite comparar progreso bajo el mismo horizonte.

| Rival | Piloto de unos 100 s | Corrida de 1000 s | Cambio |
| --- | --- | --- | --- |
| passive | 0/40 (0.0%) | 0/40 (0.0%) | +0.0 puntos |
| chase | 16/40 (40.0%) | 8/40 (20.0%) | -20.0 puntos |
| edge | 16/40 (40.0%) | 1/40 (2.5%) | -37.5 puntos |

Control adicional: se conservó el checkpoint de 99,952 decisiones de esta misma corrida, antes de continuar hasta 1000 segundos. Comparte el horizonte de descenso de la tasa de aprendizaje con el modelo final.

| Rival | Control de la corrida larga | Final de 1000 s |
| --- | --- | --- |
| passive | 1/40 | 0/40 |
| chase | 9/40 | 8/40 |
| edge | 7/40 | 1/40 |

Las evaluaciones usan pesos congelados, sin recompensa auxiliar, semilla base 9001 y 40 episodios por rival. Se verificaron las victorias, derrotas y cierres por tiempo contra los eventos del reglamento. Son resultados exploratorios de una semilla y una muestra pequeña.

Los diagnósticos detallados de contactos, agarres, caídas y duración de rondas están en `comparison.json`. El modelo final está en `128x2-a4/GangFightFighterV3.onnx`; el checkpoint permite continuar entrenando.

## Interpretación y siguiente experimento

El entrenamiento largo mejoró la supervivencia frente al muñeco: las derrotas bajaron de 21 a 1 y los cierres por tiempo subieron de 19 a 39. No produjo victorias en esa evaluación. Frente a edge, el modelo final registró 999 impactos y 443 agarres, pero 0 expulsiones atribuidas a él. Más contactos acumulados no significan mayor eficacia: las rondas duraron más.

Mi interpretación es que el perfil actual favorece una política que evita perder, sin aprender a convertir contactos en expulsiones. El reglamento da cero al agotar el tiempo y menos uno a una derrota; la recompensa auxiliar actual depende del desplazamiento del rival hacia el borde. Con un rival inmóvil, una política que sobrevive puede estabilizarse sin explorar suficientemente el combate. Esta es una explicación plausible de los resultados, no una demostración causal.

No ampliaría esta misma corrida sin cambiar el escenario. Probaría un currículo con posiciones iniciales más cercanas o arena menor, después rivales activos que provoquen encuentros, y una recompensa de progreso que guíe el acercamiento y la expulsión manteniendo la victoria como objetivo principal. Evaluaría checkpoints intermedios en semillas de validación aparte y elegiría por victorias y expulsiones, no por el último checkpoint ni por supervivencia sola.

Antes de aumentar el tamaño de la red, repetiría el perfil revisado con tres semillas y al menos 200 partidas por rival. Este experimento confirma capacidad de cómputo local, pero no que más tiempo en el perfil actual mejore la habilidad de ganar.
