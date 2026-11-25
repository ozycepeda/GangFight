"""Compare frozen 100 s and 1000 s policies using authoritative round events."""
from collections import Counter
import json
from pathlib import Path

import onnx
import torch

ROOT = Path('results/pilot-1000s')
OLD = Path('results/pilot-100k')


def read(path):
    return json.loads(path.read_text())


def diagnostics(folder, stem, record):
    events = [json.loads(line) for line in (folder / f'{stem}-{record["opponent"]}.jsonl').read_text().splitlines()]
    endings = [e for e in events if e['type'] in {'winner', 'draw', 'timeout'}][:record['rounds']]
    outcomes = Counter('win' if e['type'] == 'winner' and e['slot'] == 0 else 'loss' if e['type'] == 'winner' else e['type'] for e in endings)
    assert len(endings) == record['rounds'] and dict(outcomes) == record['outcomes']
    keys = {(e['arena'], e['round']) for e in endings}
    selected = [e for e in events if (e['arena'], e['round']) in keys]
    return dict(rounds_verified=len(endings), mean_round_seconds=sum(e['elapsed_ticks'] for e in endings)/50/len(endings),
                learner_hits=sum(e['type'].endswith('_hit') and e.get('slot') == 0 for e in selected),
                learner_grabs=sum(e['type'].startswith('grab_') and e.get('slot') == 0 for e in selected),
                learner_self_falls=sum(e['type'] == 'elimination' and e['slot'] == 0 and e['other'] == -1 for e in selected),
                rival_ejections_credited_to_learner=sum(e['type'] == 'elimination' and e['slot'] == 1 and e['other'] == 0 for e in selected))


def main():
    old = read(OLD / 'evaluation-final.json')
    new = read(ROOT / 'evaluation.json')
    control = read(ROOT / 'control-100k/evaluation.json')
    timing = read(ROOT / 'timing.json')
    assert timing['stop_reason'] == 'wall_clock_budget' and 1000 <= timing['learning_seconds'] < 1005
    checkpoint = ROOT / '128x2-a4/GangFightFighterV3/checkpoint.pt'
    state = torch.load(checkpoint, map_location='cpu')
    steps = int(state['global_step']['_GlobalSteps__global_step'].item())
    assert timing['steps']['GangFightFighterV3'] == steps
    onnx_path = ROOT / '128x2-a4/GangFightFighterV3.onnx'
    onnx.checker.check_model(onnx.load(str(onnx_path)))
    control_state = torch.load(ROOT / 'control-100k/GangFightFighterV3.pt', map_location='cpu')
    control_steps = int(control_state['global_step']['_GlobalSteps__global_step'].item())
    comparisons = []
    for a, b, c in zip(old, new, control):
        assert a['opponent'] == b['opponent'] and a['rounds'] == b['rounds'] and a['seed'] == b['seed']
        comparisons.append(dict(opponent=a['opponent'], old=a, new=b, delta_win_percentage_points=100*(b['win_rate']-a['win_rate']),
                                old_diagnostics=diagnostics(OLD, 'evaluation-final', a), new_diagnostics=diagnostics(ROOT, 'evaluation', b), control=c, control_diagnostics=diagnostics(ROOT / 'control-100k', 'evaluation', c)))
    output = dict(timing=timing, steps=steps, control_steps=control_steps, average_steps_per_second=steps/timing['learning_seconds'], onnx=str(onnx_path), comparisons=comparisons,
                  caveat='Same architecture, seed, PPO core parameters, opponent, shaping and arena count; max_steps annealing horizon was 100000 in the short run and 2000000 in the timed run. This is a budget comparison, not an isolated time-only causal experiment.')
    (ROOT / 'comparison.json').write_text(json.dumps(output, indent=2))
    lines = ['# Comparación de entrenamiento de 100 y 1000 segundos', '',
             f'La nueva corrida entrenó desde cero durante {timing["learning_seconds"]:.3f} segundos y alcanzó {steps:,} decisiones. El reloj empieza al entrar al ciclo de aprendizaje, después de conectar Unity, y termina entre pasos completos; la exportación se mide aparte.', '',
             'Se mantuvieron dos capas de 128 unidades, semilla 41, PPO, CPU nativa con un hilo, un proceso con cuatro arenas, duelo contra `passive` y recompensa auxiliar. No se reanudó el piloto anterior.', '',
             'Se amplió `max_steps` de 100000 a 2000000 para que el límite efectivo fuera el reloj. Como PPO utiliza ese horizonte para reducir la tasa de aprendizaje, la comparación con el piloto anterior combina más tiempo y un descenso más lento de esa tasa. El control intermedio de la nueva corrida permite comparar progreso bajo el mismo horizonte.', '',
             '| Rival | Piloto de unos 100 s | Corrida de 1000 s | Cambio |', '| --- | --- | --- | --- |']
    for r in comparisons:
        a,b=r['old'],r['new']
        lines.append(f'| {r["opponent"]} | {a["outcomes"].get("win",0)}/{a["rounds"]} ({100*a["win_rate"]:.1f}%) | {b["outcomes"].get("win",0)}/{b["rounds"]} ({100*b["win_rate"]:.1f}%) | {r["delta_win_percentage_points"]:+.1f} puntos |')
    lines += ['', f'Control adicional: se conservó el checkpoint de {control_steps:,} decisiones de esta misma corrida, antes de continuar hasta 1000 segundos. Comparte el horizonte de descenso de la tasa de aprendizaje con el modelo final.', '', '| Rival | Control de la corrida larga | Final de 1000 s |', '| --- | --- | --- |']
    for r in comparisons:
        c,b=r['control'],r['new']
        lines.append(f'| {r["opponent"]} | {c["outcomes"].get("win",0)}/{c["rounds"]} | {b["outcomes"].get("win",0)}/{b["rounds"]} |')
    lines += ['', 'Las evaluaciones usan pesos congelados, sin recompensa auxiliar, semilla base 9001 y 40 episodios por rival. Se verificaron las victorias, derrotas y cierres por tiempo contra los eventos del reglamento. Son resultados exploratorios de una semilla y una muestra pequeña.', '',
              'Los diagnósticos detallados de contactos, agarres, caídas y duración de rondas están en `comparison.json`. El modelo final está en `128x2-a4/GangFightFighterV3.onnx`; el checkpoint permite continuar entrenando.']
    passive = next(r for r in comparisons if r['opponent'] == 'passive')
    edge = next(r for r in comparisons if r['opponent'] == 'edge')
    a, b = passive['old']['outcomes'], passive['new']['outcomes']
    d = edge['new_diagnostics']
    lines += ['', '## Interpretación y siguiente experimento', '',
              f'El entrenamiento largo mejoró la supervivencia frente al muñeco: las derrotas bajaron de {a.get("loss",0)} a {b.get("loss",0)} y los cierres por tiempo subieron de {a.get("timeout",0)} a {b.get("timeout",0)}. No produjo victorias en esa evaluación. Frente a edge, el modelo final registró {d["learner_hits"]} impactos y {d["learner_grabs"]} agarres, pero {d["rival_ejections_credited_to_learner"]} expulsiones atribuidas a él. Más contactos acumulados no significan mayor eficacia: las rondas duraron más.', '',
              'Mi interpretación es que el perfil actual favorece una política que evita perder, sin aprender a convertir contactos en expulsiones. El reglamento da cero al agotar el tiempo y menos uno a una derrota; la recompensa auxiliar actual depende del desplazamiento del rival hacia el borde. Con un rival inmóvil, una política que sobrevive puede estabilizarse sin explorar suficientemente el combate. Esta es una explicación plausible de los resultados, no una demostración causal.', '',
              'No ampliaría esta misma corrida sin cambiar el escenario. Probaría un currículo con posiciones iniciales más cercanas o arena menor, después rivales activos que provoquen encuentros, y una recompensa de progreso que guíe el acercamiento y la expulsión manteniendo la victoria como objetivo principal. Evaluaría checkpoints intermedios en semillas de validación aparte y elegiría por victorias y expulsiones, no por el último checkpoint ni por supervivencia sola.', '',
              'Antes de aumentar el tamaño de la red, repetiría el perfil revisado con tres semillas y al menos 200 partidas por rival. Este experimento confirma capacidad de cómputo local, pero no que más tiempo en el perfil actual mejore la habilidad de ganar.']
    (ROOT / 'comparacion.md').write_text('\n'.join(lines)+'\n')
    print(json.dumps({k:v for k,v in output.items() if k!='comparisons'}, indent=2))
    for r in comparisons:
        print(r['opponent'], r['old']['outcomes'], '->',r['new']['outcomes'])


if __name__ == '__main__':
    main()
