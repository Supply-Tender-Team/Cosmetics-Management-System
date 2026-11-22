using DotNetEnv;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Knowledge;

/// <summary>
/// Settings of the knowledge base (AI-18), from .env:
///   AI_KB_ENABLED=true            false = the assistant never searches the company documents
///   AI_KB_EMBED_MODEL=bge-m3      the local Ollama embedding model (ollama pull bge-m3). Changing it needs "Re-index" on the Admin page.
///   AI_KB_TOP_K=4                 passages given to the assistant for one question
///   AI_KB_MIN_SCORE=0.45          a passage must be at least this similar (0 to 1) to the question, otherwise it is not used
///   AI_KB_CHUNK_WORDS=300         size of one passage, in words
///   AI_KB_OVERLAP_WORDS=50        words repeated between two neighbouring passages
///   AI_KB_MAX_MB=10               largest file an Admin may upload
///   AI_KB_EMBED_TIMEOUT_SECONDS=120   time allowed for one batch of passages to be embedded
/// </summary>
public class KnowledgeOptions
{
    public bool Enabled { get; set; } = true;
    public string EmbedModel { get; set; } = "bge-m3";
    public int TopK { get; set; } = 4;
    public double MinScore { get; set; } = 0.45;
    public int ChunkWords { get; set; } = 300;
    public int OverlapWords { get; set; } = 50;
    public int MaxMb { get; set; } = 10;
    public int EmbedTimeoutSeconds { get; set; } = 120;
}

/// <summary>Registers the knowledge base. Found automatically by Program.cs (IFeatureModule).</summary>
public class KnowledgeModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KnowledgeOptions>(o =>
        {
            o.Enabled = Env.GetBool("AI_KB_ENABLED", o.Enabled);
            o.EmbedModel = Env.GetString("AI_KB_EMBED_MODEL", o.EmbedModel);
            o.TopK = Math.Clamp(Env.GetInt("AI_KB_TOP_K", o.TopK), 1, 12);
            o.MinScore = Math.Clamp(Env.GetDouble("AI_KB_MIN_SCORE", o.MinScore), 0, 1);
            o.ChunkWords = Math.Clamp(Env.GetInt("AI_KB_CHUNK_WORDS", o.ChunkWords), 50, 1000);
            o.OverlapWords = Math.Clamp(Env.GetInt("AI_KB_OVERLAP_WORDS", o.OverlapWords), 0, 300);
            o.MaxMb = Math.Clamp(Env.GetInt("AI_KB_MAX_MB", o.MaxMb), 1, 50);
            o.EmbedTimeoutSeconds = Math.Clamp(Env.GetInt("AI_KB_EMBED_TIMEOUT_SECONDS", o.EmbedTimeoutSeconds), 10, 900);
        });
        services.AddScoped<AiEmbeddingService>();
        services.AddScoped<KnowledgeBaseService>();
    }
}
