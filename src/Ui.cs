using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexUsageTaskbar
{
    public static class Ui
    {
        public static bool English
        {
            get {
#if ENGLISH
                return true;
#else
                return false;
#endif
            }
        }
        static readonly Dictionary<string, string> translations = Load();
        public static string TitleFontFamily { get { return English ? "Segoe UI" : "Malgun Gothic"; } }
        public static int MeasureTitle(int size)
        {
            string title = Ui.Text("GPT 사용량");
            if (English) return WidgetLayout.Measure(title, size, TitleFontFamily, FontStyle.Bold);
            int koreanSize = (int)Math.Round(size * .9);
            return WidgetLayout.Measure(title.Substring(0, 4), size, TitleFontFamily, FontStyle.Bold)
                + WidgetLayout.Measure(title.Substring(4), koreanSize, TitleFontFamily, FontStyle.Bold);
        }
        public static int MeasureWidgetTitle(int size)
        {
            return (int)Math.Ceiling((MeasureTitle(22) + 4) * size / 22.0);
        }
        public static void DrawTitle(Graphics graphics, float scale, int size, Color color, Color background, Rectangle bounds, int koreanOffset = 0)
        {
            string title = Ui.Text("GPT 사용량");
            var pixels = Rectangle.FromLTRB((int)Math.Round(bounds.Left*scale),(int)Math.Round(bounds.Top*scale),
                (int)Math.Round(bounds.Right*scale),(int)Math.Round(bounds.Bottom*scale));
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            using (var font = new Font(TitleFontFamily, size*scale, FontStyle.Bold, GraphicsUnit.Pixel)) {
                if (English) {
                    TextRenderer.DrawText(graphics,title,font,pixels,color,background,flags);
                    return;
                }
                string prefix = title.Substring(0, 4);
                int prefixWidth = TextRenderer.MeasureText(graphics, prefix, font, new Size(int.MaxValue, int.MaxValue), flags).Width;
                TextRenderer.DrawText(graphics,prefix,font,pixels,color,background,flags);
                pixels.X += prefixWidth;
                pixels.Width = Math.Max(1, pixels.Width - prefixWidth);
                pixels.Y += (int)Math.Round(koreanOffset * scale);
                using (var koreanFont = new Font(TitleFontFamily, (int)Math.Round(size * .9)*scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(graphics,title.Substring(4),koreanFont,pixels,color,background,flags);
            }
        }
        public static void DrawWidgetTitle(Graphics graphics, float scale, int size, Color color, Color background, Rectangle bounds)
        {
            const int panelTitleSize = 22;
            const int panelTitleHeight = 32;
            const int supersampling = 2;
            int sourceWidth = MeasureTitle(panelTitleSize) + 4;
            float ratio = size / (float)panelTitleSize;
            // 정상 패널 제목의 영문·한글 비율을 그대로 축소합니다. 가로와 세로에 동일한 배율을 씁니다.
            float pixelWidth = sourceWidth * ratio * scale;
            float pixelHeight = panelTitleHeight * ratio * scale;
            using (var bitmap = new Bitmap(sourceWidth * supersampling, panelTitleHeight * supersampling))
            using (var titleGraphics = Graphics.FromImage(bitmap)) {
                titleGraphics.Clear(background);
                DrawTitle(titleGraphics, supersampling, panelTitleSize, color, background,
                    new Rectangle(0, 0, sourceWidth, panelTitleHeight));
                var saved = graphics.Save();
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(bitmap,
                    new RectangleF(bounds.Left * scale, (bounds.Top + (bounds.Height - panelTitleHeight * ratio) / 2) * scale, pixelWidth, pixelHeight),
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
                graphics.Restore(saved);
            }
        }
        static Dictionary<string, string> Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("translations.en.json"))
            using (var reader = new StreamReader(stream))
                return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }
        public static string Text(string korean)
        {
            if (!English) return korean;
            string english;
            if (!translations.TryGetValue(korean, out english)) throw new InvalidOperationException("Missing English translation: " + korean);
            return english;
        }
    }
}
