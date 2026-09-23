using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GraphEngineering.Api.Persistence.Migrations;

[DbContext(typeof(WorkflowDbContext))]
public sealed class WorkflowDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");
        modelBuilder.Entity("GraphEngineering.Api.Persistence.WorkflowRecord", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("TEXT");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
            entity.Property<string>("DefinitionJson").IsRequired().HasColumnType("TEXT");
            entity.Property<string>("Description").IsRequired().HasMaxLength(2000).HasColumnType("TEXT");
            entity.Property<string>("LayoutJson").IsRequired().HasColumnType("TEXT");
            entity.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
            entity.Property<long>("Revision").IsConcurrencyToken().HasColumnType("INTEGER");
            entity.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("UpdatedAtUtc");
            entity.ToTable("Workflows");
        });
    }
}
