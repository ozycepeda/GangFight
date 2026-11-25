"""Consolidate completed measurements and extrapolate compute time, not convergence."""
import json
from pathlib import Path

root=Path('results')
records=[]
for summary in sorted({*root.glob('benchmark*/summary.json'), *root.glob('pilot*/summary.json')}):
    for source in json.loads(summary.read_text()):
        if source.get('returncode') or 'steps_per_second' not in source:
            continue
        # This report describes historical V3 measurements and their parameter counts.
        if source.get('behavior', 'GangFightFighterV3') != 'GangFightFighterV3':
            continue
        row={k:v for k,v in source.items() if k!='scalars'}
        row['source']=str(summary)
        contaminated=summary.parent.name in {'benchmark-baseline-native','benchmark-baseline-clean','benchmark-optimized-t4'} or (summary.parent.name=='benchmark-optimized-t1' and row['arenas']==1)
        row['excluded_from_comparison']=contaminated
        h,l=row['units'],row['layers']
        row['actor_parameters']=105*h+(l-1)*h*(h+1)+18
        row['actor_plus_critic_parameters']=row['actor_parameters']+88*h+(l-1)*h*(h+1)+1
        row['hours_for_steps']={str(n): n/row['steps_per_second']/3600 for n in [100000,1000000,5000000,10000000,50000000,100000000]}
        vals=source.get('scalars',{}).get('GangFight/LearnerWin',[])
        row['last_training_win_rate']=vals[-1]['value'] if vals else None
        records.append(row)
result=dict(completed_runs=len(records),requested_steps=sum(r['steps_requested'] for r in records),records=records)
(root/'benchmark-analysis.json').write_text(json.dumps(result,indent=2))
for row in records:
    print(Path(row['source']).parent.name,row['case'],round(row['steps_per_second']), 'EXCLUDED' if row['excluded_from_comparison'] else '')
