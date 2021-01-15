using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blast.API.Controllers.Keyboard;
using Blast.API.Core.Controllers.Keyboard;
using Blast.API.Search;
using Blast.API.Search.SearchOperations;
using Blast.Core;
using Blast.Core.Interfaces;
using Blast.Core.Objects;
using Blast.Core.Results;

namespace Clipboard.Fluent.Plugin
{
    public class ClipboardSearchApp : ISearchApplication
    {
        private const string SearchTag = "clipboard";
        private const string SearchAppName = "Clipboard";
        private const string CopyIconGlyph = "\uE8C8";

        private readonly Thread _thread;
        private readonly SearchApplicationInfo _applicationInfo;
        private readonly HashSet<string> _clipboardHistory = new();
        private readonly List<ISearchOperation> _supportedOperations;
        private readonly IKeyboardController _keyboardControllerInstance;
        private readonly List<SearchTag> _searchTags;


        public ClipboardSearchApp()
        {
            _searchTags = new List<SearchTag>
            {
                new() {Name = SearchTag, IconGlyph = CopyIconGlyph}
            };
            _supportedOperations = new List<ISearchOperation>
            {
                new PasteSearchOperation(),
                new CopySearchOperation(),
                new RemoveSearchOperation()
            };
            _applicationInfo = new SearchApplicationInfo(SearchAppName,
                "Search in your clipboard history", _supportedOperations)
            {
                MinimumSearchLength = 0,
                IsProcessSearchEnabled = false,
                IsProcessSearchOffline = false,
                ApplicationIconGlyph = CopyIconGlyph,
                SearchAllTime = ApplicationSearchTime.Fast,
                DefaultSearchTags = new List<SearchTag>()
            };
            _keyboardControllerInstance = KeyboardController.KeyboardControllerInstance;
            _thread = new Thread(async () =>
            {
                while (true)
                {
                    string currentText = TextCopy.Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(currentText) && !_clipboardHistory.Contains(currentText))
                        _clipboardHistory.Add(currentText);

                    await Task.Delay(5000);
                }
            });
        }

        public ValueTask LoadSearchApplicationAsync()
        {
            _thread.Start();
            return ValueTask.CompletedTask;
        }

        public SearchApplicationInfo GetApplicationInfo()
        {
            return _applicationInfo;
        }

        public IAsyncEnumerable<ISearchResult> SearchAsync(SearchRequest searchRequest,
            CancellationToken cancellationToken)
        {
            IEnumerable<ISearchResult> SearchClipboardHistory()
            {
                if (searchRequest.SearchType == SearchType.SearchProcess)
                    yield break;

                string searchedText = searchRequest.SearchedText;
                string searchedTag = searchRequest.SearchedTag;

                bool searchTagIsNotEmpty = !string.IsNullOrWhiteSpace(searchedTag);
                bool searchTextIsEmpty = string.IsNullOrWhiteSpace(searchedText);
                if (searchTagIsNotEmpty && !searchedTag.Equals(SearchTag) ||
                    !searchTagIsNotEmpty && searchTextIsEmpty)
                    yield break;

                for (var i = _clipboardHistory.Count - 1; i >= 0; i--)
                {
                    string copy = _clipboardHistory.ElementAt(i);
                    double score = copy.SearchTokens(searchedText) * 2;
                    // Return results if the search worked or the tag used 
                    if (score > 0 || searchTagIsNotEmpty && searchTextIsEmpty)
                        yield return new ClipboardSearchResult(CopyIconGlyph, searchedText, copy, score,
                            _supportedOperations,
                            _searchTags);
                }
            }

            // Using this to not make this method async
            return new SynchronousAsyncEnumerable(SearchClipboardHistory());
        }

        public ValueTask<ISearchResult> GetSearchResultForId(string serializedSearchObjectId)
        {
            // This is used to calculate a search result after Fluent Search has been restarted
            // This is only used by the custom search tag feature
            return new();
        }

        public ValueTask<IHandleResult> HandleSearchResult(ISearchResult searchResult)
        {
            ISearchOperation selectedOperation = searchResult.SelectedOperation;
            Type selectedOperationType = selectedOperation.GetType();

            if (selectedOperationType == typeof(RemoveSearchOperation))
            {
                _clipboardHistory.Remove(searchResult.ResultName);
                searchResult.RemoveResult = true;
                // If the current text is in the clipboard then replace it with empty string so it won't be added to history again
                if (TextCopy.Clipboard.GetText() == searchResult.ResultName)
                    TextCopy.Clipboard.SetText(string.Empty);

                return new ValueTask<IHandleResult>(new HandleResult(true, false));
            }

            // Regardless of the operation we want to copy the text
            TextCopy.Clipboard.SetText(searchResult.ResultName);
            if (selectedOperationType == typeof(PasteSearchOperation))
                _keyboardControllerInstance.PressKeysCombo(new List<VirtualKey> {VirtualKey.CTRL, VirtualKey.V});

            return new ValueTask<IHandleResult>(new HandleResult(true, false));
        }
    }
}