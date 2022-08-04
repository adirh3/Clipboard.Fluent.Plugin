using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blast.API.Search;
using Blast.API.Search.SearchOperations;
using Blast.API.Settings;
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
        private const string PinnedIcon = "\ue840";

        private readonly Thread _thread;
        private readonly SearchApplicationInfo _applicationInfo;
        private HashSet<ClipboardHistoryItem> _clipboardHistory = new();
        private readonly List<ISearchOperation> _supportedOperations;
        private readonly List<SearchTag> _searchTags;
        private readonly ClipboardSearchAppSettings _clipboardSettings;


        public ClipboardSearchApp()
        {
            _searchTags = new List<SearchTag>
            {
                new() {Name = SearchTag, IconGlyph = CopyIconGlyph}
            };
            _supportedOperations = new List<ISearchOperation>
            {
                new PasteSearchOperationSelfRun(),
                new CopySearchOperationSelfRun(),
                new SaveSearchOperation(),
                new RemoveSearchOperation(),
                new ClearAllSearchOperation()
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
            _applicationInfo.SettingsPage = _clipboardSettings = new ClipboardSearchAppSettings(_applicationInfo);
            // ReSharper disable once AsyncVoidLambda
            _thread = new Thread(async () =>
            {
                while (true)
                {
                    try
                    {
                        string currentText = TextCopy.Clipboard.GetText();
                        var clipboardHistoryItem = new ClipboardHistoryItem {Text = currentText};

                        if (!string.IsNullOrWhiteSpace(currentText))
                        {
                            if (_clipboardHistory.TryGetValue(clipboardHistoryItem,
                                    out ClipboardHistoryItem cachedItem))
                                cachedItem.LastCopied = DateTime.Now;
                            else
                                _clipboardHistory.Add(clipboardHistoryItem);
                        }
                    }
                    catch (Win32Exception)
                    {
                        // Access denied, can't copy this text
                    }

                    await Task.Delay(5000);
                }
            });
        }

        public ValueTask LoadSearchApplicationAsync()
        {
            // Update this only in LoadSearchApplicationAsync, since it happens AFTER settings are loaded 
            _clipboardHistory = new HashSet<ClipboardHistoryItem>(_clipboardSettings.ClipboardHistoryItems);
            _thread.Start();
            return ValueTask.CompletedTask;
        }

        public SearchApplicationInfo GetApplicationInfo()
        {
            return _applicationInfo;
        }
        
        public ValueTask<ISearchResult> GetSearchResultForId(object searchObjectId)
        {
            if (searchObjectId is ClipboardHistoryItem clipboardHistoryItem)
                return new ValueTask<ISearchResult>(new ClipboardSearchResult(PinnedIcon, string.Empty, 0,
                    _supportedOperations, _searchTags, clipboardHistoryItem));
            return new ValueTask<ISearchResult>();
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

                foreach (ClipboardHistoryItem clipboardHistoryItem in _clipboardHistory.OrderByDescending(s =>
                             s.LastCopied))
                {
                    string copy = clipboardHistoryItem.Text;
                    double score = copy.SearchTokens(searchedText);

                    // Return results if the search worked or the tag used 
                    if (score > 0 || searchTagIsNotEmpty && searchTextIsEmpty)
                    {
                        // Increase score based on items that are more recent
                        score += 2.0 * clipboardHistoryItem.LastCopied.ToFileTime() / DateTime.Now.ToFileTime();
                        if (clipboardHistoryItem.IsSaved)
                            score += 2;
                        yield return new ClipboardSearchResult(
                            clipboardHistoryItem.IsSaved ? PinnedIcon : CopyIconGlyph,
                            searchedText, score, _supportedOperations, _searchTags, clipboardHistoryItem);
                    }
                }
            }

            // Using this to not make this method async
            return new SynchronousAsyncEnumerable(SearchClipboardHistory());
        }

        public ValueTask<IHandleResult> HandleSearchResult(ISearchResult searchResult)
        {
            if (searchResult is not ClipboardSearchResult clipboardSearchResult)
                throw new InvalidCastException(nameof(ClipboardSearchResult));

            ISearchOperation selectedOperation = searchResult.SelectedOperation;
            Type selectedOperationType = selectedOperation.GetType();

            if (selectedOperationType == typeof(SaveSearchOperation))
            {
                _clipboardSettings.ClipboardHistoryItems.Add(clipboardSearchResult.ClipboardHistoryItem);
                clipboardSearchResult.ClipboardHistoryItem.IsSaved = true;

                // Save settings
                SettingsUtils.SaveSettings(_clipboardSettings);

                // Search again
                return new ValueTask<IHandleResult>(new HandleResult(true, true));
            }

            if (selectedOperationType == typeof(ClearAllSearchOperation))
            {
                _clipboardHistory.RemoveWhere(s => !s.IsSaved);
                return new ValueTask<IHandleResult>(new HandleResult(true, true));
            }

            if (selectedOperationType == typeof(RemoveSearchOperation))
            {
                _clipboardHistory.Remove(clipboardSearchResult.ClipboardHistoryItem);
                clipboardSearchResult.ClipboardHistoryItem.IsSaved = false;
                if (_clipboardSettings.ClipboardHistoryItems.Contains(clipboardSearchResult.ClipboardHistoryItem))
                    _clipboardSettings.ClipboardHistoryItems.Remove(clipboardSearchResult.ClipboardHistoryItem);
                searchResult.RemoveResult = true;
                // If the current text is in the clipboard then replace it with empty string so it won't be added to history again
                if (TextCopy.Clipboard.GetText() == searchResult.ResultName)
                    TextCopy.Clipboard.SetText(string.Empty);

                SettingsUtils.SaveSettings(_clipboardSettings);
                return new ValueTask<IHandleResult>(new HandleResult(true, true));
            }


            return new ValueTask<IHandleResult>(new HandleResult(true, false));
        }
    }
}