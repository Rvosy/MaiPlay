"""Compare two frozen level scans, including lost CP inside net-improved charts."""

import argparse
import hashlib
import json
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def scan_complete(root, manifest):
    path = root / 'ranking.json'
    if not path.exists():
        return False
    ranking = read(path)
    if ranking['coreSha256'] != manifest['coreSha256'] or ranking['manifestSha256'] != sha(root / 'manifest.json'):
        raise ValueError(f'Scan summary fingerprint mismatch: {root}')
    count = len(manifest['charts'])
    return (ranking['complete'] and ranking['planned'] == count and ranking['completed'] == count
            and ranking['summary']['validCharts'] == count and ranking['summary']['processOrValidationErrors'] == 0)


def result(root, chart, core_hash):
    directory = root / 'charts' / chart['key']
    record = read(directory / 'scan-record.json')
    if record['status'] == 'error':
        raise ValueError(record['error'])
    if record['coreSha256'] != core_hash or record['chartSha256'] != chart['chartSha256']:
        raise ValueError('Recorded Core or chart fingerprint mismatch')
    path = directory / 'results.json'
    if sha(path) != record['resultsSha256']:
        raise ValueError('Recorded result fingerprint mismatch')
    data = read(path)
    notes = {note['id']: note for note in data['results']}
    if len(notes) != len(data['results']) or len(notes) != chart['notes'] or data['notes'] != chart['notes']:
        raise ValueError('Result count or unique IDs mismatch')
    errors = [note for note in notes.values() if note['grade'] != 'Perfect']
    if record['nonCP'] != len(errors) or data['difficulty'] != chart['difficulty']:
        raise ValueError('Recorded result summary or difficulty mismatch')
    return data, notes, record


def compare_chart(chart, baseline, candidate, base_hash, candidate_hash):
    before, old_notes, _ = result(baseline, chart, base_hash)
    after, new_notes, candidate_record = result(candidate, chart, candidate_hash)
    location_path = candidate / 'charts' / chart['key'] / 'note-locations.json'
    if sha(location_path) != candidate_record['noteLocationsSha256']:
        raise ValueError('Recorded note-location fingerprint mismatch')
    locations = read(location_path)
    if locations['coreSha256'] != candidate_hash or locations['chartSha256'] != chart['chartSha256'] or locations['key'] != chart['key']:
        raise ValueError('Note-location source identity mismatch')
    positions = {note['id']: note for note in locations['notes']}
    if len(positions) != len(locations['notes']) or old_notes.keys() != new_notes.keys() or new_notes.keys() != positions.keys():
        raise ValueError('Note identities changed between scans')
    changes = []
    for note_id, old in old_notes.items():
        new = new_notes[note_id]
        if old['kind'] != new['kind'] or abs(old['time'] - new['time']) > 1e-9:
            raise ValueError(f'Note {note_id} kind or onset changed between scans')
        if new['kind'] != positions[note_id]['kind'] or abs(new['time'] - positions[note_id]['time']) > 1e-9:
            raise ValueError(f'Note {note_id} location kind or onset differs from result')
        if old['grade'] != new['grade']:
            changes.append({'noteId': note_id, 'location': positions[note_id],
                            'before': {key: old[key] for key in ('grade', 'differenceSeconds', 'reason')},
                            'after': {key: new[key] for key in ('grade', 'differenceSeconds', 'reason')}})
    misses = {'Miss', 'TooFast'}
    old_non_cp = sum(note['grade'] != 'Perfect' for note in old_notes.values())
    new_non_cp = sum(note['grade'] != 'Perfect' for note in new_notes.values())
    old_miss = sum(note['grade'] in misses for note in old_notes.values())
    new_miss = sum(note['grade'] in misses for note in new_notes.values())
    lost_cp = [note['noteId'] for note in changes if note['before']['grade'] == 'Perfect']
    recovered_cp = [note['noteId'] for note in changes if note['after']['grade'] == 'Perfect']
    new_misses = [note['noteId'] for note in changes
                  if note['before']['grade'] not in misses and note['after']['grade'] in misses]
    new_low = [note['noteId'] for note in changes
               if note['before']['grade'] == 'Perfect' and note['after']['grade'] not in misses]
    regressions = []
    if new_non_cp > old_non_cp:
        regressions.append('nonCP-increased')
    if new_miss > old_miss:
        regressions.append('miss-increased')
    if after['achievement'] < before['achievement'] - 1e-8:
        regressions.append('achievement-decreased')
    if after['dxScore'] < before['dxScore']:
        regressions.append('dxScore-decreased')
    return dict(chart, before={'nonCP': old_non_cp, 'miss': old_miss, 'gaps': len(before['gaps']),
                              'achievement': before['achievement'], 'dxScore': before['dxScore']},
                after={'nonCP': new_non_cp, 'miss': new_miss, 'gaps': len(after['gaps']),
                       'achievement': after['achievement'], 'dxScore': after['dxScore']},
                nonCPDelta=new_non_cp - old_non_cp, missDelta=new_miss - old_miss,
                gapDelta=len(after['gaps']) - len(before['gaps']),
                achievementDelta=after['achievement'] - before['achievement'],
                dxScoreDelta=after['dxScore'] - before['dxScore'],
                regressionReasons=regressions, lostCP=lost_cp, recoveredCP=recovered_cp,
                newMisses=new_misses, newMissesFromCP=sorted(set(new_misses) & set(lost_cp)),
                newMissesFromLowGrade=sorted(set(new_misses) - set(lost_cp)),
                newLowJudgementsFromCP=new_low,
                netImprovedWithLostCP=new_non_cp < old_non_cp and bool(lost_cp), noteChanges=changes)


def save(report, directory):
    directory.mkdir(parents=True, exist_ok=True)
    temporary = directory / 'comparison.tmp.json'
    temporary.write_bytes((json.dumps(report, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
    temporary.replace(directory / 'comparison.json')
    summary = report['summary']
    lines = ['# 等级扫描对照', '',
             f'状态：{"完整" if report["complete"] else "未完成"}；对照证据错误 {len(report["errors"])} 条。', '',
             f'基线 Core：`{report["baselineCoreSha256"]}`。', '',
             f'修复版 Core：`{report["candidateCoreSha256"]}`。', '',
             f'已比较 {report["compared"]}/{report["planned"]} 张同源谱面。'
             f'全 CP 从 {summary["beforeAllCP"]} 张变为 {summary["afterAllCP"]} 张；'
             f'非 CP 从 {summary["beforeNonCP"]} 个变为 {summary["afterNonCP"]} 个；'
             f'Miss/TooFast 从 {summary["beforeMiss"]} 个变为 {summary["afterMiss"]} 个；'
             f'Gap 从 {summary["beforeGaps"]} 条变为 {summary["afterGaps"]} 条。', '',
             f'整谱非 CP 减少 {summary["improvedCharts"]} 张、增加 {summary["worseNonCPCharts"]} 张，'
             f'CP/Miss/达成率/DX 分任一指标回退 {summary["regressedCharts"]} 张。', '',
             f'逐音符恢复 CP {summary["recoveredCP"]} 个，新丢失 CP {summary["lostCP"]} 个。'
             f'新增 Miss/TooFast {summary["newMisses"]} 个，其中原 CP 变 Miss {summary["newMissesFromCP"]} 个、'
             f'原低判变 Miss {summary["newMissesFromLowGrade"]} 个；原 CP 变低判 {summary["newLowJudgementsFromCP"]} 个。'
             f'整谱非 CP 净减少但仍有新丢 CP 的谱面 {summary["netImprovedWithLostCPCharts"]} 张。', '',
             '## 回退与新丢 CP', '',
             '| 曲目 | 难度 | 非 CP 前→后 | Miss 前→后 | 新丢 CP 编号 | 新 Miss 编号 | 回退指标 |',
             '|---|---|---:|---:|---|---|---|']
    flagged = [row for row in report['rows'] if row['regressionReasons'] or row['lostCP'] or row['newMisses']]
    for row in flagged:
        values = [row['title'], row['difficultyName'], f'{row["before"]["nonCP"]}→{row["after"]["nonCP"]}',
                  f'{row["before"]["miss"]}→{row["after"]["miss"]}', ', '.join(map(str, row['lostCP'])) or '—',
                  ', '.join(map(str, row['newMisses'])) or '—', ', '.join(row['regressionReasons']) or '无']
        lines.append('| ' + ' | '.join(str(value).replace('|', '\\|') for value in values) + ' |')
    if not flagged:
        lines.append('| 无 | — | — | — | — | — | — |')
    lines += ['', '## 对照口径', '',
              '清单中的路径、难度、源谱指纹及全部谱面元数据必须逐条相同。'
              '按音符编号比对完整判定数组，校验原始结果指纹、音符数量、类型和起始时刻。'
              '整谱回退同时观察非 CP、Miss/TooFast、达成率和 DX 分；达成率比较容差为 1e-8。', '',
              '新增 Miss 包括原 CP 或原低判变为 Miss/TooFast。原 CP 变低判单独列出，'
              '不会用整谱改善抵消这些音符。仅发生判定变化的音符记录前后等级、时差、原因及结构化位置。', '',
              '[完整逐谱与逐音符对照](comparison.json)。当前剩余问题按修复版完整排行中的非 CP 与错误簇数排序。', '']
    (directory / 'README.md').write_bytes('\n'.join(lines).encode('utf-8'))


def main():
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, 'reconfigure'):
            stream.reconfigure(encoding='utf-8', errors='replace')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--candidate', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--partial', action='store_true', help='Compare completed candidates while a scan is still running')
    args = parser.parse_args()
    before = read(args.baseline / 'manifest.json')
    after = read(args.candidate / 'manifest.json')
    for manifest in (before, after):
        if manifest['parseErrors']:
            raise ValueError('Catalog contains parse errors')
        charts = manifest['charts']
        if len({chart['key'] for chart in charts}) != len(charts) or len({(chart['path'], chart['difficulty']) for chart in charts}) != len(charts):
            raise ValueError('Catalog contains duplicate chart identities')
    if before['charts'] != after['charts']:
        raise ValueError('Chart catalogs differ')
    rows, errors, pending = [], [], []
    for chart in before['charts']:
        if not (args.candidate / 'charts' / chart['key'] / 'scan-record.json').exists():
            pending.append(chart['key'])
            continue
        try:
            rows.append(compare_chart(chart, args.baseline, args.candidate, before['coreSha256'], after['coreSha256']))
        except Exception as error:
            errors.append({'key': chart['key'], 'path': chart['path'], 'difficulty': chart['difficulty'], 'error': str(error)})
    summary = {
        'notesCompared': sum(row['notes'] for row in rows),
        'beforeAllCP': sum(row['before']['nonCP'] == 0 for row in rows),
        'afterAllCP': sum(row['after']['nonCP'] == 0 for row in rows),
        **{prefix + metric: sum(row[field][key] for row in rows)
           for prefix, field in [('before', 'before'), ('after', 'after')]
           for metric, key in [('NonCP', 'nonCP'), ('Miss', 'miss'), ('Gaps', 'gaps')]},
        'improvedCharts': sum(row['nonCPDelta'] < 0 for row in rows),
        'worseNonCPCharts': sum(row['nonCPDelta'] > 0 for row in rows),
        'regressedCharts': sum(bool(row['regressionReasons']) for row in rows),
        **{key: sum(len(row[key]) for row in rows) for key in
           ('lostCP', 'recoveredCP', 'newMisses', 'newMissesFromCP', 'newMissesFromLowGrade', 'newLowJudgementsFromCP')},
        'netImprovedWithLostCPCharts': sum(row['netImprovedWithLostCP'] for row in rows),
        'gradeTransitions': dict(sorted(Counter(f'{change["before"]["grade"]} -> {change["after"]["grade"]}'
                                               for row in rows for change in row['noteChanges']).items()))}
    base_complete = scan_complete(args.baseline, before)
    candidate_complete = scan_complete(args.candidate, after)
    report = {'createdAtUtc': datetime.now(timezone.utc).isoformat(),
              'baselineCoreSha256': before['coreSha256'], 'candidateCoreSha256': after['coreSha256'],
              'baselineManifestSha256': sha(args.baseline / 'manifest.json'),
              'candidateManifestSha256': sha(args.candidate / 'manifest.json'),
              'complete': base_complete and candidate_complete and not pending and not errors,
              'baselineScanComplete': base_complete, 'candidateScanComplete': candidate_complete,
              'planned': len(before['charts']), 'compared': len(rows),
              'pending': pending, 'errors': errors, 'summary': summary, 'rows': rows}
    save(report, args.output)
    print(json.dumps({'compared': len(rows), 'pending': len(pending), 'errors': errors, 'summary': summary}, ensure_ascii=False))
    for row in rows:
        if row['regressionReasons'] or row['lostCP'] or row['newMisses']:
            print(json.dumps({key: row[key] for key in
                              ('key', 'title', 'difficulty', 'before', 'after', 'regressionReasons', 'lostCP', 'newMisses')}, ensure_ascii=False))
    return 0 if base_complete and not errors and (args.partial or report['complete']) else 1


if __name__ == '__main__':
    sys.exit(main())
