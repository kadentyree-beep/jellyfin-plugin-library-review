"""Real Jellyfin/Radarr, synthetic video only, isolated GitHub runner.

Never point this harness at existing services. Fixed test-only container names,
ports and a freshly created temp directory; no live credentials or media mounts.
"""
import hashlib, json, os, pathlib, shutil, sqlite3, subprocess, tempfile, time
import urllib.request, urllib.error, uuid

assert os.environ.get('GITHUB_ACTIONS') == 'true', 'Only run on an isolated GitHub Actions runner.'
ROOT = pathlib.Path(tempfile.mkdtemp(prefix='lr-disposable-'))
OUT = pathlib.Path('artifacts/integration'); OUT.mkdir(parents=True, exist_ok=True)
J = 'http://127.0.0.1:18096'; R = 'http://127.0.0.1:17878'
PLUGIN = '9eba6ec4-c190-40c2-961c-71e92174680e'
checks = []

class ApiObject(dict):
    # Jellyfin 12 uses camelCase; accept either casing without duplicate keys.
    def key(self, name):
        return next((k for k in self if k.casefold()==name.casefold()),name)
    def __getitem__(self,name):return super().__getitem__(self.key(name))
    def get(self,name,default=None):return super().get(self.key(name),default)
    def __setitem__(self,name,value):super().__setitem__(self.key(name),value)
    def update(self,*args,**kwargs):
        for k,v in dict(*args,**kwargs).items():self[k]=v
def checked(name):
    checks.append(name); print('PASS:', name, flush=True)
    (OUT/'results.json').write_text(json.dumps({'checks':checks},indent=2))
def cmd(*args):
    return subprocess.check_output(args, text=True).strip()
def call(base, path, body=None, token=None, method=None, expect=None):
    headers = {'Content-Type':'application/json'}
    if base==J:
        headers['Authorization']='MediaBrowser Client="DisposableIntegration", Device="CI", DeviceId="disposable-ci", Version="1"'
        if token: headers['Authorization']+=', Token="'+token+'"'
    elif token: headers['X-Api-Key'] = token
    req=urllib.request.Request(base+path, headers=headers, data=None if body is None else json.dumps(body).encode(), method=method)
    try:
        with urllib.request.urlopen(req, timeout=120) as response:
            data=response.read(); status=response.status
    except urllib.error.HTTPError as e:
        if expect==e.code:return None
        print('HTTP FAILURE', path, e.code, e.read().decode()[:1500], flush=True);raise
    if expect is not None: assert status==expect,(path,status,expect)
    return json.loads(data,object_pairs_hook=lambda pairs:ApiObject(pairs)) if data else None
def wait_ready(base,path):
    for _ in range(90):
        try:return call(base,path)
        except Exception:time.sleep(2)
    raise RuntimeError('Test server did not start')
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

for folder in ['jellyfin','radarr','media','downloads']:
    (ROOT/folder).mkdir(); (ROOT/folder).chmod(0o777)
plugin_dir=ROOT/'jellyfin/plugins/LibraryReview';plugin_dir.mkdir(parents=True)
shutil.copy('src/LibraryReview/bin/Release/net10.0/Jellyfin.Plugin.LibraryReview.dll',plugin_dir)
cmd('docker','network','create','lr-test-net')
cmd('docker','pull','jellyfin/jellyfin:12.2')
cmd('docker','pull','lscr.io/linuxserver/radarr:latest')
for image in ['jellyfin/jellyfin:12.2','lscr.io/linuxserver/radarr:latest']:
    print('IMAGE',cmd('docker','image','inspect',image,'--format','{{json .RepoDigests}}'),flush=True)
movie_dir=ROOT/'media/Inception (2010)';movie_dir.mkdir();movie_dir.chmod(0o777)
video=movie_dir/'Inception (2010) Bluray-1080p.mkv'
cmd('docker','run','--rm','--network','none','--user',f'{os.getuid()}:{os.getgid()}','-v',f'{ROOT}/media:/fixtures',
    '--entrypoint','/usr/lib/jellyfin-ffmpeg/ffmpeg','jellyfin/jellyfin:12.2',
    '-v','error','-f','lavfi','-i','color=c=blue:s=64x64:r=1','-f','lavfi','-i','anullsrc=r=8000:cl=mono',
    '-t','600','-c:v','libx264','-preset','ultrafast','-c:a','aac','-b:a','8k','-metadata:s:a:0','language=eng',
    '/fixtures/Inception (2010)/'+video.name)
video.chmod(0o666)
(movie_dir/'movie.nfo').write_text('<movie><title>Inception</title><year>2010</year><uniqueid type="tmdb" default="true">27205</uniqueid><lockdata>true</lockdata></movie>')
(movie_dir/'movie.en.srt').write_text('1\n00:00:00,000 --> 00:00:01,000\nSynthetic fixture\n')
(movie_dir/'poster.jpg').write_bytes(b'fixture-sidecar-not-real-art')
# The second edition is added after Radarr indexes the first, to avoid selection ambiguity.
original_hash=sha(video)
cmd('docker','run','-d','--name','lr-test-radarr','--network','lr-test-net','-p','127.0.0.1:17878:7878',
    '-e',f'PUID={os.getuid()}','-e',f'PGID={os.getgid()}','-e','TZ=UTC','-v',f'{ROOT}/radarr:/config','-v',f'{ROOT}/media:/movies',
    'lscr.io/linuxserver/radarr:latest')
cmd('docker','run','-d','--name','lr-test-jellyfin','--network','lr-test-net','-p','127.0.0.1:18096:8096',
    '-v',f'{ROOT}/jellyfin:/config','-v',f'{ROOT}/media:/media/movies','jellyfin/jellyfin:12.2')
wait_ready(J,'/System/Info/Public')
for _ in range(60):
    if (ROOT/'radarr/config.xml').exists():break
    time.sleep(2)
import xml.etree.ElementTree as ET
rad_key=ET.parse(ROOT/'radarr/config.xml').findtext('ApiKey')
for _ in range(60):
    try:rad=call(R,'/api/v3/system/status',token=rad_key);break
    except Exception:time.sleep(2)
wait_ready(J,'/Startup/Configuration')
print('VERSIONS',call(J,'/System/Info/Public')['Version'],rad['version'],flush=True)
password=uuid.uuid4().hex
call(J,'/Startup/Configuration',{'UICulture':'en-US','MetadataCountryCode':'US','PreferredMetadataLanguage':'en'})
call(J,'/Startup/User')
call(J,'/Startup/User',{'Name':'fixture-admin','Password':password})
call(J,'/Startup/RemoteAccess',{'EnableRemoteAccess':False,'EnableAutomaticPortMapping':False})
call(J,'/Startup/Complete',{})
admin=call(J,'/Users/AuthenticateByName',{'Username':'fixture-admin','Pw':password})
token=admin['AccessToken']
plugins=call(J,'/Plugins',token=token)
assert any(uuid.UUID(p['Id'])==uuid.UUID(PLUGIN) and p['Status']=='Active' for p in plugins),[(p['Name'],p['Status']) for p in plugins]
checked('Plugin loaded in real Jellyfin 12.2')
call(J,'/LibraryReview',expect=401)
guest=call(J,'/Users/New',{'Name':'fixture-viewer','Password':password},token=token)
guest_auth=call(J,'/Users/AuthenticateByName',{'Username':'fixture-viewer','Pw':password})
call(J,'/LibraryReview',token=guest_auth['AccessToken'],expect=403)
checked('Anonymous and non-admin access rejected')
call(R,'/api/v3/rootfolder',{'path':'/movies'},token=rad_key)
profiles=call(R,'/api/v3/qualityprofile',token=rad_key)
lookup=call(R,'/api/v3/movie/lookup/tmdb?tmdbId=27205',token=rad_key)
if isinstance(lookup,list):lookup=lookup[0]
lookup.update(qualityProfileId=profiles[0]['id'],rootFolderPath='/movies',path='/movies/Inception (2010)',monitored=True,addOptions={'searchForMovie':False})
movie=call(R,'/api/v3/movie',lookup,token=rad_key); mid=movie['id']
call(R,'/api/v3/command',{'name':'RescanMovie','movieId':mid},token=rad_key)
for _ in range(60):
    movie=call(R,f'/api/v3/movie/{mid}',token=rad_key)
    if movie.get('hasFile'):break
    time.sleep(2)
assert movie.get('hasFile'), 'Synthetic file was not imported'
fid=movie['movieFile']['id']
# Seed only synthetic provenance in the DISPOSABLE database. No real download,
# credentials, NZB, indexer, or downloader is configured in this Radarr instance.
cmd('docker','stop','lr-test-radarr')
db=sqlite3.connect(ROOT/'radarr/radarr.db')
columns=[r[1] for r in db.execute('pragma table_info(History)')]
print('HISTORY_SCHEMA',columns,flush=True)
release='Inception.2010.1080p.SYNTHETIC-LibraryReviewTest'
db.execute('update MovieFiles set SceneName=? where Id=?',(release,fid))
quality=json.dumps(movie['movieFile']['quality'])
for event,data in [(1,{'indexer':'SyntheticTest','protocol':'1','indexerId':'0'}),(3,{'fileId':str(fid),'importedPath':movie['movieFile']['path']})]:
    values={'MovieId':mid,'SourceTitle':release,'Date':'2026-10-27T00:00:00Z','Quality':quality,'Languages':'[{"id":1,"name":"English"}]','EventType':event,'Data':json.dumps(data),'DownloadId':'synthetic-fixture-download'}
    values={k:v for k,v in values.items() if k in columns}
    db.execute('insert into History ('+','.join(values)+') values ('+','.join('?' for _ in values)+')',list(values.values()))
db.commit();db.close();cmd('docker','start','lr-test-radarr');time.sleep(5)
second=movie_dir/'Other edition.keep.mkv';shutil.copy(video,second)
neighbors={p.name:sha(p) for p in movie_dir.iterdir() if p!=video}
call(J,'/Library/VirtualFolders?name=Disposable&collectionType=movies&refreshLibrary=true',
    {'LibraryOptions':{'PathInfos':[{'Path':'/media/movies'}],'EnableRealtimeMonitor':False,'EnableInternetProviders':False,'EnableChapterImageExtraction':False,'EnableTrickplayImageExtraction':False}},token=token)
config=call(J,'/Plugins/'+PLUGIN+'/Configuration',token=token)
config.update(RadarrUrl='http://lr-test-radarr:7878',RadarrApiKey=rad_key,JellyfinMovieRoot='/media/movies',RadarrMovieRoot='/movies',EnableReplacement=False)
call(J,'/Plugins/'+PLUGIN+'/Configuration',config,token=token)
for _ in range(60):
    call(J,'/LibraryReview/Scan',{},token=token)
    report=call(J,'/LibraryReview',token=token)
    selected=[i for i in report['items'] if i['file']['path'].endswith(video.name)]
    if selected:break
    time.sleep(2)
assert len(selected)==1,report
item=selected[0];iid=item['id']
assert any(f['code']=='runtime' for f in item['findings']),item
assert sha(video)==original_hash and all(sha(movie_dir/n)==h for n,h in neighbors.items())
checked('Read-only scan finds synthetic runtime mismatch and preserves all files')
call(J,f'/LibraryReview/{iid}/Decision',{'status':'problem'},token=token)
call(J,f'/LibraryReview/{iid}/Preview',{},token=token,expect=409)
checked('Replacement disabled by default')
config['EnableReplacement']=True;call(J,'/Plugins/'+PLUGIN+'/Configuration',config,token=token)
preview=call(J,f'/LibraryReview/{iid}/Preview',{},token=token)
assert sha(video)==original_hash and all(sha(movie_dir/n)==h for n,h in neighbors.items())
checked('Preview does not change any file')
call(J,f'/LibraryReview/{iid}/Replace',{'token':preview['token'],'confirmTitle':'WRONG'},token=token,expect=409)
assert video.exists()
checked('Incorrect typed confirmation cannot delete')
# A test-only proxy forwards reads to real Radarr but rejects blocklisting.
# This exercises restoration after the plugin has staged the exact video.
import http.server, threading
class FaultProxy(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def do_GET(self):
        request=urllib.request.Request(R+self.path,headers={'X-Api-Key':rad_key})
        with urllib.request.urlopen(request,timeout=30) as response:
            self.send_response(response.status);self.end_headers();self.wfile.write(response.read())
    def do_POST(self):
        self.send_response(503);self.end_headers()
proxy=http.server.ThreadingHTTPServer(('0.0.0.0',17879),FaultProxy)
threading.Thread(target=proxy.serve_forever,daemon=True).start()
gateway=json.loads(cmd('docker','network','inspect','lr-test-net'))[0]['IPAM']['Config'][0]['Gateway']
config['RadarrUrl']=f'http://{gateway}:17879'
call(J,'/Plugins/'+PLUGIN+'/Configuration',config,token=token)
preview=call(J,f'/LibraryReview/{iid}/Preview',{},token=token)
call(J,f'/LibraryReview/{iid}/Replace',{'token':preview['token'],'confirmTitle':item['title']},token=token,expect=409)
assert sha(video)==original_hash and all(sha(movie_dir/n)==h for n,h in neighbors.items())
assert not list(movie_dir.glob('*.quarantine'))
checked('Failed blocklist restores selected video and preserves all neighbors')
config['RadarrUrl']='http://lr-test-radarr:7878'
call(J,'/Plugins/'+PLUGIN+'/Configuration',config,token=token)
proxy.shutdown()
preview=call(J,f'/LibraryReview/{iid}/Preview',{},token=token)
result=call(J,f'/LibraryReview/{iid}/Replace',{'token':preview['token'],'confirmTitle':item['title']},token=token)
assert not video.exists() and all(sha(movie_dir/n)==h for n,h in neighbors.items())
assert not list(movie_dir.glob('*.quarantine'))
checked('Confirmed action deletes only selected synthetic video; sidecars and other edition preserved')
call(J,f'/LibraryReview/{iid}/Replace',{'token':preview['token'],'confirmTitle':item['title']},token=token,expect=409)
checked('Repeated confirmation cannot run twice')
block=call(R,'/api/v3/blocklist?pageSize=100',token=rad_key)
assert any(x['sourceTitle']==release for x in block['records'])
commands=call(R,'/api/v3/command',token=rad_key)
assert any(c['name']=='MoviesSearch' for c in commands)
checked('Actual Radarr blocklist and replacement search verified without indexers/downloaders')
print('ALL DISPOSABLE INTEGRATION CHECKS PASSED',flush=True)
