using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CodexUsageTaskbar
{
    public static class LocalData
    {
        public static string DirectoryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPTUsageTaskbar"); } }
        public static T Read<T>(string path) where T : class
        {
            try { return new JavaScriptSerializer { MaxJsonLength = 64000000 }.Deserialize<T>(File.ReadAllText(path)); }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
            return null;
        }
        public static bool Write(string path, object value, bool keepBackup=false)
        {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer { MaxJsonLength = 64000000 }.Serialize(value));
                if (File.Exists(path)) File.Replace(temporary, path, keepBackup?path+".bak":null, true); else File.Move(temporary, path);
                return true;
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return false;
        }
    }
    public sealed class UsageAlert
    {
        public string Title;
        public string Message;
        public long ExpiresAt;
    }
    public sealed class AlertEngine
    {
        readonly string path;
        readonly Dictionary<string, long> sent;
        public AlertEngine(string statePath)
        {
            path = statePath;
            sent = LocalData.Read<Dictionary<string, long>>(path) ?? new Dictionary<string, long>();
        }
        public List<UsageAlert> Evaluate(UsageSnapshot data, DateTimeOffset now, Settings settings)
        {
            var alerts = new List<UsageAlert>();
            if (data == null || now - data.FetchedAt > TimeSpan.FromMinutes(5) || data.FetchedAt > now.AddMinutes(1)) return alerts;
            long current = now.ToUnixTimeSeconds();
            foreach (string key in sent.Where(x => x.Value < current - 14 * 86400L).Select(x => x.Key).ToArray()) sent.Remove(key);
            CheckLow(data.FiveHour, Ui.Text("5시간"), "five", now, settings.LowQuotaAlert && settings.FiveHourLowAlert, settings.FiveHourLowPercent, alerts);
            CheckLow(data.Weekly, Ui.Text("주간"), "week", now, settings.LowQuotaAlert && settings.WeeklyLowAlert, settings.WeeklyLowPercent, alerts);
            if (settings.FiveHourResetAlert && ValidFuture(data.FiveHour, current) && data.FiveHour.ResetsAt.Value - current <= settings.FiveHourResetMinutes*60L)
                Add("hour:" + data.FiveHour.ResetsAt.Value + (settings.FiveHourResetMinutes==60?"":":"+settings.FiveHourResetMinutes), now, Ui.Text("5시간 한도 재설정 안내"), Ui.Text("재설정까지 ") + Widget.RemainingAmount(Widget.FormatRemainingTime(data.FiveHour, now)) + Ui.Text(" 남았습니다."), alerts);
            if (settings.WeeklyResetAlert && ValidFuture(data.Weekly, current)) {
                DateTimeOffset reset = ExpiryDisplay.InKorea(DateTimeOffset.FromUnixTimeSeconds(data.Weekly.ResetsAt.Value));
                var scheduled = new DateTimeOffset(reset.Date.AddDays(-settings.WeeklyReminderDays).AddHours(settings.WeeklyReminderHour).AddMinutes(settings.WeeklyReminderMinute), TimeSpan.FromHours(9));
                var koreaNow = ExpiryDisplay.InKorea(now);
                bool due=settings.WeeklyResetMode==1 ? data.Weekly.ResetsAt.Value-current<=settings.WeeklyResetMinutes*60L : koreaNow.Date==scheduled.Date&&now>=scheduled;
                string criterion=settings.WeeklyResetMode==1 ? ":minutes:"+settings.WeeklyResetMinutes : settings.WeeklyReminderDays==1&&settings.WeeklyReminderHour==12&&settings.WeeklyReminderMinute==30 ? "" : ":calendar:"+settings.WeeklyReminderDays+":"+settings.WeeklyReminderHour+":"+settings.WeeklyReminderMinute;
                if (due)
                    Add("day:" + data.Weekly.ResetsAt.Value+criterion, now, Ui.Text("주간 한도 재설정 안내"), Ui.Text("재설정: ") + Widget.FormatResetTime(data.Weekly, true) + Ui.Text(" (한국 시간)"), alerts);
            }
            if (alerts.Count > 0) LocalData.Write(path, sent);
            return alerts;
        }
        static bool ValidFuture(QuotaWindow quota, long now)
        {
            return quota != null && quota.ResetsAt.HasValue && quota.ResetsAt.Value > now && quota.ResetsAt.Value <= 253402300799L;
        }
        void CheckLow(QuotaWindow quota, string label, string prefix, DateTimeOffset now, bool enabled, int threshold, List<UsageAlert> alerts)
        {
            if (!enabled || quota == null || quota.Remaining > threshold) return;
            if (quota.ResetsAt.HasValue && quota.ResetsAt.Value <= now.ToUnixTimeSeconds()) return;
            string cycle = quota.ResetsAt.HasValue ? quota.ResetsAt.Value.ToString() : ExpiryDisplay.InKorea(now).ToString("yyyyMMdd");
            Add("low:" + prefix + ":" + cycle+(threshold==20?"":":"+threshold), now, label + Ui.Text(" 잔량이 ")+threshold+Ui.Text("% 이하입니다"), Ui.Text("현재 남은 사용량: ") + quota.PercentText + ".", alerts);
        }
        void Add(string key, DateTimeOffset now, string title, string message, List<UsageAlert> alerts)
        {
            if (sent.ContainsKey(key)) return;
            sent[key] = now.ToUnixTimeSeconds();
            alerts.Add(new UsageAlert { Title = title, Message = message, ExpiresAt = now.AddMinutes(5).ToUnixTimeSeconds() });
        }
    }
    public sealed class PollSchedule
    {
        public DateTimeOffset Next = DateTimeOffset.MinValue;
        int failures;
        readonly HashSet<long> checkedResets = new HashSet<long>();
        public void Wake(DateTimeOffset now) { Next = now; failures = 0; }
        public bool Due(DateTimeOffset now, bool online, bool suspended) { return online && !suspended && now >= Next; }
        public void Completed(DateTimeOffset now, bool success, bool efficient, bool battery)
        {
            failures = success ? 0 : Math.Min(5, failures + 1);
            int seconds = success ? (efficient && battery ? 120 : 60) : efficient ? Math.Min(900, 60 * (1 << failures)) : 60;
            Next = now.AddSeconds(seconds);
        }
        public bool ResetDue(UsageSnapshot data, DateTimeOffset now)
        {
            if (data == null) return false;
            bool due = false;
            foreach (var window in new[] { data.FiveHour, data.Weekly }) {
                if (window == null || !window.ResetsAt.HasValue) continue;
                long reset = window.ResetsAt.Value;
                if (reset <= now.ToUnixTimeSeconds() && reset >= now.AddDays(-1).ToUnixTimeSeconds() && checkedResets.Add(reset)) due = true;
            }
            if (checkedResets.Count > 20) checkedResets.RemoveWhere(x => x < now.AddDays(-1).ToUnixTimeSeconds());
            return due;
        }
    }
    public sealed class HistorySample
    {
        public long At;
        public double? Five;
        public double? Week;
        public long? FiveReset;
        public long? WeekReset;
    }
    public sealed class DailyUsageEstimate
    {
        public double PercentPerDay, ObservedHoursPerDay;
        public double? ActiveHoursPerDay;
        public int ObservedDays;
    }
    internal static class HistoryIntervals
    {
        internal static bool StartsNewCycle(HistorySample a,HistorySample b,bool weekly)
        {
            double? av=weekly?a.Week:a.Five,bv=weekly?b.Week:b.Five;
            if(av.HasValue&&bv.HasValue&&bv.Value>av.Value+.000001)return true;
            long? ar=weekly?a.WeekReset:a.FiveReset,br=weekly?b.WeekReset:b.FiveReset;
            return ar.HasValue&&br.HasValue&&Math.Abs(br.Value-ar.Value)>600;
        }
        internal static bool Observed(HistorySample a,HistorySample b,bool weekly)
        {
            double? av=weekly?a.Week:a.Five,bv=weekly?b.Week:b.Five;
            return b.At>a.At&&b.At-a.At<=600&&av.HasValue&&bv.HasValue&&!StartsNewCycle(a,b,weekly);
        }
    }
    public sealed class UsageHistory
    {
        readonly string path;
        readonly Settings preferences;
        bool readFailed;
        string lastViewKey;
        readonly UsageForecast[] forecastCache = new UsageForecast[2];
        readonly long[] forecastKeys = new long[2];
        public List<HistorySample> Samples { get; private set; }
        public bool StorageFailed { get; private set; }
        public UsageHistory(string historyPath, Settings options = null)
        {
            path = historyPath; preferences=options ?? new Settings(); preferences.Normalize();
            var loaded = ReadStored() ?? new List<HistorySample>();
            Samples = loaded
                .Where(x => x != null && x.At >= DateTimeOffset.Now.AddDays(-preferences.HistoryDays).ToUnixTimeSeconds() && x.At <= DateTimeOffset.Now.AddMinutes(1).ToUnixTimeSeconds() &&
                    Valid(x.Five) && Valid(x.Week)).OrderBy(x => x.At).GroupBy(x=>x.At).Select(x=>x.Last()).ToList();
            Compact(DateTimeOffset.Now);
            if (!readFailed&&loaded.Count != Samples.Count) StorageFailed = !LocalData.Write(path, Samples,true);
        }
        List<HistorySample> ReadStored()
        {
            var stored=LocalData.Read<List<HistorySample>>(path);
            if(stored==null&&File.Exists(path))stored=LocalData.Read<List<HistorySample>>(path+".bak");
            readFailed=stored==null&&File.Exists(path);
            if(readFailed)StorageFailed=true;
            return stored;
        }
        void Compact(DateTimeOffset now)
        {
            long cutoff=now.AddDays(-preferences.HistoryDays).ToUnixTimeSeconds(), detailed=now.AddDays(-14).ToUnixTimeSeconds();
            // Older samples retain five-minute resolution; preserve reset boundaries.
            Samples=Samples.Where(x=>x.At>=cutoff).GroupBy(x=>new {Bucket=x.At<detailed?x.At/300:x.At,Old=x.At<detailed,x.FiveReset,x.WeekReset}).Select(x=>x.Last()).OrderBy(x=>x.At).ToList();
        }
        public void ApplyRetention() { ReloadStoredSamples();Compact(DateTimeOffset.Now);StorageFailed=readFailed||!LocalData.Write(path,Samples,true); }
        public void ReloadStoredSamples()
        {
            var stored=ReadStored();
            if(stored==null)return;
            long cutoff=DateTimeOffset.Now.AddDays(-preferences.HistoryDays).ToUnixTimeSeconds();
            long latest=DateTimeOffset.Now.AddMinutes(1).ToUnixTimeSeconds();
            Samples=Samples.Concat(stored.Where(x=>x!=null&&x.At>=cutoff&&x.At<=latest&&Valid(x.Five)&&Valid(x.Week)))
                .GroupBy(x=>x.At).Select(x=>x.Last()).OrderBy(x=>x.At).ToList();
            Compact(DateTimeOffset.Now);
            forecastCache[0]=forecastCache[1]=null;
        }
        public void WriteViewReport(int days,DateTimeOffset end,int width,int height,float scale)
        {
            string key=days+":"+Samples.Count+":"+end.ToUnixTimeSeconds()/60+":"+width+":"+height;
            if(lastViewKey==key)return;lastViewKey=key;
            long start=end.AddDays(-days).ToUnixTimeSeconds(),finish=end.ToUnixTimeSeconds();
            LocalData.Write(Path.Combine(Path.GetDirectoryName(path),"history-view.json"),new {
                HistoryPath=path,Days=days,End=finish,Width=width,Height=height,Scale=scale,
                MemoryCount=Samples.Count,Visible=Samples.Where(x=>x.At>=start&&x.At<=finish).ToArray()
            });
        }
        static bool Valid(double? percent) { return !percent.HasValue || (!double.IsNaN(percent.Value) && !double.IsInfinity(percent.Value) && percent >= 0 && percent <= 100); }
        public void Record(UsageSnapshot data)
        {
            if (data == null) return;
            ReloadStoredSamples();
            long timestamp = data.FetchedAt.ToUnixTimeSeconds();
            if (Samples.Count > 0 && timestamp <= Samples[Samples.Count - 1].At) return;
            Samples.Add(new HistorySample { At = timestamp, Five = data.FiveHour == null ? (double?)null : data.FiveHour.Remaining,
                Week = data.Weekly == null ? (double?)null : data.Weekly.Remaining,
                FiveReset = data.FiveHour == null ? null : data.FiveHour.ResetsAt, WeekReset = data.Weekly == null ? null : data.Weekly.ResetsAt });
            Compact(data.FetchedAt);
            StorageFailed = readFailed||!LocalData.Write(path, Samples,true);
        }
        public void Clear()
        {
            Samples.Clear();readFailed=false;StorageFailed = !LocalData.Write(path, Samples);
            // 명시적 삭제 이후 백업에서 기록이 되살아나지 않도록 합니다.
            try {if(File.Exists(path+".bak"))File.Delete(path+".bak");}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        public double? Rate(bool weekly, UsageSnapshot current)
        {
            QuotaWindow quota = current == null ? null : weekly ? current.Weekly : current.FiveHour;
            if (quota == null || !quota.ResetsAt.HasValue) return null;
            long latest = current.FetchedAt.ToUnixTimeSeconds();
            var points = Samples.Where(x => x.At >= latest - 60 * 60 && x.At <= latest &&
                (weekly ? x.WeekReset : x.FiveReset) == quota.ResetsAt && (weekly ? x.Week : x.Five).HasValue).ToList();
            int start = 0;
            for (int i = 1; i < points.Count; i++) {
                double previous = (weekly ? points[i-1].Week : points[i-1].Five).Value;
                double value = (weekly ? points[i].Week : points[i].Five).Value;
                if (value > previous + .1 || points[i].At - points[i-1].At > 10 * 60) start = i;
            }
            points = points.Skip(start).ToList();
            if (points.Count < 3 || points[points.Count - 1].At - points[0].At < 10 * 60 || latest - points[points.Count - 1].At > 3 * 60) return null;
            double drop=0, duration=0;
            for(int i=1;i<points.Count;i++) {
                double weight=Math.Pow(.5,(latest-(points[i].At+points[i-1].At)/2.0)/(15*60));
                drop+=Math.Max(0,(weekly?points[i-1].Week:points[i-1].Five).Value-(weekly?points[i].Week:points[i].Five).Value)*weight;
                duration+=(points[i].At-points[i-1].At)/3600.0*weight;
            }
            return duration>0 ? (double?)(drop/duration) : null;
        }
        public double? BaselineRate(bool weekly,UsageSnapshot current)
        {
            if(current==null)return null;
            long latest=current.FetchedAt.ToUnixTimeSeconds(),cutoff=latest-preferences.ForecastDays*86400L;
            double drop=0,weightedHours=0,observedSeconds=0; int intervals=0;
            for(int i=1;i<Samples.Count;i++) {
                var a=Samples[i-1];var b=Samples[i];double? av=weekly?a.Week:a.Five,bv=weekly?b.Week:b.Five;
                long? ar=weekly?a.WeekReset:a.FiveReset,br=weekly?b.WeekReset:b.FiveReset;
                long elapsed=b.At-a.At;
                if(a.At<cutoff||b.At>latest||!HistoryIntervals.Observed(a,b,weekly))continue;
                double weight=Math.Pow(.5,(latest-(a.At+b.At)/2.0)/(Math.Max(1,preferences.ForecastDays/3.0)*86400));
                drop+=(av.Value-bv.Value)*weight;weightedHours+=elapsed/3600.0*weight;observedSeconds+=elapsed;intervals++;
            }
            return intervals>=3&&observedSeconds>=3600&&weightedHours>0 ? (double?)(drop/weightedHours) : null;
        }
        public UsageForecast Estimate(bool weekly, UsageSnapshot current)
        {
            int index = weekly ? 1 : 0;
            long key = 17;
            unchecked {
                key = key * 31 + preferences.ForecastDays;
                key = key * 31 + (current == null ? 0 : current.FetchedAt.ToUnixTimeSeconds());
                var quota = current == null ? null : weekly ? current.Weekly : current.FiveHour;
                key = key * 31 + (quota == null ? 0 : quota.ResetsAt.GetValueOrDefault());
                long cutoff = current == null ? 0 : current.FetchedAt.ToUnixTimeSeconds() - preferences.ForecastDays * 86400L;
                foreach (var point in Samples.Where(x => x.At >= cutoff)) {
                    key = key * 31 + point.At;
                    key = key * 31 + (weekly ? point.Week : point.Five).GetHashCode();
                    key = key * 31 + (weekly ? point.WeekReset : point.FiveReset).GetHashCode();
                }
            }
            if (forecastCache[index] == null || key != forecastKeys[index]) {
                forecastCache[index] = IntermittentForecast.Estimate(Samples, weekly, current, preferences.ForecastDays);
                forecastKeys[index] = key;
            }
            return forecastCache[index];
        }
        public double? PredictionRate(bool weekly,UsageSnapshot current)
        {
            return Estimate(weekly, current).ActivePercentPerHour;
        }
        public string ForecastDetail(bool weekly,UsageSnapshot current)
        {
            if(current==null)return Ui.Text("한도 정보 없음");
            if(DateTimeOffset.Now-current.FetchedAt>TimeSpan.FromMinutes(5)||current.FetchedAt>DateTimeOffset.Now.AddMinutes(1))return Ui.Text("최신 조회 후 계산합니다.");
            var available=weekly?current.Weekly:current.FiveHour;
            if(available==null)return Ui.Text("한도 정보 없음");
            if(available.ResetsAt.HasValue&&available.ResetsAt.Value<=current.FetchedAt.ToUnixTimeSeconds())return Ui.Text("재설정 후 최신 조회가 필요합니다.");
            var rate=PredictionRate(weekly,current);
            if (!rate.HasValue) return Ui.Text("소모가 관측된 사용 기록이 더 필요합니다.");
            if (rate.Value <= .01) return Ui.Text("최근 소모 없음 · 추정 보류");
            double hours = available.Remaining / rate.Value;
            if (available.ResetsAt.HasValue && hours >= (available.ResetsAt.Value-current.FetchedAt.ToUnixTimeSeconds())/3600.0)
                return Ui.Text("계속 사용 기준 · 소진 전에 재설정됩니다.");
            return Ui.Text("현재 작업 강도로 쉬지 않고 계속 사용하는 기준입니다.");
        }
        public string PatternForecast(bool weekly, UsageSnapshot current)
        {
            if (current == null || DateTimeOffset.Now - current.FetchedAt > TimeSpan.FromMinutes(5) || current.FetchedAt > DateTimeOffset.Now.AddMinutes(1)) return Ui.Text("기록 패턴: 최신 조회 필요");
            var quota = weekly ? current.Weekly : current.FiveHour;
            if (quota == null || !quota.ResetsAt.HasValue) return Ui.Text("기록 패턴: 재설정 정보 필요");
            if (quota.ResetsAt.Value <= current.FetchedAt.ToUnixTimeSeconds()) return Ui.Text("재설정 조회 대기");
            var estimate = Estimate(weekly,current);
            if (!estimate.ExpectedConsumption.HasValue) return weekly ? Ui.Text("기록 패턴: 하루 4시간·3일 기록 필요") : Ui.Text("기록 패턴: 4시간 이상 기록 필요");
            return Ui.Text("기록 패턴: 재설정 전 약 ") + estimate.ExpectedConsumption.Value.ToString("0.#") + Ui.Text("%p 소비 예상");
        }
        static string HoursMinutes(double hours)
        {
            int minutes=Math.Max(0,(int)Math.Min(int.MaxValue,Math.Ceiling(hours*60-1e-7)));
            return minutes/60+Ui.Text("시간 ")+minutes%60+Ui.Text("분");
        }
        public string Forecast(bool weekly, UsageSnapshot current)
        {
            var quota = current == null ? null : weekly ? current.Weekly : current.FiveHour;
            if (quota == null) return Ui.Text("한도 정보 없음");
            if (DateTimeOffset.Now - current.FetchedAt > TimeSpan.FromMinutes(5)) return Ui.Text("최신 조회 필요");
            if(current.FetchedAt>DateTimeOffset.Now.AddMinutes(1))return Ui.Text("조회 시각 확인 필요");
            if(quota.ResetsAt.HasValue&&quota.ResetsAt.Value<=current.FetchedAt.ToUnixTimeSeconds())return Ui.Text("재설정 조회 대기");
            if (quota.Remaining <= 0) return Ui.Text("현재 잔량 없음");
            double? rate = PredictionRate(weekly,current);
            if (!rate.HasValue) return Ui.Text("소진 예측: 사용 기록 더 필요");
            if(rate.Value<=.01)return Ui.Text("최근 소모 없음 · 추정 보류");
            double hours = quota.Remaining / rate.Value;
            return (weekly ? Ui.Text("연속 작업 시 ")+Math.Ceiling(hours-1e-9)+Ui.Text("시간") : Ui.Text("약 ")+HoursMinutes(hours)) + Ui.Text(" 후 소진 예측");
        }
        public string WeeklyDailyForecast(UsageSnapshot current)
        {
            if(current==null || current.Weekly==null) return Ui.Text("일평균 기준: 한도 정보 없음");
            if(DateTimeOffset.Now-current.FetchedAt>TimeSpan.FromMinutes(5) || current.FetchedAt>DateTimeOffset.Now.AddMinutes(1)) return Ui.Text("일평균 기준: 최신 조회 필요");
            if(current.Weekly.ResetsAt.HasValue && current.Weekly.ResetsAt.Value<=current.FetchedAt.ToUnixTimeSeconds()) return Ui.Text("재설정 조회 대기");
            if(current.Weekly.Remaining<=0) return Ui.Text("현재 잔량 없음");
            var daily=WeeklyDailyEstimate(current);
            if(daily==null || !daily.ActiveHoursPerDay.HasValue || daily.ActiveHoursPerDay.Value<=.000001 || daily.PercentPerDay<=.000001)
                return Ui.Text("일평균 작업 기준: 기록 더 필요");
            double days=current.Weekly.Remaining/daily.PercentPerDay;
            // 표시한 작업 시간과 일수의 관계가 일치하도록 충분한 소수 자릿수를 유지합니다.
            return Ui.Text("일평균 작업 시간 ")+daily.ActiveHoursPerDay.Value.ToString("0.##")+Ui.Text("시간 기준 ")+
                (Math.Ceiling(days*10-1e-9)/10).ToString("0.#")+Ui.Text("일 후 소진 예측");
        }
        public DailyUsageEstimate WeeklyDailyEstimate(UsageSnapshot current)
        {
            if (current == null || current.Weekly == null) return null;
            long latest = current.FetchedAt.ToUnixTimeSeconds();
            long today = (latest + 32400) / 86400, firstDay = today - preferences.ForecastDays;
            var days = new Dictionary<long, double[]>();
            for (int i = 1; i < Samples.Count; i++) {
                var a = Samples[i - 1]; var b = Samples[i];
                long elapsed = b.At - a.At;
                if (b.At > latest || !HistoryIntervals.Observed(a,b,true)) continue;
                long cursor = a.At;
                while (cursor < b.At) {
                    long day = (cursor + 32400) / 86400;
                    long end = Math.Min(b.At, (day + 1) * 86400 - 32400);
                    if (day >= firstDay && day < today) {
                        double[] bucket;
                        if (!days.TryGetValue(day, out bucket)) days[day] = bucket = new double[3];
                        bucket[0] += (a.Week.Value - b.Week.Value) * (end - cursor) / elapsed;
                        bucket[1] += end - cursor;
                        bucket[2]++;
                    }
                    cursor = end;
                }
            }
            var activeDays = IntermittentForecast.Bins(Samples,true,latest,preferences.ForecastDays)
                .Where(x=>x.Amount>.000001).GroupBy(x=>(x.At+32400)/86400).ToDictionary(x=>x.Key,x=>x.Sum(b=>b.Covered)/3600);
            double total = 0, hours = 0, activeHours = 0, weights = 0; int count = 0;
            bool timeAvailable = true;
            foreach (var day in days) {
                if (day.Value[1] < 3600 || day.Value[2] < 3) continue;
                double weight = Math.Pow(.5, (today - 1 - day.Key) / Math.Max(1, preferences.ForecastDays / 3.0));
                total += day.Value[0] * weight;
                hours += day.Value[1] / 3600 * weight;
                double active;
                if (activeDays.TryGetValue(day.Key,out active)) activeHours += active * weight;
                else if (day.Value[0]>0) timeAvailable=false;
                weights += weight; count++;
            }
            return weights > 0 ? new DailyUsageEstimate { PercentPerDay = total / weights,
                ObservedHoursPerDay = hours / weights, ActiveHoursPerDay=timeAvailable?(double?)(activeHours/weights):null, ObservedDays = count } : null;
        }
        public string WeeklyDailyText(UsageSnapshot current, bool detailed = false)
        {
            if (current == null || DateTimeOffset.Now - current.FetchedAt > TimeSpan.FromMinutes(5) || current.FetchedAt > DateTimeOffset.Now.AddMinutes(1))
                return Ui.Text("주간 하루 평균: 최신 조회 필요");
            var estimate = WeeklyDailyEstimate(current);
            if (estimate == null) return Ui.Text("주간 하루 평균: 이전 날짜의 1시간 이상 기록 필요");
            return Ui.Text("하루 평균 ") + estimate.PercentPerDay.ToString("0.#") +
                (estimate.ActiveHoursPerDay.HasValue ? Ui.Text("% 소모 · 사용 약 ")+HoursMinutes(estimate.ActiveHoursPerDay.Value)+Ui.Text(" (추정)") : Ui.Text("% 소모 · 사용 시간: 관측 부족")) +
                (detailed ? " · "+estimate.ObservedDays+Ui.Text("일 기준") : "");
        }
        public double Consumption(DateTimeOffset start, DateTimeOffset end, bool weekly)
        {
            double total = 0;
            for (int i = 1; i < Samples.Count; i++) {
                var a = Samples[i-1]; var b = Samples[i];
                double? previous = weekly ? a.Week : a.Five; double? value = weekly ? b.Week : b.Five;
                long? previousReset = weekly ? a.WeekReset : a.FiveReset; long? reset = weekly ? b.WeekReset : b.FiveReset;
                if (!HistoryIntervals.Observed(a,b,weekly)) continue;
                long overlap=Math.Min(b.At,end.ToUnixTimeSeconds())-Math.Max(a.At,start.ToUnixTimeSeconds());
                if(overlap>0)total+=Math.Max(0,previous.Value-value.Value)*overlap/(b.At-a.At);
            }
            return total;
        }
    }
    static class HistoryExtensions
    {
        public static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> sequence, int count)
        {
            var array = sequence.ToArray(); return array.Skip(Math.Max(0, array.Length - count));
        }
    }
}
