using System.Text.Json;
using GraphEngineering.Core.Documents;
using GraphEngineering.Api.Providers;
using Microsoft.EntityFrameworkCore;

namespace GraphEngineering.Api.Persistence;

public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowRecord> Workflows => Set<WorkflowRecord>();
    public DbSet<ProviderRecord> ProviderProfiles => Set<ProviderRecord>();
    public DbSet<ProviderCredential> ProviderCredentials => Set<ProviderCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkflowRecord>();
        entity.ToTable("Workflows");
        entity.HasKey(workflow => workflow.Id);
        entity.Property(workflow => workflow.Id).ValueGeneratedNever();
        entity.Property(workflow => workflow.Name).HasMaxLength(120).IsRequired();
        entity.Property(workflow => workflow.Description).HasMaxLength(2000).IsRequired();
        entity.Property(workflow => workflow.Revision).IsConcurrencyToken();
        entity.Property(workflow => workflow.DefinitionJson).IsRequired();
        entity.Property(workflow => workflow.LayoutJson).IsRequired();
        entity.HasIndex(workflow => workflow.UpdatedAtUtc);
        var provider = modelBuilder.Entity<ProviderRecord>();
        provider.ToTable("ProviderProfiles");
        provider.HasKey(x => x.Id);
        provider.Property(x => x.Id).ValueGeneratedNever();
        provider.Property(x => x.Name).HasMaxLength(120).IsRequired();
        provider.Property(x => x.Protocol).HasMaxLength(40).IsRequired();
        provider.Property(x => x.BaseUrl).HasMaxLength(2048).IsRequired();
        provider.Property(x => x.ModelId).HasMaxLength(200).IsRequired();
        provider.Property(x => x.AuthMode).HasMaxLength(20).IsRequired();
        provider.Property(x => x.Revision).IsConcurrencyToken();
        var credential = modelBuilder.Entity<ProviderCredential>();
        credential.ToTable("ProviderCredentials");
        credential.HasKey(x => x.ProviderId);
        credential.Property(x => x.ProviderId).ValueGeneratedNever();
        credential.Property(x => x.Ciphertext).IsRequired();
        provider.HasOne(x => x.Credential).WithOne().HasForeignKey<ProviderCredential>(x => x.ProviderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WorkflowRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string DefinitionJson { get; set; } = "";
    public string LayoutJson { get; set; } = "";

    public WorkflowMetadata Metadata() => new(Id, Name, Description, Revision,
        new DateTimeOffset(DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc)));
    public WorkflowDocument Document() => new(1, Metadata(),
        JsonSerializer.Deserialize<WorkflowDefinition>(DefinitionJson, DocumentJson.Options)!,
        JsonSerializer.Deserialize<EditorLayout>(LayoutJson, DocumentJson.Options)!);
}
