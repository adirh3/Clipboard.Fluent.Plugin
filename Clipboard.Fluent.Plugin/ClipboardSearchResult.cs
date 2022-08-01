using System.Collections.Generic;
using Blast.Core.Interfaces;
using Blast.Core.Results;

namespace Clipboard.Fluent.Plugin
{
    public sealed class ClipboardSearchResult : SearchResultBase
    {
        public ClipboardHistoryItem ClipboardHistoryItem { get; }

        public ClipboardSearchResult(string iconGlyph, string searchedText, double score,
            IList<ISearchOperation> searchOperations, ICollection<SearchTag> searchTags,
            ClipboardHistoryItem clipboardHistoryItem) : base(clipboardHistoryItem.Text, searchedText,
            "Copy", score, searchOperations, searchTags)
        {
            ClipboardHistoryItem = clipboardHistoryItem;
            UseIconGlyph = true;
            IconGlyph = iconGlyph;
            ShouldCacheResult = false;
            DisableMachineLearning = true; // Making sure results are ordered by time
        }

        protected override void OnSelectedSearchResultChanged()
        {
        }
    }
}