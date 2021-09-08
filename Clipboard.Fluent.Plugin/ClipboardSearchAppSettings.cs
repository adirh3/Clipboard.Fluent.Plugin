using System.Collections.Generic;
using Blast.API.Settings;
using Blast.Core.Objects;

namespace Clipboard.Fluent.Plugin
{
    public class ClipboardSearchAppSettings : SearchApplicationSettingsPage
    {
        public ClipboardSearchAppSettings(SearchApplicationInfo searchApplicationInfo) : base(searchApplicationInfo)
        {
        }

        /// <summary>
        /// Setting used to cache results in a file, the setting is not visible (RenderSetting = false)
        /// </summary>
        [Setting(Name = "ClipboardHistory", RenderSetting = false)]
        public List<ClipboardHistoryItem> ClipboardHistoryItems { get; set; } = new();
    }
}