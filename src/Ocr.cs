// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Uses the OCR engine built into Windows 10/11 (Windows.Media.Ocr) through the .NET Framework's
// WinRT bridge - no Tesseract, no downloads, no installs. Bound late (reflection) so the app
// compiles with the plain csc.exe that ships in Windows.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace HaulingManager
{
    public static class WindowsOcr
    {
        static readonly object sync = new object();
        static bool initialized;
        static string initError;
        static object engine;
        static MethodInfo asTaskGeneric;
        static Type tStorageFile, tRandomAccessStream, tFileAccessMode, tBitmapDecoder, tSoftwareBitmap, tOcrEngine, tOcrResult, tLanguage;
        public static int MaxImageDimension = 2600;
        public static string LanguageTag = "";

        static Type WinRt(string fullName, params string[] assemblies)
        {
            foreach (string a in assemblies)
            {
                Type t = Type.GetType(fullName + ", " + a + ", ContentType=WindowsRuntime", false);
                if (t != null) return t;
            }
            Type t2 = Type.GetType(fullName + ", Windows, ContentType=WindowsRuntime", false);
            if (t2 != null) return t2;
            throw new InvalidOperationException("Windows Runtime type not available: " + fullName + ". Windows 10 or 11 is required.");
        }

        public static string Initialize()
        {
            lock (sync)
            {
                if (initialized) return initError;
                initialized = true;
                try
                {
                    Assembly wrt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                    Type ext = wrt.GetType("System.WindowsRuntimeSystemExtensions", true);
                    asTaskGeneric = ext.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m =>
                        m.Name == "AsTask" && m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");

                    tStorageFile = WinRt("Windows.Storage.StorageFile", "Windows.Storage", "Windows.Foundation");
                    tRandomAccessStream = WinRt("Windows.Storage.Streams.IRandomAccessStream", "Windows.Storage.Streams", "Windows.Storage", "Windows.Foundation");
                    tFileAccessMode = WinRt("Windows.Storage.FileAccessMode", "Windows.Storage", "Windows.Foundation");
                    tBitmapDecoder = WinRt("Windows.Graphics.Imaging.BitmapDecoder", "Windows.Graphics", "Windows.Foundation");
                    tSoftwareBitmap = WinRt("Windows.Graphics.Imaging.SoftwareBitmap", "Windows.Graphics", "Windows.Foundation");
                    tOcrEngine = WinRt("Windows.Media.Ocr.OcrEngine", "Windows.Foundation", "Windows.Media");
                    tOcrResult = WinRt("Windows.Media.Ocr.OcrResult", "Windows.Foundation", "Windows.Media");
                    tLanguage = WinRt("Windows.Globalization.Language", "Windows.Foundation", "Windows.Globalization");

                    try { MaxImageDimension = Convert.ToInt32(tOcrEngine.GetProperty("MaxImageDimension").GetValue(null, null)); }
                    catch { MaxImageDimension = 2600; }

                    // Prefer English (contracts are English); fall back to the user's Windows languages.
                    foreach (string tag in new[] { "en-US", "en-GB", "en" })
                    {
                        try
                        {
                            object lang = Activator.CreateInstance(tLanguage, tag);
                            engine = tOcrEngine.GetMethod("TryCreateFromLanguage").Invoke(null, new object[] { lang });
                            if (engine != null) { LanguageTag = tag; break; }
                        }
                        catch { }
                    }
                    if (engine == null)
                    {
                        engine = tOcrEngine.GetMethod("TryCreateFromUserProfileLanguages").Invoke(null, null);
                        LanguageTag = "user profile";
                    }
                    if (engine == null)
                        initError = "Windows OCR has no language available. In Windows Settings > Time & language > Language & region, add English (United States).";
                }
                catch (Exception ex)
                {
                    initError = "Windows OCR could not start: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                }
                return initError;
            }
        }

        static object Await(object winRtOperation, Type resultType)
        {
            MethodInfo asTask = asTaskGeneric.MakeGenericMethod(resultType);
            Task task = (Task)asTask.Invoke(null, new object[] { winRtOperation });
            task.Wait();
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        static void TryDispose(object o)
        {
            IDisposable d = o as IDisposable;
            if (d != null) { try { d.Dispose(); } catch { } }
        }

        // Returns the recognized lines, top to bottom.
        public static List<string> Recognize(Bitmap bmp)
        {
            string err = Initialize();
            if (err != null) throw new InvalidOperationException(err);

            string tmp = Path.Combine(Path.GetTempPath(), "schm_ocr_" + Guid.NewGuid().ToString("N") + ".png");
            bmp.Save(tmp, ImageFormat.Png);
            object file = null, stream = null, softwareBitmap = null;
            try
            {
                file = Await(tStorageFile.GetMethod("GetFileFromPathAsync").Invoke(null, new object[] { tmp }), tStorageFile);
                object readMode = Enum.ToObject(tFileAccessMode, 0);
                stream = Await(tStorageFile.GetMethod("OpenAsync", new[] { tFileAccessMode }).Invoke(file, new object[] { readMode }), tRandomAccessStream);
                object decoder = Await(tBitmapDecoder.GetMethod("CreateAsync", new[] { tRandomAccessStream }).Invoke(null, new object[] { stream }), tBitmapDecoder);
                softwareBitmap = Await(tBitmapDecoder.GetMethod("GetSoftwareBitmapAsync", Type.EmptyTypes).Invoke(decoder, null), tSoftwareBitmap);
                object result = Await(tOcrEngine.GetMethod("RecognizeAsync").Invoke(engine, new object[] { softwareBitmap }), tOcrResult);

                List<string> lines = new List<string>();
                IEnumerable ocrLines = (IEnumerable)tOcrResult.GetProperty("Lines").GetValue(result, null);
                foreach (object line in ocrLines)
                {
                    object text = line.GetType().GetProperty("Text").GetValue(line, null);
                    if (text != null) lines.Add(text.ToString());
                }
                return lines;
            }
            finally
            {
                TryDispose(softwareBitmap);
                TryDispose(stream);
                try { File.Delete(tmp); } catch { }
            }
        }
    }

    public class CaptureResult
    {
        public ParseResult Parse;
        public string Text = "";
        public string PassUsed = "";
        public Bitmap Processed;
    }

    public static class ContractReader
    {
        // OCR the (already cropped) capture. Auto mode runs a normal and an inverted pass and keeps the better parse.
        public static CaptureResult Read(Bitmap capture, AppSettings s)
        {
            List<bool> passes = new List<bool>();
            if (s.OcrMode == 1) passes.Add(false);
            else if (s.OcrMode == 2) passes.Add(true);
            else { passes.Add(true); passes.Add(false); }

            WindowsOcr.Initialize();
            CaptureResult best = null;
            foreach (bool invert in passes)
            {
                Bitmap prep = ImagePrep.Prepare(capture, s.Upscale, WindowsOcr.MaxImageDimension, invert);
                List<string> lines = WindowsOcr.Recognize(prep);
                string text = string.Join("\r\n", lines.ToArray());
                CaptureResult cr = new CaptureResult();
                cr.Text = text; cr.Parse = ContractParser.Parse(text); cr.PassUsed = invert ? "inverted" : "normal"; cr.Processed = prep;
                if (best == null || cr.Parse.Score() > best.Parse.Score())
                {
                    if (best != null && best.Processed != null) best.Processed.Dispose();
                    best = cr;
                }
                else prep.Dispose();
            }
            return best;
        }
    }
}
