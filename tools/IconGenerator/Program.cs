using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var destination = Path.GetFullPath(args.Length > 0 ? args[0] : "MediaSorter/Assets");
Directory.CreateDirectory(destination);
using var bitmap = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bitmap))
{
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);
    using var back = new SolidBrush(ColorTranslator.FromHtml("#aaa0ff"));
    using var rear = new GraphicsPath(); rear.AddLines([new(28,67),new(28,45),new(97,45),new(118,66),new(228,66),new(228,219),new(28,219)]); rear.CloseFigure(); g.FillPath(back,rear);
    using var lavender = new SolidBrush(ColorTranslator.FromHtml("#d8d1ff"));
    var transform = g.Save(); g.TranslateTransform(133,105); g.RotateTransform(9); g.TranslateTransform(-133,-105); FillRound(g,lavender,new RectangleF(61,27,145,157),19); g.Restore(transform);
    FillRound(g,Brushes.White,new RectangleF(49,38,145,157),19);
    using var pale = new SolidBrush(ColorTranslator.FromHtml("#eeeaff")); FillRound(g,pale,new RectangleF(63,53,117,87),10);
    using var sun = new SolidBrush(ColorTranslator.FromHtml("#ffbe83")); g.FillEllipse(sun,139,64,24,24);
    using var mountain = new SolidBrush(ColorTranslator.FromHtml("#9482e6")); g.FillPolygon(mountain,[new Point(65,128),new Point(94,89),new Point(118,116),new Point(136,99),new Point(179,139),new Point(65,139)]);
    using var front = new GraphicsPath(); front.StartFigure(); front.AddBezier(25,123,25,111,31,105,43,105); front.AddLine(43,105,98,105); front.AddLine(98,105,116,125); front.AddLine(116,125,211,125); front.AddBezier(211,125,227,125,231,131,229,145); front.AddLine(229,145,220,204); front.AddBezier(220,204,218,220,212,224,198,224); front.AddLine(198,224,51,224); front.AddBezier(51,224,37,224,32,218,30,205); front.CloseFigure();
    using var purple = new LinearGradientBrush(new Point(30,105),new Point(225,224),ColorTranslator.FromHtml("#8b75ff"),ColorTranslator.FromHtml("#5543c4")); g.FillPath(purple,front);
    using var pen = new Pen(Color.White,9) { StartCap=LineCap.Round, EndCap=LineCap.Round }; g.DrawLine(pen,91,164,167,164); g.DrawLine(pen,91,181,147,181);
}
bitmap.Save(Path.Combine(destination,"app.png"),ImageFormat.Png);
var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
var images = sizes.Select(size => { using var small = new Bitmap(size,size,PixelFormat.Format32bppArgb); using var graphics = Graphics.FromImage(small); graphics.InterpolationMode=InterpolationMode.HighQualityBicubic; graphics.DrawImage(bitmap,0,0,size,size); using var memory = new MemoryStream(); small.Save(memory,ImageFormat.Png); return memory.ToArray(); }).ToArray();
using var writer = new BinaryWriter(File.Create(Path.Combine(destination,"app.ico")));
writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
var offset = 6+16*sizes.Length;
for(var i=0;i<sizes.Length;i++) { writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[i].Length); writer.Write(offset); offset+=images[i].Length; }
foreach(var image in images)writer.Write(image);
Console.WriteLine("Created multi-resolution app.ico and app.png in " + destination);

static void FillRound(Graphics g,Brush brush,RectangleF r,float radius)
{
    using var p = new GraphicsPath(); var d=radius*2;
    p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();g.FillPath(brush,p);
}
