"""Decode game AWB assets locally; reuse one OGG per cue."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed
import json, subprocess, shutil, os, argparse, sys

def main():
    ap=argparse.ArgumentParser();ap.add_argument('output',type=Path);ap.add_argument('--decoder',type=Path,required=True);args=ap.parse_args()
    root=args.output.resolve();report=json.loads((root/'report.json').read_text(encoding='utf-8'))
    source=Path(report['source']);assets={p.name.lower():p for p in sorted(source.rglob('*.awb'))}
    decoder=args.decoder.resolve()
    ffmpeg=shutil.which('ffmpeg');cache=root/'audio';cache.mkdir(exist_ok=True)
    if not decoder.is_file(): raise FileNotFoundError(decoder)
    if not ffmpeg: raise FileNotFoundError('ffmpeg is not on PATH')
    cues=sorted({s['cue'] for s in report['songs'] if s['charts'] and s['cue'] is not None})
    def decode(cue):
        name='music'+str(cue).zfill(6);src=assets.get(name+'.awb');ogg=cache/(name+'.ogg');wav=cache/(name+'.wav');partial=cache/(name+'.partial.ogg')
        if src is None: return {'cue':cue,'error':'AWB missing'}
        try:
            meta=subprocess.run([str(decoder),'-m',str(src)],capture_output=True,text=True,check=True).stdout
            if 'stream count:' in meta and 'stream count: 1\n' not in meta: raise ValueError('Multiple audio streams require selection: '+meta)
            if not ogg.exists():
                subprocess.run([str(decoder),'-i','-o',str(wav),str(src)],capture_output=True,check=True)
                subprocess.run([ffmpeg,'-hide_banner','-loglevel','error','-y','-i',str(wav),'-c:a','libvorbis','-q:a','5',str(partial)],capture_output=True,check=True)
                subprocess.run([ffmpeg,'-hide_banner','-loglevel','error','-xerror','-i',str(partial),'-f','null','-'],capture_output=True,check=True)
                partial.replace(ogg)
                wav.unlink()
            return {'cue':cue,'source':str(src),'output':str(ogg.relative_to(root)),'bytes':ogg.stat().st_size,'metadata':meta}
        except subprocess.CalledProcessError as e:
            stderr=e.stderr.decode('utf-8',errors='replace') if isinstance(e.stderr,bytes) else e.stderr
            return {'cue':cue,'source':str(src),'error':str(e),'stderr':stderr}
        except Exception as e: return {'cue':cue,'source':str(src),'error':str(e)}
    results=[]
    with ThreadPoolExecutor(max_workers=8) as pool:
        futures=[pool.submit(decode,c) for c in cues]
        for future in as_completed(futures):
            results.append(future.result())
            if len(results)%100==0: print('Audio processed',len(results),'/',len(cues),flush=True)
    bycue={r['cue']:r for r in results};linked=0;unavailable=[]
    for song in report['songs']:
        if not song['charts']:continue
        result=bycue.get(song['cue'])
        if result is None or 'error' in result: unavailable.append(song['id']);continue
        target=root/song.get('directory',song['id'])/'track.ogg';original=root/result['output']
        if not target.exists(): os.link(original,target)
        linked+=1
    summary={'audio':results,'linkedSongs':linked,'unavailableSongs':unavailable}
    (root/'audio-report.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'cues':len(results),'errors':[r for r in results if 'error' in r],'linkedSongs':linked,'unavailableSongs':unavailable},ensure_ascii=False),flush=True)
    return 1 if unavailable else 0
if __name__=='__main__':sys.exit(main())
