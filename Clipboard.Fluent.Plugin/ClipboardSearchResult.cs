using System;
using System.Collections.Generic;
using System.IO;
using Blast.Core.Interfaces;
using Blast.Core.Results;

namespace Clipboard.Fluent.Plugin
{
    public sealed class ClipboardSearchResult : SearchResultBase
    {
        public ClipboardHistoryItem ClipboardHistoryItem { get; }

        public ClipboardSearchResult(string iconGlyph, string searchedText, double score,
            IList<ISearchOperation> searchOperations, ICollection<SearchTag> searchTags,
            ClipboardHistoryItem clipboardHistoryItem) : base(clipboardHistoryItem.GetDisplayText(), searchedText,
            "Copy", score, searchOperations, searchTags)
        {
            SearchObjectId = clipboardHistoryItem;
            ClipboardHistoryItem = clipboardHistoryItem;
            UseIconGlyph = true;
            IconGlyph = iconGlyph;
            ShouldCacheResult = false;
            DisableMachineLearning = true; // Making sure results are ordered by time

            if (clipboardHistoryItem.IsImage())
                TrySetPreviewImage(clipboardHistoryItem.ImageHash);
        }

        private void TrySetPreviewImage(string hash)
        {
            try
            {
                string thumbPath = ClipboardImageHelper.GetThumbnailPath(hash);
                if (!File.Exists(thumbPath))
                    return;

                // Thumbnails are small, read them fully so no file handle stays open
                var stream = new MemoryStream(File.ReadAllBytes(thumbPath));
                PreviewImage = new BitmapImageResult(stream, false);
                UseIconGlyph = false;
            }
            catch (Exception)
            {
                // Fall back to the icon glyph
                UseIconGlyph = true;
            }
        }

        protected override void OnSelectedSearchResultChanged()
        {
        }
    }
}
