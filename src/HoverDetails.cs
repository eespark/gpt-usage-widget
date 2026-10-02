using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace CodexUsageTaskbar
{
    public sealed class HoverDetails : Form
    {
        UsageSnapshot data;
        Settings preferences;
        UsageHistory history;
        string status;
        bool loading, stale, demo;
        float scale = 1;
        bool historyPage, pinned;
        HistoryPage page;
        Rectangle lastAnchor;
        OutsideClickDismissal outsideClick;
        public bool Pinned {get {return pinned;}}
        public bool IsHistoryPage {get {return historyPage;}}
        static readonly Color Orange = Color.FromArgb(208, 122, 20);
        public event Action RefreshRequested;
        public event Action HistoryRequested;
        public HoverDetails()
        {
            Text = Ui.Text("GPT 사용량 상세");
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true; BackColor = Color.White;
            outsideClick=new OutsideClickDismissal(this,delegate(Point point){return Bounds.Contains(point)||(page!=null&&page.ContainsPopupPoint(point));},delegate {Hide();});
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
        public static Rectangle GetPlacement(Rectangle anchor, Size size, Rectangle workArea, int gap)
        {
            int x = Math.Max(workArea.Left, Math.Min(anchor.Right - size.Width, workArea.Right - size.Width));
            int y = anchor.Top - size.Height - gap;
            if (y < workArea.Top) y = anchor.Bottom + gap;
            y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - size.Height));
            return new Rectangle(x, y, size.Width, size.Height);
        }
        static long?[] Expiries(UsageSnapshot snapshot)
        {
            return snapshot == null || snapshot.ResetCreditExpirations == null ? new long?[0] :
                snapshot.ResetCreditExpirations.OrderBy(x => x ?? long.MaxValue).Take(4).ToArray();
        }
        static bool Incomplete(UsageSnapshot snapshot)
        {
            return snapshot != null && snapshot.ResetCredits.GetValueOrDefault() > 0 &&
                (snapshot.ResetCreditExpirations == null || snapshot.ResetCreditExpirations.Count != snapshot.ResetCredits.Value ||
                 snapshot.ResetCreditExpirations.Count > 4 || snapshot.ResetCreditExpirations.Any(x => !x.HasValue));
        }
        public static int ContentHeight(UsageSnapshot snapshot, string message, bool isLoading, bool isStale, bool isDemo, bool includeHistory = false)
        {
            return 334 + Math.Max(1, Expiries(snapshot).Length) * 30 + (Incomplete(snapshot) ? 24 : 0) +
                (isStale || isDemo || (!isLoading && message != null) ? 44 : 0) + (includeHistory ? 114 : 0);
        }
        public void Present(UsageSnapshot snapshot, string message, bool isLoading, bool isStale, bool isDemo,
            string accessibleText, Rectangle anchor, float dpiScale, Settings options = null, UsageHistory store = null)
        {
            preferences = options; if(!historyPage)history = store; data = snapshot; status = message; loading = isLoading; stale = isStale; demo = isDemo; scale = dpiScale; lastAnchor=anchor;
            var area=Screen.FromRectangle(anchor).WorkingArea;
            Size size = historyPage ? HistoryPage.GetPanelSize(area,scale) : new Size((int)Math.Round(400 * scale), (int)Math.Round(ContentHeight(data, status, loading, stale, demo, history != null) * scale));
            Bounds = GetPlacement(anchor, size, area, (int)(6 * scale));
            if(historyPage&&page!=null) {page.Bounds=new Rectangle((int)(12*scale),(int)(12*scale),Width-(int)(24*scale),Height-(int)(24*scale));page.UpdateSnapshot(data);}
            using (var path = Rounded(new RectangleF(0, 0, Width, Height), 16 * scale))
            {
                Region old = Region; Region = new Region(path); if (old != null) old.Dispose();
            }
            AccessibleName = Ui.Text("GPT 사용량 상세"); AccessibleDescription = accessibleText;
            if (!Visible) Show();
            Invalidate();
        }
        public void ShowHistory(UsageHistory store,UsageSnapshot snapshot,Settings options,Rectangle anchor,float dpiScale)
        {
            if(page!=null){Controls.Remove(page);page.Dispose();}
            history=store;historyPage=true;pinned=true;Cursor=Cursors.Default;
            page=new HistoryPage(store,snapshot,options,dpiScale);
            page.BackRequested+=delegate {historyPage=false;Controls.Remove(page);page.Dispose();page=null;Present(data,status,loading,stale,demo,AccessibleDescription,lastAnchor,scale,preferences,preferences.HistoryEnabled?history:null);};
            Controls.Add(page);
            Present(snapshot,status,loading,stale,demo,AccessibleDescription,anchor,dpiScale,options,store);
            page.BringToFront();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if(outsideClick!=null){if(Visible)outsideClick.Start();else outsideClick.Stop();}
            if(!Visible){pinned=false;historyPage=false;if(page!=null){Controls.Remove(page);page.Dispose();page=null;}}
        }
        protected override void Dispose(bool disposing){if(disposing&&outsideClick!=null)outsideClick.Dispose();base.Dispose(disposing);}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if(historyPage) {
                var palette=Palette.For(preferences.DarkTheme);e.Graphics.Clear(palette.Background);
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                using(var pen=new Pen(palette.Border,scale))using(var path=Rounded(new RectangleF(.5f*scale,.5f*scale,Width-scale,Height-scale),16*scale))e.Graphics.DrawPath(pen,path);
                return;
            }
            PaintPanel(e.Graphics, scale, data, status, loading, stale, demo, preferences, history);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if(historyPage)return;
            if(e.Button==MouseButtons.Left&&history!=null&&ForecastBounds(data,status,loading,stale,demo,scale).Contains(e.Location)) {
                if(HistoryRequested!=null)HistoryRequested();return;
            }
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && RefreshRequested != null) RefreshRequested();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor=!historyPage&&history!=null&&ForecastBounds(data,status,loading,stale,demo,scale).Contains(e.Location)?Cursors.Hand:Cursors.Default;
        }
        public static Rectangle ForecastBounds(UsageSnapshot snapshot,string message,bool isLoading,bool isStale,bool isDemo,float dpiScale)
        {
            int y=ContentHeight(snapshot,message,isLoading,isStale,isDemo,false)-36;
            return Rectangle.FromLTRB((int)Math.Round(22*dpiScale),(int)Math.Round(y*dpiScale),(int)Math.Round(378*dpiScale),(int)Math.Round((y+104)*dpiScale));
        }
        public static void PaintPanel(Graphics g, float s, UsageSnapshot snapshot, string message, bool isLoading, bool isStale, bool isDemo, Settings options = null, UsageHistory store = null)
        {
            var p = Palette.For(options != null && options.DarkTheme);
            int height = ContentHeight(snapshot, message, isLoading, isStale, isDemo, store != null);
            var state = g.Save(); g.Clear(p.Background);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var pen = new Pen(p.Border, s))
            using (var path = Rounded(new RectangleF(.5f * s, .5f * s, 399 * s, (height - 1) * s), 16 * s)) g.DrawPath(pen, path);
            Ui.DrawTitle(g, s, 22, p.Ink, p.Background, new Rectangle(22,15,230,32), 1);
            TextAt(g, s, Ui.Text("남은 사용량"), 12, FontStyle.Regular, p.Muted, p.Background, 22, 48, 200, 20);
            string badge = isDemo ? Ui.Text("데모") : isLoading ? Ui.Text("갱신 중") : isStale || message != null ? Ui.Text("이전 조회") : Ui.Text("최신 상태");
            Pill(g, s, p, badge, 292, 23, 86, isStale || (!isLoading && message != null) ? Orange : Widget.Accent);
            QuotaCard(g, s, p, Ui.Text("5시간"), snapshot == null ? null : snapshot.FiveHour, 80);
            QuotaCard(g, s, p, Ui.Text("주간"), snapshot == null ? null : snapshot.Weekly, 169);
            int y = 270;
            TextAt(g, s, Ui.Text("초기화"), 13, FontStyle.Bold, p.Ink, p.Background, 22, y, 180, 22);
            string count = snapshot != null && snapshot.ResetCredits.HasValue ? snapshot.ResetCredits.Value + Ui.Text("개") : Ui.Text("미제공");
            TextAt(g, s, count, 14, FontStyle.Bold, Widget.Accent, p.Background, 280, y, 98, 22, true);
            y += 28;
            var expiries = Expiries(snapshot);
            if (expiries.Length == 0)
            {
                TextAt(g, s, snapshot != null && snapshot.ResetCredits == 0 ? Ui.Text("사용 가능한 초기화가 없습니다.") : Ui.Text("만료 정보가 제공되지 않았습니다."),
                    12, FontStyle.Regular, p.Muted, p.Background, 22, y, 356, 24);
                y += 30;
            }
            else foreach (var expiry in expiries)
            {
                string date = Ui.Text("만료 시각 미제공");
                ExpiryDisplay display = null;
                if (expiry.HasValue)
                {
                    try {
                        date = ExpiryDisplay.InKorea(DateTimeOffset.FromUnixTimeSeconds(expiry.Value)).ToString("yyyy/MM/dd hh:mm tt", System.Globalization.CultureInfo.InvariantCulture);
                        display = ExpiryDisplay.For(new UsageSnapshot { ResetCredits = 1,
                            ResetCreditExpirations = new System.Collections.Generic.List<long?> { expiry } }, DateTimeOffset.Now);
                    } catch (ArgumentOutOfRangeException) { }
                }
                using (var brush = new SolidBrush(Color.FromArgb(205, 214, 234))) g.FillEllipse(brush, 24 * s, (y + 10) * s, 5 * s, 5 * s);
                TextAt(g, s, date, 12, FontStyle.Regular, p.Ink, p.Background, 38, y, 246, 25);
                if (display != null) Pill(g, s, p, display.Text, 292, y, 86, display.Emphasize ? Orange : Widget.Accent);
                y += 30;
            }
            if (Incomplete(snapshot))
            {
                string note = snapshot.ResetCreditExpirations != null && snapshot.ResetCreditExpirations.Count > 4 ? Ui.Text("가장 가까운 만료일 4개를 표시합니다.") : Ui.Text("일부 초기화의 만료 정보만 제공되었습니다.");
                TextAt(g, s, note, 11, FontStyle.Regular, p.Muted, p.Background, 22, y, 356, 22); y += 24;
            }
            if (isStale || isDemo || (!isLoading && message != null))
            {
                string note = isDemo ? Ui.Text("데모 · 실제 계정 사용량이 아닙니다.") : isStale ? Ui.Text("마지막 조회 값입니다. 다시 조회해 주세요.") : message;
                FillRound(g, s, new RectangleF(22, y, 356, 36), Color.FromArgb(255, 247, 235), 8);
                TextAt(g, s, note, 11, FontStyle.Regular, Orange, Color.FromArgb(255, 247, 235), 32, y + 5, 336, 26); y += 44;
            }
            if (store != null) {
                FillRound(g,s,new RectangleF(22,y,356,104),p.Card,12);
                TextAt(g,s,Ui.Text("소진 예측"),12,FontStyle.Bold,p.Ink,p.Card,34,y+5,180,22);
                TextAt(g,s,Ui.Text("사용 추이 ›"),10,FontStyle.Regular,Widget.Accent,p.Card,244,y+5,120,22,true);
                TextAt(g,s,Ui.Text("5시간  ") + store.Forecast(false,snapshot),11,FontStyle.Bold,p.Ink,p.Card,34,y+28,330,20);
                TextAt(g,s,Ui.Text("주간  ")+store.Forecast(true,snapshot),11,FontStyle.Bold,p.Ink,p.Card,34,y+49,330,20);
                var rect=new RectangleF(34*s,(y+76)*s,332*s,18*s);
                DateTimeOffset end=DateTimeOffset.Now;
                HistoryCanvas.DrawSeries(g,rect,store,end.AddHours(-24),end,false,Widget.Accent,s);
                HistoryCanvas.DrawSeries(g,rect,store,end.AddHours(-24),end,true,Color.FromArgb(142,100,230),s);
                y+=114;
            }
            using (var pen = new Pen(p.Divider, s)) g.DrawLine(pen, 22 * s, y * s, 378 * s, y * s);
            string fetched = snapshot == null ? Ui.Text("조회 대기 중") : Ui.Text("조회 ") + ExpiryDisplay.InKorea(snapshot.FetchedAt).ToString("HH:mm:ss");
            TextAt(g, s, fetched, 10, FontStyle.Regular, p.Muted, p.Background, 22, y + 5, 150, 24);
            TextAt(g, s, Ui.Text("한국 시간 · 클릭하여 새로고침"), 10, FontStyle.Regular, p.Muted, p.Background, 168, y + 5, 210, 24, true);
            g.Restore(state);
        }
        static void QuotaCard(Graphics g, float s, Palette p, string label, QuotaWindow quota, int y)
        {
            FillRound(g, s, new RectangleF(22, y, 356, 79), p.Card, 12);
            TextAt(g, s, label, 13, FontStyle.Bold, p.Ink, p.Card, 36, y + 8, 180, 27);
            TextAt(g, s, quota == null ? "—" : quota.PercentText, 24, FontStyle.Bold, Widget.Accent, p.Card, 266, y + 2, 98, 34, true);
            FillRound(g, s, new RectangleF(36, y + 38, 328, 6), p.Track, 3);
            if (quota != null && quota.Remaining > 0)
            {
                float width = (float)(328 * Math.Max(0, Math.Min(100, quota.Remaining)) / 100);
                var clip = g.Save();
                using (var path = Rounded(new RectangleF(36 * s, (y + 38) * s, 328 * s, 6 * s), 3 * s)) g.SetClip(path);
                using (var brush = new LinearGradientBrush(new RectangleF(36 * s, (y + 38) * s, 328 * s, 6 * s), Widget.Accent, Color.FromArgb(111, 157, 255), 0f))
                    g.FillRectangle(brush, 36 * s, (y + 38) * s, width * s, 6 * s);
                g.Restore(clip);
            }
            TextAt(g, s, Ui.Text("초기화  ") + Widget.FormatResetTime(quota, true), 11, FontStyle.Regular, p.Muted, p.Card, 36, y + 49, 328, 22);
        }
        static void Pill(Graphics g, float s, Palette p, string text, int x, int y, int width, Color color)
        {
            Color background = color == Orange ? Color.FromArgb(255, 243, 225) : Color.FromArgb(235, 241, 255);
            FillRound(g, s, new RectangleF(x, y, width, 24), background, 12);
            TextAt(g, s, text, 11, FontStyle.Regular, color, background, x + 8, y, width - 16, 24, false, true);
        }
        static void TextAt(Graphics g, float s, string text, float size, FontStyle style, Color color, Color background,
            int x, int y, int width, int height, bool right = false, bool center = false)
        {
            using (var font = new Font("Malgun Gothic", size * s, style, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, text, font, Rectangle.FromLTRB((int)Math.Round(x * s), (int)Math.Round(y * s),
                    (int)Math.Round((x + width) * s), (int)Math.Round((y + height) * s)), color, background,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | (right ? TextFormatFlags.Right : center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
        }
        static void FillRound(Graphics g, float s, RectangleF rect, Color color, float radius)
        {
            using (var path = Rounded(new RectangleF(rect.X * s, rect.Y * s, rect.Width * s, rect.Height * s), radius * s))
            using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
        }
        static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            var path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }
}
