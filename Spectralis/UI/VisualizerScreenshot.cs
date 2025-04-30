using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public static class VisualizerScreenshot
    {
        public static void CaptureToFile(Control control, string outputPath = null)
        {
            if (outputPath == null)
            {
                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                outputPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    $"Spectralis_{ts}.png");
            }

            var bmp = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(bmp, new Rectangle(0, 0, control.Width, control.Height));
            bmp.Save(outputPath, ImageFormat.Png);
            bmp.Dispose();
        }

        public static void CaptureToClipboard(Control control)
        {
            var bmp = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(bmp, new Rectangle(0, 0, control.Width, control.Height));
            Clipboard.SetImage(bmp);
            bmp.Dispose();
        }
    }
}
