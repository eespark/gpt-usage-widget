using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CodexUsageTaskbar
{
    public sealed class StorageMigrationState
    {
        public Dictionary<string,string> Sources=new Dictionary<string,string>();
        public long HistoryClearedAt;
        public bool Completed;
    }
    public static class StorageMigration
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
        static extern uint GetFinalPathNameByHandle(IntPtr handle,StringBuilder name,uint size,uint flags);
        public static void Migrate()
        {
            if(IsCompleted(LocalData.DirectoryPath))return;
            string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var sources=new List<string>{Path.Combine(local,"GPTUsageTaskbar")};
            string packages=Path.Combine(local,"Packages");
            try {
                if(Directory.Exists(packages))foreach(string package in Directory.GetDirectories(packages)) {
                    string candidate=Path.Combine(package,"LocalCache","Local","GPTUsageTaskbar");
                    if(Directory.Exists(candidate))sources.Add(candidate);
                }
            }catch(IOException){return;}catch(UnauthorizedAccessException){return;}
            MergeInto(LocalData.DirectoryPath,sources,DateTimeOffset.Now);
        }
        static bool IsCompleted(string folder)
        {
            string path=Path.Combine(folder,"storage-migration.json");
            var state=LocalData.Read<StorageMigrationState>(path)??LocalData.Read<StorageMigrationState>(path+".bak");
            return state!=null&&state.Completed;
        }
        internal static bool MergeInto(string destination,IEnumerable<string> sources,DateTimeOffset now)
        {
            try {
                Directory.CreateDirectory(destination);
                string historyPath=Path.Combine(destination,"history.json"),statePath=Path.Combine(destination,"storage-migration.json");
                var state=LocalData.Read<StorageMigrationState>(statePath);
                if(state==null&&File.Exists(statePath))state=LocalData.Read<StorageMigrationState>(statePath+".bak");
                if(state==null&&File.Exists(statePath))return false;
                state=state??new StorageMigrationState();
                if(state.Completed)return true;
                if(state.Sources==null)state.Sources=new Dictionary<string,string>();
                var history=LocalData.Read<List<HistorySample>>(historyPath);
                if(history==null&&File.Exists(historyPath))history=LocalData.Read<List<HistorySample>>(historyPath+".bak");
                if(history==null&&File.Exists(historyPath))return false;
                history=(history??new List<HistorySample>()).Where(x=>x!=null&&Valid(x.Five)&&Valid(x.Week)).ToList();
                bool changed=false;var completed=new Dictionary<string,string>();
                foreach(string folder in sources.Distinct(StringComparer.OrdinalIgnoreCase)) {
                    string sourcePath=Path.Combine(folder,"history.json");
                    var incoming=LocalData.Read<List<HistorySample>>(sourcePath);
                    if(incoming==null&&File.Exists(sourcePath))incoming=LocalData.Read<List<HistorySample>>(sourcePath+".bak");
                    if(incoming==null){if(File.Exists(sourcePath))return false;continue;}
                    string identity=PhysicalPath(sourcePath).ToLowerInvariant(),fingerprint=Fingerprint(incoming);
                    string previous;if(state.Sources.TryGetValue(identity,out previous)&&previous==fingerprint)continue;
                    long latest=now.AddMinutes(1).ToUnixTimeSeconds();
                    var valid=incoming.Where(x=>x!=null&&x.At>state.HistoryClearedAt&&x.At<=latest&&Valid(x.Five)&&Valid(x.Week));
                    // 현재 저장된 값을 우선하고, 이전 저장소에서 빠진 시각과 항목만 추가합니다.
                    history=valid.Concat(history).GroupBy(x=>x.At).Select(x=>MergeRecord(x.ToArray())).OrderBy(x=>x.At).ToList();
                    completed[identity]=fingerprint;changed=true;
                }
                if(changed) {
                    if(!LocalData.Write(historyPath,history,true))return false;
                    foreach(var source in completed)state.Sources[source.Key]=source.Value;
                    if(!LocalData.Write(statePath,state,true))return false;
                }
                foreach(string folder in sources) {
                    string settingsPath=Path.Combine(destination,"settings.json");
                    if(!File.Exists(settingsPath)) {
                        var settings=LocalData.Read<Settings>(Path.Combine(folder,"settings.json"));
                        if(settings!=null&&!LocalData.Write(settingsPath,settings))return false;
                    }
                    var alerts=LocalData.Read<Dictionary<string,long>>(Path.Combine(folder,"alerts.json"));
                    if(alerts==null)continue;
                    string alertsPath=Path.Combine(destination,"alerts.json");
                    var saved=LocalData.Read<Dictionary<string,long>>(alertsPath)??new Dictionary<string,long>();
                    foreach(var alert in alerts){long at;if(!saved.TryGetValue(alert.Key,out at)||alert.Value>at)saved[alert.Key]=alert.Value;}
                    if(!LocalData.Write(alertsPath,saved))return false;
                }
                state.Completed=true;
                return LocalData.Write(statePath,state,true);
            }catch(IOException){}catch(UnauthorizedAccessException){}
            return false;
        }
        static HistorySample MergeRecord(HistorySample[] rows)
        {
            var merged=new HistorySample{At=rows[0].At};
            foreach(var row in rows) {
                if(row.Five.HasValue){merged.Five=row.Five;merged.FiveReset=row.FiveReset;}
                if(row.Week.HasValue){merged.Week=row.Week;merged.WeekReset=row.WeekReset;}
            }
            return merged;
        }
        static bool Valid(double? value){return !value.HasValue||(!double.IsNaN(value.Value)&&!double.IsInfinity(value.Value)&&value.Value>=0&&value.Value<=100);}
        static string Fingerprint(List<HistorySample> rows)
        {
            string text=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=64000000}.Serialize(rows);
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","");
        }
        static string PhysicalPath(string path)
        {
            try {using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                var name=new StringBuilder(32768);
                if(GetFinalPathNameByHandle(stream.SafeFileHandle.DangerousGetHandle(),name,(uint)name.Capacity,0)>0)return name.ToString();
            }}catch(IOException){}catch(UnauthorizedAccessException){}
            return Path.GetFullPath(path);
        }
        public static bool MarkHistoryCleared(string folder,long at)
        {
            string path=Path.Combine(folder,"storage-migration.json");
            var state=LocalData.Read<StorageMigrationState>(path)??new StorageMigrationState();
            state.HistoryClearedAt=at;return LocalData.Write(path,state,true);
        }
    }
}
