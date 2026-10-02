using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexUsageTaskbar
{
    public sealed class SettingsDialog : Form
    {
        public void Present(Rectangle anchor)
        {
            // 메뉴가 닫힌 뒤, 현재 작업표시줄이 있는 화면 안에서 독립된 설정 창을 활성화합니다.
            StartPosition=FormStartPosition.Manual;
            WindowState=FormWindowState.Normal;
            TopMost=true;
            Bounds=VisibleBounds(Size,Screen.FromRectangle(anchor).WorkingArea);
            Show();
            Bounds=VisibleBounds(Size,Screen.FromRectangle(anchor).WorkingArea);
            BringToFront(); Activate();
        }
        internal static Rectangle VisibleBounds(Size size,Rectangle area)
        {
            int width=Math.Min(size.Width,area.Width),height=Math.Min(size.Height,area.Height);
            return new Rectangle(area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height);
        }
        public SettingsDialog(Settings preferences, Action apply, Action clearHistory)
        {
            Text = Ui.Text("GPT · 표시와 알림 설정");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Malgun Gothic", 9); ClientSize = new Size(560, Math.Max(450,Math.Min(760,Screen.PrimaryScreen.WorkingArea.Height-100))); AutoScaleMode = AutoScaleMode.Dpi;
            var palette=Palette.For(preferences.DarkTheme);
            BackColor=palette.Background; ForeColor=palette.Ink;
            var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute,86));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute,66));
            Controls.Add(shell);
            var heading=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            heading.Controls.Add(new Label {Text=Ui.Text("설정"),Font=new Font("Malgun Gothic",19,FontStyle.Bold),AutoSize=true,Location=new Point(22,12)});
            heading.Controls.Add(new Label {Text=Ui.Text("표시·알림·기록 설정"),ForeColor=palette.Muted,AutoSize=true,Location=new Point(24,55)});
            shell.Controls.Add(heading,0,0);
            var scroll=new Panel {Name="SettingsScroll",Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty,Padding=new Padding(10,0,10,0),AutoScrollMargin=new Size(0,16)};
            shell.Controls.Add(scroll,0,1);
            var stack=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Padding=new Padding(4,0,4,12)};
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); scroll.Controls.Add(stack);
            stack.SizeChanged+=delegate {scroll.AutoScrollMinSize=new Size(0,stack.Height+16);};
            Action fitCards=delegate {
                stack.MaximumSize=new Size(Math.Max(180,scroll.ClientSize.Width-scroll.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth),0);
                foreach(Control card in stack.Controls)card.MaximumSize=new Size(Math.Max(160,stack.MaximumSize.Width-stack.Padding.Horizontal-card.Margin.Horizontal),0);
            };
            scroll.ClientSizeChanged+=delegate {fitCards();};
            var table=Section(stack,Ui.Text("표시"),palette);
            var theme = Toggle(Ui.Text("다크 모드"), preferences.DarkTheme);
            AddWide(table, theme);
            var font = Number(preferences.FontSize, 11, 14); Add(table, Ui.Text("글자 크기"), font);
            var width = Number(preferences.BarWidth, 50, 110); Add(table, Ui.Text("막대 길이"), width);
            var thickness = Number(preferences.BarThickness, 3, 7); Add(table, Ui.Text("막대 굵기"), thickness);
            var reset = Toggle(Ui.Text("초기화 구역"), preferences.ShowReset); AddWide(table, reset);
            var usage = Toggle(Ui.Text("사용량 구역"), preferences.ShowUsage); AddWide(table, usage);
            var remaining = Toggle(Ui.Text("남은 시간 구역"), preferences.ShowRemaining); AddWide(table, remaining);
            table=Section(stack,Ui.Text("알림"),palette);
            var lowFive = Toggle(Ui.Text("5시간 잔량 알림"), preferences.LowQuotaAlert&&preferences.FiveHourLowAlert); AddWide(table, lowFive);
            var lowFiveValue=Number(preferences.FiveHourLowPercent,1,100);Add(table,Ui.Text("알림 기준 (% 이하)"),lowFiveValue);
            var lowWeek = Toggle(Ui.Text("주간 잔량 알림"), preferences.LowQuotaAlert&&preferences.WeeklyLowAlert); AddWide(table, lowWeek);
            var lowWeekValue=Number(preferences.WeeklyLowPercent,1,100);Add(table,Ui.Text("알림 기준 (% 이하)"),lowWeekValue);
            var hour = Toggle(Ui.Text("5시간 한도 재설정 전 알림"), preferences.FiveHourResetAlert); AddWide(table, hour);
            var hourValue=Number(preferences.FiveHourResetMinutes,1,300);Add(table,Ui.Text("재설정 몇 분 전"),hourValue);
            var day = Toggle(Ui.Text("주간 한도 재설정 전 알림"), preferences.WeeklyResetAlert); AddWide(table, day);
            var weekMode=Choice(new object[]{Ui.Text("날짜·시각 (한국 시간)"),Ui.Text("재설정까지 남은 시간")},preferences.WeeklyResetMode);Add(table,Ui.Text("주간 알림 방식"),weekMode);
            var weekDays=Number(preferences.WeeklyReminderDays,0,7);Add(table,Ui.Text("재설정 며칠 전 (0=당일)"),weekDays);
            var weekClock=new DateTimePicker {Format=DateTimePickerFormat.Custom,CustomFormat="HH:mm",ShowUpDown=true,Dock=DockStyle.Fill,Value=DateTime.Today.AddHours(preferences.WeeklyReminderHour).AddMinutes(preferences.WeeklyReminderMinute)};Add(table,Ui.Text("알림 시각"),weekClock);
            var weekMinutes=Number(preferences.WeeklyResetMinutes,1,10080);Add(table,Ui.Text("재설정 몇 분 전"),weekMinutes);
            Action updateAlertInputs=delegate {lowFiveValue.Enabled=lowFive.Checked;lowWeekValue.Enabled=lowWeek.Checked;hourValue.Enabled=hour.Checked;weekMode.Enabled=day.Checked;weekDays.Enabled=weekClock.Enabled=day.Checked&&weekMode.SelectedIndex==0;weekMinutes.Enabled=day.Checked&&weekMode.SelectedIndex==1;};
            lowFive.CheckedChanged+=delegate {updateAlertInputs();};lowWeek.CheckedChanged+=delegate {updateAlertInputs();};hour.CheckedChanged+=delegate {updateAlertInputs();};day.CheckedChanged+=delegate {updateAlertInputs();};weekMode.SelectedIndexChanged+=delegate {updateAlertInputs();};updateAlertInputs();
            table=Section(stack,Ui.Text("기록과 조회"),palette);
            var efficient = Toggle(Ui.Text("절전·연결 상태에 맞춰 조회 간격 조절"), preferences.EfficientPolling); AddWide(table, efficient);
            var history = Toggle(Ui.Text("사용 추이 기록 (이 PC에 저장)"), preferences.HistoryEnabled); AddWide(table, history);
            int[] retentionDays = {30,90,180,365}, forecastDays = {1,3,7,14};
            var retention = Choice(new object[] {Ui.Text("30일"),Ui.Text("90일 (권장)"),Ui.Text("180일"),Ui.Text("365일")}, Array.IndexOf(retentionDays,preferences.HistoryDays));
            Add(table,Ui.Text("기록 보관 기간"),retention);
            var forecast = Choice(new object[] {Ui.Text("1일"),Ui.Text("3일"),Ui.Text("7일 (권장)"),Ui.Text("14일")}, Array.IndexOf(forecastDays,preferences.ForecastDays));
            Add(table,Ui.Text("예측 참고 기간"),forecast);
            AddWide(table,new Label {Text=Ui.Text("사용 강도와 사용·휴식 패턴을 따로 추정합니다. 보관 기간을 줄이면 이전 기록은 삭제됩니다."),AutoSize=true,MaximumSize=new Size(410,50),ForeColor=Color.DimGray});
            AddWide(table, new Label { Text = Ui.Text("인증 정보는 기록하지 않습니다. 알림 표시는 Windows 알림 설정에 따릅니다."), AutoSize = true, MaximumSize = new Size(370, 50), ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 8) });
            var delete = new Button {Name="HistoryDelete",Text = Ui.Text("사용 추이 기록 삭제"), AutoSize = true,MinimumSize=new Size(160,34),Anchor=AnchorStyles.Left};
            delete.Click += delegate {
                if (MessageBox.Show(this, Ui.Text("이 PC의 사용 추이 기록을 삭제할까요?"), "GPT", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) clearHistory();
            };
            var buttons = new TableLayoutPanel {Name="SettingsActions",ColumnCount=3,RowCount=1,Padding=new Padding(22,12,22,12),Dock=DockStyle.Fill,Margin=Padding.Empty};
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,88));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,88));
            var save = new Button { Text = Ui.Text("저장"), Dock=DockStyle.Fill };
            var cancel = new Button { Text = Ui.Text("취소"), Dock=DockStyle.Fill, DialogResult = DialogResult.Cancel };
            cancel.Click+=delegate {Close();};
            save.Click += delegate {
                if (!reset.Checked && !usage.Checked && !remaining.Checked) { MessageBox.Show(this, Ui.Text("표시할 구역을 하나 이상 선택해 주세요."), "GPT"); return; }
                int chosenDays=retentionDays[retention.SelectedIndex];
                if (chosenDays < preferences.HistoryDays && MessageBox.Show(this,Ui.Text("보관 기간을 줄이면 ")+chosenDays+Ui.Text("일 이전 기록이 삭제됩니다. 계속할까요?"),"GPT",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
                preferences.DarkTheme = theme.Checked; preferences.FontSize = (int)font.Value;
                preferences.BarWidth = (int)width.Value; preferences.BarThickness = (int)thickness.Value;
                preferences.ShowReset = reset.Checked; preferences.ShowUsage = usage.Checked; preferences.ShowRemaining = remaining.Checked;
                preferences.LowQuotaAlert = lowFive.Checked||lowWeek.Checked;preferences.FiveHourLowAlert=lowFive.Checked;preferences.WeeklyLowAlert=lowWeek.Checked;
                preferences.FiveHourLowPercent=(int)lowFiveValue.Value;preferences.WeeklyLowPercent=(int)lowWeekValue.Value;
                preferences.FiveHourResetAlert = hour.Checked; preferences.WeeklyResetAlert = day.Checked;
                preferences.FiveHourResetMinutes=(int)hourValue.Value;preferences.WeeklyResetMinutes=(int)weekMinutes.Value;preferences.WeeklyResetMode=weekMode.SelectedIndex;
                preferences.WeeklyReminderDays=(int)weekDays.Value;preferences.WeeklyReminderHour=weekClock.Value.Hour;preferences.WeeklyReminderMinute=weekClock.Value.Minute;
                preferences.EfficientPolling = efficient.Checked; preferences.HistoryEnabled = history.Checked;
                preferences.HistoryDays=chosenDays; preferences.ForecastDays=forecastDays[forecast.SelectedIndex];
                preferences.Save(); apply(); DialogResult = DialogResult.OK; Close();
            };
            buttons.Controls.Add(delete,0,0);buttons.Controls.Add(cancel,1,0);buttons.Controls.Add(save,2,0);shell.Controls.Add(buttons,0,2);
            AcceptButton = save; CancelButton = cancel;
            StyleControls(stack,palette);
            StyleButton(save,Widget.Accent,Color.White,Widget.Accent);
            StyleButton(cancel,palette.Card,palette.Ink,palette.Border);
            StyleButton(delete,palette.Background,preferences.DarkTheme?Color.FromArgb(255,153,153):Color.FromArgb(181,62,62),palette.Border);
            fitCards();stack.PerformLayout();scroll.AutoScrollMinSize=new Size(0,stack.Height+16);
        }
        static TableLayoutPanel Section(TableLayoutPanel stack,string title,Palette palette)
        {
            int row=stack.RowCount++;stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var section=new SettingsCard(palette) {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=2,Padding=new Padding(18,12,18,14),Margin=new Padding(6,0,6,12)};
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,56));section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,44));
            AddWide(section,new Label {Text=title,AutoSize=true,Font=new Font("Malgun Gothic",11,FontStyle.Bold),ForeColor=Widget.Accent,Margin=new Padding(3,3,3,10)});
            stack.Controls.Add(section,0,row);return section;
        }
        static void StyleControls(Control parent,Palette palette)
        {
            foreach(Control control in parent.Controls) {
                var label=control as Label;
                if(label!=null) {if(label.ForeColor==Color.DimGray)label.ForeColor=palette.Muted;else if(label.ForeColor!=Widget.Accent)label.ForeColor=palette.Ink;label.MaximumSize=new Size(440,0);}
                if(control is CheckBox)control.ForeColor=palette.Ink;
                if(control is NumericUpDown || control is ComboBox || control is DateTimePicker) {control.BackColor=palette.Background;control.ForeColor=palette.Ink;control.Margin=new Padding(3,5,3,5);}
                StyleControls(control,palette);
            }
        }
        static void StyleButton(Button button,Color background,Color foreground,Color border)
        {
            button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=border;button.FlatAppearance.BorderSize=1;
            button.BackColor=background;button.ForeColor=foreground;button.UseVisualStyleBackColor=false;button.Cursor=Cursors.Hand;
        }
        static NumericUpDown Number(int value, int min, int max) { return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Dock = DockStyle.Fill }; }
        static ComboBox Choice(object[] items,int selected) { var box=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};box.Items.AddRange(items);box.SelectedIndex=Math.Max(0,selected);return box; }
        static CheckBox Toggle(string text, bool value) { return new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(3, 6, 3, 6) }; }
        static void Add(TableLayoutPanel table, string label, Control control)
        {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 6, 3, 6) }, 0, row); table.Controls.Add(control, 1, row);
        }
        static void AddWide(TableLayoutPanel table, Control control)
        {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(control, 0, row); table.SetColumnSpan(control, 2);
        }
    }
    internal sealed class SettingsCard : TableLayoutPanel
    {
        readonly Palette palette;
        internal SettingsCard(Palette colors){palette=colors;BackColor=colors.Card;DoubleBuffered=true;}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(palette.Background);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float diameter=20;var rect=new RectangleF(.5f,.5f,Math.Max(1,Width-1),Math.Max(1,Height-1));
            using(var path=new GraphicsPath()) {
                path.AddArc(rect.Left,rect.Top,diameter,diameter,180,90);path.AddArc(rect.Right-diameter,rect.Top,diameter,diameter,270,90);
                path.AddArc(rect.Right-diameter,rect.Bottom-diameter,diameter,diameter,0,90);path.AddArc(rect.Left,rect.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();
                using(var brush=new SolidBrush(palette.Card))e.Graphics.FillPath(brush,path);
                using(var pen=new Pen(palette.Border))e.Graphics.DrawPath(pen,path);
            }
        }
    }
}
