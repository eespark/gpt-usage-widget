using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexUsageTaskbar
{
    public sealed class UsageForecast
    {
        public double? ActivePercentPerHour, ExpectedConsumption, ValidationMae;
        public double HorizonHours, ObservedHours, DailyObservedHours;
        public int ObservedDays, ActiveBins, ValidationOrigins;
        public long LastActiveAt;
        public string Model;
    }
    internal sealed class UsageBin
    {
        public long At, Cycle;
        public double Amount, Covered;
        public bool Invalid;
    }
    // TSB의 발생 확률/사용 강도 분리 원리를 적용합니다. 누락된 관측은 0으로 학습하지 않습니다.
    internal static class IntermittentForecast
    {
        internal const int BinSeconds = 900;
        const double Positive = .000001;
        internal static List<UsageBin> Bins(IEnumerable<HistorySample> samples, bool weekly, long latest, int days)
        {
            long cutoff = latest - days * 86400L;
            var points = samples.Where(x => x.At >= cutoff && x.At <= latest).OrderBy(x => x.At)
                .GroupBy(x => x.At).Select(x => x.Last()).ToArray();
            var buckets = new Dictionary<long, UsageBin>();
            long cycle=0;
            for (int i = 1; i < points.Length; i++) {
                var a = points[i - 1]; var b = points[i];
                double? av = weekly ? a.Week : a.Five, bv = weekly ? b.Week : b.Five;
                long? ar = weekly ? a.WeekReset : a.FiveReset, br = weekly ? b.WeekReset : b.FiveReset;
                long elapsed = b.At - a.At;
                if(HistoryIntervals.StartsNewCycle(a,b,weekly))cycle++;
                if (elapsed<=0 || elapsed > 600 || !av.HasValue || !bv.HasValue || !ar.HasValue || !br.HasValue) continue;
                bool valid = HistoryIntervals.Observed(a,b,weekly) && av > Positive && a.At < Math.Max(ar.Value,br.Value) && b.At <= Math.Max(ar.Value,br.Value);
                for (long cursor = a.At; cursor < b.At;) {
                    long at = cursor / BinSeconds * BinSeconds;
                    long end = Math.Min(b.At, at + BinSeconds);
                    UsageBin bucket;
                    if (!buckets.TryGetValue(at, out bucket)) buckets[at] = bucket = new UsageBin { At = at, Cycle = cycle };
                    if (!valid || bucket.Cycle != cycle) bucket.Invalid = true;
                    else {
                        bucket.Covered += end - cursor;
                        bucket.Amount += (av.Value - bv.Value) * (end - cursor) / elapsed;
                    }
                    cursor = end;
                }
            }
            // 진행 중인 구간, 재설정 구간, 관측률 90% 미만 구간은 제외합니다.
            return buckets.Values.Where(x => !x.Invalid && x.At + BinSeconds <= latest && x.Covered >= BinSeconds * .9)
                .OrderBy(x => x.At).Select(x => new UsageBin { At = x.At, Cycle = x.Cycle,
                    Amount = x.Amount * BinSeconds / x.Covered, Covered = x.Covered }).ToList();
        }
        sealed class Candidate
        {
            public string Name;
            public int Kind;
            public double Alpha, Beta;
        }
        sealed class Fit
        {
            public double Probability, Size, Mean, Recent;
            public long Origin;
            public double HalfLife;
            public double[] PeriodTotal = new double[16], PeriodWeights = new double[16];
            public HashSet<long>[] PeriodDays = new HashSet<long>[16];
        }
        static readonly Candidate[] candidates = {
            new Candidate {Name="TSB", Alpha=.1, Beta=.02},
            new Candidate {Name="TSB", Alpha=.1, Beta=.05},
            new Candidate {Name="TSB", Alpha=.3, Beta=.02},
            new Candidate {Name="TSB", Alpha=.3, Beta=.1},
            new Candidate {Name="Mean", Kind=1},
            new Candidate {Name="Recent", Kind=2},
            new Candidate {Name="TimeOfDay", Kind=3, Alpha=.1, Beta=.02}
        };
        static Fit Train(List<UsageBin> points, int count, Candidate candidate, int days)
        {
            var fit = new Fit { Origin = points[count - 1].At + BinSeconds,
                HalfLife = Math.Max(1, days / 3.0) * 86400 };
            int initial = Math.Min(16, count);
            var first = points.Take(initial).ToArray();
            var active = first.Where(x => x.Amount > Positive).ToArray();
            fit.Probability = active.Length / (double)initial;
            fit.Size = active.Length > 0 ? active.Average(x => x.Amount) : 0;
            double sum = 0, weights = 0;
            for (int i = 0; i < count; i++) {
                var bin = points[i];
                double weight = Math.Pow(.5, (fit.Origin - bin.At) / fit.HalfLife);
                sum += bin.Amount * weight; weights += weight;
                if (candidate.Kind == 3) {
                    int period = TimeGroup(bin.At);
                    fit.PeriodTotal[period] += bin.Amount * weight; fit.PeriodWeights[period] += weight;
                    if (fit.PeriodDays[period] == null) fit.PeriodDays[period] = new HashSet<long>();
                    fit.PeriodDays[period].Add((bin.At + 32400) / 86400);
                }
                if (i < initial) continue;
                fit.Probability += candidate.Beta * ((bin.Amount > Positive ? 1 : 0) - fit.Probability);
                if (bin.Amount > Positive) {
                    if (fit.Size <= Positive) fit.Size = bin.Amount;
                    else fit.Size += candidate.Alpha * (bin.Amount - fit.Size);
                }
            }
            fit.Mean = sum / weights;
            fit.Recent = points.Take(count).Where(x => x.At >= fit.Origin - 3600).Average(x => x.Amount);
            return fit;
        }
        static int TimeGroup(long at)
        {
            var local = ExpiryDisplay.InKorea(DateTimeOffset.FromUnixTimeSeconds(at));
            bool weekend = local.DayOfWeek == DayOfWeek.Saturday || local.DayOfWeek == DayOfWeek.Sunday;
            return local.Hour / 3 + (weekend ? 8 : 0);
        }
        static double PredictBin(Fit fit, Candidate candidate, long at)
        {
            if (candidate.Kind == 1) return fit.Mean;
            if (candidate.Kind == 2) return fit.Recent;
            double ordinary = fit.Probability * fit.Size;
            if (candidate.Kind != 3) return ordinary;
            int period = TimeGroup(at);
            if (fit.PeriodDays[period] == null || fit.PeriodDays[period].Count < 3) return ordinary;
            // 적은 시간대 관측이 전체를 덮어쓰지 않도록 8구간 상당의 전체 예측으로 축약합니다.
            return (fit.PeriodTotal[period] + 8 * ordinary) / (fit.PeriodWeights[period] + 8);
        }
        static double Project(Fit fit, Candidate candidate, long start, double hours)
        {
            double seconds = hours * 3600, total = 0;
            for (long at = start; seconds > 0; at += BinSeconds) {
                double span = Math.Min(seconds, BinSeconds);
                total += PredictBin(fit, candidate, at) * span / BinSeconds;
                seconds -= span;
            }
            return total;
        }
        static Candidate Select(List<UsageBin> points, int days, UsageForecast result)
        {
            var errors = new double[candidates.Length]; double weights = 0; int origins = 0;
            // 각 시점 이전 기록만 학습하고 1시간/5시간 후 소비량을 비교합니다.
            for (int origin = Math.Max(16, points.Count - 512); origin + 4 <= points.Count; origin += 4) {
                double weight = Math.Pow(.5, (points[points.Count - 1].At - points[origin - 1].At) / (Math.Max(1, days / 3.0) * 86400));
                bool evaluated = false;
                Fit[] fits = null;
                foreach (int horizon in new[] {4, 20}) {
                    if (origin + horizon > points.Count) continue;
                    bool complete = true; double actual = 0;
                    for (int i = origin; i < origin + horizon; i++) {
                        if (points[i].At != points[origin - 1].At + (i - origin + 1) * BinSeconds || points[i].Cycle != points[origin - 1].Cycle) { complete = false; break; }
                        actual += points[i].Amount;
                    }
                    if (!complete) continue;
                    if (fits == null) fits = candidates.Select(x => Train(points, origin, x, days)).ToArray();
                    for (int c = 0; c < candidates.Length; c++) {
                        double predicted = Project(fits[c], candidates[c], points[origin].At, horizon / 4.0);
                        errors[c] += Math.Abs(predicted - actual) / (horizon / 4.0) * weight;
                    }
                    weights += weight; evaluated = true;
                }
                if (evaluated) origins++;
            }
            result.ValidationOrigins = origins;
            if (origins < 12 || weights == 0) return candidates[0];
            int best = 0;
            bool mostlyContinuous = points.Count(x => x.Amount > Positive) >= points.Count * .8;
            for (int c = 1; c < candidates.Length; c++) {
                // 최근 속도는 비교 기준이며, 간헐적 사용자에게는 단독 예측으로 선택하지 않습니다.
                if (candidates[c].Kind == 2 && !mostlyContinuous) continue;
                if (errors[c] < errors[best] - .000001) best = c;
            }
            result.ValidationMae = errors[best] / weights;
            return candidates[best];
        }
        internal static UsageForecast Estimate(IEnumerable<HistorySample> samples, bool weekly, UsageSnapshot current, int days)
        {
            var result = new UsageForecast();
            var quota = current == null ? null : weekly ? current.Weekly : current.FiveHour;
            if (quota == null || !quota.ResetsAt.HasValue) return result;
            long latest = current.FetchedAt.ToUnixTimeSeconds();
            result.HorizonHours = Math.Max(0, (quota.ResetsAt.Value - latest) / 3600.0);
            if (result.HorizonHours <= 0) return result;
            var bins = Bins(samples, weekly, latest, days);
            result.ObservedHours = bins.Sum(x => x.Covered) / 3600;
            result.ObservedDays = bins.Select(x => (x.At + 32400) / 86400).Distinct().Count();
            var active = bins.Where(x => x.Amount > Positive).ToList();
            result.ActiveBins = active.Count;
            if (active.Count > 0) result.LastActiveAt = active[active.Count - 1].At + BinSeconds;
            if (bins.Count < 4) return result;
            Candidate chosen = bins.Count >= 16 ? Select(bins, days, result) : candidates[0];
            result.Model = chosen.Name;
            // 사용 강도는 양의 소비가 있는 구간에서만 갱신합니다. 짧은 휴식은 사용 가능 시간을 부풀리지 않습니다.
            if (active.Count >= 3) {
                double size = active.Take(3).Average(x => x.Amount);
                foreach (var bin in active.Skip(3)) size += .1 * (bin.Amount - size);
                result.ActivePercentPerHour = size * 4;
            }
            if (!weekly && bins.Count >= 16) {
                var fit = Train(bins, bins.Count, chosen, days);
                result.ExpectedConsumption = Project(fit, chosen, latest, Math.Min(5, result.HorizonHours));
                result.HorizonHours = Math.Min(5, result.HorizonHours);
            }
            if (weekly) WeeklyProjection(bins, latest, days, result);
            return result;
        }
        static void WeeklyProjection(List<UsageBin> bins, long latest, int days, UsageForecast result)
        {
            long today = (latest + 32400) / 86400;
            // 하루 4시간 이상 기록된 완료 날짜 3개가 있어야 장기 소비량을 추정합니다.
            var completed = bins.Where(x => (x.At + 32400) / 86400 < today)
                .GroupBy(x => (x.At + 32400) / 86400).Where(x => x.Sum(b => b.Covered) >= 4 * 3600).ToArray();
            if (completed.Length < 3) return;
            double weightedTotal = 0, weightedHours = 0, weights = 0;
            var profile = new double[96];
            foreach (var day in completed) {
                double weight = Math.Pow(.5, (today - 1 - day.Key) / Math.Max(1, days / 3.0));
                weightedTotal += day.Sum(x => x.Amount) * weight;
                weightedHours += day.Sum(x => x.Covered) / 3600 * weight; weights += weight;
                foreach (var bin in day) profile[(int)((bin.At + 32400) % 86400 / BinSeconds)] += bin.Amount * weight;
            }
            result.DailyObservedHours = weightedHours / weights;
            double mean = weightedTotal / weights, profileTotal = profile.Sum();
            if (profileTotal <= Positive) { result.ExpectedConsumption = 0; return; }
            // 과거 관측 소비의 시각 분포로 배분합니다. 시간당 속도를 24배 하지 않습니다.
            double seconds = Math.Min(7 * 86400, result.HorizonHours * 3600), expected = 0;
            for (long at = latest; seconds > 0;) {
                int slot = (int)((at + 32400) % 86400 / BinSeconds);
                double span = Math.Min(seconds, BinSeconds - at % BinSeconds);
                expected += mean * profile[slot] / profileTotal * span / BinSeconds;
                at += (long)span; seconds -= span;
            }
            result.HorizonHours = Math.Min(168, result.HorizonHours);
            result.ExpectedConsumption = expected;
        }
    }
}
