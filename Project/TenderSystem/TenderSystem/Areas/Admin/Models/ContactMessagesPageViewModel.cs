namespace TenderSystem.Areas.Admin.Models;

/// <summary>Everything the Contact Messages inbox page shows: search, filter, sort, paging, counts and the rows.</summary>
public class ContactMessagesPageViewModel
{
    /// <summary>The allowed filter values. Anything else falls back to "all".</summary>
    public static readonly string[] Filters = ["all", "unread", "read"];

    /// <summary>The allowed sort values. Anything else falls back to "newest".</summary>
    public static readonly string[] Sorts = ["newest", "oldest"];

    public const int PageSize = 20;

    public string? Search { get; set; }
    public string Filter { get; set; } = "all";
    public string Sort { get; set; } = "newest";

    // Tab counts respect the current search, so each tab shows how many rows it would list.
    public int AllCount { get; set; }
    public int UnreadCount { get; set; }
    public int ReadCount { get; set; }

    // Whole-inbox totals (ignore search and filter). They feed the summary cards.
    public int TotalMessages { get; set; }
    public int TotalUnread { get; set; }
    public int TotalRead => TotalMessages - TotalUnread;

    /// <summary>True when the inbox has no messages at all (before search and filter).</summary>
    public bool InboxIsEmpty => TotalMessages == 0;

    /// <summary>How many messages match the search and the current tab (all pages together).</summary>
    public int ResultCount { get; set; }

    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;

    /// <summary>The route value for "filter": null for the default tab so URLs stay short.</summary>
    public string? FilterRoute => Filter == "all" ? null : Filter;

    /// <summary>The route value for "sort": null for the default order so URLs stay short.</summary>
    public string? SortRoute => Sort == "newest" ? null : Sort;

    public List<ContactMessageRow> Messages { get; set; } = [];
}
