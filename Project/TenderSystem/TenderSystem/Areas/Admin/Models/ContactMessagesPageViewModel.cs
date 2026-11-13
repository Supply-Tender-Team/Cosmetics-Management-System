namespace TenderSystem.Areas.Admin.Models;

/// <summary>Everything the Contact Messages inbox page shows: search, filter, tab counts and the rows.</summary>
public class ContactMessagesPageViewModel
{
    /// <summary>The allowed filter values. Anything else falls back to "all".</summary>
    public static readonly string[] Filters = ["all", "unread", "read"];

    public string? Search { get; set; }
    public string Filter { get; set; } = "all";

    // Counts respect the current search, so each tab shows how many rows it would list.
    public int AllCount { get; set; }
    public int UnreadCount { get; set; }
    public int ReadCount { get; set; }

    /// <summary>True when the inbox has no messages at all (before search and filter).</summary>
    public bool InboxIsEmpty { get; set; }

    public List<ContactMessageRow> Messages { get; set; } = [];
}
