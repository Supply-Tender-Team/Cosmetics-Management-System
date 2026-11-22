using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Ai;
using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Audit;

namespace TenderSystem.Services.Knowledge;

public sealed record KnowledgeDocItem(int Id, string Title, string FileName, string[] Roles, bool IsEnabled, int ChunkCount, int CharCount,
    string EmbeddingModel, bool ModelMatches, DateTime UploadedAt, string UploadedBy);

/// <summary>A passage found for a question. Score is the cosine similarity: 1 = same meaning, near 0 = unrelated.</summary>
public sealed record KnowledgeHit(int DocumentId, string Title, int Position, string Text, double Score);

public sealed record KnowledgeAddResult(bool Ok, string Message, int? DocumentId = null);

/// <summary>
/// The company documents the full-page assistant answers from (AI-18).
///   Adding: text is read from the file, cut into passages (KnowledgeChunker), each passage is embedded (AiEmbeddingService) and saved.
///   Searching: the question is embedded the same way and compared with the passages. ONLY passages of enabled documents whose allowed
///   roles contain the user's role are compared, so a passage the user may not read can never reach the model.
/// </summary>
public sealed class KnowledgeBaseService
{
    /// <summary>Roles an Admin may give a document to. Suppliers never get answers from company documents.</summary>
    public static readonly string[] AllowedRoleNames = [.. Roles.All.Where(r => r != Roles.Supplier)];

    private const int MaxChunksPerDocument = 1500;

    // One copy of all vectors in memory, so a question does not read the whole table. It is rebuilt after any change.
    private sealed record Entry(int ChunkId, int DocumentId, string Title, string Roles, float[] Vector);
    private static readonly SemaphoreSlim IndexLock = new(1, 1);
    private static int _version;
    private static int _loadedVersion = -1;
    private static string _loadedModel = "";
    private static List<Entry> _index = [];

    private readonly ApplicationDbContext _db;
    private readonly AiEmbeddingService _embedding;
    private readonly KnowledgeOptions _options;
    private readonly AuditService _audit;
    private readonly ILogger<KnowledgeBaseService> _log;

    public KnowledgeBaseService(ApplicationDbContext db, AiEmbeddingService embedding, IOptions<KnowledgeOptions> options,
        AuditService audit, ILogger<KnowledgeBaseService> log)
    {
        _db = db;
        _embedding = embedding;
        _options = options.Value;
        _audit = audit;
        _log = log;
    }

    public bool Enabled => _options.Enabled;
    public string Model => _options.EmbedModel;
    public int MaxMb => _options.MaxMb;
    public double MinScore => _options.MinScore;

    // ------------------------------------------------------------------ adding and managing documents

    public async Task<KnowledgeAddResult> AddAsync(string userId, byte[] file, string fileName, string? title, IEnumerable<string> roles, CancellationToken ct)
    {
        if (!Enabled) return new(false, "The knowledge base is turned off (AI_KB_ENABLED=false).");
        var chosen = RolesText(roles);
        if (chosen is null) return new(false, "Choose at least one role that may get answers from this document.");
        if (file.Length == 0) return new(false, "The file is empty.");
        if (file.Length > _options.MaxMb * 1024L * 1024L) return new(false, $"The file is larger than {_options.MaxMb} MB.");

        var (text, error) = KnowledgeTextReader.Read(file, Path.GetExtension(fileName));
        if (text is null) return new(false, error ?? "The file cannot be read.");

        var pieces = KnowledgeChunker.Split(text, _options.ChunkWords, _options.OverlapWords);
        if (pieces.Count == 0) return new(false, "The file has too little text.");
        if (pieces.Count > MaxChunksPerDocument) return new(false, $"The file is too long ({pieces.Count} passages, the limit is {MaxChunksPerDocument}). Split it into smaller documents.");

        List<float[]> vectors;
        try { vectors = await _embedding.EmbedAsync(pieces, ct); }
        catch (AiUnavailableException ex) { return new(false, ex.Message); }

        var cleanTitle = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim();
        var document = new KnowledgeDocument
        {
            Title = Limit(cleanTitle, 200),
            FileName = Limit(Path.GetFileName(fileName), 260),
            AllowedRoles = chosen,
            ChunkCount = pieces.Count,
            CharCount = text.Length,
            EmbeddingModel = _options.EmbedModel,
            UploadedById = userId,
            Chunks = pieces.Select((p, i) => new KnowledgeChunk { Position = i + 1, Text = p, Embedding = AiEmbeddingService.ToBytes(vectors[i]) }).ToList(),
        };
        _db.KnowledgeDocuments.Add(document);
        await _db.SaveChangesAsync(CancellationToken.None);
        Invalidate();
        await _audit.LogAsync("KnowledgeDocumentAdded", "KnowledgeDocument", document.Id.ToString(), $"{document.Title}: {pieces.Count} passages");   // never the text
        return new(true, $"\"{document.Title}\" was added ({pieces.Count} passages).", document.Id);
    }

    public async Task<List<KnowledgeDocItem>> ListAsync(CancellationToken ct) =>
        (await _db.KnowledgeDocuments.AsNoTracking().OrderByDescending(d => d.UploadedAt)
            .Select(d => new { d.Id, d.Title, d.FileName, d.AllowedRoles, d.IsEnabled, d.ChunkCount, d.CharCount, d.EmbeddingModel, d.UploadedAt, By = d.UploadedBy.FullName })
            .ToListAsync(ct))
        .Select(d => new KnowledgeDocItem(d.Id, d.Title, d.FileName, SplitRoles(d.AllowedRoles), d.IsEnabled, d.ChunkCount, d.CharCount,
            d.EmbeddingModel, d.EmbeddingModel == _options.EmbedModel, d.UploadedAt, d.By ?? ""))
        .ToList();

    public async Task<bool> SetEnabledAsync(int id, bool enabled, CancellationToken ct)
    {
        var document = await _db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null) return false;
        document.IsEnabled = enabled;
        await _db.SaveChangesAsync(ct);
        Invalidate();
        await _audit.LogAsync(enabled ? "KnowledgeDocumentEnabled" : "KnowledgeDocumentDisabled", "KnowledgeDocument", id.ToString(), document.Title);
        return true;
    }

    public async Task<bool> SetRolesAsync(int id, IEnumerable<string> roles, CancellationToken ct)
    {
        var chosen = RolesText(roles);
        var document = await _db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null || chosen is null) return false;
        document.AllowedRoles = chosen;
        await _db.SaveChangesAsync(ct);
        Invalidate();
        await _audit.LogAsync("KnowledgeDocumentRolesChanged", "KnowledgeDocument", id.ToString(), $"{document.Title}: {string.Join(", ", SplitRoles(chosen))}");
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var title = await _db.KnowledgeDocuments.Where(d => d.Id == id).Select(d => d.Title).FirstOrDefaultAsync(ct);
        if (title is null) return false;

        // The passages go first (every foreign key of our tables is "Restrict"), in one transaction.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.KnowledgeChunks.Where(c => c.DocumentId == id).ExecuteDeleteAsync(ct);
        await _db.KnowledgeDocuments.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        Invalidate();
        await _audit.LogAsync("KnowledgeDocumentDeleted", "KnowledgeDocument", id.ToString(), title);
        return true;
    }

    /// <summary>Makes the vectors of a document again with the current embedding model, from the passages already saved (after AI_KB_EMBED_MODEL was changed).</summary>
    public async Task<KnowledgeAddResult> ReindexAsync(int id, CancellationToken ct)
    {
        var document = await _db.KnowledgeDocuments.Include(d => d.Chunks).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null) return new(false, "The document was not found.");

        var chunks = document.Chunks.OrderBy(c => c.Position).ToList();
        List<float[]> vectors;
        try { vectors = await _embedding.EmbedAsync(chunks.Select(c => c.Text).ToList(), ct); }
        catch (AiUnavailableException ex) { return new(false, ex.Message); }

        for (var i = 0; i < chunks.Count; i++) chunks[i].Embedding = AiEmbeddingService.ToBytes(vectors[i]);
        document.EmbeddingModel = _options.EmbedModel;
        await _db.SaveChangesAsync(CancellationToken.None);
        Invalidate();
        await _audit.LogAsync("KnowledgeDocumentReindexed", "KnowledgeDocument", id.ToString(), $"{document.Title}: {_options.EmbedModel}");
        return new(true, $"\"{document.Title}\" was re-indexed with {_options.EmbedModel}.", id);
    }

    // ------------------------------------------------------------------ searching

    /// <summary>True when at least one passage could be shown to this role (a cheap check before the question is embedded).</summary>
    public async Task<bool> HasDocumentsForAsync(string role, CancellationToken ct) =>
        Enabled && (await LoadIndexAsync(ct)).Any(e => RoleAllowed(e.Roles, role));

    /// <summary>
    /// The passages of the documents this ROLE may read that are most similar to the question: best first, at most AI_KB_TOP_K,
    /// each at least AI_KB_MIN_SCORE similar. An empty list means "nothing in the company documents answers this".
    /// </summary>
    public async Task<List<KnowledgeHit>> SearchAsync(string role, string question, CancellationToken ct)
    {
        question = question?.Trim() ?? "";
        if (!Enabled || question.Length == 0 || role.Length == 0) return [];

        var allowed = (await LoadIndexAsync(ct)).Where(e => RoleAllowed(e.Roles, role)).ToList();
        if (allowed.Count == 0) return [];

        var vector = (await _embedding.EmbedAsync([question], ct))[0];
        var best = allowed
            .Select(e => (Entry: e, Score: AiEmbeddingService.Dot(vector, e.Vector)))
            .Where(x => x.Score >= _options.MinScore)
            .OrderByDescending(x => x.Score)
            .GroupBy(x => x.Entry.DocumentId)
            .SelectMany(g => g.Take(2))                                   // no document may fill every slot
            .OrderByDescending(x => x.Score)
            .Take(_options.TopK)
            .ToList();
        if (best.Count == 0) return [];

        var ids = best.Select(b => b.Entry.ChunkId).ToList();
        var chunks = await _db.KnowledgeChunks.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Position, c.Text }).ToDictionaryAsync(c => c.Id, ct);
        return best.Where(b => chunks.ContainsKey(b.Entry.ChunkId))
            .Select(b => new KnowledgeHit(b.Entry.DocumentId, b.Entry.Title, chunks[b.Entry.ChunkId].Position, chunks[b.Entry.ChunkId].Text, Math.Round(b.Score, 3)))
            .ToList();
    }

    /// <summary>
    /// Numbers the passages for the prompt: "[1] Title: text". Passages of the same document share one number. Returns the text and the titles in number order.
    /// </summary>
    public static (string Text, List<string> Titles) NumberPassages(IEnumerable<KnowledgeHit> hits)
    {
        var titles = new List<string>();
        var text = new System.Text.StringBuilder();
        foreach (var hit in hits)
        {
            var n = titles.IndexOf(hit.Title) + 1;
            if (n == 0) { titles.Add(hit.Title); n = titles.Count; }
            text.Append('[').Append(n).Append("] ").Append(hit.Title).Append(": ").AppendLine(hit.Text.Replace("\n", " "));
        }
        return (text.ToString().Trim(), titles);
    }

    private async Task<List<Entry>> LoadIndexAsync(CancellationToken ct)
    {
        if (_loadedVersion == _version && _loadedModel == _options.EmbedModel) return _index;
        await IndexLock.WaitAsync(ct);
        try
        {
            var version = _version;
            if (_loadedVersion == version && _loadedModel == _options.EmbedModel) return _index;

            // only vectors made by the CURRENT model can be compared with a question
            var rows = await _db.KnowledgeChunks.AsNoTracking()
                .Where(c => c.Document.IsEnabled && c.Document.EmbeddingModel == _options.EmbedModel)
                .Select(c => new { c.Id, c.DocumentId, c.Document.Title, c.Document.AllowedRoles, c.Embedding })
                .ToListAsync(ct);
            _index = rows.Select(r => new Entry(r.Id, r.DocumentId, r.Title, r.AllowedRoles, AiEmbeddingService.FromBytes(r.Embedding))).ToList();
            _loadedVersion = version;
            _loadedModel = _options.EmbedModel;
            _log.LogInformation("Knowledge index loaded: {Count} passages.", _index.Count);
            return _index;
        }
        finally { IndexLock.Release(); }
    }

    private static void Invalidate() => Interlocked.Increment(ref _version);

    // ------------------------------------------------------------------ roles

    private static bool RoleAllowed(string roles, string role) => roles.Contains("," + role + ",", StringComparison.Ordinal);

    public static string[] SplitRoles(string roles) => roles.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>",Admin,Requester," from the chosen names, or null when none is valid. Unknown names and Supplier are dropped.</summary>
    private static string? RolesText(IEnumerable<string> roles)
    {
        var valid = AllowedRoleNames.Where(r => roles.Contains(r)).ToList();
        return valid.Count == 0 ? null : "," + string.Join(",", valid) + ",";
    }

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max];
}
