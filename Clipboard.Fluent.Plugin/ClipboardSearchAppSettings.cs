using System.Collections.Generic;
using Blast.API.Settings;
using Blast.Core.Objects;

namespace Clipboard.Fluent.Plugin
{
    public class ClipboardSearchAppSettings : SearchApplicationSettingsPage
    {
        private List<ClipboardHistoryItem> _clipboardHistoryItems = new();

        public ClipboardSearchAppSettings(SearchApplicationInfo searchApplicationInfo) : base(searchApplicationInfo)
        {
        }

        /// <summary>
        /// Setting used to cache results in a file, the setting is not visible (RenderSetting = false)
        /// </summary>
        [Setting(Name = "ClipboardHistory", RenderSetting = false)]
        public List<ClipboardHistoryItem> ClipboardHistoryItems
        {
            get => _clipboardHistoryItems;
            set
            {
                if (value == null) return;
                _clipboardHistoryItems = value;
            }
        }
    }
}