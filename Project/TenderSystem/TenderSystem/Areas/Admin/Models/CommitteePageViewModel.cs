using TenderSystem.Services.Committee;

namespace TenderSystem.Areas.Admin.Models;

/// <summary>Admin &gt; Evaluation committee: the three fixed positions, each with one person.</summary>
public class CommitteePageViewModel
{
    public List<CommitteeBoxViewModel> Boxes { get; set; } = [];

    /// <summary>Every active Evaluation Committee account (the only people that can be put in a box).</summary>
    public List<EvaluatorOption> Options { get; set; } = [];

    /// <summary>What is missing or wrong right now. Empty = complete.</summary>
    public List<string> Problems { get; set; } = [];

    public List<DepartmentCoverageViewModel> Departments { get; set; } = [];
    public bool IsComplete => Problems.Count == 0;
}

public class CommitteeBoxViewModel
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SelectedUserId { get; set; }
    public string? CurrentName { get; set; }
    public string? CurrentDepartment { get; set; }
    public string? Warning { get; set; }
}

public class DepartmentCoverageViewModel
{
    public string Name { get; set; } = "";
    public int Available { get; set; }
    public string People { get; set; } = "";
}

/// <summary>Posted by the page: position code -> chosen user id (empty = not assigned).</summary>
public class SaveCommitteeInput
{
    public Dictionary<string, string?> Selected { get; set; } = new();
}
