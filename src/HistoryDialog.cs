using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CodexUsageTaskbar
{
    public sealed class HistoryPage : UserControl
    {
        readonly HistoryCanvas canvas;
        readonly Panel scroll;
        readonly float scale;
        readonly Timer refresh=new Timer {Interval=60000};
        readonly ComboBox periods;
        public event Action BackRequested;
        public HistoryPage(UsageHistory history,UsageSnapshot snapshot,Settings settings,float dpiScale)
        {
            scale=dpiScale;AutoScaleMode=AutoScaleMode.None;
            var palette=Palette.For(settings.DarkTheme);BackColor=palette.Background;ForeColor=palette.Ink;
            Font=new Font("Malgun Gothic",12*scale,FontStyle.Regular,GraphicsUnit.Pixel);
            canvas=new HistoryCanvas(history,snapshot,settings,scale);
            scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=palette.Background};scroll.Controls.Add(canvas);Controls.Add(scroll);
            var toolbar=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=(int)(44*scale),WrapContents=false,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,(int)(6*scale),(int)(12*scale),0),BackColor=palette.Background};
            var back=new Button {Text=Ui.Text("‹ 뒤로"),Width=(int)(80*scale),Height=(int)(30*scale),FlatStyle=FlatStyle.Flat,BackColor=palette.Card,ForeColor=Widget.Accent,Cursor=Cursors.Hand};
            back.FlatAppearance.BorderColor=palette.Border;
            back.Click+=delegate {if(BackRequested!=null)BackRequested();};toolbar.Controls.Add(back);
            periods=new UpwardComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=(int)(130*scale),FlatStyle=FlatStyle.Flat,BackColor=palette.Card,ForeColor=palette.Ink};
            var days=new[]{1,7,30,90,180,365}.Where(x=>x<=settings.HistoryDays).ToArray();
            foreach(int day in days)periods.Items.Add(day==1?Ui.Text("최근 24시간"):Ui.Text("최근 ")+day+Ui.Text("일"));periods.SelectedIndex=0;
            periods.SelectedIndexChanged+=delegate {canvas.RangeDays=days[periods.SelectedIndex];canvas.Invalidate();};toolbar.Controls.Add(periods);
            Controls.Add(toolbar);Resize+=delegate {LayoutCanvas();};LayoutCanvas();
            AccessibleName=Ui.Text("사용량 상세 패널의 사용 추이 화면");
            refresh.Tick+=delegate {canvas.Invalidate();};refresh.Start();
        }
        void LayoutCanvas() {if(canvas!=null&&scroll!=null)canvas.Size=new Size(Math.Max((int)(720*scale),scroll.ClientSize.Width),Math.Max((int)(620*scale),scroll.ClientSize.Height));}
        public void UpdateSnapshot(UsageSnapshot snapshot){canvas.UpdateSnapshot(snapshot);canvas.Invalidate();}
        public bool ContainsPopupPoint(Point point)
        {
            if(!periods.DroppedDown||!periods.IsHandleCreated)return false;
            var info=new Native.ComboBoxInfo {Size=System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.ComboBoxInfo))};Native.Rect rect;
            return Native.GetComboBoxInfo(periods.Handle,ref info)&&Native.GetWindowRect(info.List,out rect)&&rect.Rectangle.Contains(point);
        }
        public static Size GetPanelSize(Rectangle area,float scale){return new Size(Math.Min((int)(760*scale),Math.Max(1,area.Width-12)),Math.Min((int)(700*scale),Math.Max(1,area.Height-12)));}
        protected override void Dispose(bool disposing){if(disposing)refresh.Dispose();base.Dispose(disposing);}
    }
    internal sealed class UpwardComboBox : ComboBox
    {
        protected override void OnDropDown(EventArgs e)
        {
            base.OnDropDown(e);
            // Native Windows places the list after this event; reposition afterward.
            BeginInvoke(new Action(delegate {
                if(IsDisposed||!IsHandleCreated||!DroppedDown)return;
                var info=new Native.ComboBoxInfo {Size=System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.ComboBoxInfo))};Native.Rect list;
                if(!Native.GetComboBoxInfo(Handle,ref info)||!Native.GetWindowRect(info.List,out list))return;
                var anchor=RectangleToScreen(ClientRectangle);var area=Screen.FromControl(this).WorkingArea;
                var bounds=GetUpwardBounds(anchor,list.Rectangle.Size,area);
                Native.SetWindowPos(info.List,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0010|0x0040);
            }));
        }
        internal static Rectangle GetUpwardBounds(Rectangle anchor,Size list,Rectangle area)
        {
            int width=Math.Min(list.Width,area.Width);
            int height=Math.Min(list.Height,Math.Max(1,anchor.Top-area.Top));
            return new Rectangle(Math.Max(area.Left,Math.Min(anchor.Left,area.Right-width)),Math.Max(area.Top,anchor.Top-height),width,height);
        }
    }
    public sealed class HistoryDialog : Form
    {
        readonly Timer refresh = new Timer { Interval = 60000 };
        public HistoryDialog(UsageHistory history, UsageSnapshot data, Settings settings, Func<UsageSnapshot> latest = null)
        {
            Text = Ui.Text("GPT · 사용 추이"); StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720,640); MinimumSize = new Size(640,620); AutoScaleMode = AutoScaleMode.Dpi;
            var canvas = new HistoryCanvas(history,data,settings) { Dock = DockStyle.Fill };
            Controls.Add(canvas);
            var periods=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=120};
            var days=new[]{1,7,30,90,180,365}.Where(x=>x<=settings.HistoryDays).ToArray();
            foreach(int day in days)periods.Items.Add(day==1?Ui.Text("최근 24시간"):Ui.Text("최근 ")+day+Ui.Text("일"));
            periods.SelectedIndex=0;
            periods.SelectedIndexChanged+=delegate {canvas.RangeDays=days[periods.SelectedIndex];canvas.Invalidate();};
            var toolbar=new FlowLayoutPanel {Dock=DockStyle.Top,Height=36,Padding=new Padding(24,4,0,0),BackColor=Palette.For(settings.DarkTheme).Background};
            toolbar.Controls.Add(periods);Controls.Add(toolbar);
            refresh.Tick += delegate { if (latest != null) canvas.UpdateSnapshot(latest()); canvas.Invalidate(); };
            refresh.Start();
        }
        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
    }
    public sealed class HistoryCanvas : Control
    {
        readonly UsageHistory history; UsageSnapshot data; readonly Settings settings;
        readonly float? layoutScale;
        public int RangeDays {get;set;}
        public HistoryCanvas(UsageHistory store, UsageSnapshot snapshot, Settings preferences, float? dpiScale = null)
        {
            history=store; data=snapshot; settings=preferences; layoutScale=dpiScale; DoubleBuffered=true;
            RangeDays=1; AccessibleName=Ui.Text("선택 기간의 잔량과 구간별 관측 소모량 및 추가 사용 가능 시간 추정");
        }
        public void UpdateSnapshot(UsageSnapshot snapshot) { data = snapshot; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g=e.Graphics; var p=Palette.For(settings.DarkTheme);
            float s=layoutScale ?? g.DpiX/96f; g.Clear(p.Background); g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var title=new Font("Malgun Gothic",20*s,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var body=new Font("Malgun Gothic",11*s,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var bold=new Font("Malgun Gothic",12*s,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var ink=new SolidBrush(p.Ink)) using(var muted=new SolidBrush(p.Muted)) {
                Card(g,new RectangleF(12*s,74*s,Width-24*s,96*s),p.Card,12*s);
                Card(g,new RectangleF(12*s,197*s,Width-24*s,169*s),p.Card,12*s);
                Card(g,new RectangleF(12*s,374*s,Width-24*s,Math.Max(100,Height-410*s)),p.Card,12*s);
                g.DrawString(Ui.Text("사용 추이"),title,ink,24*s,16*s);
                string note=history.StorageFailed ? Ui.Text("기록 저장에 실패했습니다. 사용자 폴더 접근 권한을 확인해 주세요.") : settings.HistoryEnabled ? Ui.Text("최근 ")+(RangeDays==1?Ui.Text("24시간"):RangeDays+Ui.Text("일"))+Ui.Text(" 표시 · ")+settings.HistoryDays+Ui.Text("일 보관 · 예측 참고 ")+settings.ForecastDays+Ui.Text("일") : Ui.Text("기록 꺼짐 · 기존 자료만 표시합니다.");
                g.DrawString(note,body,muted,24*s,52*s);
                Line(g,Ui.Text("5시간  ") + (settings.HistoryEnabled ? history.Forecast(false,data) : Ui.Text("기록 꺼짐")),bold,ink,24*s,80*s,Width-48*s,22*s);
                if(settings.HistoryEnabled)Line(g,history.ForecastDetail(false,data),body,muted,24*s,103*s,Width-48*s,22*s);
                float weeklyStart=Math.Max(66*s,24*s+g.MeasureString(Ui.Text("주간"),bold).Width+12*s);
                string weeklyForecast=settings.HistoryEnabled ? history.Forecast(true,data) : Ui.Text("기록 꺼짐");
                float forecastWidth=Math.Min(g.MeasureString(weeklyForecast,bold).Width+4*s,Math.Max(100*s,Width-weeklyStart-240*s));
                Line(g,Ui.Text("주간"),bold,ink,24*s,126*s,weeklyStart-32*s,22*s);
                Line(g,weeklyForecast,bold,ink,weeklyStart,126*s,forecastWidth,22*s);
                if(settings.HistoryEnabled) {
                    float explanationStart=weeklyStart+forecastWidth+12*s;
                    Line(g,Ui.Text("현재 작업 강도로 쉬지 않고 계속 사용하는 기준입니다."),body,muted,explanationStart,126*s,Width-24*s-explanationStart,22*s);
                    Line(g,history.WeeklyDailyForecast(data),bold,ink,weeklyStart,149*s,Width-24*s-weeklyStart,22*s);
                }
                if(settings.HistoryEnabled)using(var dailyInk=new SolidBrush(Widget.Accent))Line(g,history.WeeklyDailyText(data,true),body,dailyInk,24*s,176*s,Width-48*s,20*s);
                var chart=new RectangleF(54*s,228*s,Math.Max(100,Width-82*s),110*s);
                DateTimeOffset end=DateTimeOffset.Now; DateTimeOffset start=end.AddDays(-RangeDays);
                g.DrawString(Ui.Text("남은 사용량 (%)"),bold,ink,24*s,203*s);
                using(var grid=new Pen(p.Divider,s)) for(int i=0;i<=2;i++) {
                    float y=chart.Top+chart.Height*i/2; g.DrawLine(grid,chart.Left,y,chart.Right,y);
                    g.DrawString((100-i*50).ToString(),body,muted,20*s,y-8*s);
                }
                DrawSeries(g,chart,history,start,end,false,Widget.Accent,s);
                Color weeklyColor=Color.FromArgb(111,157,255);
                DrawSeries(g,chart,history,start,end,true,weeklyColor,s);
                for(int i=0;i<=4;i++) {
                    var at=ExpiryDisplay.InKorea(start.AddDays(RangeDays*i/4.0)); float x=chart.Left+chart.Width*i/4;
                    g.DrawString(at.ToString(RangeDays==1?"HH:mm":"MM/dd"),body,muted,x-15*s,chart.Bottom+6*s);
                }
                g.DrawString(RangeDays==1?Ui.Text("시간별 관측 소모량 (%p)"):Ui.Text("구간별 관측 소모량 (%p) · 선택 기간을 24구간으로 분할"),bold,ink,24*s,380*s);
                var bars=new RectangleF(chart.Left,410*s,chart.Width,Math.Max(50,Height-475*s));
                var hour=RangeDays==1?new DateTimeOffset(ExpiryDisplay.InKorea(end).Date.AddHours(ExpiryDisplay.InKorea(end).Hour),TimeSpan.FromHours(9)).AddHours(-23):start;
                double bucketHours=RangeDays==1?1:RangeDays;
                double max=1; var five=new double[24]; var week=new double[24];
                for(int i=0;i<24;i++) { var a=hour.AddHours(i*bucketHours); five[i]=history.Consumption(a,a.AddHours(bucketHours),false); week[i]=history.Consumption(a,a.AddHours(bucketHours),true); max=Math.Max(max,Math.Max(five[i],week[i])); }
                using(var blue=new SolidBrush(Widget.Accent)) using(var purple=new SolidBrush(weeklyColor)) {
                    float step=bars.Width/24;
                    for(int i=0;i<24;i++) {
                        float a=(float)(bars.Height*five[i]/max),b=(float)(bars.Height*week[i]/max);
                        if(a>0)g.FillRectangle(blue,bars.Left+i*step,bars.Bottom-a,step*.38f,a);
                        if(b>0)g.FillRectangle(purple,bars.Left+(i+.42f)*step,bars.Bottom-b,step*.38f,b);
                    }
                    g.FillRectangle(blue,chart.Right-135*s,203*s,8*s,8*s);g.DrawString(Ui.Text("5시간"),body,muted,chart.Right-123*s,197*s);
                    g.FillRectangle(purple,chart.Right-63*s,203*s,8*s,8*s);g.DrawString(Ui.Text("주간"),body,muted,chart.Right-51*s,197*s);
                }
                using(var baseline=new Pen(p.Divider,s))g.DrawLine(baseline,bars.Left,bars.Bottom,bars.Right,bars.Bottom);
                for(int i=0;i<24;i+=6)g.DrawString(ExpiryDisplay.InKorea(hour.AddHours(i*bucketHours)).ToString(RangeDays==1?"HH:mm":"MM/dd"),body,muted,bars.Left+bars.Width*i/24-12*s,bars.Bottom+3*s);
                g.DrawString("0",body,muted,28*s,bars.Bottom-8*s);g.DrawString(max.ToString("0.#"),body,muted,18*s,bars.Top-8*s);
                g.DrawString(Ui.Text("관측하지 못한 시간은 소모량을 추정하지 않습니다. 모든 시간은 한국 시간입니다."),body,muted,24*s,Height-30*s);
                if(history.Samples.Count<2)g.DrawString(Ui.Text("기록을 수집 중입니다. 사용 구간과 휴식 패턴이 쌓이면 추정합니다."),body,muted,chart.Left+8*s,chart.Top+45*s);
            }
        }
        static void Line(Graphics g,string text,Font font,Brush brush,float x,float y,float width,float height)
        {
            using(var format=new StringFormat {FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter})
                g.DrawString(text,font,brush,new RectangleF(x,y,Math.Max(1,width),height),format);
        }
        static void Card(Graphics g,RectangleF rect,Color color,float radius)
        {
            using(var path=new GraphicsPath())using(var brush=new SolidBrush(color)) {
                float d=radius*2;path.AddArc(rect.Left,rect.Top,d,d,180,90);path.AddArc(rect.Right-d,rect.Top,d,d,270,90);path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);path.AddArc(rect.Left,rect.Bottom-d,d,d,90,90);path.CloseFigure();g.FillPath(brush,path);
            }
        }
        public static void DrawSeries(Graphics g,RectangleF rect,UsageHistory history,DateTimeOffset start,DateTimeOffset end,bool weekly,Color color,float scale)
        {
            var samples=history.Samples.Where(x=>x.At>=start.ToUnixTimeSeconds()&&x.At<=end.ToUnixTimeSeconds()).ToArray();
            using(var pen=new Pen(color,1.8f*scale))for(int i=1;i<samples.Length;i++) {
                var a=samples[i-1];var b=samples[i];double? av=weekly?a.Week:a.Five,bv=weekly?b.Week:b.Five;
                long? ar=weekly?a.WeekReset:a.FiveReset,br=weekly?b.WeekReset:b.FiveReset;
                if(!av.HasValue||!bv.HasValue||b.At-a.At>600||ar!=br)continue;
                double seconds=(end-start).TotalSeconds;
                var left=new PointF(rect.Left+(float)((a.At-start.ToUnixTimeSeconds())/seconds*rect.Width),rect.Bottom-(float)(av.Value/100*rect.Height));
                var right=new PointF(rect.Left+(float)((b.At-start.ToUnixTimeSeconds())/seconds*rect.Width),rect.Bottom-(float)(bv.Value/100*rect.Height));
                g.DrawLine(pen,left,right);
            }
        }
    }
}
