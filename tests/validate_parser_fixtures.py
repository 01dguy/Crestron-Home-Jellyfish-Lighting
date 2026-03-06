#!/usr/bin/env python3
import json
from pathlib import Path

fixtures = json.loads(Path('tests/parser_fixtures.json').read_text())

for fixture in fixtures:
    frame = json.loads(fixture['frame'])
    assert frame['cmd'] == 'fromCtlr', fixture['name']
    expect = fixture['expect']

    if 'runPattern' in frame:
      rp = frame['runPattern']
      assert rp.get('file') == expect.get('scene'), fixture['name']
      assert (rp.get('id') or (rp.get('zoneName') or [''])[0]) == expect.get('zone'), fixture['name']
      nested = json.loads(rp.get('data', '{}'))
      assert nested.get('brightness') == expect.get('brightness'), fixture['name']
      assert nested.get('speed') == expect.get('speed'), fixture['name']

    if 'patternFileData' in frame:
      nested = json.loads(frame['patternFileData']['jsonData'])
      run_data = nested.get('runData', nested)
      assert run_data.get('brightness') == expect.get('brightness'), fixture['name']
      assert run_data.get('speed') == expect.get('speed'), fixture['name']

    if 'zones' in frame:
      zone_names = list(frame['zones'].keys())
      assert zone_names == expect.get('zones'), fixture['name']

print(f'validated {len(fixtures)} parser fixtures')
