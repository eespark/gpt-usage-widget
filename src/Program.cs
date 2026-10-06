using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace CodexUsageTaskbar
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Native.EnableDpi();
            if (Ui.English) {
                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            }
            if (args.Contains("--diagnose"))
            {
                IntPtr bar = Native.FindWindow("Shell_TrayWnd", null);
                IntPtr card = Native.FindWindow(null, "GPT Usage Widget");
                IntPtr notification = Native.FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
                Native.Rect barRect, cardRect, notificationRect;
                Native.GetWindowRect(bar, out barRect);
                Native.GetWindowRect(card, out cardRect);
                Native.GetWindowRect(notification, out notificationRect);
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layout-result.json"), new JavaScriptSerializer().Serialize(new {
                    taskbarFound = bar != IntPtr.Zero, taskbarVisible = Native.IsWindowVisible(bar), taskbar = barRect.Rectangle,
                    cardFound = card != IntPtr.Zero, cardVisible = Native.IsWindowVisible(card), card = cardRect.Rectangle,
                    notification = notificationRect.Rectangle, dpiScale = Native.Dpi(bar), fullscreen = Native.IsFullscreen(card, bar)
                }));
                return 0;
            }
            if (args.Contains("--probe"))
            {
                try {
                    using (var client = new UsageClient()) {
                        UsageSnapshot usage = client.FetchAsync().GetAwaiter().GetResult();
                        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe-result.json");
                        File.WriteAllText(path, new JavaScriptSerializer().Serialize(usage));
                        return 0;
                    }
                } catch (Exception ex) {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe-result.json"), new JavaScriptSerializer().Serialize(new { error = ex.Message })); return 1;
                }
            }
            if (args.Contains("--render-demo"))
            {
                var usage = new UsageSnapshot { ResetCredits = 2, FetchedAt = DateTimeOffset.Now,
                    ResetCreditExpirations = new System.Collections.Generic.List<long?> { DateTimeOffset.Now.AddDays(7).ToUnixTimeSeconds() },
                    FiveHour = new QuotaWindow { Remaining = 60, DurationMinutes = 300, ResetsAt = DateTimeOffset.Now.AddHours(3).ToUnixTimeSeconds() },
                    Weekly = new QuotaWindow { Remaining = 80, DurationMinutes = 10080, ResetsAt = DateTimeOffset.Now.AddDays(5).ToUnixTimeSeconds() } };
                using (var bitmap = new Bitmap(Widget.LogicalWidth * 3, 120))
                using (var graphics = Graphics.FromImage(bitmap)) {
                    Widget.PaintCard(graphics, 3, bitmap.Size, usage, false, false, true);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview.png"), ImageFormat.Png);
                }
                using (var bitmap = new Bitmap(Widget.LogicalWidth, 40))
                using (var graphics = Graphics.FromImage(bitmap)) {
                    Widget.PaintCard(graphics, 1, bitmap.Size, usage, false, false, true);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-100.png"), ImageFormat.Png);
                }
                using (var bitmap = new Bitmap(800, HoverDetails.ContentHeight(usage, null, false, false, true) * 2))
                using (var graphics = Graphics.FromImage(bitmap)) {
                    HoverDetails.PaintPanel(graphics, 2, usage, null, false, false, true);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-details.png"), ImageFormat.Png);
                }
                Application.EnableVisualStyles();
                var history = new UsageHistory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unused-demo-history.json"));
                var demoToday = new DateTimeOffset(ExpiryDisplay.InKorea(usage.FetchedAt).Date, TimeSpan.FromHours(9));
                for (int day = 4; day >= 1; day--) for (int i = 0; i <= 96; i++) {
                    var start = demoToday.AddDays(-day).AddHours(9);
                    history.Samples.Add(new HistorySample {At=start.AddMinutes(i*5).ToUnixTimeSeconds(),
                        Five=95-(i%48)*.2, Week=90-i*.04,
                        FiveReset=start.AddHours(i<48?4:8).AddMinutes(5).ToUnixTimeSeconds(), WeekReset=usage.Weekly.ResetsAt});
                }
                for (int i = 0; i <= 60; i++) history.Samples.Add(new HistorySample {
                    At = usage.FetchedAt.AddMinutes(i - 60).ToUnixTimeSeconds(), Five = 80 - i / 3.0, Week = 90 - i / 6.0,
                    FiveReset = usage.FiveHour.ResetsAt, WeekReset = usage.Weekly.ResetsAt });
                var options = new Settings { DarkTheme = true };
                var layout = WidgetLayout.Create(usage, false, false, options, usage.FetchedAt);
                using (var bitmap = new Bitmap(layout.Width * 2, 80))
                using (var graphics = Graphics.FromImage(bitmap)) {
                    Widget.PaintCard(graphics, 2, bitmap.Size, usage, false, false, false, 0, options);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-dark.png"), ImageFormat.Png);
                }
                using (var bitmap = new Bitmap(800, HoverDetails.ContentHeight(usage, null, false, false, true, true) * 2))
                using (var graphics = Graphics.FromImage(bitmap)) {
                    HoverDetails.PaintPanel(graphics, 2, usage, null, false, false, true, options, history);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-details-dark.png"), ImageFormat.Png);
                }
                var lightOptions=new Settings();
                var lightLayout=WidgetLayout.Create(usage,false,false,lightOptions,usage.FetchedAt);
                using(var bitmap=new Bitmap(lightLayout.Width*2,80))using(var graphics=Graphics.FromImage(bitmap)) {
                    Widget.PaintCard(graphics,2,bitmap.Size,usage,false,false,false,0,lightOptions);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-widget-light.png"),ImageFormat.Png);
                }
                using(var bitmap=new Bitmap(800,HoverDetails.ContentHeight(usage,null,false,false,true,true)*2))using(var graphics=Graphics.FromImage(bitmap)) {
                    HoverDetails.PaintPanel(graphics,2,usage,null,false,false,true,lightOptions,history);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-details-light.png"),ImageFormat.Png);
                }
                using (var chart = new HistoryCanvas(history, usage, options) { Size = new Size(720, 620) })
                using (var bitmap = new Bitmap(chart.Width, chart.Height)) {
                    chart.DrawToBitmap(bitmap, chart.ClientRectangle);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-history.png"), ImageFormat.Png);
                }
                using (var form = new SettingsDialog(new Settings(), delegate { }, delegate { }))
                using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                using (var graphics = Graphics.FromImage(bitmap)) {
                    form.PerformLayout();
                    foreach(Control control in form.Controls)RenderControls(control, graphics, control.Location);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-settings.png"), ImageFormat.Png);
                    var settingsScroll=(Panel)form.Controls.Find("SettingsScroll",true)[0];
                    settingsScroll.AutoScrollMinSize=new Size(0,settingsScroll.Controls[0].Height+16);
                    settingsScroll.AutoScrollPosition=new Point(0,settingsScroll.AutoScrollMinSize.Height);
                    // 표시하지 않은 폼에서는 네이티브 스크롤 위치가 적용되지 않아 미리보기만 끝 위치로 배치합니다.
                    var settingsStack=settingsScroll.Controls[0];
                    settingsStack.Dock=DockStyle.None;
                    settingsStack.Top=-Math.Max(0,settingsStack.Height-settingsScroll.ClientSize.Height+16);
                    graphics.Clear(form.BackColor);
                    foreach(Control control in form.Controls)RenderControls(control,graphics,control.Location);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-settings-bottom.png"),ImageFormat.Png);
                }
                foreach(bool dark in new[]{true,false})
                using(var page=new HistoryPage(history,usage,new Settings {DarkTheme=dark},1) {Size=new Size(736,676)})
                using(var bitmap=new Bitmap(page.Width,page.Height))using(var graphics=Graphics.FromImage(bitmap)) {
                    page.PerformLayout();RenderControls(page,graphics,Point.Empty);
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,dark?"preview-history-page.png":"preview-history-page-light.png"),ImageFormat.Png);
                }
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\CodexUsageTaskbar", out created))
            {
                if (!created) return 0;
                Directory.CreateDirectory(LocalData.DirectoryPath);
                FileStream lease;
                try {lease=new FileStream(Path.Combine(LocalData.DirectoryPath,"instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                catch(IOException){return 0;}
                using(lease) {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Widget(args.Contains("--demo")));
                }
            }
            return 0;
        }
        static void RenderControls(Control control, Graphics graphics, Point offset)
        {
            control.PerformLayout();
            if (control.Width <= 0 || control.Height <= 0) return;
            var clip=graphics.Save();
            graphics.SetClip(new Rectangle(offset,control.Size),System.Drawing.Drawing2D.CombineMode.Intersect);
            using (var bitmap = new Bitmap(control.Width, control.Height)) {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
                graphics.DrawImageUnscaled(bitmap, offset);
            }
            var combo = control as ComboBox;
            if (combo != null && combo.SelectedItem != null)
                TextRenderer.DrawText(graphics, combo.SelectedItem.ToString(), combo.Font,
                    new Rectangle(offset.X + 5, offset.Y + 2, control.Width - 25, control.Height - 4), combo.ForeColor, combo.BackColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            foreach (Control child in control.Controls) RenderControls(child, graphics, new Point(offset.X + child.Left, offset.Y + child.Top));
            graphics.Restore(clip);
        }
    }
}
