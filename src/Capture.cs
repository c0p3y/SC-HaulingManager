// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Screen capture of the Star Citizen window + image clean-up for OCR. Read-only: nothing is injected into the game.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HaulingManager
{
    public static class ScreenCapture
    {
        public static IntPtr FindGameWindow(string processName)
        {
            string name = (processName ?? "").Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            foreach (Process p in Process.GetProcessesByName(name))
            {
                try { if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle; }
                catch { }
            }
            return IntPtr.Zero;
        }

        public static int GamePid(string processName)
        {
            string name = (processName ?? "").Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            foreach (Process p in Process.GetProcessesByName(name))
            {
                try { if (p.MainWindowHandle != IntPtr.Zero) return p.Id; }
                catch { }
            }
            return -1;
        }

        public static Bitmap CaptureWindow(string processName, int method, out string error)
        {
            error = null;
            IntPtr h = FindGameWindow(processName);
            if (h == IntPtr.Zero) { error = "Star Citizen window not found (process '" + processName + "')."; return null; }
            if (NativeMethods.IsIconic(h)) { error = "Star Citizen is minimized."; return null; }

            NativeMethods.RECT rc;
            NativeMethods.GetClientRect(h, out rc);
            int w = rc.Right - rc.Left, ht = rc.Bottom - rc.Top;
            if (w < 50 || ht < 50) { error = "Star Citizen window is too small to read."; return null; }
            NativeMethods.POINT pt = new NativeMethods.POINT();
            NativeMethods.ClientToScreen(h, ref pt);

            Bitmap bmp = new Bitmap(w, ht, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                if (method == 1)
                {
                    IntPtr hdc = g.GetHdc();
                    try { NativeMethods.PrintWindow(h, hdc, 0x1 | 0x2); } // PW_CLIENTONLY | PW_RENDERFULLCONTENT
                    finally { g.ReleaseHdc(hdc); }
                }
                else
                {
                    g.CopyFromScreen(pt.X, pt.Y, 0, 0, new Size(w, ht), CopyPixelOperation.SourceCopy);
                }
            }
            if (IsMostlyBlack(bmp))
                error = "The capture came back black. Set Star Citizen to Borderless (Graphics settings) or switch the capture method in Settings.";
            return bmp;
        }

        static bool IsMostlyBlack(Bitmap b)
        {
            int bright = 0, total = 0;
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 24; x++)
                {
                    Color c = b.GetPixel(b.Width * x / 24, b.Height * y / 24);
                    total++;
                    if (c.R + c.G + c.B > 30) bright++;
                }
            return bright < total / 50;
        }
    }

    public static class ImagePrep
    {
        public static Bitmap Crop(Bitmap src, double fx, double fy, double fw, double fh)
        {
            int x = Clamp((int)Math.Round(fx * src.Width), 0, src.Width - 1);
            int y = Clamp((int)Math.Round(fy * src.Height), 0, src.Height - 1);
            int w = Clamp((int)Math.Round(fw * src.Width), 1, src.Width - x);
            int h = Clamp((int)Math.Round(fh * src.Height), 1, src.Height - y);
            return src.Clone(new Rectangle(x, y, w, h), PixelFormat.Format32bppArgb);
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // Upscale (bounded by the OCR engine's max size), convert to grayscale using the brightest
        // channel (SC text is cyan/white on dark blue), stretch contrast, optionally invert to dark-on-light.
        public static Bitmap Prepare(Bitmap src, double upscale, int maxDim, bool invert)
        {
            double scale = upscale <= 0 ? 1 : upscale;
            int longest = Math.Max(src.Width, src.Height);
            if (maxDim > 0 && longest * scale > maxDim) scale = (double)maxDim / longest;
            int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));

            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }

            Rectangle r = new Rectangle(0, 0, w, h);
            BitmapData data = dst.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int bytes = Math.Abs(data.Stride) * h;
            byte[] px = new byte[bytes];
            Marshal.Copy(data.Scan0, px, 0, bytes);

            int[] hist = new int[256];
            for (int i = 0; i < bytes; i += 4)
            {
                byte v = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                px[i] = v; hist[v]++;
            }
            int count = w * h, lo = 0, hi = 255, acc = 0;
            for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc > count * 0.02) { lo = i; break; } }
            acc = 0;
            for (int i = 255; i >= 0; i--) { acc += hist[i]; if (acc > count * 0.01) { hi = i; break; } }
            if (hi <= lo + 10) { lo = 0; hi = 255; }

            for (int i = 0; i < bytes; i += 4)
            {
                int v = (px[i] - lo) * 255 / (hi - lo);
                if (v < 0) v = 0; if (v > 255) v = 255;
                if (invert) v = 255 - v;
                byte b = (byte)v;
                px[i] = b; px[i + 1] = b; px[i + 2] = b; px[i + 3] = 255;
            }
            Marshal.Copy(px, 0, data.Scan0, bytes);
            dst.UnlockBits(data);
            return dst;
        }
    }
}
