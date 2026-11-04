using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Blast.Core.Results;

namespace Clipboard.Fluent.Plugin
{
    /// <summary>
    /// Extracts text from images using the Windows OCR that is built into Fluent Search.
    /// Fluent Search's OCR classes are not part of the public Blast.API package, so they are called
    /// through reflection: if they are missing (or change), OCR is simply skipped.
    /// The matching Windows OCR language pack must be installed for a language to be recognized.
    /// </summary>
    internal static class ClipboardOcr
    {
        private const string OcrAssemblyName = "Blast.ScreenSearchApp.WindowsCoreOcr";
        private const string OcrTypeName = "Blast.ScreenSearchApp.WindowsCoreOcr.WindowsOcr";

        // The OCR engine inserts spaces between Japanese / Chinese characters, which breaks searching
        private static readonly Regex SpaceBetweenCjk = new(
            @"(?<=[　-ヿ㐀-鿿＀-￯])[ \t]+(?=[　-ヿ㐀-鿿＀-￯])",
            RegexOptions.Compiled);

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        private static object _ocr;
        private static MethodInfo _recognizeMethod;
        private static bool _unavailable;

        /// <summary>
        /// Returns the recognized text (single line, normalized), or null if nothing was recognized
        /// or OCR is not available.
        /// </summary>
        public static async Task<string> RecognizeAsync(byte[] png)
        {
            try
            {
                if (!TryGetOcr(out object ocr, out MethodInfo recognize))
                    return null;

                using var stream = new MemoryStream(png);
                using var bitmap = new System.Drawing.Bitmap(stream);
                var image = new BitmapImageResult(bitmap);

                // WindowsOcr.RecognizeAsync(BitmapImageResult) returns IAsyncEnumerable<ScreenGroupResult>,
                // IAsyncEnumerable<T> is covariant so it can be consumed as IAsyncEnumerable<object>
                var groups = (IAsyncEnumerable<object>) recognize.Invoke(ocr, new object[] {image});
                var text = new StringBuilder();
                await foreach (object group in groups)
                {
                    var groupText = group.GetType().GetProperty("Text")?.GetValue(group) as string;
                    if (!string.IsNullOrWhiteSpace(groupText))
                        text.Append(groupText).Append(' ');
                }

                return Normalize(text.ToString());
            }
            catch (Exception)
            {
                // OCR is a bonus, never break clipboard monitoring because of it
                return null;
            }
        }

        private static bool TryGetOcr(out object ocr, out MethodInfo recognize)
        {
            ocr = _ocr;
            recognize = _recognizeMethod;
            if (ocr != null)
                return true;
            if (_unavailable)
                return false;

            try
            {
                Type ocrType = Assembly.Load(OcrAssemblyName).GetType(OcrTypeName, true);
                recognize = ocrType.GetMethod("RecognizeAsync");
                if (recognize == null)
                    throw new MissingMethodException(OcrTypeName, "RecognizeAsync");

                CultureInfo culture = CultureInfo.CurrentUICulture;
                foreach (string language in new[] {culture.Name, culture.TwoLetterISOLanguageName, "en-US"})
                {
                    try
                    {
                        ocr = Activator.CreateInstance(ocrType, language);
                        _ocr = ocr;
                        _recognizeMethod = recognize;
                        return true;
                    }
                    catch (Exception)
                    {
                        // Language not supported, try the next one
                    }
                }
            }
            catch (Exception)
            {
                // Fluent Search without (a compatible) OCR
            }

            _unavailable = true;
            return false;
        }

        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            string singleLine = Whitespace.Replace(text, " ").Trim();
            return SpaceBetweenCjk.Replace(singleLine, string.Empty);
        }
    }
}
