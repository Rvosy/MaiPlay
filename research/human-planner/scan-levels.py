"""Run a frozen HumanPlanner over a catalog, or verify and collect existing results."""

import argparse
import concurrent.futures
import hashlib
import json
import subprocess
import sys
import time
import traceback
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path


CLUSTER_RULE = ('Unique note IDs in non-Perfect results union gaps; order by Note.Time '
                'then ID; merge adjacent time differences <= 1 second transitively '
                '(1e-9 tolerance); no total-span cap.')


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def reference(path):
    path = path.resolve()
    try:
        path = path.relative_to(Path.cwd())
    except ValueError:
        pass
    return path.as_posix()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix('.tmp.json')
    temporary.write_bytes((json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
    temporary.replace(path)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def locations_for(chart, manifest, directory):
    record = read(directory / 'note-locations.json')
    require(record['coreSha256'] == manifest['coreSha256'], 'Note-location Core hash mismatch')
    require(record['chartSha256'] == chart['chartSha256'], 'Note-location chart hash mismatch')
    require(record['key'] == chart['key'], 'Note-location chart key mismatch')
    locations = {note['id']: note for note in record['notes']}
    require(len(locations) == len(record['notes']) == chart['notes'], 'Parsed note count or IDs mismatch')
    return locations


def summarize(chart, manifest, directory, process_record):
    source = Path(manifest['chartRoot']) / chart['path']
    require(sha(source) == chart['chartSha256'], 'Source chart hash mismatch')
    locations = locations_for(chart, manifest, directory)
    result_path = directory / 'results.json'
    result = read(result_path)
    notes = {note['id']: note for note in result['results']}
    require(result['difficulty'] == chart['difficulty'], 'Result difficulty mismatch')
    require(result['level'] == chart['level'] and result['title'] == chart['title'], 'Result chart identity mismatch')
    require(result['notes'] == len(result['results']) == len(notes) == chart['notes'], 'Result note count or duplicate IDs')
    require(notes.keys() == locations.keys(), 'Result note IDs differ from parsed chart')
    for note_id, note in notes.items():
        location = locations[note_id]
        require(note['kind'] == location['kind'] and abs(note['time'] - location['time']) <= 1e-9,
                f'Result note {note_id} kind or onset differs from parsed chart')
    errors = [note for note in result['results'] if note['grade'] != 'Perfect']
    require(result['allCriticalPerfect'] == (not errors), 'AllCriticalPerfect disagrees with result grades')
    expected_exit = 0 if result['allCriticalPerfect'] else 2
    require(process_record['processExitCode'] == expected_exit,
            f'Process exit {process_record["processExitCode"]} disagrees with expected {expected_exit}')
    error_ids = {note['id'] for note in errors}
    gap_ids = {gap['noteId'] for gap in result['gaps']}
    require(gap_ids <= notes.keys(), 'Gap references an unknown note ID')
    incidents = sorted(error_ids | gap_ids, key=lambda note_id: (locations[note_id]['time'], note_id))
    clusters = []
    for note_id in incidents:
        note_time = locations[note_id]['time']
        if not clusters or note_time - clusters[-1]['end'] > 1 + 1e-9:
            clusters.append({'start': note_time, 'end': note_time, 'noteIds': []})
        clusters[-1]['end'] = note_time
        clusters[-1]['noteIds'].append(note_id)
    for cluster in clusters:
        ids = set(cluster['noteIds'])
        cluster.update(spanSeconds=cluster['end'] - cluster['start'], uniqueAffectedCount=len(ids),
                       errorNoteIds=sorted(ids & error_ids), gapNoteIds=sorted(ids & gap_ids),
                       overlapCount=len(ids & error_ids & gap_ids))
    failures = [dict(note, location=locations[note['id']]) for note in errors]
    gaps = [dict(gap, location=locations[gap['noteId']]) for gap in result['gaps']]
    movement = result['diagnostics']['movement']
    hashes = {'resultsSha256': sha(result_path), 'handsSha256': sha(directory / 'hands.json'),
              'noteLocationsSha256': sha(directory / 'note-locations.json')}
    for field, value in hashes.items():
        if field in process_record:
            require(process_record[field] == value, f'Existing {field} mismatch')
    return dict(chart, coreSha256=manifest['coreSha256'], seconds=process_record['seconds'],
                processExitCode=process_record['processExitCode'], directory=str(directory.resolve()),
                status='all-cp' if not errors else 'partial', cp=len(notes) - len(errors), nonCP=len(errors),
                miss=sum(note['grade'] in ('Miss', 'TooFast') for note in errors), gaps=len(gaps),
                uniqueAffected=len(incidents), clusterCount=len(clusters), achievement=result['achievement'],
                dxScore=result['dxScore'], failures=failures, gapDetails=gaps, clusters=clusters,
                movement={'speedLimitViolations': movement['speedLimitViolations'],
                          'peakSpeedMillimetersPerSecond': movement['peakSpeedMillimetersPerSecond']}, **hashes)


def error_record(chart, manifest, directory, error, process_record=None):
    return dict(chart, coreSha256=manifest['coreSha256'], directory=str(directory.resolve()),
                status='error', error=str(error), nonCP=None, clusterCount=None,
                **(process_record or {}))


def run(chart, manifest, harness, output):
    directory = output / 'charts' / chart['key']
    process_record = {'processExitCode': None, 'seconds': 0}
    started = time.monotonic()
    try:
        source = Path(manifest['chartRoot']) / chart['path']
        require(sha(source) == chart['chartSha256'], 'Source chart hash mismatch')
        locations_for(chart, manifest, directory)
        process = subprocess.run(['dotnet', str(harness), '--plan-human', str(source),
                                  str(directory), str(chart['difficulty'])],
                                 capture_output=True, text=True, encoding='utf-8', errors='replace')
        (directory / 'run.log').write_text(process.stdout + process.stderr, encoding='utf-8')
        process_record.update(processExitCode=process.returncode, seconds=time.monotonic() - started)
        require(process.returncode in (0, 2),
                f'Harness exited {process.returncode}: {process.stderr or process.stdout}')
        row = summarize(chart, manifest, directory, process_record)
    except Exception:
        process_record['seconds'] = time.monotonic() - started
        row = error_record(chart, manifest, directory, traceback.format_exc(), process_record)
    write(directory / 'scan-record.json', row)
    return row


def collect(chart, manifest, output):
    directory = output / 'charts' / chart['key']
    process_record = {}
    try:
        record = read(directory / 'scan-record.json')
        process_record = {key: record.get(key) for key in ('processExitCode', 'seconds')}
        require(record['coreSha256'] == manifest['coreSha256'], 'Recorded Core hash mismatch')
        for field in ('key', 'path', 'chartSha256', 'difficulty'):
            require(record[field] == chart[field], f'Recorded {field} mismatch')
        require(record['status'] != 'error', record.get('error', 'Recorded process failed'))
        return summarize(chart, manifest, directory, record)
    except Exception:
        return error_record(chart, manifest, directory, traceback.format_exc(), process_record)


def sort_key(row):
    return (row['nonCP'] is None, row['nonCP'] if row['nonCP'] is not None else 10**9,
            row['clusterCount'] if row['clusterCount'] is not None else 10**9, row['path'], row['difficulty'])


def statistics(rows):
    valid = [row for row in rows if row['status'] != 'error']
    return {'charts': len(rows), 'validCharts': len(valid), 'processOrValidationErrors': len(rows) - len(valid),
            'allCP': sum(row['nonCP'] == 0 for row in valid),
            **{field: sum(row[field] for row in valid)
               for field in ('notes', 'cp', 'nonCP', 'miss', 'gaps', 'uniqueAffected', 'clusterCount')},
            'errorGrades': dict(sorted(Counter(note['grade'] for row in valid for note in row['failures']).items())),
            'errorKinds': dict(sorted(Counter(note['kind'] for row in valid for note in row['failures']).items())),
            'errorReasons': dict(sorted(Counter(note['reason'] for row in valid for note in row['failures']).items())),
            'speedLimitViolations': sum(row['movement']['speedLimitViolations'] for row in valid),
            'chartsWithSpeedLimitViolations': sum(row['movement']['speedLimitViolations'] > 0 for row in valid),
            'nonCPDistribution': dict(sorted(Counter(row['nonCP'] for row in valid).items())),
            'clusterCountDistribution': dict(sorted(Counter(row['clusterCount'] for row in valid).items()))}


def make_report(rows, manifest, args, complete, seconds):
    ordered = sorted(rows, key=sort_key)
    groups = sorted({(chart['difficulty'], chart['difficultyName'], chart['displayLevel']) for chart in manifest['charts']})
    return {'createdAtUtc': datetime.now(timezone.utc).isoformat(), 'coreSha256': manifest['coreSha256'],
            'manifestSha256': sha(args.manifest), 'complete': complete and all(row['status'] != 'error' for row in rows),
            'scope': {'files': manifest['files'], 'levels': manifest['levels'],
                      'displayLevelRule': manifest['displayLevelRule'], 'parseErrors': manifest['parseErrors']},
            'inputs': {'manifest': reference(args.manifest), 'harness': reference(args.harness),
                       'artifactRoot': reference(args.output), 'chartRoot': reference(Path(manifest['chartRoot']))},
            'planned': len(manifest['charts']), 'completed': len(rows), 'seconds': seconds,
            'mode': 'collect-existing' if args.collect_only else 'run',
            'workers': None if args.collect_only else args.workers,
            'clusterRule': CLUSTER_RULE,
            'rankingRule': 'nonCP ascending, clusterCount ascending, path ordinal, difficulty ascending; errors last',
            'summary': statistics(rows),
            'groups': [dict(difficulty=difficulty, difficultyName=name, displayLevel=level,
                            **statistics([row for row in rows if row['difficulty'] == difficulty and row['displayLevel'] == level]))
                       for difficulty, name, level in groups],
            'rows': ordered}


def write_statistics(report, directory):
    # Published statistics exclude source notation and complete input/trajectory logs.
    published = dict(report, rows=[])
    for row in report['rows']:
        entry = {key: value for key, value in row.items() if key != 'directory'}
        entry['evidence'] = reference(Path(row['directory']))
        entry['failures'] = [{key: value for key, value in note.items() if key != 'source'}
                             for note in row.get('failures', [])]
        published['rows'].append(entry)
    write(directory / 'ranking.json', published)
    write_markdown(published, directory)


def write_markdown(report, directory):
    def cell(value):
        return str(value).replace('|', '\\|').replace('\n', ' ')

    summary = report['summary']
    lines = ['# HumanPlanner 等级扫描', '',
             f'冻结 Core：`{report["coreSha256"]}`。清单 SHA-256：`{report["manifestSha256"]}`。', '',
             f'曲库包含 {report["scope"]["files"]} 个 maidata.txt，解析失败 {len(report["scope"]["parseErrors"])} 个；'
             f'选取显示等级 {"、".join(report["scope"]["levels"])}，遍历所有难度编号。', '',
             f'已核验 {summary["validCharts"]}/{report["planned"]} 张谱面，'
             f'进程或证据校验错误 {summary["processOrValidationErrors"]} 张。'
             f'全 CP {summary["allCP"]} 张；{summary["notes"]} 个音符中有 {summary["nonCP"]} 个非 CP，'
             f'其中 Miss/TooFast {summary["miss"]} 个。Gap {summary["gaps"]} 条，'
             f'非 CP 与 Gap 合并去重后影响 {summary["uniqueAffected"]} 个音符，共 {summary["clusterCount"]} 簇。', '',
             f'运动诊断共记录 {summary["speedLimitViolations"]} 次速度限制违例，'
             f'涉及 {summary["chartsWithSpeedLimitViolations"]} 张谱面。', '',
             '| 难度 | 显示等级 | 谱面 | 全 CP | 非 CP | Miss/TooFast | Gap | 错误簇 |',
             '|---|---|---:|---:|---:|---:|---:|---:|']
    for group in report['groups']:
        lines.append('| ' + ' | '.join(cell(group[key]) for key in
                     ('difficultyName', 'displayLevel', 'charts', 'allCP', 'nonCP', 'miss', 'gaps', 'clusterCount')) + ' |')
    lines += ['', '## 统计口径', '',
              '扫描清单包含曲库中所有所选显示等级与难度编号的组合；同曲不同难度分别计数。'
              '定数的小数部分达到 0.6 时显示为加号，显式加号也可识别。', '',
              'CP 对应判定枚举 Perfect。非 CP 是其余所有判定；Miss 统计包含 TooFast。'
              'Gap 单独计数，不能与非 CP 直接相加。成绩来自冻结求解器生成输入后交给 PlaybackSession 的完整回放。', '',
              '错误簇先合并非 CP 与 Gap 的音符编号，再按原始 Note.Time、编号排序。'
              '相邻时间差不超过 1 秒时传递合并，浮点容差为 1e-9；簇总跨度可以超过 1 秒。'
              'Hold/Slide 使用起始时刻，不使用判定发生时刻或持续区间。', '',
              '排序依次为非 CP 数、错误簇数、谱面路径、难度编号，均升序；进程或校验错误排在最后。'
              '完整排行保留零错误谱面。错误少只是调查顺序，不代表问题容易修复。', '',
              '结果数量、唯一音符编号、音符类型与起始时刻均与冻结解析器导出的谱面记录比对；'
              '同时检查源谱、Core、原始结果与轨迹 SHA-256，以及进程退出码与 AllCriticalPerfect。'
              '运动诊断保留速度限制违例和逐谱峰值速度；它们按当前输入模型计算，不代表实机人体测量。', '',
              '## 非 CP 类型', '', '| 音符类型 | 数量 |', '|---|---:|']
    lines += [f'| {cell(kind)} | {count} |' for kind, count in summary['errorKinds'].items()]
    lines += ['', '## 判定分布', '', '| 判定 | 数量 |', '|---|---:|']
    lines += [f'| {cell(grade)} | {count} |' for grade, count in summary['errorGrades'].items()]
    lines += ['', '## 每谱错误簇分布', '', '| 错误簇数 | 谱面数 |', '|---|---:|']
    lines += [f'| {count} | {charts} |' for count, charts in summary['clusterCountDistribution'].items()]
    lines += ['', '[完整排行](charts.md) 保留全部谱面，[单错清单](one-error-charts.md) 单独列出仅有一个非 CP 的谱面；'
              '[机器可读报告](ranking.json) 还保留每个非 CP 与 Gap 的位置、'
              '持续时间、判定偏差、原因和簇内音符编号。原始谱面不随报告发布；整曲轨迹与原始回放结果保存在 artifacts/。', '',
              '## 复现输入', '',
              f'- 冻结 Harness：`{report["inputs"]["harness"]}`。',
              f'- 扫描清单：`{report["inputs"]["manifest"]}`。',
              f'- 原始结果目录：`{report["inputs"]["artifactRoot"]}`。',
              f'- 曲库根目录：`{report["inputs"]["chartRoot"]}`。', '',
              '使用仓库中的 `research/human-planner/scan-levels.py --collect-only` 可从这些证据重算统计。'
              '报告生成时间和本次重聚合用时会变化；逐谱证据及其原始字节指纹保持不变。', '']
    (directory / 'README.md').write_bytes('\n'.join(lines).encode('utf-8'))
    lines = ['# 完整排行', '',
             '按非 CP、错误簇数、谱面路径、难度编号依次升序。[统计口径](README.md)。', '',
             '| 排名 | 曲目 | 难度 | 定数 | CP/音符 | 非 CP | Miss | Gap | 簇 | 证据编号 |',
             '|---:|---|---|---|---:|---:|---:|---:|---:|---|']
    for rank, row in enumerate(report['rows'], 1):
        values = [rank, row['title'], row['difficultyName'], row['level'],
                  f'{row.get("cp", "—")}/{row["notes"]}', row['nonCP'], row.get('miss', '—'),
                  row.get('gaps', '—'), row['clusterCount'], f'`{row["key"]}`']
        lines.append('| ' + ' | '.join(cell(value) for value in values) + ' |')
    (directory / 'charts.md').write_bytes(('\n'.join(lines) + '\n').encode('utf-8'))
    candidates = [row for row in report['rows'] if row['nonCP'] == 1]
    lines = ['# 单错谱面清单', '',
             f'共 {len(candidates)} 张谱面仅有一个非 CP，按完整排行顺序列出。'
             '时间使用原始 Note.Time；持续音符同时列出 EndTime。Gap 与非 CP 可能重叠，不相加。', '',
             '| 序号 | 曲目 | 难度 | 定数 | 音符 | 时间（秒） | 类型与位置 | 判定 | 原因 | Gap |',
             '|---:|---|---|---|---:|---|---|---|---|---:|']
    for rank, row in enumerate(candidates, 1):
        note = row['failures'][0]
        location = note['location']
        position = location['touchArea'] if location['kind'] in ('Touch', 'TouchHold') else location['startPosition']
        if location['kind'] == 'Slide':
            position = f'{position}→{location["endPosition"]}'
        timing = f'{location["time"]:.6f}'
        if location['endTime'] > location['time'] + 1e-9:
            timing += f'–{location["endTime"]:.6f}'
        values = [rank, row['title'], row['difficultyName'], row['level'], note['id'], timing,
                  f'{location["kind"]} {position}', note['grade'], note['reason'] or '—', row['gaps']]
        lines.append('| ' + ' | '.join(cell(value) for value in values) + ' |')
    lines += ['', '完整精度、触区及起止位置、判定偏差、Gap 原因和证据编号见 [ranking.json](ranking.json)。', '']
    (directory / 'one-error-charts.md').write_bytes('\n'.join(lines).encode('utf-8'))


def main():
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, 'reconfigure'):
            stream.reconfigure(encoding='utf-8', errors='replace')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--harness', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--workers', type=int, default=8)
    parser.add_argument('--collect-only', action='store_true', help='Verify existing evidence without rerunning or changing scan records')
    parser.add_argument('--report', type=Path, help='Optional directory for source-free ranked statistics')
    args = parser.parse_args()
    require(args.workers > 0, '--workers must be positive')
    manifest = read(args.manifest)
    core = args.harness.parent / 'MaiPlay.Core.dll'
    require(sha(core) == manifest['coreSha256'], 'Core fingerprint mismatch')
    charts = manifest['charts']
    require(not manifest['parseErrors'], 'Catalog contains parse errors')
    require(len({chart['key'] for chart in charts}) == len(charts), 'Duplicate chart keys')
    require(len({(chart['path'], chart['difficulty']) for chart in charts}) == len(charts), 'Duplicate chart identities')
    args.output.mkdir(parents=True, exist_ok=True)
    if not args.collect_only:
        for chart in charts:
            directory = args.output / 'charts' / chart['key']
            for name in ('scan-record.json', 'results.json', 'hands.json', 'run.log', 'failure.json'):
                require(not (directory / name).exists(), f'Existing evidence: {directory / name}; use --collect-only or a new output directory')
    rows = []
    started = time.monotonic()
    if args.collect_only:
        rows = [collect(chart, manifest, args.output) for chart in charts]
    else:
        with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as executor:
            pending = {executor.submit(run, chart, manifest, args.harness, args.output): chart for chart in charts}
            for future in concurrent.futures.as_completed(pending):
                chart = pending[future]
                try:
                    row = future.result()
                except Exception:
                    row = error_record(chart, manifest, args.output / 'charts' / chart['key'], traceback.format_exc())
                rows.append(row)
                write(args.output / 'ranking.json', make_report(rows, manifest, args, False, time.monotonic() - started))
                print(f'{len(rows)}/{len(charts)} {row["title"]} #{row["difficulty"]}: '
                      f'nonCP={row["nonCP"]}, clusters={row["clusterCount"]}', flush=True)
    require(sha(core) == manifest['coreSha256'], 'Core fingerprint changed during batch')
    report = make_report(rows, manifest, args, True, time.monotonic() - started)
    write(args.output / 'ranking.json', report)
    if args.report:
        write_statistics(report, args.report)
    print(json.dumps(report['summary'], ensure_ascii=False), flush=True)
    return 0 if report['complete'] else 1


if __name__ == '__main__':
    sys.exit(main())
