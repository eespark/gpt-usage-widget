using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class CreateIcon
{
    static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddArc(x,y,r*2,r*2,180,90); p.AddArc(x+w-r*2,y,r*2,r*2,270,90);
        p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90); p.AddArc(x,y+h-r*2,r*2,r*2,90,90);
        p.CloseFigure(); return p;
    }
    static Bitmap Draw(int size)
    {
        using (var large = new Bitmap(size*4,size*4,PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(large))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(size*4/64f,size*4/64f);
                using (var p = Rounded(2,2,60,60,15))
                using (var brush = new LinearGradientBrush(new Point(0,0),new Point(64,64),Color.FromArgb(69,119,255),Color.FromArgb(41,83,211))) g.FillPath(brush,p);
                using (var p = Rounded(13,20,38,8,4))
                using (var brush = new SolidBrush(Color.FromArgb(75,255,255,255))) g.FillPath(brush,p);
                using (var p = Rounded(13,36,38,8,4))
                using (var brush = new SolidBrush(Color.FromArgb(75,255,255,255))) g.FillPath(brush,p);
                using (var brush = new SolidBrush(Color.White))
                {
                    using (var p = Rounded(13,20,30,8,4)) g.FillPath(brush,p);
                    using (var p = Rounded(13,36,22,8,4)) g.FillPath(brush,p);
                }
            }
            var result = new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(large,new Rectangle(0,0,size,size),0,0,large.Width,large.Height,GraphicsUnit.Pixel);
            }
            return result;
        }
    }
    static void Main(string[] args)
    {
        string folder = args[0]; Directory.CreateDirectory(folder);
        int[] sizes = {16,20,24,32,40,48,64,96,128,256};
        var frames = new byte[sizes.Length][];
        for (int i=0;i<sizes.Length;i++)
            using (var bitmap = Draw(sizes[i]))
            using (var stream = new MemoryStream())
            { bitmap.Save(stream,ImageFormat.Png); frames[i]=stream.ToArray(); }
        using (var stream = File.Create(Path.Combine(folder,"app.ico")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16*sizes.Length;
            for (int i=0;i<sizes.Length;i++)
            {
                writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset+=frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
        using (var bitmap = Draw(128)) bitmap.Save(Path.Combine(folder,"app-preview.png"),ImageFormat.Png);
        using (var bitmap = Draw(512)) bitmap.Save(Path.Combine(folder,"app-512.png"),ImageFormat.Png);
    }
}
