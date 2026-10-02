using System;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using System.Windows.Forms;

namespace CodexUsageTaskbar
{
    public sealed class Settings
    {
        public int HorizontalOffset { get; set; }
        public bool AboveTaskbar { get; set; }
        public bool DarkTheme { get; set; }
        public int FontSize { get; set; }
        public int BarWidth { get; set; }
        public int BarThickness { get; set; }
        public bool ShowReset { get; set; }
        public bool ShowUsage { get; set; }
        public bool ShowRemaining { get; set; }
        public bool LowQuotaAlert { get; set; }
        public bool FiveHourResetAlert { get; set; }
        public bool WeeklyResetAlert { get; set; }
        public bool FiveHourLowAlert { get; set; }
        public bool WeeklyLowAlert { get; set; }
        public int FiveHourLowPercent { get; set; }
        public int WeeklyLowPercent { get; set; }
        public int FiveHourResetMinutes { get; set; }
        public int WeeklyResetMinutes { get; set; }
        public int WeeklyResetMode { get; set; }
        public int WeeklyReminderDays { get; set; }
        public int WeeklyReminderHour { get; set; }
        public int WeeklyReminderMinute { get; set; }
        public bool EfficientPolling { get; set; }
        public bool HistoryEnabled { get; set; }
        public int HistoryDays { get; set; }
        public int ForecastDays { get; set; }
        public Settings()
        {
            FontSize = 12; BarWidth = 74; BarThickness = 5;
            HistoryDays = 90; ForecastDays = 7;
            FiveHourLowAlert=WeeklyLowAlert=true; FiveHourLowPercent=WeeklyLowPercent=20;
            FiveHourResetMinutes=60; WeeklyResetMinutes=1440;
            WeeklyReminderDays=1; WeeklyReminderHour=12; WeeklyReminderMinute=30;
            ShowReset = ShowUsage = ShowRemaining = true;
            LowQuotaAlert = FiveHourResetAlert = WeeklyResetAlert = EfficientPolling = HistoryEnabled = true;
        }
        public static string SettingsPath { get { return Path.Combine(LocalData.DirectoryPath, "settings.json"); } }
        public void Normalize()
        {
            HorizontalOffset = Math.Max(0, Math.Min(3000, HorizontalOffset));
            FontSize = Math.Max(11, Math.Min(14, FontSize));
            BarWidth = Math.Max(50, Math.Min(110, BarWidth));
            BarThickness = Math.Max(3, Math.Min(7, BarThickness));
            FiveHourLowPercent=Math.Max(1,Math.Min(100,FiveHourLowPercent));
            WeeklyLowPercent=Math.Max(1,Math.Min(100,WeeklyLowPercent));
            FiveHourResetMinutes=Math.Max(1,Math.Min(300,FiveHourResetMinutes));
            WeeklyResetMinutes=Math.Max(1,Math.Min(10080,WeeklyResetMinutes));
            WeeklyResetMode=WeeklyResetMode==1?1:0;
            WeeklyReminderDays=Math.Max(0,Math.Min(7,WeeklyReminderDays));
            WeeklyReminderHour=Math.Max(0,Math.Min(23,WeeklyReminderHour));
            WeeklyReminderMinute=Math.Max(0,Math.Min(59,WeeklyReminderMinute));
            if (HistoryDays != 30 && HistoryDays != 90 && HistoryDays != 180 && HistoryDays != 365) HistoryDays = 90;
            if (ForecastDays != 1 && ForecastDays != 3 && ForecastDays != 7 && ForecastDays != 14) ForecastDays = 7;
            if (!ShowReset && !ShowUsage && !ShowRemaining) ShowUsage = true;
        }
        public static Settings Load()
        {
            var settings = LocalData.Read<Settings>(SettingsPath) ??
                LocalData.Read<Settings>(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json")) ?? new Settings();
            settings.Normalize(); return settings;
        }
        public void Save()
        {
            Normalize();
            if (!LocalData.Write(SettingsPath, this)) MessageBox.Show(Ui.Text("설정을 저장할 수 없습니다. 사용자 폴더의 접근 권한을 확인해 주세요."), "GPT");
        }
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "CodexUsageTaskbar";
        public static bool StartupEnabled
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && key.GetValue(RunName) != null; }
        }
        public static void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue(RunName, false);
            }
        }
    }
}
