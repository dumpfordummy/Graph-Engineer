using System.Text.Json;
using GraphEngineering.Core.Documents;
using Microsoft.EntityFrameworkCore;

namespace GraphEngineering.Api.Persistence;

public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowRecord> Workflows => Set<WorkflowRecord>();

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
