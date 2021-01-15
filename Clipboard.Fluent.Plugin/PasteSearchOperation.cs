using Blast.Core.Results;

namespace Clipboard.Fluent.Plugin
{
    public class RemoveSearchOperation : SearchOperationBase
    {
        protected internal RemoveSearchOperation() : base("Remove from history",
            "Removes this copy from the clipboard history",
            "\uE74D")
        {
            HideMainWindow = false;
        }
    }

    public class PasteSearchOperation : SearchOperationBase
    {
        protected internal PasteSearchOperation() : base("Paste text", "Closes Fluent Search and pastes the text",
            "\uE77F")
        {
            // Makes sure that Fluent is hidden
            HideMainWindow = true;
        }
    }
}