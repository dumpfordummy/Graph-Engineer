using GraphEngineering.Api.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GraphEngineering.Api.Persistence.Migrations;

[DbContext(typeof(WorkflowDbContext))]
[Migration("20260924000100_Runs")]
public sealed class Runs : Migration
{
    protected override void Up(MigrationBuilder migration)
    {
        migration.CreateTable("Runs", table => new
        {
            Id = table.Column<Guid>("TEXT", nullable: false),
            SubmissionId = table.Column<Guid>("TEXT", nullable: false),
            Fingerprint = table.Column<string>("TEXT", nullable: false),
            WorkflowId = table.Column<Guid>("TEXT", nullable: false),
            WorkflowName = table.Column<string>("TEXT", nullable: false),
            WorkflowRevision = table.Column<long>("INTEGER", nullable: false),
            State = table.Column<string>("TEXT", nullable: false),
            ActiveSlot = table.Column<int>("INTEGER", nullable: true),
            CreatedAtUtc = table.Column<DateTime>("TEXT", nullable: false),
            StartedAtUtc = table.Column<DateTime>("TEXT", nullable: true),
            FinishedAtUtc = table.Column<DateTime>("TEXT", nullable: true),
            LastSequence = table.Column<long>("INTEGER", nullable: false),
            FailureCode = table.Column<string>("TEXT", nullable: true),
            SnapshotJson = table.Column<string>("TEXT", nullable: false),
            InputJson = table.Column<string>("TEXT", nullable: false),
            ProfilesJson = table.Column<string>("TEXT", nullable: false),
            NodesJson = table.Column<string>("TEXT", nullable: false),
            ResultJson = table.Column<string>("TEXT", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_Runs", x => x.Id));
        migration.CreateTable("RunEvents", table => new
        {
            RunId = table.Column<Guid>("TEXT", nullable: false),
            Sequence = table.Column<long>("INTEGER", nullable: false),
            AtUtc = table.Column<DateTime>("TEXT", nullable: false),
            Kind = table.Column<string>("TEXT", nullable: false),
            NodeId = table.Column<string>("TEXT", nullable: true),
            State = table.Column<string>("TEXT", nullable: true),
            Message = table.Column<string>("TEXT", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RunEvents", x => new { x.RunId, x.Sequence });
            table.ForeignKey("FK_RunEvents_Runs_RunId", x => x.RunId, "Runs", "Id", onDelete: ReferentialAction.Cascade);
        });
        migration.CreateTable("RunArtifacts", table => new
        {
            Id = table.Column<Guid>("TEXT", nullable: false),
            RunId = table.Column<Guid>("TEXT", nullable: false),
            NodeId = table.Column<string>("TEXT", nullable: false),
            Kind = table.Column<string>("TEXT", nullable: false),
            ContentType = table.Column<string>("TEXT", nullable: false),
            ByteCount = table.Column<int>("INTEGER", nullable: false),
            Sha256 = table.Column<string>("TEXT", nullable: false),
            Text = table.Column<string>("TEXT", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RunArtifacts", x => x.Id);
            table.ForeignKey("FK_RunArtifacts_Runs_RunId", x => x.RunId, "Runs", "Id", onDelete: ReferentialAction.Cascade);
        });
        migration.CreateIndex("IX_Runs_SubmissionId", "Runs", "SubmissionId", unique: true);
        migration.CreateIndex("IX_Runs_ActiveSlot", "Runs", "ActiveSlot", unique: true);
        migration.CreateIndex("IX_Runs_WorkflowId_CreatedAtUtc", "Runs", new[] { "WorkflowId", "CreatedAtUtc" });
        migration.CreateIndex("IX_Runs_CreatedAtUtc", "Runs", "CreatedAtUtc");
        migration.CreateIndex("IX_RunArtifacts_RunId", "RunArtifacts", "RunId");
    }
    protected override void Down(MigrationBuilder migration)
    {
        migration.DropTable("RunArtifacts"); migration.DropTable("RunEvents"); migration.DropTable("Runs");
    }
    protected override void BuildTargetModel(ModelBuilder model) => RunMigrationModel.Build(model);
}

internal static class RunMigrationModel
{
    internal static void Build(ModelBuilder model)
    {
        ProviderMigrationModel.Build(model);
        model.Entity("GraphEngineering.Api.Runs.RunRecord", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("TEXT");
            entity.Property<Guid>("SubmissionId").HasColumnType("TEXT");
            entity.Property<Guid>("WorkflowId").HasColumnType("TEXT");
            foreach (var name in new[] { "Fingerprint", "WorkflowName", "State", "SnapshotJson", "InputJson", "ProfilesJson", "NodesJson" }) entity.Property<string>(name).IsRequired().HasColumnType("TEXT");
            foreach (var name in new[] { "FailureCode", "ResultJson" }) entity.Property<string>(name).HasColumnType("TEXT");
            entity.Property<long>("WorkflowRevision").HasColumnType("INTEGER");
            entity.Property<long>("LastSequence").IsConcurrencyToken().HasColumnType("INTEGER");
            entity.Property<int?>("ActiveSlot").HasColumnType("INTEGER");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
            entity.Property<DateTime?>("StartedAtUtc").HasColumnType("TEXT");
            entity.Property<DateTime?>("FinishedAtUtc").HasColumnType("TEXT");
            entity.HasKey("Id"); entity.HasIndex("SubmissionId").IsUnique(); entity.HasIndex("ActiveSlot").IsUnique();
            entity.HasIndex("WorkflowId", "CreatedAtUtc"); entity.HasIndex("CreatedAtUtc"); entity.ToTable("Runs");
        });
        model.Entity("GraphEngineering.Api.Runs.RunEventRecord", entity =>
        {
            entity.Property<Guid>("RunId").HasColumnType("TEXT");
            entity.Property<long>("Sequence").HasColumnType("INTEGER");
            entity.Property<DateTime>("AtUtc").HasColumnType("TEXT");
            entity.Property<string>("Kind").IsRequired().HasColumnType("TEXT");
            foreach (var name in new[] { "NodeId", "State", "Message" }) entity.Property<string>(name).HasColumnType("TEXT");
            entity.HasKey("RunId", "Sequence"); entity.ToTable("RunEvents");
            entity.HasOne("GraphEngineering.Api.Runs.RunRecord", null).WithMany().HasForeignKey("RunId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
        model.Entity("GraphEngineering.Api.Runs.RunArtifactRecord", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("TEXT");
            entity.Property<Guid>("RunId").HasColumnType("TEXT");
            entity.Property<int>("ByteCount").HasColumnType("INTEGER");
            foreach (var name in new[] { "NodeId", "Kind", "ContentType", "Sha256", "Text" }) entity.Property<string>(name).IsRequired().HasColumnType("TEXT");
            entity.HasKey("Id"); entity.HasIndex("RunId"); entity.ToTable("RunArtifacts");
            entity.HasOne("GraphEngineering.Api.Runs.RunRecord", null).WithMany().HasForeignKey("RunId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
    }
}
