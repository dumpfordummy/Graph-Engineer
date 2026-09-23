using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GraphEngineering.Api.Persistence.Migrations;

[DbContext(typeof(WorkflowDbContext))]
[Migration("202609230001_InitialWorkflows")]
public sealed class InitialWorkflows : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("Workflows", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
            Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
            Revision = table.Column<long>(type: "INTEGER", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
            LayoutJson = table.Column<string>(type: "TEXT", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Workflows", record => record.Id));
        migrationBuilder.CreateIndex("IX_Workflows_UpdatedAtUtc", "Workflows", "UpdatedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Workflows");

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
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
