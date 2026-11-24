using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Governance;

namespace TenderSystem.Services.Governance;

/// <summary>RPT-01 setting (.env): EVALUATION_SLA_DAYS, calendar days from the first share with the committee to the deadline. Default 7.</summary>
public class EvaluationScheduleOptions
{
    public int Days { get; set; } = 7;
}

/// <summary>
/// Writes the evaluation deadline of a tender. The code that shares a tender with the committee calls StartAsync once, in the same
/// transaction; calling it again is harmless (the first deadline is kept and returned).
/// </summary>
public class EvaluationScheduleService
{
    private readonly ApplicationDbContext _db;
    private readonly EvaluationScheduleOptions _options;

    public EvaluationScheduleService(ApplicationDbContext db, IOptions<EvaluationScheduleOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    /// <summary>Saves EvaluationStartedAt = now and EvaluationDueAt = now + the default days, if the tender has no deadline yet. Returns the saved row, or null when the tender does not exist.</summary>
    public async Task<EvaluationSchedule?> StartAsync(int tenderId, CancellationToken ct = default)
    {
        var existing = await _db.EvaluationSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.TenderId == tenderId, ct);
        if (existing != null) return existing;
        if (!await _db.Tenders.AsNoTracking().AnyAsync(t => t.Id == tenderId, ct)) return null;

        var days = Math.Max(1, _options.Days);
        var now = DateTime.UtcNow;
        var row = new EvaluationSchedule { TenderId = tenderId, EvaluationStartedAt = now, SlaDays = days, EvaluationDueAt = now.AddDays(days) };
        _db.EvaluationSchedules.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
            return row;
        }
        catch (DbUpdateException)
        {
            // Someone else saved the deadline of this tender at the same moment (unique index). Keep theirs.
            _db.Entry(row).State = EntityState.Detached;
            return await _db.EvaluationSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.TenderId == tenderId, ct) ?? throw new InvalidOperationException("The evaluation deadline could not be saved.");
        }
    }
}
