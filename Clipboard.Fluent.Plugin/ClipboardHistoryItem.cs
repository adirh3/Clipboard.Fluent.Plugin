using System;

namespace Clipboard.Fluent.Plugin
{
    public class ClipboardHistoryItem
    {
        public string Text { get; set; }

        /// <summary>
        /// SHA256 of the PNG data when this item is an image, null for text items.
        /// The image itself is stored on disk (see <see cref="ClipboardImageHelper"/>).
        /// </summary>
        public string ImageHash { get; set; }

        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }

        /// <summary>
        /// Text found in the image by OCR (single line), null if there is none or OCR hasn't finished yet.
        /// </summary>
        public string OcrText { get; set; }

        public DateTime LastCopied { get; set; } = DateTime.Now;
        public bool IsSaved { get; set; }

        private const int MaxOcrDisplayLength = 80;

        // Methods (not properties) so they never end up in the saved settings
        public bool IsImage() => !string.IsNullOrEmpty(ImageHash);

        public string GetDisplayText()
        {
            if (!IsImage())
                return Text;

            string title = $"Image {ImageWidth}x{ImageHeight}";
            string ocrText = OcrText;
            if (string.IsNullOrEmpty(ocrText))
                return title;
            string snippet = ocrText.Length > MaxOcrDisplayLength
                ? ocrText.Substring(0, MaxOcrDisplayLength) + "…"
                : ocrText;
            return $"{title} · {snippet}";
        }

        /// <summary>
        /// The text the search query is matched against (for images that includes the full OCR text).
        /// </summary>
        public string GetSearchText()
        {
            if (!IsImage())
                return Text;
            string ocrText = OcrText;
            return string.IsNullOrEmpty(ocrText) ? $"Image {ImageWidth}x{ImageHeight}" : $"Image {ImageWidth}x{ImageHeight} {ocrText}";
        }

        private bool Equals(ClipboardHistoryItem other)
        {
            if (IsImage() || other.IsImage())
                return ImageHash == other.ImageHash;
            return Text == other.Text;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ClipboardHistoryItem) obj);
        }

        public override int GetHashCode()
        {
            if (IsImage()) return ImageHash.GetHashCode();
            return (Text != null ? Text.GetHashCode() : 0);
        }
    }
}
