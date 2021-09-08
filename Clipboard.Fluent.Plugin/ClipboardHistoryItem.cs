using System;

namespace Clipboard.Fluent.Plugin
{
    public class ClipboardHistoryItem
    {
        public string Text { get; set; }
        
        public DateTime LastCopied { get; set; } = DateTime.Now;
        public bool IsSaved { get; set; }

        private bool Equals(ClipboardHistoryItem other)
        {
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
            return (Text != null ? Text.GetHashCode() : 0);
        }
    }
}