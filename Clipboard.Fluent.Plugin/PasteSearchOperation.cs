using Avalonia.Input;
using Blast.Core.Results;

namespace Clipboard.Fluent.Plugin
{
    public class SaveSearchOperation : SearchOperationBase
    {
        protected internal SaveSearchOperation() : base("Keep result",
            "Save the result, even after restart",
            "\uE74E")
        {
            HideMainWindow = false;
        }
    }

    public class RemoveSearchOperation : SearchOperationBase
    {
        protected internal RemoveSearchOperation() : base("Remove from history",
            "Removes this copy from the clipboard history",
            "\uE74D")
        {
            HideMainWindow = false;
            // Defaults to Delete gesture
            KeyGesture = new KeyGesture(Key.Delete);
        }
    }
    
    public class ClearAllSearchOperation : SearchOperationBase
    {
        protected internal ClearAllSearchOperation() : base("Clear all (except saved)",
            "Clear all clipboard results except saved ones",
            "\uE74D")
        {
            HideMainWindow = false;
            // Defaults to Delete gesture
            KeyGesture = new KeyGesture(Key.Delete);
        }
    }
    
}