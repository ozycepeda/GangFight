"""Cross-check frozen-policy outcomes against the game's authoritative events."""
from pathlib import Path
import json
from collections import Counter

report=[]
for name in ['evaluation-final','evaluation-random']:
    results=json.loads(Path('results/pilot-100k',name+'.json').read_text())
    for r in results:
        path=Path('results/pilot-100k',name+'-'+r['opponent']+'.jsonl')
        events=[json.loads(line) for line in path.read_text().splitlines()]
        endings=[x for x in events if x['type'] in {'winner','draw','timeout'}][:r['rounds']]
        outcomes=Counter('win' if x['type']=='winner' and x['slot']==0 else 'loss' if x['type']=='winner' else x['type'] for x in endings)
        assert dict(outcomes)==r['outcomes'],(name,r['opponent'],outcomes,r['outcomes'])
        assert len(endings)==r['rounds']
        keys={(x['arena'],x['round']) for x in endings}
        relevant=[x for x in events if (x['arena'],x['round']) in keys]
        wins={(x['arena'],x['round']) for x in endings if x['type']=='winner' and x['slot']==0}
        credits=Counter(str(x['other']) for x in relevant if x['type']=='elimination' and x['slot']==1 and (x['arena'],x['round']) in wins)
        hits=sum(x.get('slot')==0 and x['type'].endswith('_hit') for x in relevant)
        report.append(dict(evaluation=name,opponent=r['opponent'],wins=outcomes['win'],opponent_fall_credits_in_wins=dict(credits),learner_hits=hits,rounds_verified=len(endings)))
Path('results/pilot-100k/event-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
