using System;
using System.Drawing;
using System.IO;

namespace CodexUsageTaskbar
{
    internal static class Tests
    {
        static int count;
        static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAIL: " + label);
            count++; Console.WriteLine("PASS: " + label);
        }
        static void Reject(string json, string label)
        {
            try { UsageParser.Parse(json); } catch (InvalidDataException) { Check(true, label); return; }
            Check(false, label);
        }
        static void MonitoringChecks()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                var now = new DateTimeOffset(2026,9,30,12,29,59,TimeSpan.FromHours(9));
                var settings = new Settings { FiveHourResetAlert=false, WeeklyResetAlert=false };
                var snapshot = new UsageSnapshot { FetchedAt=now, FiveHour=new QuotaWindow { Remaining=21,DurationMinutes=300,ResetsAt=now.AddHours(2).ToUnixTimeSeconds() },
                    Weekly=new QuotaWindow { Remaining=80,DurationMinutes=10080,ResetsAt=now.AddDays(2).ToUnixTimeSeconds() } };
                string state=Path.Combine(root,"alerts.json"); var engine=new AlertEngine(state);
                Check(engine.Evaluate(snapshot,now,settings).Count==0,"Quota above 20 percent stays quiet");
                snapshot.FiveHour.Remaining=20;
                Check(engine.Evaluate(snapshot,now,settings).Count==1,"Exactly 20 percent alerts once");
                Check(new AlertEngine(state).Evaluate(snapshot,now.AddSeconds(1),settings).Count==0,"Low quota alert remains deduplicated after restart");
                snapshot.FiveHour.ResetsAt=now.AddHours(3).ToUnixTimeSeconds();
                Check(engine.Evaluate(snapshot,now,settings).Count==1,"A new quota cycle can alert again");
                settings.LowQuotaAlert=false;settings.FiveHourResetAlert=true;
                snapshot.FiveHour.ResetsAt=now.AddSeconds(3601).ToUnixTimeSeconds();
                Check(engine.Evaluate(snapshot,now,settings).Count==0,"Reset alert waits until one hour remains");
                Check(engine.Evaluate(snapshot,now.AddSeconds(1),settings).Count==1,"Five-hour reset alerts at the one-hour boundary");
                Check(engine.Evaluate(snapshot,now.AddSeconds(2),settings).Count==0,"Reset reminder is not repeated each tick");
                settings.FiveHourResetAlert=false;settings.WeeklyResetAlert=true;
                snapshot.Weekly.ResetsAt=new DateTimeOffset(2026,10,1,18,0,0,TimeSpan.FromHours(9)).ToUnixTimeSeconds();
                Check(engine.Evaluate(snapshot,now,settings).Count==0,"Weekly reminder waits for 12:30 Korea time on D-1");
                Check(engine.Evaluate(snapshot,now.AddSeconds(1),settings).Count==1,"Weekly reminder fires at D-1 12:30 Korea time");
                var restartedAlerts = new AlertEngine(state).Evaluate(snapshot,now.AddSeconds(2),settings);
                Check(restartedAlerts.Count==0,"Weekly reminder stays deduplicated after restart");
                snapshot.FetchedAt=now.AddMinutes(-6);
                snapshot.Weekly.ResetsAt=new DateTimeOffset(2026,10,1,19,0,0,TimeSpan.FromHours(9)).ToUnixTimeSeconds();
                Check(engine.Evaluate(snapshot,now.AddSeconds(1),settings).Count==0,"Stale quota data does not trigger a notification");
                snapshot.FetchedAt=now.AddHours(2);
                Check(engine.Evaluate(snapshot,now.AddHours(2),settings).Count==1,"Same-day resume delivers a missed weekly reminder once");
                settings.WeeklyResetAlert=false;
                Check(engine.Evaluate(snapshot,now.AddHours(2),settings).Count==0,"Disabled reminders stay quiet");
                CustomAlertChecks(root,now);
                var poll=new PollSchedule();
                Check(!poll.Due(now,false,false)&&!poll.Due(now,true,true),"Polling pauses offline and during suspend");
                poll.Completed(now,true,true,false);Check(poll.Next==now.AddSeconds(60),"Normal polling is once per minute");
                poll.Completed(now,true,true,true);Check(poll.Next==now.AddSeconds(120),"Battery polling uses two minutes");
                poll.Completed(now,false,true,false);Check(poll.Next==now.AddSeconds(120),"Failures start exponential backoff");
                for(int i=0;i<8;i++)poll.Completed(now,false,true,false);
                Check(poll.Next==now.AddSeconds(900),"Failure backoff is capped at 15 minutes");
                poll.Wake(now);Check(poll.Due(now,true,false),"Resume and reconnect wake polling immediately");
                snapshot.FiveHour.ResetsAt=now.ToUnixTimeSeconds();
                Check(poll.ResetDue(snapshot,now)&&!poll.ResetDue(snapshot,now),"An elapsed reset schedules one immediate refresh");
                var realNow=DateTimeOffset.Now;var history=new UsageHistory(Path.Combine(root,"history.json"));
                snapshot.FiveHour.ResetsAt=realNow.AddHours(2).ToUnixTimeSeconds();snapshot.Weekly.ResetsAt=realNow.AddDays(2).ToUnixTimeSeconds();
                for(int i=0;i<3;i++) { snapshot.FetchedAt=realNow.AddMinutes(-10+i*5);snapshot.FiveHour.Remaining=50-i*10;snapshot.Weekly.Remaining=80-i*2;history.Record(snapshot); }
                Check(Math.Abs(history.Rate(false,snapshot).Value-120)<.001,"Consumption speed is calculated from a ten-minute observation");
                Check(Math.Abs(history.Consumption(realNow.AddMinutes(-15),realNow.AddSeconds(1),false)-20)<.001,"Hourly consumption sums observed percentage point drops");
                Check(history.PredictionRate(false,snapshot)==null,"Ten-minute startup data cannot establish an intermittent usage pattern");
                string saved=File.ReadAllText(Path.Combine(root,"history.json"));
                Check(!saved.Contains("ResetCredit")&&!saved.Contains("token")&&!saved.Contains("Plan"),"History only stores percentages and timing, not auth or reset credits");
                Check(new UsageHistory(Path.Combine(root,"history.json")).Samples.Count==3,"Local history survives restart");
                snapshot.FetchedAt=realNow.AddSeconds(1);snapshot.FiveHour.ResetsAt=realNow.AddHours(5).ToUnixTimeSeconds();snapshot.FiveHour.Remaining=100;history.Record(snapshot);
                Check(history.Rate(false,snapshot)==null,"Forecast does not cross a quota reset");
                Check(Math.Abs(history.Consumption(realNow.AddMinutes(-15),realNow.AddSeconds(2),false)-20)<.001,"Reset replenishment is not treated as negative consumption");
                history.Clear();Check(new UsageHistory(Path.Combine(root,"history.json")).Samples.Count==0,"Deleting history removes stored observations");
                var display=new Settings { DarkTheme=true,FontSize=14,BarWidth=110,BarThickness=7 };
                var layout=WidgetLayout.Create(snapshot,false,false,display,realNow);
                Check(layout.Common>=layout.Time+WidgetLayout.Measure(layout.FiveText,14)&&layout.Width>layout.Common,"Enlarged text fits beside the shared label without overlap");
                display.ShowReset=false;display.ShowUsage=false;
                Check(WidgetLayout.Create(snapshot,false,false,display,realNow).Width<layout.Width,"Hiding panes actually reduces the card width");
                using(var bitmap=new Bitmap(layout.Width,40))using(var g=Graphics.FromImage(bitmap)){
                    Widget.PaintCard(g,1,bitmap.Size,snapshot,false,false,false,0,display);
                    Check(bitmap.GetPixel(5,35).R<80,"Dark theme changes the widget background");
                }
                using(var bitmap=new Bitmap(400,HoverDetails.ContentHeight(snapshot,null,false,false,false,true)))using(var g=Graphics.FromImage(bitmap)){
                    HoverDetails.PaintPanel(g,1,snapshot,null,false,false,false,display,history);
                    Check(bitmap.GetPixel(15,200).R<80,"Dark detail panel renders with the history section");
                }
                var restored=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>("{\"HorizontalOffset\":20}");
                Check(restored.HistoryEnabled&&restored.FontSize==12&&restored.LowQuotaAlert&&restored.HistoryDays==90&&restored.ForecastDays==7,"Old settings retain recommended retention and forecast defaults");
                ExtendedHistoryChecks(root,realNow);
                IntermittentForecastChecks(root,realNow);
            } finally {
                if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);
            }
        }
        static void CustomAlertChecks(string root,DateTimeOffset now)
        {
            var settings=new Settings {FiveHourResetAlert=false,WeeklyResetAlert=false,FiveHourLowPercent=35,WeeklyLowPercent=10};
            var data=new UsageSnapshot {FetchedAt=now,FiveHour=new QuotaWindow {Remaining=35.1,ResetsAt=now.AddHours(2).ToUnixTimeSeconds()},Weekly=new QuotaWindow {Remaining=11,ResetsAt=now.AddDays(3).ToUnixTimeSeconds()}};
            string path=Path.Combine(root,"custom-alerts.json");var alerts=new AlertEngine(path);
            Check(alerts.Evaluate(data,now,settings).Count==0,"Independent user thresholds wait above each configured percentage");
            data.FiveHour.Remaining=35;
            Check(alerts.Evaluate(data,now,settings).Count==1,"Custom five-hour threshold triggers at its exact boundary");
            Check(new AlertEngine(path).Evaluate(data,now,settings).Count==0,"Custom criteria remain deduplicated after restart");
            settings.FiveHourLowAlert=false;data.Weekly.Remaining=10;
            Check(alerts.Evaluate(data,now,settings).Count==1,"Weekly low-quota alert can operate independently");
            settings.LowQuotaAlert=false;settings.FiveHourResetAlert=true;settings.FiveHourResetMinutes=30;
            data.FiveHour.ResetsAt=now.AddSeconds(1801).ToUnixTimeSeconds();
            Check(alerts.Evaluate(data,now,settings).Count==0,"Selected five-hour lead time waits until its boundary");
            Check(alerts.Evaluate(data,now.AddSeconds(1),settings).Count==1,"Selected thirty-minute reset reminder triggers at the boundary");
            settings.FiveHourResetAlert=false;settings.WeeklyResetAlert=true;settings.WeeklyResetMode=1;settings.WeeklyResetMinutes=120;
            data.Weekly.ResetsAt=now.AddSeconds(7201).ToUnixTimeSeconds();
            Check(alerts.Evaluate(data,now,settings).Count==0&&alerts.Evaluate(data,now.AddSeconds(1),settings).Count==1,"Weekly relative-time mode respects a selected two-hour lead time");
            settings.WeeklyResetMode=0;settings.WeeklyReminderDays=2;settings.WeeklyReminderHour=9;settings.WeeklyReminderMinute=15;
            var calendarNow=new DateTimeOffset(2026,9,30,9,14,59,TimeSpan.FromHours(9));
            data.FetchedAt=calendarNow;data.Weekly.ResetsAt=new DateTimeOffset(2026,10,2,18,0,0,TimeSpan.FromHours(9)).ToUnixTimeSeconds();
            Check(alerts.Evaluate(data,calendarNow,settings).Count==0&&alerts.Evaluate(data,calendarNow.AddSeconds(1),settings).Count==1,"Weekly calendar mode respects selected days and Korean local time");
            settings.FiveHourLowPercent=0;settings.WeeklyLowPercent=200;settings.FiveHourResetMinutes=999;settings.WeeklyReminderHour=99;settings.Normalize();
            Check(settings.FiveHourLowPercent==1&&settings.WeeklyLowPercent==100&&settings.FiveHourResetMinutes==300&&settings.WeeklyReminderHour==23,"Custom notification settings remain within supported quota and time ranges");
            string config=Path.Combine(root,"custom-settings.json");LocalData.Write(config,settings);var restored=LocalData.Read<Settings>(config);
            Check(restored.WeeklyReminderDays==2&&restored.WeeklyReminderMinute==15&&restored.WeeklyResetMinutes==120,"Notification criteria survive a settings save and reload");
            var legacy=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>("{\"LowQuotaAlert\":false}");
            Check(!legacy.LowQuotaAlert&&legacy.FiveHourResetMinutes==60&&legacy.WeeklyReminderDays==1&&legacy.WeeklyReminderHour==12&&legacy.WeeklyReminderMinute==30,"Legacy notification defaults and disabled alerts are preserved");
        }
        static void ExtendedHistoryChecks(string root,DateTimeOffset now)
        {
            var options=new Settings();string path=Path.Combine(root,"long-history.json");
            LocalData.Write(path,new[] {
                new HistorySample {At=now.AddDays(-100).ToUnixTimeSeconds(),Five=70,Week=80},
                new HistorySample {At=now.AddDays(-45).ToUnixTimeSeconds(),Five=70,Week=80},
                new HistorySample {At=now.AddDays(-10).ToUnixTimeSeconds(),Five=70,Week=80} });
            var store=new UsageHistory(path,options);
            Check(store.Samples.Count==2,"Default 90-day retention keeps history beyond two weeks and expires older data");
            options.HistoryDays=30;store.ApplyRetention();
            Check(store.Samples.Count==1&&new UsageHistory(path,options).Samples.Count==1,"Selected retention is applied and persists after restart");
            var dense=new System.Collections.Generic.List<HistorySample>();
            for(int i=0;i<22000;i++)dense.Add(new HistorySample {At=now.AddDays(-10).AddSeconds(i*30).ToUnixTimeSeconds(),Five=70,Week=80});
            LocalData.Write(path,dense);store=new UsageHistory(path,new Settings());
            Check(store.Samples.Count==22000,"History is no longer silently capped at 20160 records");
            var compact=new[] {
                new HistorySample {At=now.AddDays(-40).ToUnixTimeSeconds(),Five=70,FiveReset=1},
                new HistorySample {At=now.AddDays(-40).AddSeconds(1).ToUnixTimeSeconds(),Five=69,FiveReset=1},
                new HistorySample {At=now.AddDays(-40).AddSeconds(2).ToUnixTimeSeconds(),Five=100,FiveReset=2} };
            LocalData.Write(path,compact);store=new UsageHistory(path,new Settings());
            Check(store.Samples.Count==2,"Older records compact to five minutes without discarding a reset boundary");
            store=new UsageHistory(Path.Combine(root,"adaptive.json"),new Settings());
            long reset=now.AddDays(2).ToUnixTimeSeconds();
            var current=new UsageSnapshot {FetchedAt=now,FiveHour=new QuotaWindow {Remaining=60,ResetsAt=reset},Weekly=new QuotaWindow {Remaining=60,ResetsAt=reset}};
            for(int i=0;i<=60;i++) {
                double value=100-(i<=45?i*.1:4.5+(i-45)*1.5);
                store.Samples.Add(new HistorySample {At=now.AddMinutes(i-60).ToUnixTimeSeconds(),Five=value,Week=value,FiveReset=reset,WeekReset=reset});
            }
            Check(store.Rate(false,current)>50,"Recent acceleration outweighs the older low-use pattern");
            Check(store.PatternForecast(true,current)==Ui.Text("기록 패턴: 하루 4시간·3일 기록 필요"),"A single observed hour is not extrapolated to a whole week");
            Check(!store.Estimate(false,current).ExpectedConsumption.HasValue,"Pattern estimates wait for four hours of observed bins");
            var chosen=new Settings {ForecastDays=1};var periods=new UsageHistory(Path.Combine(root,"periods.json"),chosen);
            for(int i=0;i<=6;i++)periods.Samples.Add(new HistorySample {At=now.AddDays(-3).AddMinutes(i*10).ToUnixTimeSeconds(),Week=100-i*5,WeekReset=reset-7*86400});
            for(int i=0;i<=6;i++)periods.Samples.Add(new HistorySample {At=now.AddMinutes(-60+i*10).ToUnixTimeSeconds(),Week=80-i,WeekReset=reset});
            double oneDay=periods.BaselineRate(true,current).Value;chosen.ForecastDays=7;
            Check(periods.BaselineRate(true,current).Value>oneDay,"Changing the reference period changes the baseline without crossing resets");
            current.FiveHour.Remaining=99;current.FiveHour.ResetsAt=now.AddMinutes(5).ToUnixTimeSeconds();
            foreach(var sample in store.Samples)sample.FiveReset=current.FiveHour.ResetsAt;
            Check(store.Estimate(false,current).HorizonHours<=5/60.0+.001,"Pattern forecast horizon stops at the actual reset");
            store.Samples.Clear();
            for(int i=0;i<3;i++)store.Samples.Add(new HistorySample {At=now.AddMinutes(-10+i*5).ToUnixTimeSeconds(),Five=50,Week=60,FiveReset=reset,WeekReset=reset});
            current.FiveHour.ResetsAt=reset;current.FiveHour.Remaining=50;
            Check(!store.PredictionRate(false,current).HasValue,"Zero observed consumption does not imply unlimited usage");
            store.Samples.Clear();
            store.Samples.Add(new HistorySample {At=now.AddMinutes(-30).ToUnixTimeSeconds(),Five=90,Week=90,FiveReset=reset,WeekReset=reset});
            store.Samples.Add(new HistorySample {At=now.ToUnixTimeSeconds(),Five=50,Week=50,FiveReset=reset,WeekReset=reset});
            Check(store.PredictionRate(true,current)==null&&store.BaselineRate(true,current)==null,"Unobserved gaps do not become invented consumption rates");
            store.Samples.Clear();
            for(int i=0;i<=15;i++)store.Samples.Add(new HistorySample {At=now.AddDays(-2).AddMinutes(i*5).ToUnixTimeSeconds(),Five=90-i,Week=90-i,FiveReset=now.AddDays(-1).ToUnixTimeSeconds(),WeekReset=now.AddDays(-1).ToUnixTimeSeconds()});
            store.Record(current);
            store=new UsageHistory(Path.Combine(root,"adaptive.json"),new Settings());
            Check(!store.Rate(false,current).HasValue&&Math.Abs(store.PredictionRate(false,current).Value-12)<.001,"Persisted previous observation rate survives restart and a reset boundary without counting the gap");
            Check(store.Forecast(false,current)==Ui.Text("약 ")+4+Ui.Text("시간 ")+10+Ui.Text("분")+Ui.Text(" 후 소진 예측"),"Persisted consumption produces an hours-and-minutes exhaustion estimate");
            for(int i=0;i<2;i++)store.Samples.Add(new HistorySample {At=now.AddMinutes(-10+i*5).ToUnixTimeSeconds(),Five=50,Week=60,FiveReset=reset,WeekReset=reset});
            store.Samples.Sort((a,b)=>a.At.CompareTo(b.At));
            Check(Math.Abs(store.PredictionRate(false,current).Value-12)<.001,"Ten idle minutes do not erase the previous usage intensity");
            var expiredPattern=new UsageHistory(Path.Combine(root,"expired-pattern.json"),new Settings {ForecastDays=1});
            for(int i=0;i<3;i++)expiredPattern.Samples.Add(new HistorySample {At=now.AddDays(-2).AddMinutes(i*5).ToUnixTimeSeconds(),Five=90-i,FiveReset=reset-86400});
            Check(!expiredPattern.PredictionRate(false,current).HasValue,"Previous patterns outside the selected forecast period are not reused");
            var daily=new UsageHistory(Path.Combine(root,"daily.json"),new Settings {ForecastDays=3});
            var localToday=new DateTimeOffset(ExpiryDisplay.InKorea(now).Date,TimeSpan.FromHours(9));
            for(int day=2;day>=0;day--)for(int i=0;i<=6;i++) {
                double amount=day==2?1:day==1?2:10;
                daily.Samples.Add(new HistorySample {At=localToday.AddDays(-day).AddMinutes(i*10).ToUnixTimeSeconds(),Week=100-i*amount,WeekReset=reset-day*86400});
            }
            var dailyEstimate=daily.WeeklyDailyEstimate(current);
            Check(dailyEstimate!=null&&dailyEstimate.ObservedDays==2&&Math.Abs(dailyEstimate.PercentPerDay-10)<.001&&Math.Abs(dailyEstimate.ObservedHoursPerDay-1)<.001,"Daily forecast weights observed daily totals without multiplying hourly use by 24 or including today");
            daily.Samples.Insert(7,new HistorySample {At=localToday.AddDays(-2).AddMinutes(90).ToUnixTimeSeconds(),Week=20,WeekReset=reset-2*86400});
            daily.Samples.Insert(8,new HistorySample {At=localToday.AddDays(-2).AddMinutes(100).ToUnixTimeSeconds(),Week=10,WeekReset=reset});
            Check(Math.Abs(daily.WeeklyDailyEstimate(current).PercentPerDay-10)<.001,"Daily totals exclude unobserved gaps and quota reset jumps");
            Check(daily.WeeklyDailyText(current,true).Contains("2"+Ui.Text("일 기준"))&&daily.WeeklyDailyEstimate(current).ActiveHoursPerDay==1,"Daily summary includes estimated active hours and qualifying days");
            var idleDay=new UsageHistory(Path.Combine(root,"idle-day.json"),new Settings());
            for(int i=0;i<=6;i++)idleDay.Samples.Add(new HistorySample {At=localToday.AddDays(-1).AddMinutes(i*10).ToUnixTimeSeconds(),Week=80,WeekReset=reset});
            Check(idleDay.WeeklyDailyEstimate(current).PercentPerDay==0,"Observed zero-use days remain in the daily average");
            Check(idleDay.WeeklyDailyEstimate(current).ActiveHoursPerDay==0,"Polling for an idle hour does not count as an hour of use");
            var mixedDay=new UsageHistory(Path.Combine(root,"mixed-day.json"),new Settings());
            AddForecastBins(mixedDay,localToday.AddDays(-1).AddHours(12).ToUnixTimeSeconds(),new double[]{1,0,1,0,1,0,1,0},reset);
            var mixedEstimate=mixedDay.WeeklyDailyEstimate(current);
            Check(mixedEstimate.ObservedHoursPerDay==2&&mixedEstimate.ActiveHoursPerDay==1,"Average daily use counts only consumption buckets and excludes observed idle buckets");
            Check(mixedDay.WeeklyDailyForecast(current)==Ui.Text("일평균 작업 시간 ")+1+Ui.Text("시간 기준 ")+15+Ui.Text("일 후 소진 예측"),"Daily exhaustion converts weekly active hours using observed average work hours per day");
            var consistentDay=new UsageHistory(Path.Combine(root,"consistent-day.json"),new Settings());
            AddForecastBins(consistentDay,localToday.AddDays(-1).AddHours(12).ToUnixTimeSeconds(),new double[]{4,0,0,0},reset);
            var eighty=new UsageSnapshot {FetchedAt=now,Weekly=new QuotaWindow {Remaining=80,ResetsAt=reset}};
            Check(consistentDay.WeeklyDailyForecast(eighty)==Ui.Text("일평균 작업 시간 ")+0.25.ToString("0.##")+Ui.Text("시간 기준 ")+20+Ui.Text("일 후 소진 예측"),"Four percent daily consumption with eighty percent remaining yields twenty days, not sixty-two");
            Check(consistentDay.Forecast(true,eighty)==Ui.Text("소진 예측: 사용 기록 더 필요"),"Daily statistics do not substitute for missing continuous-work intensity records");
            AddForecastBins(consistentDay,now.ToUnixTimeSeconds()/900*900-3600,new double[]{.5,.5,.5,.5},reset);
            double continuousHours=eighty.Weekly.Remaining/consistentDay.PredictionRate(true,eighty).Value;
            Check(consistentDay.Forecast(true,eighty)==Ui.Text("연속 작업 시 ")+Math.Ceiling(continuousHours-1e-9)+Ui.Text("시간")+Ui.Text(" 후 소진 예측")&&Math.Abs(continuousHours-5)>1,"Continuous exhaustion follows active intensity independently of daily work hours");
            // 자정 직후에는 최근 한 시간 자료 일부가 전날에 속하므로 완료 날짜 통계를 다시 확인합니다.
            var updatedDaily=consistentDay.WeeklyDailyEstimate(eighty);
            double updatedDays=Math.Ceiling(eighty.Weekly.Remaining/updatedDaily.PercentPerDay*10-1e-9)/10;
            Check(consistentDay.WeeklyDailyForecast(eighty)==Ui.Text("일평균 작업 시간 ")+updatedDaily.ActiveHoursPerDay.Value.ToString("0.##")+Ui.Text("시간 기준 ")+updatedDays.ToString("0.#")+Ui.Text("일 후 소진 예측"),"Daily exhaustion uses completed-day consumption independently of current active intensity, including near midnight");
            Check(idleDay.WeeklyDailyForecast(current)==Ui.Text("일평균 작업 기준: 기록 더 필요"),"Zero work hours never divide daily exhaustion by zero");
            var settingsArea=new Rectangle(-1920,0,1920,1040);
            Check(settingsArea.Contains(SettingsDialog.VisibleBounds(new Size(480,800),settingsArea)),"Settings window centers inside the widget monitor including negative coordinates");
            var smallArea=new Rectangle(100,200,640,480);
            Check(SettingsDialog.VisibleBounds(new Size(960,1600),smallArea)==smallArea,"Settings window fits a smaller remote-desktop working area");
            foreach(float density in new[]{1f,2f})using(var form=new SettingsDialog(new Settings(),delegate {},delegate {})) {
                form.AutoScaleMode=System.Windows.Forms.AutoScaleMode.None;
                form.ClientSize=new Size(560,450);form.Scale(new SizeF(density,density));
                form.PerformLayout();
                var actions=form.Controls.Find("SettingsActions",true)[0];actions.PerformLayout();
                var deletion=form.Controls.Find("HistoryDelete",true)[0];
                var scroll=form.Controls.Find("SettingsScroll",true)[0];scroll.PerformLayout();
                Check(actions.ClientRectangle.Contains(deletion.Bounds)&&deletion.Height>=30*density,
                    "History deletion stays fully visible in the fixed settings footer at "+density+" scale");
                Check(((System.Windows.Forms.ScrollableControl)scroll).AutoScrollMinSize.Height>=scroll.Controls[0].Height,
                    "Settings scrollbar reserves the full card content height at "+density+" scale");
            }
            idleDay.Samples.RemoveAt(6);
            Check(idleDay.WeeklyDailyEstimate(current)==null,"Short partial daily observations do not produce an unsupported daily estimate");
            current.FetchedAt=now.AddMinutes(-6);
            Check(store.Forecast(false,current)==Ui.Text("최신 조회 필요"),"Stale data suppresses additional usage estimates");
            options.HistoryDays=0;options.ForecastDays=999;options.Normalize();
            Check(options.HistoryDays==90&&options.ForecastDays==7,"Invalid period settings fall back to recommendations");
            using(var bitmap=new Bitmap(720,620))using(var graphics=Graphics.FromImage(bitmap))using(var chart=new HistoryCanvas(store,current,new Settings()) {Size=bitmap.Size,RangeDays=30}) {
                chart.DrawToBitmap(bitmap,chart.ClientRectangle);
                Check(bitmap.GetPixel(0,0).R>200,"History chart renders the selected longer period offscreen");
            }
            var gapChart=new UsageHistory(Path.Combine(root,"chart-gaps.json"));
            long gapStart=now.AddHours(-2).ToUnixTimeSeconds();
            gapChart.Samples.Add(new HistorySample {At=gapStart,Five=80,FiveReset=gapStart+86400});
            gapChart.Samples.Add(new HistorySample {At=gapStart+3600,Five=80,FiveReset=gapStart+86400});
            using(var bitmap=new Bitmap(120,100))using(var graphics=Graphics.FromImage(bitmap)) {
                graphics.Clear(Color.White);
                HistoryCanvas.DrawSeries(graphics,new RectangleF(10,10,100,80),gapChart,DateTimeOffset.FromUnixTimeSeconds(gapStart),DateTimeOffset.FromUnixTimeSeconds(gapStart+3600),false,Color.Blue,1);
                int colored=0,blank=0;for(int x=20;x<100;x++){if(bitmap.GetPixel(x,26).B>bitmap.GetPixel(x,26).R)colored++;else blank++;}
                Check(colored>0&&blank==0,"Observation gaps are connected with a solid visual line");
                gapChart.Samples[1].FiveReset++;
                graphics.Clear(Color.White);
                HistoryCanvas.DrawSeries(graphics,new RectangleF(10,10,100,80),gapChart,DateTimeOffset.FromUnixTimeSeconds(gapStart),DateTimeOffset.FromUnixTimeSeconds(gapStart+3600),false,Color.Blue,1);
                Check(bitmap.GetPixel(50,26).B>bitmap.GetPixel(50,26).R,"Small reset timestamp changes do not interrupt the observed line");
                gapChart.Samples[1].Five=90;
                graphics.Clear(Color.White);
                HistoryCanvas.DrawSeries(graphics,new RectangleF(10,10,100,80),gapChart,DateTimeOffset.FromUnixTimeSeconds(gapStart),DateTimeOffset.FromUnixTimeSeconds(gapStart+3600),false,Color.Blue,1);
                Check(bitmap.GetPixel(50,26).ToArgb()==Color.White.ToArgb(),"Reset cycles are never bridged by the history chart");
                Check(bitmap.GetPixel(108,18).ToArgb()==Color.White.ToArgb(),"Reset observations below 100% do not receive a start marker");
                Check(bitmap.GetPixel(8,26).B>bitmap.GetPixel(8,26).R,"Last observation before reset receives a small endpoint ring");
                gapChart.Samples[1].Five=100;
                graphics.Clear(Color.White);
                HistoryCanvas.DrawSeries(graphics,new RectangleF(10,10,100,80),gapChart,DateTimeOffset.FromUnixTimeSeconds(gapStart),DateTimeOffset.FromUnixTimeSeconds(gapStart+3600),false,Color.Blue,1);
                Check(bitmap.GetPixel(108,10).B>bitmap.GetPixel(108,10).R,"Reset to 100% receives a small ring marker");
                gapChart.Samples[1].Five=0;
                graphics.Clear(Color.White);
                HistoryCanvas.DrawSeries(graphics,new RectangleF(10,10,100,80),gapChart,DateTimeOffset.FromUnixTimeSeconds(gapStart),DateTimeOffset.FromUnixTimeSeconds(gapStart+3600),false,Color.Blue,1);
                Check(bitmap.GetPixel(110,92).B>bitmap.GetPixel(110,92).R,"Exhausted quota receives one endpoint ring");
                Check(gapChart.Consumption(DateTimeOffset.FromUnixTimeSeconds(gapStart),DateTimeOffset.FromUnixTimeSeconds(gapStart+3600),false)==0,"Visual gap connections do not add unobserved consumption");
            }
            foreach(int density in new[]{96,192})using(var bitmap=new Bitmap(720,620)) {
                bitmap.SetResolution(density,density);
                using(var graphics=Graphics.FromImage(bitmap))
                using(var chart=new HistoryCanvas(store,current,new Settings(),1) {Size=bitmap.Size})
                using(var paint=new System.Windows.Forms.PaintEventArgs(graphics,chart.ClientRectangle)) {
                    typeof(HistoryCanvas).GetMethod("OnPaint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(chart,new object[]{paint});
                    Check(bitmap.GetPixel(15,250).ToArgb()==Palette.For(false).Card.ToArgb()&&bitmap.GetPixel(15,430).ToArgb()==Palette.For(false).Card.ToArgb(),
                        "History charts keep the panel layout scale when graphics density is "+density+" DPI");
                }
            }
        }
        static void AddForecastBins(UsageHistory history, long start, double[] amounts, long reset)
        {
            double remaining = 100;
            history.Samples.Add(new HistorySample {At=start, Five=remaining, Week=remaining, FiveReset=reset, WeekReset=reset});
            for (int i=0;i<amounts.Length;i++) for(int step=1;step<=3;step++) {
                history.Samples.Add(new HistorySample {At=start+i*900+step*300, Five=remaining-amounts[i]*step/3,
                    Week=remaining-amounts[i]*step/3, FiveReset=reset, WeekReset=reset});
                if(step==3) remaining-=amounts[i];
            }
        }
        static void IntermittentForecastChecks(string root,DateTimeOffset now)
        {
            long end=now.ToUnixTimeSeconds()/900*900,reset=end+3*3600;
            var current=new UsageSnapshot {FetchedAt=DateTimeOffset.FromUnixTimeSeconds(end),
                FiveHour=new QuotaWindow {Remaining=60,ResetsAt=reset},Weekly=new QuotaWindow {Remaining=70,ResetsAt=reset}};
            var store=new UsageHistory(Path.Combine(root,"bursts.json"));
            var amounts=new double[32];for(int i=0;i<amounts.Length;i++)amounts[i]=i%8<4?1:0;
            AddForecastBins(store,end-32*900,amounts,reset);
            var before=store.Estimate(false,current);
            Check(Math.Abs(before.ActivePercentPerHour.Value-4)<.000001,"Active-period intensity excludes idle quarter hours");
            Check(before.ExpectedConsumption>0&&before.ExpectedConsumption<before.ActivePercentPerHour*before.HorizonHours,"Typical use includes idle periods instead of extending active speed continuously");
            var displayCurrent=new UsageSnapshot {FetchedAt=now,FiveHour=current.FiveHour,Weekly=current.Weekly};
            Check(store.Forecast(false,displayCurrent).Contains(Ui.Text(" 후 소진 예측"))&&store.ForecastDetail(false,displayCurrent)==Ui.Text("계속 사용 기준 · 소진 전에 재설정됩니다."),"Exhaustion estimate warns when reset occurs before projected depletion");
            Check(store.Forecast(false,displayCurrent)==Ui.Text("약 ")+15+Ui.Text("시간 ")+0+Ui.Text("분")+Ui.Text(" 후 소진 예측"),"Continuous exhaustion excludes idle time even when a typical-use model is available");
            Check(store.Forecast(true,displayCurrent)==Ui.Text("연속 작업 시 ")+18+Ui.Text("시간")+Ui.Text(" 후 소진 예측"),"Weekly continuous-use forecast rounds projected exhaustion to whole hours");
            Check(store.ForecastDetail(true,displayCurrent)==Ui.Text("계속 사용 기준 · 소진 전에 재설정됩니다."),"Sparse history still explains the continuous-use assumption when reset occurs first");
            var last=store.Samples[store.Samples.Count-1];
            store.Samples.Add(new HistorySample {At=end+300,Five=last.Five,Week=last.Week,FiveReset=reset,WeekReset=reset});
            store.Samples.Add(new HistorySample {At=end+600,Five=last.Five,Week=last.Week,FiveReset=reset,WeekReset=reset});
            current.FetchedAt=DateTimeOffset.FromUnixTimeSeconds(end+600);
            var paused=store.Estimate(false,current);
            Check(paused.ActivePercentPerHour==before.ActivePercentPerHour,"A ten-minute pause leaves conditional usage intensity unchanged");
            Check(Math.Abs(paused.ExpectedConsumption.Value/paused.HorizonHours-before.ExpectedConsumption.Value/before.HorizonHours)<.000001,"A ten-minute pause cannot collapse the typical-use forecast");
            store.Samples.RemoveAt(store.Samples.Count-1);store.Samples.RemoveAt(store.Samples.Count-1);
            for(int i=1;i<=120;i++)store.Samples.Add(new HistorySample {At=end+i*300,Five=last.Five,Week=last.Week,FiveReset=reset+86400,WeekReset=reset+86400});
            current.FetchedAt=DateTimeOffset.FromUnixTimeSeconds(end+120*300);current.FiveHour.ResetsAt=reset+86400;
            var sustainedIdle=store.Estimate(false,current);
            Check(sustainedIdle.ActivePercentPerHour==before.ActivePercentPerHour,"Sustained idle does not pretend the user works more slowly when active");
            Check(sustainedIdle.ExpectedConsumption.Value/sustainedIdle.HorizonHours<before.ExpectedConsumption.Value/before.HorizonHours,"Sustained observed inactivity lowers typical consumption");
            Check(sustainedIdle.ValidationOrigins>=12&&sustainedIdle.ValidationMae.HasValue,"Enough complete windows enable causal rolling-origin model selection");
            double oldActive=sustainedIdle.ActivePercentPerHour.Value;
            current.FetchedAt=current.FetchedAt.AddDays(1);current.FiveHour.ResetsAt=current.FetchedAt.AddHours(3).ToUnixTimeSeconds();
            store.Samples.Add(new HistorySample {At=current.FetchedAt.ToUnixTimeSeconds(),Five=50,Week=50,FiveReset=current.FiveHour.ResetsAt,WeekReset=current.FiveHour.ResetsAt});
            Check(store.Estimate(false,current).ActivePercentPerHour==oldActive,"An unobserved overnight gap is not learned as idle time");
            var blank=new UsageHistory(Path.Combine(root,"gaps-only.json"));
            blank.Samples.Add(new HistorySample {At=end-7200,Five=80,FiveReset=reset});
            blank.Samples.Add(new HistorySample {At=end,Five=60,FiveReset=reset});
            Check(IntermittentForecast.Bins(blank.Samples,false,end,7).Count==0,"Long gaps cannot be divided into invented observations");
            var lowCoverage=new UsageHistory(Path.Combine(root,"low-coverage.json"));
            lowCoverage.Samples.Add(new HistorySample {At=end-900,Five=80,FiveReset=reset});
            lowCoverage.Samples.Add(new HistorySample {At=end-300,Five=79,FiveReset=reset});
            Check(IntermittentForecast.Bins(lowCoverage.Samples,false,end,7).Count==0,"Incomplete quarter hours below 90 percent coverage are excluded");
            lowCoverage.Samples.Add(new HistorySample {At=end,Five=100,FiveReset=reset+3600});
            Check(IntermittentForecast.Bins(lowCoverage.Samples,false,end,7).Count==0,"A reset invalidates its entire bucket instead of adding consumption");
            var starved=new UsageHistory(Path.Combine(root,"exhausted.json"));
            AddForecastBins(starved,end-3600,new[]{100.0,0,0,0},reset);
            Check(IntermittentForecast.Bins(starved.Samples,false,end,7).Count==1,"Quota-exhausted time is censored instead of teaching the model zero demand");
            var causal=new UsageHistory(Path.Combine(root,"causal.json"));
            AddForecastBins(causal,end-32*900,amounts,reset);
            current.FetchedAt=DateTimeOffset.FromUnixTimeSeconds(end);current.FiveHour.ResetsAt=reset;
            var first=causal.Estimate(false,current);
            causal.Samples.Add(new HistorySample {At=end+300,Five=1,FiveReset=reset});
            var afterFuture=causal.Estimate(false,current);
            Check(first.ExpectedConsumption==afterFuture.ExpectedConsumption&&first.ActivePercentPerHour==afterFuture.ActivePercentPerHour,"Future readings cannot leak into training or forecast selection");
            current.FiveHour.ResetsAt=end+120;Check(Math.Abs(causal.Estimate(false,current).HorizonHours-1/30.0)<.000001,"Projection integrates only the fraction before an imminent reset");
            var daily=new UsageHistory(Path.Combine(root,"weekly-pattern.json"));
            long today=(end+32400)/86400*86400-32400;
            for(int day=3;day>=1;day--) {
                var dayAmounts=new double[32];for(int i=0;i<32;i++)dayAmounts[i]=i%2==0?.2:0;
                long start=today-day*86400+9*3600;
                AddForecastBins(daily,start,dayAmounts,start+2*86400);
            }
            current.FetchedAt=DateTimeOffset.FromUnixTimeSeconds(today);current.Weekly.ResetsAt=today+2*86400;
            var weekly=daily.Estimate(true,current);
            Check(Math.Abs(weekly.ExpectedConsumption.Value-6.4)<.000001&&Math.Abs(weekly.DailyObservedHours-8)<.000001,"Weekly projection repeats recorded daily use rather than active hourly rate times 24");
            current.Weekly.ResetsAt=today+4*3600;
            Check(daily.Estimate(true,current).ExpectedConsumption==0,"Within-day weekly projection follows observed usage timing rather than spreading work through the night");
            var shortDays=new UsageHistory(Path.Combine(root,"short-days.json"));
            for(int day=3;day>=1;day--)AddForecastBins(shortDays,today-day*86400+9*3600,new[]{1.0,0,1,0},today-day*86400+2*86400);
            Check(!shortDays.Estimate(true,current).ExpectedConsumption.HasValue,"Sparse daily coverage is not presented as a whole-week prediction");
            PatternHoldoutChecks(root,end);
        }
        static void PatternHoldoutChecks(string root,long end)
        {
            double totalOld=0,totalNew=0;int evaluated=0;
            var history=new UsageHistory(Path.Combine(root,"holdout.json"));
            var series=new double[160];for(int i=0;i<series.Length;i++)series[i]=i%8<4?.3:0;
            long start=end-series.Length*900,reset=end+86400;
            AddForecastBins(history,start,series,reset);
            // 마지막 32구간에서 각 예측 시점의 미래를 숨기는 순차 평가를 수행합니다.
            for(int origin=128;origin+4<=series.Length;origin+=4) {
                long at=start+origin*900;
                var snapshot=new UsageSnapshot {FetchedAt=DateTimeOffset.FromUnixTimeSeconds(at),FiveHour=new QuotaWindow {Remaining=50,ResetsAt=at+3600}};
                var rate=history.Rate(false,new UsageSnapshot {FetchedAt=snapshot.FetchedAt,FiveHour=new QuotaWindow {ResetsAt=reset}});
                var forecast=history.Estimate(false,snapshot);
                double actual=0;for(int i=origin;i<origin+4;i++)actual+=series[i];
                if(!rate.HasValue||!forecast.ExpectedConsumption.HasValue)continue;
                totalOld+=Math.Abs(rate.Value-actual);totalNew+=Math.Abs(forecast.ExpectedConsumption.Value-actual);evaluated++;
            }
            Check(evaluated==8&&totalNew<totalOld,"Unseen burst/pause windows improve aggregate MAE over the former recent-rate baseline");
            Console.WriteLine("HOLDOUT burst/pause 1h MAE: old="+(totalOld/evaluated).ToString("0.000")+", new="+(totalNew/evaluated).ToString("0.000")+" percentage points; origins="+evaluated);
        }
        static int Main()
        {
            try
            {
                var normal = UsageParser.Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":40,""windowDurationMins"":300,""resetsAt"":1790730000},""secondary"":{""usedPercent"":20,""windowDurationMins"":10080}}}");
                Check(normal.FiveHour.Remaining == 60 && normal.Weekly.Remaining == 80, "Remaining is 100 minus used");
                Check(normal.FiveHour.ResetsAt == 1790730000, "Reset timestamp is retained");
                Check(normal.ResetCredits == null, "Missing reset credits stay unavailable");
                var credits = UsageParser.Parse(@"{""result"":{""rateLimits"":{},""rateLimitResetCredits"":{""availableCount"":2,""credits"":[]}}}");
                Check(credits.ResetCredits == 2, "Reset count uses authoritative availableCount, not detail list length");
                var zeroCredits = UsageParser.Parse(@"{""rateLimits"":{},""rateLimitResetCredits"":{""availableCount"":0}}");
                Check(zeroCredits.ResetCredits == 0, "Zero reset credits is distinct from unavailable");
                var badCredits = UsageParser.Parse(@"{""rateLimits"":{},""rateLimitResetCredits"":{""availableCount"":-1}}");
                Check(badCredits.ResetCredits == null, "Invalid reset counts are not displayed");
                var expiryNow = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(9));
                Check(Widget.FormatRemainingTime(new QuotaWindow { DurationMinutes = 300, ResetsAt = expiryNow.AddHours(2.5).ToUnixTimeSeconds() }, expiryNow) == Ui.Text("2시간 남음"), "Five-hour countdown shows whole hours");
                Check(Widget.FormatRemainingTime(new QuotaWindow { DurationMinutes = 300, ResetsAt = expiryNow.AddHours(1).ToUnixTimeSeconds() }, expiryNow) == Ui.Text("1시간 남음"), "Exactly one hour uses hours");
                Check(Widget.FormatRemainingTime(new QuotaWindow { DurationMinutes = 300, ResetsAt = expiryNow.AddMinutes(59).ToUnixTimeSeconds() }, expiryNow) == Ui.Text("59분 남음"), "Below one hour uses minutes");
                Check(Widget.FormatRemainingTime(new QuotaWindow { DurationMinutes = 10080, ResetsAt = expiryNow.AddHours(25).ToUnixTimeSeconds() }, expiryNow) == Ui.Text("1일 남음"), "Weekly countdown uses days above 24 hours");
                Check(Widget.FormatRemainingTime(new QuotaWindow { DurationMinutes = 10080, ResetsAt = expiryNow.AddHours(23).ToUnixTimeSeconds() }, expiryNow) == Ui.Text("23시간 남음"), "Weekly countdown uses hours below one day");
                Check(Widget.FormatRemainingTime(new QuotaWindow { DurationMinutes = 10080, ResetsAt = expiryNow.AddSeconds(30).ToUnixTimeSeconds() }, expiryNow) == Ui.Text("1분 남음"), "Positive sub-minute countdown does not show zero");
                Check(Widget.FormatRemainingTime(new QuotaWindow { ResetsAt = expiryNow.ToUnixTimeSeconds() }, expiryNow) == Ui.Text("재설정 대기"), "Elapsed reset does not invent renewed quota");
                Check(Widget.FormatRemainingTime(null, expiryNow) == "—" && Widget.FormatRemainingTime(new QuotaWindow { ResetsAt = long.MaxValue }, expiryNow) == "—", "Unavailable countdown remains explicit");
                Check(Widget.FormatResetTime(new QuotaWindow { DurationMinutes = 300, ResetsAt = expiryNow.ToUnixTimeSeconds() }) == "PM 12:00", "Five-hour quota shows AM/PM before time");
                Check(Widget.FormatResetTime(new QuotaWindow { DurationMinutes = 10080, ResetsAt = expiryNow.ToUnixTimeSeconds() }) == "26. 09. 30.", "Weekly quota shows short year, month and day");
                Check(Widget.FormatResetTime(new QuotaWindow { DurationMinutes = 10080, ResetsAt = expiryNow.AddDays(1).ToUnixTimeSeconds() }) == "26. 10. 1.", "Weekly day is not zero padded");
                Check(Widget.FormatResetTime(new QuotaWindow { ResetsAt = expiryNow.ToUnixTimeSeconds() }, true) == "2026/09/30 12:00 PM", "Tooltip includes full date and explicit PM");
                Check(Widget.FormatResetTime(new QuotaWindow { ResetsAt = expiryNow.AddHours(-12).ToUnixTimeSeconds() }, true) == "2026/09/30 12:00 AM", "Midnight tooltip is correctly labelled AM");
                Check(Widget.FormatResetTime(null) == "—" && Widget.FormatResetTime(new QuotaWindow { ResetsAt = long.MaxValue }) == "—", "Unknown or invalid reset time stays unavailable");
                var expiryData = new UsageSnapshot { ResetCredits = 2, ResetCreditExpirations = new System.Collections.Generic.List<long?> {
                    expiryNow.AddDays(7).AddHours(-4).ToUnixTimeSeconds(), expiryNow.AddDays(10).ToUnixTimeSeconds() } };
                var expiryDisplay = ExpiryDisplay.For(expiryData, expiryNow);
                Check(expiryDisplay.Text == "D-7" && expiryDisplay.Emphasize, "D-7 is emphasized using Korean calendar dates");
                expiryData.ResetCreditExpirations[0] = expiryNow.AddDays(8).ToUnixTimeSeconds();
                Check(!ExpiryDisplay.For(expiryData, expiryNow).Emphasize, "D-8 remains muted");
                expiryData.ResetCreditExpirations[0] = expiryNow.AddHours(1).ToUnixTimeSeconds();
                Check(ExpiryDisplay.For(expiryData, expiryNow).Text == Ui.Text("오늘 만료"), "Same-day expiry is explicit");
                expiryData.ResetCreditExpirations[0] = expiryNow.AddDays(-1).ToUnixTimeSeconds();
                Check(ExpiryDisplay.For(expiryData, expiryNow).Text == "D-10", "Expired entries do not replace upcoming expiry");
                expiryData.ResetCreditExpirations[1] = null;
                Check(ExpiryDisplay.For(expiryData, expiryNow).Text == Ui.Text("만료됨"), "Past expiry is never displayed as a negative countdown");
                expiryData.ResetCreditExpirations[0] = null;
                Check(ExpiryDisplay.For(expiryData, expiryNow) == null, "Unknown expiry hides the countdown");
                expiryData.ResetCredits = 0;
                expiryData.ResetCreditExpirations[0] = expiryNow.AddDays(1).ToUnixTimeSeconds();
                Check(ExpiryDisplay.For(expiryData, expiryNow) == null, "Zero available credits hides expiry even if detail records remain");
                var parsedExpiry = UsageParser.Parse(@"{""rateLimits"":{},""rateLimitResetCredits"":{""availableCount"":2,""credits"":[{""status"":""available"",""expiresAt"":1791360000},{""status"":""consumed"",""expiresAt"":1791000000},{""status"":""available"",""expiresAt"":null}]}}");
                Check(parsedExpiry.ResetCreditExpirations.Count == 2 && parsedExpiry.ResetCreditExpirations[0] == 1791360000 && parsedExpiry.ResetCreditExpirations[1] == null,
                    "Only available credits contribute expiry; missing timestamp stays unknown");
                var bucket = UsageParser.Parse(@"{""result"":{""rateLimits"":{""primary"":{""usedPercent"":90,""windowDurationMins"":300}},""rateLimitsByLimitId"":{""codex_spark"":{""primary"":{""usedPercent"":99,""windowDurationMins"":300}},""codex"":{""secondary"":{""usedPercent"":25,""windowDurationMins"":300},""primary"":{""usedPercent"":70,""windowDurationMins"":10080}}}}}");
                Check(bucket.FiveHour.Remaining == 75 && bucket.Weekly.Remaining == 30, "Shared Codex bucket wins; duration determines labels");
                Reject(@"{""rateLimitsByLimitId"":{""codex_spark"":{}},""rateLimits"":{}}", "Never substitute a model-specific bucket");
                var missing = UsageParser.Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":null,""windowDurationMins"":300},""secondary"":null}}");
                Check(missing.FiveHour == null && missing.Weekly == null, "Missing values stay unavailable, never zero or full");
                var otherDuration = UsageParser.Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":5,""windowDurationMins"":15}}}");
                Check(otherDuration.FiveHour == null, "A 15-minute window is not labelled five-hour");
                var clamped = UsageParser.Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":120,""windowDurationMins"":300},""secondary"":{""usedPercent"":-10,""windowDurationMins"":10080}}}");
                Check(clamped.FiveHour.Remaining == 0 && clamped.Weekly.Remaining == 100, "Remaining is clamped to 0..100");
                var decimalValue = UsageParser.Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":99.6,""windowDurationMins"":300}}}");
                Check(decimalValue.FiveHour.Remaining > 0 && decimalValue.FiveHour.PercentText == "0%", "Integer display never overstates remaining quota");
                Reject(@"{""rateLimits"":null}", "No quota response is explicit error");
                var location = Native.GetPlacement(new Rectangle(0, 1032, 1920, 48), new Rectangle(1700, 1032, 220, 48), 1, 0, false);
                Check(location == new Rectangle(1308, 1036, 388, 40), "Card fits taskbar and precedes notification area");
                var hidpi = Native.GetPlacement(new Rectangle(0, 1548, 2880, 72), new Rectangle(2550, 1548, 330, 72), 1.5f, 20, false);
                Check(hidpi == new Rectangle(1932, 1554, 582, 60), "150% DPI scales size, margin and offset");
                var above = Native.GetPlacement(new Rectangle(0, 1032, 1920, 48), Rectangle.Empty, 1, 0, true);
                Check(above.Bottom == 1028, "Above-taskbar mode stays above the bar");
                var negativeMonitor = Native.GetPlacement(new Rectangle(-1920, 1032, 1920, 48), new Rectangle(-220, 1032, 220, 48), 1, 0, false);
                Check(negativeMonitor.X == -612, "Negative monitor coordinates are preserved");
                Check(HoverDetails.GetPlacement(new Rectangle(1000, 1036, 384, 40), new Size(400, 280),
                    new Rectangle(0, 0, 1920, 1032), 6) == new Rectangle(984, 750, 400, 280), "Hover details appear above the widget");
                Check(HoverDetails.GetPlacement(new Rectangle(100, 10, 384, 40), new Size(400, 200),
                    new Rectangle(0, 0, 1920, 1080), 6).Top == 56, "Hover details fit below when there is no space above");
                var remoteArea=new Rectangle(-1024,0,1024,550);
                var remotePage=HistoryPage.GetPanelSize(remoteArea,1.5f);
                Check(remoteArea.Contains(HoverDetails.GetPlacement(new Rectangle(-420,550,384,40),remotePage,remoteArea,6)),"Integrated history fits a small remote screen with a negative monitor origin");
                var smallArea=new Rectangle(0,0,640,420);
                Check(smallArea.Contains(HoverDetails.GetPlacement(new Rectangle(100,420,384,40),HistoryPage.GetPanelSize(smallArea,2),smallArea,6)),"Integrated history remains inside a small high-DPI work area");
                var periodAnchor=new Rectangle(800,650,130,25);
                var listBounds=UpwardComboBox.GetUpwardBounds(periodAnchor,new Size(150,180),new Rectangle(0,0,1024,720));
                Check(listBounds.Bottom==periodAnchor.Top&&!listBounds.IntersectsWith(new Rectangle(0,720,1024,48)),"Period list opens above its selector and avoids the taskbar");
                var edgeList=UpwardComboBox.GetUpwardBounds(new Rectangle(-90,55,80,24),new Size(140,180),new Rectangle(-640,0,640,420));
                Check(new Rectangle(-640,0,640,420).Contains(edgeList)&&edgeList.Bottom==55,"Upward period list fits a small remote monitor near its top and right edges");
                using (var bitmap = new Bitmap(Widget.LogicalWidth, 40))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    Widget.PaintCard(graphics, 1, bitmap.Size, normal, false, false, false);
                    Check(bitmap.GetPixel(176, 10).B > bitmap.GetPixel(176, 10).G, "Remaining quota is visibly filled blue");
                    Widget.PaintCard(graphics, 1, bitmap.Size, missing, false, true, false);
                    Check(bitmap.GetPixel(176, 10).R > 200, "Unavailable quota draws an empty track");
                    Widget.PaintCard(graphics, 1, bitmap.Size, normal, true, false, false, 0);
                    using (var rotated = new Bitmap(Widget.LogicalWidth, 40))
                    using (var rotatedGraphics = Graphics.FromImage(rotated)) {
                        Widget.PaintCard(rotatedGraphics, 1, rotated.Size, normal, true, false, false, 45);
                        int changedPixels = 0;
                        for (int x = 8; x < WidgetLayout.Create(normal,false,false,new Settings(),DateTimeOffset.Now).First; x++) for (int y = 5; y < 21; y++)
                            if (bitmap.GetPixel(x, y) != rotated.GetPixel(x, y)) changedPixels++;
                        Check(changedPixels > 10, "Refresh arrow rotates between animation frames");
                        Widget.PaintCard(graphics, 1, bitmap.Size, normal, false, false, false, 0);
                        Widget.PaintCard(rotatedGraphics, 1, rotated.Size, normal, false, false, false, 45);
                        int idleChanges = 0;
                        for (int x = 8; x < WidgetLayout.Create(normal,false,false,new Settings(),DateTimeOffset.Now).First; x++) for (int y = 5; y < 21; y++)
                            if (bitmap.GetPixel(x, y) != rotated.GetPixel(x, y)) idleChanges++;
                        Check(idleChanges == 0, "Idle circle is stable and does not rotate");
                    }
                }
                using (var panel = new Bitmap(400, HoverDetails.ContentHeight(normal, null, false, false, false)))
                using (var panelGraphics = Graphics.FromImage(panel)) {
                    HoverDetails.PaintPanel(panelGraphics, 1, normal, null, false, false, false);
                    Check(panel.GetPixel(100, 121).B > panel.GetPixel(100, 121).G, "Hover card renders remaining quota as a blue bar");
                    HoverDetails.PaintPanel(panelGraphics, 1, missing, null, false, false, false);
                    Check(panel.GetPixel(100, 121).R > 200, "Hover card preserves an empty bar for unavailable quota");
                }
                using (var panel = new Bitmap(800, HoverDetails.ContentHeight(null, Ui.Text("로그인을 확인해 주세요."), false, false, false) * 2))
                using (var panelGraphics = Graphics.FromImage(panel)) {
                    HoverDetails.PaintPanel(panelGraphics, 2, null, Ui.Text("로그인을 확인해 주세요."), false, false, false);
                    Check(panel.GetPixel(400, 162).R > 200, "Hover panel renders at 200% DPI with no account data");
                }
                MonitoringChecks();
                Console.WriteLine("Passed " + count + " checks."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
    }
}
