"""Local MA2 to Simai extraction; source game files are read-only."""
from pathlib import Path
from collections import defaultdict, Counter
from bisect import bisect_right
import argparse, json, re, subprocess, sys, xml.etree.ElementTree as ET

LEGACY = {'BRK':'BRTAP','XTP':'EXTAP','XHO':'EXHLD','BST':'BRSTR','XST':'EXSTR'}
KINDS = {'TAP','STR','HLD','TTP','THO','SI_','SCL','SCR','SUL','SUR','SSL','SSR','SV_','SXL','SXR','SLL','SLR','SF_'}
META = {'VERSION','FES_MODE','BPM_DEF','MET_DEF','RESOLUTION','CLK_DEF','COMPATIBLE_CODE','BPM','MET','CLK','SEF'}
def num(v): return format(v,'.12f').rstrip('0').rstrip('.') or '0'

def convert(path):
    rows=[line.split() for line in path.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
    resolution=int(next(r[1] for r in rows if r[0]=='RESOLUTION'))
    bpms=sorted((int(r[1])*resolution+int(r[2]),float(r[3])) for r in rows if r[0]=='BPM')
    if not bpms or bpms[0][0]!=0: raise ValueError('Missing initial BPM')
    ticks=[b[0] for b in bpms]; times=[0.0]
    for i in range(1,len(bpms)): times.append(times[-1]+(ticks[i]-ticks[i-1])*240/resolution/bpms[i-1][1])
    def seconds(t):
        i=bisect_right(ticks,t)-1
        return times[i]+(t-ticks[i])*240/resolution/bpms[i][1]
    notes=[]; controls=Counter()
    for line,r in enumerate(rows):
        cmd=r[0]
        if cmd in META or cmd.startswith(('T_REC_','T_NUM_','T_JUDGE_','TTM_')):
            if cmd in ('SEF','CLK'): controls[cmd]+=1
            continue
        cmd=LEGACY.get(cmd,'NM'+cmd if cmd in KINDS else cmd)
        if cmd[:2] not in ('NM','BR','EX','BX','CN') or cmd[2:] not in KINDS: raise ValueError('Unknown command '+cmd)
        n={'cmd':cmd,'kind':cmd[2:],'tick':int(r[1])*resolution+int(r[2]),'pos':int(r[3])+1,'row':r,'line':line}
        if n['kind'].startswith('S') and n['kind']!='STR':
            if cmd[:2] in ('EX','BX'): raise ValueError('Unsupported EX slide '+cmd)
            n.update(wait=int(r[4]),length=int(r[5]),end=int(r[6])+1,chain=[])
        notes.append(n)
    slides=[n for n in notes if 'chain' in n and not n['cmd'].startswith('CN')]
    connections=sorted((n for n in notes if n['cmd'].startswith('CN')),key=lambda n:(n['tick'],n['line']))
    for n in connections:
        candidates=[]
        for head in slides:
            tail=head['chain'][-1] if head['chain'] else head
            if tail['end']==n['pos'] and tail['tick']+tail['wait']+tail['length']==n['tick']: candidates.append(head)
        if not candidates: raise ValueError(f'Connection has no parent at {n["tick"]}')
        # MA2 stores coincident slide branches in source order; consume the first open tail.
        candidates.sort(key=lambda s:(s['tick'],s['line']))
        if n['wait']!=0: raise ValueError('Connection has nonzero wait')
        candidates[0]['chain'].append(n)
    notes=[n for n in notes if not n['cmd'].startswith('CN')]
    def shape(n):
        k=n['kind']; s=n['pos']
        symbol={'SI_':'-','SUL':'p','SUR':'q','SSL':'s','SSR':'z','SV_':'v','SXL':'pp','SXR':'qq','SF_':'w'}.get(k)
        if k=='SCL': symbol='>' if s in (3,4,5,6) else '<'
        if k=='SCR': symbol='<' if s in (3,4,5,6) else '>'
        if k=='SLL': symbol='V'+str((s-3)%8+1)
        if k=='SLR': symbol='V'+str((s+1)%8+1)
        if symbol is None: raise ValueError('Unknown slide '+k)
        return symbol+str(n['end'])
    groups=defaultdict(list); expected=[]
    for n in notes: groups[n['tick']].append(n)
    events=[]
    for tick,group in sorted(groups.items()):
        tokens=[]; used=set()
        for n in group:
            if n['line'] in used: continue
            k=n['kind']; r=n['row']; t=seconds(tick); end=t; start=t
            br=n['cmd'][0]=='B'; ex=n['cmd'][1]=='X'
            flags=('b' if br else '')+('x' if ex else '')
            kind={'TAP':'Tap','STR':'Tap','HLD':'Hold','TTP':'Touch','THO':'TouchHold'}.get(k,'Slide')
            if kind=='Slide': continue
            token=str(n['pos'])+flags
            if k=='STR': token+='$'
            if k=='HLD':
                end=seconds(tick+int(r[4])); token+='h[#'+num(end-t)+']'
            if k in ('TTP','THO'):
                region=r[4] if k=='TTP' else r[5]
                token=region+('' if region=='C' else str(n['pos']))
                if (r[5] if k=='TTP' else r[6])=='1': token+='f'
                if k=='THO':
                    end=seconds(tick+int(r[4]));token+='h[#'+num(end-t)+']'
            attached=[s for s in group if 'chain' in s and s['pos']==n['pos'] and s['line'] not in used] if k=='STR' else []
            if attached:
                used.update(s['line'] for s in attached)
                token+='*'.join(make_slide(s,seconds,shape) for s in attached)
                for s in attached: expected.append(slide_expected(s,seconds))
            tokens.append(token)
            expected.append({'kind':kind,'time':t,'end':end,'start':start,'break':br,'ex':ex,'segments':0})
        for n in group:
            if 'chain' in n and n['line'] not in used:
                tokens.append(str(n['pos'])+'?'+make_slide(n,seconds,shape)); expected.append(slide_expected(n,seconds))
        events.append((seconds(tick),'/'.join(tokens)))
    output='('+num(bpms[0][1])+')'
    previous=0.0
    for t,token in events:
        if t>previous: output+='{#'+num(t-previous)+'},\n'
        output+=token;previous=t
    output+='\nE\n'
    stats={'count':len(expected),'kinds':dict(Counter(n['kind'] for n in expected)),
           'breaks':sum(n['break'] for n in expected),'ex':sum(n['ex'] for n in expected),
           'segments':sum(n['segments'] for n in expected),
           'sumTime':sum(n['time'] for n in expected),'sumEnd':sum(n['end'] for n in expected),
           'sumSlideStart':sum(n['start'] for n in expected if n['kind']=='Slide'),
           'lastEnd':max((n['end'] for n in expected),default=0),'controls':dict(controls)}
    return output,stats

def make_slide(n,seconds,shape):
    t=n['tick']; start=t+n['wait'];end=start+n['length']
    token=shape(n)+('b' if n['cmd'][0]=='B' else '')+'['+num(seconds(start)-seconds(t))+'##'+num(seconds(end)-seconds(start))+']'
    for c in n['chain']:
        token+=shape(c)+'[#'+num(seconds(c['tick']+c['length'])-seconds(c['tick']))+']'
    return token

def slide_expected(n,seconds):
    tail=n['chain'][-1] if n['chain'] else n
    return {'kind':'Slide','time':seconds(n['tick']),'start':seconds(n['tick']+n['wait']),
            'end':seconds(tail['tick']+tail['wait']+tail['length']),'break':n['cmd'][0]=='B','ex':False,'segments':1+len(n['chain'])}

def folder_name(text):
    return re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', text).strip().rstrip('.')

def song_directory(songid, title, version_id, version_name):
    return f'{int(version_id):02d}_{folder_name(version_name)}/{int(songid):06d}_{folder_name(title)}'

def main():
    parser=argparse.ArgumentParser();parser.add_argument('source',type=Path);parser.add_argument('output',type=Path);parser.add_argument('--update',action='store_true');parser.add_argument('--decoder',type=Path);args=parser.parse_args()
    if not args.source.is_dir(): parser.error('Source directory does not exist')
    xmls=sorted(args.source.rglob('Music.xml'))
    if not xmls: parser.error('No Music.xml files found in source directory')
    if args.output.exists() and not args.update: raise ValueError('Output already exists')
    args.output.mkdir(parents=True,exist_ok=True)
    assets={};report=[];versions={};excluded=[]
    for x in xmls:
        root=ET.parse(x).getroot(); songid=root.findtext('name/id'); assets[songid]=(x,root)
        version=root.findtext('AddVersion/str','').strip()
        if version:versions[root.findtext('AddVersion/id')]=version
    for songid,(xml,root) in assets.items():
        if root.findtext('utageKanjiName','').strip():
            excluded.append({'id':songid,'source':str(xml),'reason':'utage'});continue
        version_id=root.findtext('AddVersion/id');version_name=versions[version_id]
        directory=song_directory(songid,root.findtext('name/str',''),version_id,version_name)
        dest=args.output/directory;dest.mkdir(parents=True,exist_ok=True)
        fields=['&title='+root.findtext('name/str',''),'&artist='+root.findtext('artistName/str',''),'&first=0']
        record={'id':songid,'directory':directory,'versionId':version_id,'version':version_name,'title':root.findtext('name/str'),'source':str(xml),'cue':root.findtext('cueName/id'),'charts':[],'errors':[]}
        for i,entry in enumerate(root.findall('notesData/Notes')):
            name=entry.findtext('file/path',''); ma=xml.parent/name
            if not name or not ma.is_file():
                if entry.findtext('isEnable')=='true': record['errors'].append({'source':str(ma),'error':'Enabled chart file is missing'})
                continue
            try:
                text,stats=convert(ma);diff=i+2
                fields+=['&lv_'+str(diff)+'='+entry.findtext('level','0')+'.'+entry.findtext('levelDecimal','0'),
                         '&des_'+str(diff)+'='+entry.findtext('notesDesigner/str',''),'&inote_'+str(diff)+'='+text]
                record['charts'].append({'difficulty':diff,'source':str(ma),'stats':stats})
            except Exception as e: record['errors'].append({'source':str(ma),'error':str(e)})
        if record['charts']: (dest/'maidata.txt').write_text('\n'.join(fields),encoding='utf-8')
        report.append(record)
        if len(report)%200==0: print('Converted songs',len(report),flush=True)
    (args.output/'report.json').write_text(json.dumps({'source':str(args.source),'songs':report,'excluded':excluded},ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'songs':len(report),'charts':sum(len(s['charts']) for s in report),'errors':sum(len(s['errors']) for s in report),'excluded':len(excluded)}))
    if any(s['errors'] for s in report): return 1
    if args.decoder:
        return subprocess.run([sys.executable,str(Path(__file__).with_name('decode_official_audio.py')),str(args.output),'--decoder',str(args.decoder)]).returncode
    return 0
if __name__=='__main__': sys.exit(main())
