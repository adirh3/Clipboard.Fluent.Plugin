using System.Collections.Generic;
using Blast.Core.Interfaces;
using Blast.Core.Results;

namespace Clipboard.Fluent.Plugin
{
    public class ClipboardSearchResult : SearchResultBase
    {
        public ClipboardSearchResult(string iconGlyph, string searchedText, string copy, double score,
            List<ISearchOperation> searchOperations, List<SearchTag> searchTags) : base(copy, searchedText, "Copy",
            score, searchOperations, searchTags)
        {
            UseIconGlyph = true;
            IconGlyph = iconGlyph;
        }

        protected override void OnSelectedSearchResultChanged()
        {
        }
    }
}