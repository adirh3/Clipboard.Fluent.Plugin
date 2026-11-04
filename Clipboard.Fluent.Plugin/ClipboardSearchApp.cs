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
        private const string ImageIcon = "\ue91b";

        // Unsaved images are kept on disk, so only keep the latest ones
        private const int MaxUnsavedImages = 30;

        private readonly Thread _thread;
        private readonly SearchApplicationInfo _applicationInfo;
        private HashSet<ClipboardHistoryItem> _clipboardHistory = new();
        private readonly List<ISearchOperation> _supportedOperations;
        private readonly List<ISearchOperation> _imageOperations;
        private uint _lastImageSequence;
        private ClipboardHistoryItem _lastImageItem;
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
            _imageOperations = new List<ISearchOperation>
            {
                new PasteImageSearchOperation(),
                new CopyImageSearchOperation(),
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
                        else
                        {
                            // Only look for images when there is no text, since some apps (e.g. Excel)
                            // put both a text and an image representation on the clipboard
                            CaptureClipboardImage();
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

            // Unsaved images don't survive a restart, so clean up their files
            try
            {
                var savedImageHashes = new HashSet<string>(_clipboardHistory
                    .Where(s => s.IsImage()).Select(s => s.ImageHash));
                ClipboardImageHelper.DeleteUnreferencedFiles(hash => savedImageHashes.Contains(hash));
            }
            catch (Exception)
            {
                // Cleanup is best effort
            }

            _thread.Start();
            return ValueTask.CompletedTask;
        }

        private void CaptureClipboardImage()
        {
            try
            {
                if (!ClipboardImageHelper.HasImage())
                    return;

                uint sequence = ClipboardImageHelper.SequenceNumber;
                if (sequence == _lastImageSequence && _lastImageItem != null)
                {
                    // Same image is still on the clipboard
                    _lastImageItem.LastCopied = DateTime.Now;
                    return;
                }

                if (!ClipboardImageHelper.TryReadImage(out byte[] png, out int width, out int height))
                    return;

                _lastImageSequence = sequence;
                string hash = ClipboardImageHelper.ComputeHash(png);
                var imageItem = new ClipboardHistoryItem
                {
                    ImageHash = hash, ImageWidth = width, ImageHeight = height
                };

                if (_clipboardHistory.TryGetValue(imageItem, out ClipboardHistoryItem cachedItem))
                {
                    cachedItem.LastCopied = DateTime.Now;
                    _lastImageItem = cachedItem;
                    return;
                }

                ClipboardImageHelper.SaveImageFiles(hash, png);
                _clipboardHistory.Add(imageItem);
                _lastImageItem = imageItem;
                TrimImageHistory();

                // Don't wait for OCR, the image is searchable by its size until the text is ready
                _ = Task.Run(() => RecognizeImageTextAsync(imageItem, png));
            }
            catch (Exception)
            {
                // Never let an unexpected image/clipboard problem stop the clipboard monitoring
            }
        }

        private async Task RecognizeImageTextAsync(ClipboardHistoryItem imageItem, byte[] png)
        {
            string ocrText = await ClipboardOcr.RecognizeAsync(png);
            if (string.IsNullOrEmpty(ocrText))
                return;

            imageItem.OcrText = ocrText;
            // The item may have been kept while OCR was running, so make sure the text is saved too
            if (imageItem.IsSaved)
                await SettingsUtils.SaveSettings(_clipboardSettings);
        }

        private void TrimImageHistory()
        {
            List<ClipboardHistoryItem> oldImages = _clipboardHistory
                .Where(s => s.IsImage() && !s.IsSaved)
                .OrderByDescending(s => s.LastCopied)
                .Skip(MaxUnsavedImages)
                .ToList();
            foreach (ClipboardHistoryItem oldImage in oldImages)
            {
                _clipboardHistory.Remove(oldImage);
                ClipboardImageHelper.DeleteImageFiles(oldImage.ImageHash);
            }
        }

        private IList<ISearchOperation> GetOperations(ClipboardHistoryItem item) =>
            item.IsImage() ? _imageOperations : _supportedOperations;

        public SearchApplicationInfo GetApplicationInfo()
        {
            return _applicationInfo;
        }
        
        public ValueTask<ISearchResult> GetSearchResultForId(object searchObjectId)
        {
            if (searchObjectId is ClipboardHistoryItem clipboardHistoryItem)
                return new ValueTask<ISearchResult>(new ClipboardSearchResult(PinnedIcon, string.Empty, 0,
                    GetOperations(clipboardHistoryItem), _searchTags, clipboardHistoryItem));
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
                    string copy = clipboardHistoryItem.GetSearchText();
                    double score = copy.SearchTokens(searchedText);

                    // Return results if the search worked or the tag used 
                    if (score > 0 || searchTagIsNotEmpty && searchTextIsEmpty)
                    {
                        // Increase score based on items that are more recent
                        score += 2.0 * clipboardHistoryItem.LastCopied.ToFileTime() / DateTime.Now.ToFileTime();
                        if (clipboardHistoryItem.IsSaved)
                            score += 2;
                        string defaultIcon = clipboardHistoryItem.IsImage() ? ImageIcon : CopyIconGlyph;
                        yield return new ClipboardSearchResult(
                            clipboardHistoryItem.IsSaved ? PinnedIcon : defaultIcon,
                            searchedText, score, GetOperations(clipboardHistoryItem), _searchTags,
                            clipboardHistoryItem);
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

            if (selectedOperationType == typeof(PasteImageSearchOperation) ||
                selectedOperationType == typeof(CopyImageSearchOperation))
            {
                ClipboardHistoryItem imageItem = clipboardSearchResult.ClipboardHistoryItem;
                if (!imageItem.IsImage() ||
                    !ClipboardImageHelper.SetImage(ClipboardImageHelper.GetImagePath(imageItem.ImageHash)))
                    return new ValueTask<IHandleResult>(new HandleResult(false, false));

                imageItem.LastCopied = DateTime.Now;
                if (selectedOperationType == typeof(PasteImageSearchOperation))
                {
                    // Give Fluent Search time to hide its window so the previous app gets the paste
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(300);
                        ClipboardImageHelper.SendPaste();
                    });
                }

                return new ValueTask<IHandleResult>(new HandleResult(true, false));
            }

            if (selectedOperationType == typeof(ClearAllSearchOperation))
            {
                foreach (ClipboardHistoryItem unsavedImage in _clipboardHistory.Where(s => s.IsImage() && !s.IsSaved))
                    ClipboardImageHelper.DeleteImageFiles(unsavedImage.ImageHash);
                _clipboardHistory.RemoveWhere(s => !s.IsSaved);
                _lastImageItem = null;
                return new ValueTask<IHandleResult>(new HandleResult(true, true));
            }

            if (selectedOperationType == typeof(RemoveSearchOperation))
            {
                ClipboardHistoryItem removedItem = clipboardSearchResult.ClipboardHistoryItem;
                _clipboardHistory.Remove(removedItem);
                removedItem.IsSaved = false;
                if (_clipboardSettings.ClipboardHistoryItems.Contains(removedItem))
                    _clipboardSettings.ClipboardHistoryItems.Remove(removedItem);
                searchResult.RemoveResult = true;

                if (removedItem.IsImage())
                {
                    // If this image is still on the clipboard then clear it so it won't be added to history again
                    if (ClipboardImageHelper.TryReadImage(out byte[] png, out _, out _) &&
                        ClipboardImageHelper.ComputeHash(png) == removedItem.ImageHash)
                        ClipboardImageHelper.ClearClipboard();
                    ClipboardImageHelper.DeleteImageFiles(removedItem.ImageHash);
                    _lastImageItem = null;
                }
                // If the current text is in the clipboard then replace it with empty string so it won't be added to history again
                else if (TextCopy.Clipboard.GetText() == searchResult.ResultName)
                    TextCopy.Clipboard.SetText(string.Empty);

                SettingsUtils.SaveSettings(_clipboardSettings);
                return new ValueTask<IHandleResult>(new HandleResult(true, true));
            }


            return new ValueTask<IHandleResult>(new HandleResult(true, false));
        }
    }
}