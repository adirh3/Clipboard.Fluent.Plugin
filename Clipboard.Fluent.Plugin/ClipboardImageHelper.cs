using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace Clipboard.Fluent.Plugin
{
    /// <summary>
    /// Reads / writes images from / to the Windows clipboard and stores them as PNG files.
    /// </summary>
    internal static class ClipboardImageHelper
    {
        private const uint CF_DIB = 8;
        private const uint GMEM_MOVEABLE = 0x0002;
        private const int ThumbnailSize = 256;

        private static readonly uint PngFormat = RegisterClipboardFormat("PNG");

        public static string ImagesDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Blast", "ClipboardImages");

        public static uint SequenceNumber => GetClipboardSequenceNumber();

        public static bool HasImage()
        {
            return IsClipboardFormatAvailable(PngFormat) || IsClipboardFormatAvailable(CF_DIB);
        }

        /// <summary>
        /// Reads the image currently on the clipboard as PNG bytes.
        /// </summary>
        public static bool TryReadImage(out byte[] png, out int width, out int height)
        {
            png = null;
            width = height = 0;
            if (!HasImage())
                return false;

            byte[] raw = null;
            bool isPng = false;
            if (!TryOpenClipboard())
                return false;
            try
            {
                if (IsClipboardFormatAvailable(PngFormat))
                {
                    raw = CopyClipboardData(PngFormat);
                    isPng = raw != null;
                }

                if (raw == null && IsClipboardFormatAvailable(CF_DIB))
                    raw = CopyClipboardData(CF_DIB);
            }
            finally
            {
                CloseClipboard();
            }

            if (raw == null || raw.Length == 0)
                return false;

            try
            {
                byte[] encoded = isPng ? raw : DibToBmp(raw);
                using var input = new MemoryStream(encoded);
                using var image = Image.FromStream(input);
                width = image.Width;
                height = image.Height;
                if (isPng)
                {
                    png = raw;
                }
                else
                {
                    using var output = new MemoryStream();
                    image.Save(output, ImageFormat.Png);
                    png = output.ToArray();
                }

                return true;
            }
            catch (Exception)
            {
                // Unsupported / corrupted image data
                return false;
            }
        }

        public static string ComputeHash(byte[] data)
        {
            return Convert.ToHexString(SHA256.HashData(data));
        }

        public static string GetImagePath(string hash) => Path.Combine(ImagesDirectory, hash + ".png");
        public static string GetThumbnailPath(string hash) => Path.Combine(ImagesDirectory, hash + ".thumb.png");

        /// <summary>
        /// Saves the full image and a small thumbnail (used for previews) to disk.
        /// </summary>
        public static void SaveImageFiles(string hash, byte[] png)
        {
            Directory.CreateDirectory(ImagesDirectory);
            string imagePath = GetImagePath(hash);
            if (!File.Exists(imagePath))
                File.WriteAllBytes(imagePath, png);

            string thumbPath = GetThumbnailPath(hash);
            if (File.Exists(thumbPath))
                return;

            using var input = new MemoryStream(png);
            using var image = Image.FromStream(input);
            double scale = Math.Min(1.0, (double) ThumbnailSize / Math.Max(image.Width, image.Height));
            int w = Math.Max(1, (int) (image.Width * scale));
            int h = Math.Max(1, (int) (image.Height * scale));
            using var thumb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, 0, 0, w, h);
            }

            thumb.Save(thumbPath, ImageFormat.Png);
        }

        public static void DeleteImageFiles(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return;
            TryDelete(GetImagePath(hash));
            TryDelete(GetThumbnailPath(hash));
        }

        /// <summary>
        /// Deletes stored image files that are not in the given set of hashes.
        /// </summary>
        public static void DeleteUnreferencedFiles(Func<string, bool> isReferenced)
        {
            if (!Directory.Exists(ImagesDirectory))
                return;
            foreach (string file in Directory.EnumerateFiles(ImagesDirectory, "*.png"))
            {
                string name = Path.GetFileName(file);
                string hash = name.Split('.')[0];
                if (!isReferenced(hash))
                    TryDelete(file);
            }
        }

        /// <summary>
        /// Puts the stored image on the clipboard (as PNG with alpha and as a regular DIB for compatibility).
        /// </summary>
        public static bool SetImage(string imagePath)
        {
            if (!File.Exists(imagePath))
                return false;

            byte[] png = File.ReadAllBytes(imagePath);
            byte[] dib;
            using (var input = new MemoryStream(png))
            using (var image = Image.FromStream(input))
            using (var bmp = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    // Flatten transparency onto white for the plain DIB format
                    g.Clear(Color.White);
                    g.DrawImage(image, 0, 0, image.Width, image.Height);
                }

                using var output = new MemoryStream();
                bmp.Save(output, ImageFormat.Bmp);
                byte[] bmpBytes = output.ToArray();
                // Drop the 14 byte BITMAPFILEHEADER to get a DIB
                dib = new byte[bmpBytes.Length - 14];
                Buffer.BlockCopy(bmpBytes, 14, dib, 0, dib.Length);
            }

            if (!TryOpenClipboard())
                return false;
            try
            {
                EmptyClipboard();
                bool ok = SetClipboardBytes(PngFormat, png);
                ok |= SetClipboardBytes(CF_DIB, dib);
                return ok;
            }
            finally
            {
                CloseClipboard();
            }
        }

        public static void ClearClipboard()
        {
            if (!TryOpenClipboard())
                return;
            try
            {
                EmptyClipboard();
            }
            finally
            {
                CloseClipboard();
            }
        }

        /// <summary>
        /// Sends Ctrl+V to the foreground window.
        /// </summary>
        public static void SendPaste()
        {
            const ushort vkControl = 0x11;
            const ushort vkV = 0x56;
            var inputs = new[]
            {
                KeyInput(vkControl, false),
                KeyInput(vkV, false),
                KeyInput(vkV, true),
                KeyInput(vkControl, true)
            };
            SendInput((uint) inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        private static INPUT KeyInput(ushort vk, bool keyUp)
        {
            return new INPUT
            {
                type = 1, // INPUT_KEYBOARD
                U = new InputUnion
                {
                    ki = new KEYBDINPUT {wVk = vk, dwFlags = keyUp ? 0x0002u : 0u}
                }
            };
        }

        private static bool TryOpenClipboard()
        {
            // Another application may hold the clipboard for a short while
            for (int i = 0; i < 5; i++)
            {
                if (OpenClipboard(IntPtr.Zero))
                    return true;
                Thread.Sleep(30);
            }

            return false;
        }

        private static byte[] CopyClipboardData(uint format)
        {
            IntPtr handle = GetClipboardData(format);
            if (handle == IntPtr.Zero)
                return null;
            UIntPtr size = GlobalSize(handle);
            if (size == UIntPtr.Zero || size.ToUInt64() > int.MaxValue)
                return null;
            IntPtr ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                return null;
            try
            {
                var bytes = new byte[(int) size.ToUInt64()];
                Marshal.Copy(ptr, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }

        private static bool SetClipboardBytes(uint format, byte[] data)
        {
            IntPtr handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr) data.Length);
            if (handle == IntPtr.Zero)
                return false;
            IntPtr ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                GlobalFree(handle);
                return false;
            }

            Marshal.Copy(data, 0, ptr, data.Length);
            GlobalUnlock(handle);
            if (SetClipboardData(format, handle) == IntPtr.Zero)
            {
                GlobalFree(handle);
                return false;
            }

            return true; // The clipboard owns the memory now
        }

        /// <summary>
        /// Wraps a DIB (BITMAPINFOHEADER + pixels) in a BMP file header so it can be decoded.
        /// </summary>
        private static byte[] DibToBmp(byte[] dib)
        {
            int headerSize = BitConverter.ToInt32(dib, 0);
            int bitCount = BitConverter.ToInt16(dib, 14);
            int compression = BitConverter.ToInt32(dib, 16);
            int colorsUsed = BitConverter.ToInt32(dib, 32);

            int masks = headerSize == 40 && (compression == 3 || compression == 6) ? (compression == 3 ? 12 : 16) : 0;
            int palette = bitCount <= 8 ? (colorsUsed == 0 ? 1 << bitCount : colorsUsed) * 4 : colorsUsed * 4;
            int pixelOffset = 14 + headerSize + masks + palette;

            var bmp = new byte[14 + dib.Length];
            bmp[0] = (byte) 'B';
            bmp[1] = (byte) 'M';
            BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
            BitConverter.GetBytes(pixelOffset).CopyTo(bmp, 10);
            Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
            return bmp;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #region Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            // Makes the union as large as the biggest member (MOUSEINPUT) so Marshal.SizeOf is correct
            [FieldOffset(0)] private MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GlobalSize(IntPtr hMem);

        #endregion
    }
}
