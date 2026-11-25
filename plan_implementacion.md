# GangFight · Plan de implementación del juego y su entorno de IA

**Versión 2.5 · 25 de noviembre de 2026 · Tamaño de arena, agarres y KO progresivo**

GangFight será un juego de combate físico en una plataforma. Cada luchador tendrá un cuerpo articulado que sabe caminar, mantener el equilibrio y ejecutar movimientos. Tanto una persona como una política de IA dirigirán ese cuerpo con los mismos diez controles. La pregunta del proyecto es qué tácticas aprende el agente cuando puede combinar esos controles y su objetivo es quedar como único participante en la arena.

La próxima revisión jugable debe permitir elegir entre golpear y agarrar con cada mano, combinar ataques en el suelo o en el aire y aprovechar un KO temporal para expulsar al rival. El primer entregable de IA será un modelo contra el que se pueda disputar esa misma ronda desde el teclado.

Lectura por tema · [Controles](#2-controles-y-combinaciones) · [Reglas de la ronda](#4-ronda-eliminación-y-casos-límite) · [Diseño de la IA](#6-interfaz-entre-el-juego-y-la-ia) · [Entrenamiento y evaluación](#7-cómo-aprender-y-evaluar-tácticas) · [Docker y plataformas](#8-apple-silicon-windows-y-docker) · [Hitos](#9-hitos-y-orden-de-desarrollo)

## 1. Alcance y decisiones de diseño

| Aspecto | Acuerdo o propuesta |
|---|---|
| Objetivo confirmado | La IA aprende a combatir con los controles de un jugador. La locomoción y las poses del cuerpo las resuelve el juego. |
| Reglamento confirmado | El KO es temporal. Una ronda con un único participante sin eliminar termina y ese participante gana. |
| Plataformas confirmadas | Desarrollo y uso en Apple Silicon y Windows. Se prevé entrenar en una imagen Docker. El destino de despliegue queda abierto. |
| Arena de referencia | Plataforma cuadrada de 150 m², con aproximadamente 12,25 m por lado, dos luchadores distinguibles por color y cámara fija elevada desde un lateral. |
| Escala propuesta | Validar el duelo primero. Extender el mismo reglamento y controlador a tres y cuatro participantes en modalidad todos contra todos. Cuatro es la capacidad inicial del modelo de IA. |
| Presentación y modos | Personajes originales con formas simples para que se distingan pies, manos y agarres. Primero humano contra muñeco o bot, después humano contra IA e IA contra IA. Juego por red y presentación artística final son ampliaciones opcionales. |

Los valores de equilibrio, las duraciones, el número inicial de plazas y los umbrales de evaluación que siguen son propuestas ajustables. Se registrará la versión de estos ajustes con cada modelo para poder comparar resultados.

## 2. Controles y combinaciones

«Arriba» corresponde al fondo de la pantalla y al eje +Z de la arena. La derecha corresponde a +X. La cámara encuadra la plataforma desde el lateral sin seguir a los jugadores. Las posiciones iniciales se escalonan para que ambos luchadores queden visibles. El personaje orienta el cuerpo según su movimiento. Durante un agarre, el controlador permite retroceder o desplazarse de lado conservando la sujeción.

| Tecla | Acción | Comportamiento propuesto |
|---|---|---|
| W | Mover hacia arriba | Mantener para avanzar hacia +Z. |
| A | Mover hacia la izquierda | Mantener para desplazarse hacia −X. |
| S | Mover hacia abajo | Mantener para avanzar hacia −Z. |
| D | Mover hacia la derecha | Mantener para desplazarse hacia +X. |
| Espacio | Saltar | Una pulsación solicita un salto. Requiere apoyo y vuelve a estar disponible tras aterrizar y cumplir la recuperación. |
| J | Puñetazo o agarre izquierdo | Un toque y suelta inicia el puñetazo al soltar. Al mantener, el brazo se extiende y busca un rival u objeto agarrable. Tras entrar en agarre, soltar solo libera esa mano, incluso si no encontró una presa. |
| L | Puñetazo o agarre derecho | Mismas reglas con la derecha y su propio temporizador. Una mano puede sujetar mientras la otra golpea. |
| I | Alzar los brazos | Mantener para apuntar ambas manos por encima de la cabeza y elevar también una presa que ya se esté sujetando. Al soltar, vuelven a la postura que corresponda a la acción. |
| K | Cabezazo | Inicia al presionar. En el suelo, el luchador inclina más el torso y el cuello y se lanza más lejos hacia delante. Para repetir, soltar y volver a pulsar tras la recuperación. |
| Coma (,) | Patada | Inicia al presionar y alterna piernas. Misma regla de repetición que el cabezazo. |

Las direcciones opuestas se cancelan por eje y la diagonal tiene la misma velocidad máxima que el avance recto. Mantener J y tocar L permite sujetar y golpear. Mantener D, saltar y pulsar coma inicia una patada voladora hacia la derecha. El agarre doble sobre la misma presa activa el alzado automático. WASD orienta al luchador durante el agarre. Al soltar la última mano en un giro rápido, se multiplica por 1,2 la componente tangencial que ya lleva la presa con respecto al portador. Para dejar la suelta por inercia natural, usar `--gf-throw-multiplier 1` o fijar `RunOptions.DefaultThrowMultiplier` en 1 para el Editor.

**Paridad de entrada.** Física a 50 Hz y decisiones a 25 Hz para todos. Probar un umbral de 160 ms, equivalente a cuatro intervalos de decisión. Al pulsar J o L empieza el gesto de ese brazo. Si se suelta antes del umbral se resuelve como golpe de inmediato. Al alcanzar el umbral gana el agarre, aunque la suelta coincida con ese instante. Capturar en orden las pulsaciones y sueltas breves del teclado entre dos muestras y representarlas en la misma secuencia de botones que puede emitir el agente. Comprobar la latencia real desde la suelta hasta el contacto. Cada ataque tiene una sola activación. Un KO, la eliminación o un reinicio cancelan gestos pendientes y las teclas retenidas necesitan soltarse para volver a atacar.

**Prueba de aceptación.** Grabar una secuencia que use las diez teclas, incluya una diagonal, una pulsación breve, ambas manos y la liberación de una sola. Reproducir la secuencia mediante el adaptador de la IA. Comparar los comandos y sus tiempos exactamente, los eventos de salto, contacto y liberación, y las posiciones con tolerancia física en el mismo entorno. Repetir la prueba de teclado real en ambos sistemas, incluida la tecla de coma y las combinaciones de varias teclas.

## 3. Física, agarres y KO

| Sistema | Implementación prevista | Prueba jugable |
|---|---|---|
| Cuerpo articulado | Cinco articulaciones permanentes sobre un tronco rígido que reúne pelvis y torso. Una por brazo, una por pierna y una en el cuello. Mantener zonas de contacto en puños, botas y cabeza, y un punto de agarre por mano. Las uniones temporales con una presa se cuentan aparte. | Contar cinco articulaciones en reposo, comprobar ambas manos de forma independiente, sostenerse sobre una pierna mientras la otra patea y recobrar el equilibrio. |
| Masa y fuerza | El cuerpo original sumaba 21,5 kg de masa simulada. La primera reducción lo dejó en 10,75 kg. Esta revisión reduce la masa otro 20 % hasta 8,6 kg por luchador y conserva el reparto entre tronco y extremidades. | Medir si dos manos levantan a un rival del mismo peso hasta que ambos pies pierdan apoyo. Comprobar que caminar con él, soltarlo y recibir impactos siguen siendo controlables. |
| Locomoción | Reducir 30 % la distancia de cadera a suela y reajustar la altura de apoyo. Usar 5,2 m/s de velocidad objetivo libre y 3,4 m/s con agarre. Acelerar la rotación del torso y orientar la carga durante el giro. | Comparar la velocidad de carrera. Girar con el rival alzado y verificar que los brazos vuelvan a su postura de reposo al iniciar una ronda. |
| Manos y elevación | Una unión rompible por mano. Extender la búsqueda actual de rivales a objetos físicos marcados como agarrables de la misma arena. Probar con un bloque móvil de masa conocida. Elevar con dos manos sobre una misma presa. | Sujetar rival y bloque por separado, alzar, soltar solo J y conservar L. Un intento de agarrar fuera de alcance termina limpiamente al soltar. |
| Golpes | Puño, cabeza y bota tienen preparación, ventana de impacto y recuperación propias. Contabilizar una vez por ataque y rival al contactar con la parte atacante durante su ventana activa. | Un contacto sostenido produce un impacto. Una finta al aire y un roce del torso producen cero impactos de ataque. |
| KO y recuperación | Al superar el umbral de aturdimiento, aflojar los motores, soltar las presas propias y suspender acciones voluntarias. El primer KO dura 2 s y cada KO siguiente del mismo luchador añade 1 s hasta el próximo inicio de ronda. | Comprobar KO de 2, 3 y 4 s, el reinicio del contador y la recuperación de un jugador que sigue en la plataforma. |
| Arena y peligro | Plataforma cuadrada con bordes abiertos, puntos de aparición separados y plano de eliminación inferior. Incorporar un ventilador con dirección y fuerza configurables que pueda apagarse para las primeras lecciones. | Un personaje puede asomarse y volver. Un luchador que cae bajo el límite queda eliminado una vez. El ventilador afecta igual a personas, bots y modelos. |

Para ajustar el personaje se registrarán tiempos de apoyo y recuperación, velocidad horizontal, fuerza de rotura de las manos e impactos que producen KO. Los ajustes deben permitir que un jugador recorra la plataforma, se equivoque y se recupere. Se probarán también agarres recíprocos y más de dos cuerpos entrelazados, donde las oscilaciones de las articulaciones pueden cambiar el resultado.

### Cómo se consigue un KO

En el prototipo anterior, solo una patada que conecta con la bota o la espinilla durante su ventana activa aporta aturdimiento en combate. Cada acierto aporta entre 1,50 y 2,60 unidades según el impulso de contacto. El umbral es 2,25 y el aturdimiento baja 1,9 unidades por segundo. Una patada fuerte puede bastar. El KO dura 2 s. J y L solo agarraban rivales, e I levantaba los brazos.

**Aturdimiento de combate.** Los puñetazos, cabezazos y patadas comparten el medidor de aturdimiento. El impulso del contacto determina cuánto suma cada acierto, con una escala ajustable por ataque. Un contacto muy suave puede sumar cero. Un impacto fuerte o varios golpes próximos pueden noquear. La arena de pruebas muestra el aturdimiento del rival, una señal de impacto y el tiempo restante del KO de ese luchador.

**Ritmo inicial.** La patada anterior tenía un ciclo de 660 ms y podía impactar a partir de unos 190 ms. Los tiempos se cuentan desde que se activa la acción, al soltar en el caso del puñetazo. El momento del impacto depende de la distancia, la posición y la trayectoria de ambos luchadores.

| Ataque | Comienzo de la ventana activa | Duración total |
|---|---:|---:|
| Puñetazo | 40 ms | 260 ms por brazo |
| Cabezazo | 80 ms | 480 ms |
| Patada | 100 ms | 440 ms |

**Combate aéreo.** El salto aplica 5,65 m/s de velocidad vertical inicial. En vuelo, cada acción se orienta según WASD o, si está en neutro, según la orientación del personaje. El primer golpe o intento de agarre aéreo aporta un impulso horizontal por salto. La patada aérea inclina el cuerpo hacia atrás alrededor de su centro de masa, extiende la bota con más fuerza y aplica un multiplicador de 1,4 al aturdimiento del contacto antes del límite. Los siguientes ataques y agarres siguen disponibles dentro de su recuperación. Probar la altura máxima, una patada voladora diagonal, una sin dirección, una fallida junto al borde y un agarre durante el salto.

## 4. Ronda, eliminación y casos límite

1. El coordinador de la arena prepara una ronda con al menos dos participantes. Asigna posiciones, limpia fuerzas, velocidades, uniones y pulsos previos, y restablece postura y temporizadores.
2. Cada participante está **activo**, **en KO** o **eliminado**. Activo y en KO cuentan como presentes. La eliminación se confirma cuando el centro de la pelvis cruza el plano inferior. Probar inicialmente 2 m por debajo de la superficie y ajustar con el personaje definitivo.
3. Al terminar cada paso físico, el coordinador recoge todas las caídas del paso, elimina al conjunto y después cuenta a los participantes restantes. Con dos o más, la ronda continúa. Con uno, emite una sola victoria, incluso si el superviviente está en KO. Con cero, registra empate por caída simultánea de los últimos participantes.
4. Un eliminado pierde control y deja de intervenir físicamente. Se liberan también las uniones de otras manos a ese cuerpo. El resto sigue jugando. La siguiente ronda empieza únicamente cuando la anterior ha finalizado o se ha interrumpido administrativamente.
5. En juego interactivo, la ronda se decide por supervivencia. Para entrenamiento se propone interrumpir una simulación que alcance 60 s sin resolución. Ese corte admite recolección de nuevas experiencias y se contabiliza por separado de victoria, derrota y empate. Dar prioridad al resultado natural si coincide con el límite en el mismo paso.

El resultado pertenece a la arena. Cada agente recibe a lo sumo un resultado terminal por ronda. Si el aprendiz cae antes que los demás en un todos contra todos, se cierra su trayectoria individual y se suspenden sus decisiones hasta la siguiente ronda. Los rivales resuelven el combate. El reinicio local de un agente en ML-Agents debe quedar desacoplado del reinicio físico de la arena. Usar `EndEpisode()` para resultados definitivos y `EpisodeInterrupted()` para cortar una trayectoria todavía abierta por tiempo. [Ciclo de vida de Agent en Unity](https://docs.unity3d.com/Packages/com.unity.ml-agents@4.0/api/Unity.MLAgents.Agent.html)

**Casos obligatorios de prueba.** KO y recuperación sin caídas. Dos participantes en KO que siguen en juego. Caída propia sin contacto. Eliminación de dos de cuatro en el mismo paso, seguida de un duelo. Caída simultánea de los dos últimos. Último superviviente en KO. Reinicio con dos manos todavía enlazadas. Dos arenas paralelas que resuelven rondas diferentes sin intercambiar participantes, eventos ni recompensas.

## 5. Arquitectura y organización del proyecto

| Componente previsto | Responsabilidad |
|---|---|
| Adaptadores de teclado, agente, bot y repetición | Emitir los mismos ejes y botones con el reloj común. Conservar comandos grabados para depuración y demostraciones. |
| Controlador de luchador | Interpretar comandos según apoyo, KO y acciones en curso. Coordinar locomoción, postura, agarres, golpes y dirección aérea. Exponer el estado que pueden observar los demás sistemas. |
| Controlador físico y perfil de ajuste | Aplicar fuerzas y objetivos a las articulaciones. Centralizar límites, fuerza, amortiguación, agarres, umbral de KO y tiempos de recuperación. |
| Coordinador de arena | Gestionar participantes, apariciones, peligro, caída, resolución simultánea y reinicio. Identificar cada evento por ronda y paso. |
| Adaptador ML-Agents | Construir observaciones, traducir ramas discretas a comandos, recibir eventos de resultado y administrar las trayectorias. |
| Presentación y métricas | En la escena jugable, cámara, ayuda de controles, colores, KO y resultado. En entrenamiento, eventos por ronda, perfiles de rendimiento y exportación del modelo. |

La escena interactiva y la de entrenamiento usarán el mismo prefab de luchador, arena, perfil de física y reglamento. El modo de entrenamiento podrá alojar varias arenas aisladas. Cámara, interfaz y sonido pertenecerán a la presentación. La interfaz de juego indicará quién sigue en la arena y distinguirá un KO recuperable de una eliminación.

| Ubicación prevista dentro de GangFight | Contenido |
|---|---|
| `Assets/GangFight/Runtime/` | Control, personajes, arena, IA y presentación. |
| `Assets/GangFight/Prefabs/`, `Scenes/`, `Settings/` bajo `Assets/GangFight/` | Luchador, plataforma y ventilador. Escenas de juego y entrenamiento. Perfiles compartidos de física, reglas y acciones. |
| `Assets/GangFight/Tests/` y `Assets/GangFight/Editor/` | Pruebas de reglas y controles, pruebas físicas en Play Mode y compilación reproducible del simulador. |
| `training/config/`, `training/evaluation/`, `training/docker/` | Configuración PPO, lecciones, rivales de referencia, evaluación e imagen de entrenamiento. |
| `builds/`, `results/`, `Assets/GangFight/Models/` | Ejecutables por destino, registros y puntos de control por corrida, modelos ONNX seleccionados para jugar. |

## 6. Interfaz entre el juego y la IA

**Acciones.** Un comportamiento de ML-Agents, identificado como `GangFightFighterV3`, emite ocho ramas discretas de tamaños `[3, 3, 2, 2, 2, 2, 2, 2]`. Las dos primeras son X y Z con valores codificados como 0 neutro, 1 negativo y 2 positivo. Las seis binarias indican botón suelto o presionado, en el orden Espacio, J, L, I, K y coma. Así ambas manos pueden actuar al mismo tiempo. El modelo recibe el mismo rechazo físico que el teclado cuando intenta saltar sin apoyo o patear durante la recuperación.

**Observaciones de combate V3.** El vector tiene 86 valores por decisión. Los ejes de la arena están alineados con los controles.

| Bloque | Tamaño | Campos y orden |
|---|---:|---|
| Estado propio | 28 | Posición relativa al centro (3), velocidad (3), orientación horizontal (2), verticalidad del torso (1), apoyo (1), KO (1), fracción de KO restante (1), agarres efectivos izquierdo y derecho (2), alzado con presa doble y orden de alzar los brazos (2), disponibilidad de salto y patada (2), estado anterior de salto y patada (2), progreso de cada pulsación de mano (2), disponibilidad de cada puñetazo (2), disponibilidad de cabezazo (1), aturdimiento (1), impulso aéreo disponible (1) y estado anterior de K (1). |
| Rivales | 48 | Tres plazas × 16. Cada plaza contiene presencia (1), posición relativa (3), velocidad relativa (3), orientación horizontal (2), KO (1), manos agarrando (2), apoyo (1) y si cada una de mis dos manos sujeta a ese rival (2) y su aturdimiento (1). |
| Bordes | 4 | Distancia orientada desde la pelvis a cada borde, en orden +X, −X, +Z, −Z. Conservar la señal de que la pelvis ya sobrepasó el borde horizontal aunque aún pueda recuperarse. |
| Ventilador | 6 | Activo (1), desplazamiento horizontal relativo a su centro (2), dirección horizontal (2), intensidad normalizada (1). El primer ventilador tendrá una zona de influencia de tamaño fijo. Con el ventilador apagado, las seis entradas serán cero. |

Asignar las plazas de rivales al comienzo de cada ronda y mantenerlas estables hasta el final. Una plaza ausente o eliminada se rellena con ceros y su indicador de presencia vale cero. Variar el orden de las plazas durante el entrenamiento para reducir preferencias arbitrarias. El modelo observa estado físico y del escenario. Las próximas acciones de otros jugadores y la identidad de su política quedan fuera de las observaciones.

**Compatibilidad.** Guardar políticas de combate V3 en `Resources/Policies/V3/` y arrancar corridas nuevas con el contrato de 86 entradas. Los comandos grabados incluyen la versión de combate y el reproductor requiere la misma versión. El bloque agarrable está en la arena de práctica y su repetición. Si los objetos pasan a una tarea de entrenamiento, incluir su posición relativa y distinguir su agarre del agarre de un rival.

## 7. Cómo aprender y evaluar tácticas

### Recompensas

El aprendizaje principal usará PPO. En un duelo, el ganador recibe +1 y el eliminado −1. Si los dos últimos caen en el mismo paso, ambos reciben 0. En un todos contra todos, cada eliminación anterior al desenlace ya vale −1 para ese participante. El empate final afecta solo a quienes seguían en la arena. Un corte técnico agrega cero recompensa terminal y conserva la distinción entre trayectoria interrumpida y derrota. El KO tampoco termina la trayectoria.

La primera comparación usará únicamente esos resultados. La señal auxiliar descrita a continuación es un experimento posterior. Si la exploración resulta insuficiente, probar en el ejercicio con muñeco una señal auxiliar por **progreso neto** al llevarlo desde el centro hacia el borde. Calcularla por diferencia de un potencial acotado, aplicando el descuento de la política y el tratamiento terminal correspondiente. Limitar su escala frente al premio por victoria, retirarla antes de evaluar y comparar contra una corrida con solo el resultado. Agarrar y soltar repetidamente, permanecer en el borde o mantener a alguien inconsciente deben producir cero bonificación por sí mismos.

### Organización de las lecciones

| Etapa | Rivales y tarea | Evidencia para avanzar |
|---|---|---|
| 0 · Verificación | Un bot que reproduce comandos y un muñeco pasivo. Variar posición y lado de aparición. | Reproducir un golpe con cada brazo, un cabezazo, una patada, un agarre y un ataque aéreo dirigido. Completar una expulsión, asignar el resultado correcto y empezar limpio el siguiente episodio. |
| 1 · Expulsión | Un aprendiz contra un muñeco físico capaz de caer. Probar agarre, arrastre, elevación y liberación. | Superar el 90 % de victorias frente al muñeco en posiciones reservadas para evaluación. |
| 2 · Duelo contra bots | Un rival camina y persigue. Otro se aparta del borde e intenta agarrar o patear usando el mismo controlador. | Medir mejora contra cada bot y caídas propias. La meta es que el aprendiz provoque la expulsión del bot de persecución en más del 50 % de las rondas de evaluación antes de alcanzar 5 millones de decisiones de entrenamiento. |
| 3 · Duelo entre políticas | Entrenar en 1v1 mediante autojuego simétrico, mismo comportamiento y equipos 0 y 1. Alternar el bando que aprende y muestrear versiones anteriores como rivales. | Evaluar cada versión ante una colección fija de bots y modelos guardados. Conservar la mejor que también generalice al cambiar de lado. |
| 4 · Todos contra todos | Pasar a tres y después a cuatro luchadores. Entrenar un aprendiz frente a bots y modelos congelados. Rotar su plaza, los rivales y las posiciones. Las políticas congeladas también pueden eliminarse entre sí. | Comparar victorias y orden de eliminación con el mejor modelo del duelo bajo las mismas composiciones. Verificar que la ronda continúa después de una eliminación temprana. |
| 5 · Variación de arena | A partir de una política funcional, variar tamaño de plataforma, posición de salida y estado del ventilador. Reservar combinaciones nuevas para la prueba final. | Informar rendimiento con ventilador encendido y apagado. El modelo conserva su capacidad de ganar en la arena original. |

Los rivales congelados de evaluación estarán separados de los usados para practicar. El análisis de partidas puede sugerir escenarios de prueba. Una secuencia como «sujetar, levantar, avanzar y soltar» se reconocerá en el registro de eventos para estudiar cuándo funciona. Se permitirá que surjan otras secuencias. Como experimento opcional, grabar demostraciones locales con la misma interfaz y comparar una política inicializada por imitación con otra entrenada desde cero.

### Configuración de partida y evaluación

- **Primera corrida.** PPO con dos capas de 128 unidades, tasa de aprendizaje de 0,0003, lote de 128, búfer de 4.096, tres épocas, descuento inicial de 0,995 y horizonte de 256 decisiones. Usar solo recompensa extrínseca en la referencia. Son valores de partida. Validar primero que hay actualizaciones, que las pérdidas son finitas y que cabe una corrida corta de 100.000 decisiones. Para autojuego, registrar frecuencia de guardado, cambio de rival, cambio del lado que aprende y tamaño de la colección histórica. [Configuración de entrenamiento y autojuego de Unity](https://docs.unity3d.com/Packages/com.unity.ml-agents@4.0/manual/Training-Configuration-File.html)
- **Comparación reproducible.** Proponer 200 rondas por rival o composición, equilibradas por lado o plaza, y repetir el entrenamiento final con tres semillas. Separar semillas de entrenamiento, selección y evaluación final. Registrar victorias sobre el total de rondas iniciadas, empates, derrotas e interrupciones. Informar tamaño de muestra, intervalo de confianza de la tasa de victoria y variación entre corridas.
- **Tácticas.** Registrar por ronda los comandos, agarres, elevaciones, ataques iniciados, impactos y KO. Para cada acierto guardar atacante, rival, tipo de golpe, suelo o aire, impulso y aturdimiento antes y después. Esto permite reconocer secuencias como agarrar con J y golpear con L, y atribuir el KO al impacto que cruzó el umbral. Registrar también recuperaciones y expulsiones. Para atribuir una expulsión, registrar último contacto externo y tiempo hasta la caída. Probar una ventana inicial de 2 s. Marcar como inciertos los casos con varios posibles responsables. El ganador siempre se determina por supervivencia. Publicar también expulsiones sin contacto, duración y frecuencia de partidas estancadas.
- **Interpretación.** Comparar cada modelo contra el bot aleatorio, el de persecución y modelos históricos bajo idénticas condiciones. En todos contra todos, mostrar también la victoria esperable por simetría entre políticas idénticas con plazas rotadas (1/3 o 1/4). Revisar repeticiones de victorias, derrotas y empates para detectar patrones como esperar inmóvil a que los otros caigan. La frecuencia de una combinación es descriptiva. Para estimar su aporte, comparar evaluaciones pareadas con esa acción disponible y desactivada, indicando que se trata de una variante experimental.

## 8. Apple Silicon, Windows y Docker

### Herramientas y destinos

| Uso | Ruta prevista | Criterio de validación |
|---|---|---|
| Jugar y desarrollar en Mac | Editor Unity 6 LTS nativo para Apple Silicon y jugador macOS ARM64. | Abrir la escena, comprobar los diez controles y disputar una ronda frente a un ONNX local. |
| Jugar y desarrollar en Windows | Mismo parche del Editor y paquetes que en macOS. Generar jugador nativo para la arquitectura del equipo. | Repetir pruebas de teclado y física, e importar el mismo modelo sin convertirlo. |
| Entrenar en Docker | Imagen Linux `amd64` de referencia con entrenador Python y simulador Unity Linux x86-64 sin gráficos. CPU como configuración inicial. | En Windows x86-64 con Docker Desktop y WSL2, verificar comunicación, avance del optimizador, escritura de resultados y reanudación. Comprobar arquitectura, RAM y recursos asignados antes de medir velocidad. [Docker Desktop y WSL2](https://docs.docker.com/desktop/features/wsl/) |
| Usar Docker en Apple Silicon | Ejecutar esa misma imagen con plataforma explícita `linux/amd64` mediante emulación. Medir una corrida corta. Como ruta local adicional, usar el jugador macOS ARM64 y el entrenador Python nativo con versiones compatibles. | Registrar si completa episodios y guarda un modelo en cada ruta probada. Medir la velocidad de cada una por separado. Si la emulación o alguna dependencia impide entrenar en el Mac, conservar el Mac como entorno jugable y usar el host que valide la imagen para la corrida larga. [Arquitecturas y emulación de Docker](https://docs.docker.com/build/building/multi-platform/) |

**Versiones propuestas.** Usar un parche LTS de Unity 6 compatible con ML-Agents Release 23 y fijarlo en `ProjectVersion.txt`. La base de integración combina el paquete Unity `com.unity.ml-agents` 4.0.0, los paquetes Python `mlagents` y `mlagents-envs` 1.1.0 y Python 3.10.12. Fijar la versión exacta de PyTorch y todas las dependencias una vez verificados entrenamiento, guardado y exportación ONNX. Conservar `packages-lock.json`, el archivo de dependencias de Python, los hashes del simulador y el identificador de la imagen. La prueba de integración se hará antes de una corrida costosa. [Release 23 de Unity](https://github.com/Unity-Technologies/ml-agents/releases/tag/release_23_tag) · [Instalación de esa versión](https://github.com/Unity-Technologies/ml-agents/blob/release_23_tag/com.unity.ml-agents/Documentation~/Installation.md)

**Compilación del simulador.** Instalar mediante Unity Hub el módulo del destino Linux. La referencia será un jugador Linux64 Standalone con backend **Mono**, ejecutado sin gráficos. Evaluar el perfil Dedicated Server Linux con Mono como optimización si conserva el intercambio de observaciones y acciones y el comportamiento físico. El Editor se usa para construir fuera de la imagen de ejecución. Empaquetar el directorio completo del jugador, sus datos, permisos de ejecución y bibliotecas necesarias. Verificar que el entrenador recibe decisiones y actualiza pesos para detectar una caída silenciosa al modo de inferencia. [Límites del entrenamiento de ML-Agents](https://github.com/Unity-Technologies/ml-agents/blob/release_23_tag/com.unity.ml-agents/Documentation~/Package-Limitations.md) · [Compilación de Dedicated Server](https://docs.unity3d.com/6000.0/Documentation/Manual/dedicated-server-build.html)

### Ciclo previsto de una corrida

1. Partir de la revisión del juego aprobada, compilar el simulador y construir la imagen con las versiones fijadas. Incluir un perfil CPU y valorar GPU solo después de comparar tiempos de simulación y actualización. Un perfil NVIDIA para Windows requeriría WSL2 y controladores compatibles. [GPU en Docker Desktop](https://docs.docker.com/desktop/features/gpu/)
2. Dentro del contenedor, arrancar el entrenador con la configuración, la ruta al ejecutable, el número de entornos, la semilla y un identificador único. `mlagents-learn` gestiona los procesos Unity y sus puertos. Probar primero una instancia y escalar a 2, 4 y 8. Mantener la comunicación dentro del contenedor. [Opciones de la CLI de ML-Agents](https://github.com/Unity-Technologies/ml-agents/blob/release_23_tag/ml-agents/mlagents/trainers/cli_utils.py)
3. Montar el directorio de resultados en un volumen persistente. Guardar configuración efectiva, semillas, versiones, eventos, métricas de TensorBoard, puntos de control y ONNX exportado. Una parada ordenada debe permitir retomar la corrida desde su último punto guardado.
4. Para continuar en el otro equipo, transferir el directorio completo de la corrida cerrada y usar la misma imagen, configuración, versión del contrato e identificador con `--resume`. Comprobar que aumenta el contador de pasos y se registra una nueva actualización. Para jugar, importar el ONNX en los jugadores nativos y ejecutar una ronda con el entrenador desconectado.
5. Repetir un caso controlado en Mac y Windows. Esperar las mismas reglas y el mismo formato de acciones y observaciones. Comparar resultados estadísticos y trayectorias con tolerancia para diferencias numéricas de la física.

Con la configuración y el ejecutable empaquetados, la primera prueba usará una sola instancia dentro de la imagen.

```sh
mlagents-learn /opt/gangfight/config/duelo.yaml \
  --env=/opt/gangfight/build/GangFight.x86_64 \
  --num-envs=1 --no-graphics --seed=41 \
  --run-id=duelo-s41 --results-dir=/results
```

**Rendimiento.** Medir decisiones aceptadas del aprendiz por segundo de reloj real, pasos físicos de arena por segundo, número de arenas, RAM máxima, CPU y tiempo de actualización del modelo. Separar las decisiones de los rivales congelados. Tomar una línea base con una instancia, calentar 1 minuto y medir 5 minutos por configuración con el mismo escenario. Si la simulación domina, comparar más procesos con más arenas aisladas por proceso. Conservar la aspiración de superar 5.000 decisiones del aprendiz/s con ocho procesos como objetivo experimental en hardware identificado. La medición en un Mac emulado se informará por separado.

## 9. Hitos y orden de desarrollo

Los plazos toman como referencia las 12–14 semanas del plan inicial. Se revisarán tras medir el control físico y la primera corrida en los equipos disponibles. Cada hito entrega algo que se puede jugar o verificar.

| Hito | Referencia | Trabajo y dependencia | Criterio de salida |
|---|---|---|---|
| H0 · Proyecto y compatibilidad | Semana 1 | Después de la confirmación, crear el proyecto, fijar versiones y perfiles, comprobar apertura en los dos sistemas y probar comunicación Python–Unity con una escena mínima. | Versiones registradas, una decisión recibida y una respuesta aplicada en un jugador de prueba. |
| H1 · Prototipo jugable | Semanas 1–3 | Afinar el luchador de la arena existente. Integrar puñetazos, agarres, cabezazo, patada y ataques aéreos con el nuevo cuerpo. | Una persona recorre la arena, combina golpes, identifica un KO, alza y expulsa a un rival. Repetir en ambas plataformas antes del entrenamiento largo. |
| H2 · Reglamento y paridad | Semanas 4–5 | Integrar KO, rondas, bots, observaciones, adaptador ML-Agents y repeticiones. Ejecutar los casos límite y la prueba de equivalencia de controles. | No hay resurrecciones tempranas ni resultados duplicados. Pasan las pruebas de dos, tres y cuatro plazas y el primer episodio de aprendizaje. |
| H3 · Entrenamiento reproducible | Semanas 6–8 | Compilar jugador Mono, empaquetar Docker, ejecutar prueba corta, exportar ONNX y perfilar 1/2/4/8 procesos. | Una corrida se guarda y reanuda. El modelo actúa en la escena jugable. Quedan documentadas las pruebas y límites de cada ruta de hardware disponible. |
| H4 · Duelo táctico | Semanas 9–11 | Entrenar frente al muñeco, bots y adversarios históricos. Ajustar observaciones o recompensas solo con una comparación registrada. | Informe por rival, pruebas con semillas reservadas, repeticiones de combinaciones y modelo de duelo seleccionable como oponente. |
| H5 · Todos contra todos y entrega | Semanas 12–14 | Entrenar o adaptar el modelo con varios rivales. Evaluar generalización y el efecto del ventilador. Validar juego humano contra el modelo. | Partidas de hasta cuatro luchadores con resultado correcto, modelo evaluado, imagen reproducible, instrucciones de juego y entrenamiento, y tabla de resultados por plataforma. |

### Orden de implementación de la revisión de combate

| Paso | Cambio | Prueba para pasar al siguiente |
|---|---|---|
| 1. Referencia, cuerpo y velocidad | Medir la patada, el desplazamiento y el alzado actuales. Construir el cuerpo de cinco articulaciones y aplicar la reducción del 50 %. Probar la nueva velocidad libre. | Comparar masa total, frenado, equilibrio al patear y elevación del mismo rival con ambas manos. Registrar si ahora se desplaza demasiado al chocar. |
| 2. Gestos y agarres | Resolver toque y pulsación larga por separado para cada mano. Integrar el gesto de alzado elegido y los objetos agarrables. | Probar una pulsación menor que un intervalo de decisión, justo antes del umbral, exactamente en él y después. Mantener J mientras L golpea y soltar J conservando una presa de L. |
| 3. Ataques y KO | Integrar las tres clases de golpe, tiempos, ventanas, contactos y medidor de aturdimiento. Actualizar la ayuda de controles y la respuesta visual. | Acertar y fallar con cada ataque. Mantener I o K produce una acción. Repetir el contacto con varios colliders del rival cuenta una vez. Un golpe fuerte o un combo produce KO, recuperación y continuación de la ronda. |
| 4. Combate aéreo | Incorporar la dirección de entrada y el impulso horizontal según el criterio elegido. | Probar ambos puños, cabeza, bota y agarres en salto. Comprobar trayectoria diagonal, ausencia de entrada, aterrizaje y riesgo de caída al fallar. |
| 5. IA, repeticiones y prueba jugable | Adaptar bots, observaciones, registros y ayuda de controles a la versión aprobada. | Una secuencia grabada y la misma secuencia enviada por el adaptador de IA producen los mismos gestos y eventos en el mismo entorno. Jugar un duelo y revisar tiempos de impacto, golpes por KO, agarres involuntarios y caídas propias antes de fijar los ajustes. |

**Decisiones de combate.** Se mantienen las diez teclas. Sujetar la misma presa con las dos manos activa el alzado. El KO usa un umbral de aturdimiento ajustable. Cada salto admite un impulso horizontal en la primera acción aérea. La cámara queda fija desde fuera de la plataforma.

### Riesgos y señales para actuar

| Señal observable | Respuesta prevista |
|---|---|
| El luchador cae al caminar en línea recta o sale disparado al agarrar con ambas manos. | Aislar la escena física, ajustar masas, límites y fuerza de las uniones y repetir la misma secuencia grabada antes de invertir cómputo en IA. |
| El agente suma recompensa repitiendo una acción o gana porque el bot se cae solo. | Revisar atribución y repeticiones. Comparar con la referencia de recompensa terminal y con rivales que protegen el borde. |
| El modelo domina a un rival o un lado y falla en los demás. | Rotar apariciones y oponentes durante entrenamiento y reevaluar con la colección reservada. |
| Al subir el número de procesos disminuye la velocidad o se agota la memoria. | Medir costo por instancia, comparar arenas por proceso y usar el número que realmente mejore el rendimiento. |
| El modelo funciona en el entrenador y falla en el juego. | Comprobar versión de observaciones, controles y frecuencia. Reproducir la misma ronda en el ejecutable con el ONNX exportado. |
| La imagen funciona en un equipo y falla o es muy lenta en otro. | Registrar arquitectura y etapa del fallo, comprobar dependencias del binario y separar compatibilidad de rendimiento. Transportar el punto de control a la ruta validada. |

El despliegue se decidirá con el modelo, la imagen y las mediciones disponibles. Si después se incorpora juego por IP, la autoridad de la partida resolverá física, KO y eliminaciones, y los clientes transmitirán el mismo comando de jugador definido aquí.
