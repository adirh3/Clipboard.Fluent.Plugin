using System.Collections.Generic;
using Blast.Core.Interfaces;
using Blast.Core.Results;

namespace Clipboard.Fluent.Plugin
{
    public sealed class ClipboardSearchResult : SearchResultBase
    {
        public ClipboardHistoryItem ClipboardHistoryItem { get; }

        public ClipboardSearchResult(string iconGlyph, string searchedText, double score,
            List<ISearchOperation> searchOperations, List<SearchTag> searchTags,
            ClipboardHistoryItem clipboardHistoryItem) : base(clipboardHistoryItem.Text, searchedText,
            clipboardHistoryItem.IsSaved ? "Saved Copy" : "Copy",
            score, searchOperations, searchTags)
        {
            ClipboardHistoryItem = clipboardHistoryItem;
            UseIconGlyph = true;
            IconGlyph = iconGlyph;
            ShouldCacheResult = false;
        }

        protected override void OnSelectedSearchResultChanged()
        {
        }
    }
}