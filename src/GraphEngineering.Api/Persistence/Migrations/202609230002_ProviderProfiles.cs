using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GraphEngineering.Api.Persistence.Migrations;

[DbContext(typeof(WorkflowDbContext))]
[Migration("20260923000200_ProviderProfiles")]
public sealed class ProviderProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("ProviderProfiles", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
            Revision = table.Column<long>(type: "INTEGER", nullable: false),
            ConnectionVersion = table.Column<long>(type: "INTEGER", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            Protocol = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
            BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
            ModelId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
            AuthMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
            TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
            MaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
            AllowPrivateNetwork = table.Column<bool>(type: "INTEGER", nullable: false),
            AllowInsecureHttp = table.Column<bool>(type: "INTEGER", nullable: false),
            LastTestJson = table.Column<string>(type: "TEXT", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_ProviderProfiles", record => record.Id));
        migrationBuilder.CreateTable("ProviderCredentials", table => new
        {
            ProviderId = table.Column<Guid>(type: "TEXT", nullable: false),
            Ciphertext = table.Column<byte[]>(type: "BLOB", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ProviderCredentials", record => record.ProviderId);
            table.ForeignKey("FK_ProviderCredentials_ProviderProfiles_ProviderId", record => record.ProviderId, "ProviderProfiles", "Id", onDelete: ReferentialAction.Cascade);
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ProviderCredentials");
        migrationBuilder.DropTable("ProviderProfiles");
    }
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ProviderMigrationModel.Build(modelBuilder);
}

internal static class ProviderMigrationModel
{
    internal static void Build(ModelBuilder modelBuilder)
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
            entity.HasKey("Id"); entity.HasIndex("UpdatedAtUtc"); entity.ToTable("Workflows");
        });
        modelBuilder.Entity("GraphEngineering.Api.Providers.ProviderRecord", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("TEXT");
            entity.Property<string>("Name").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
            entity.Property<long>("Revision").IsConcurrencyToken().HasColumnType("INTEGER");
            entity.Property<long>("ConnectionVersion").HasColumnType("INTEGER");
            entity.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
            entity.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
            entity.Property<string>("Protocol").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            entity.Property<string>("BaseUrl").IsRequired().HasMaxLength(2048).HasColumnType("TEXT");
            entity.Property<string>("ModelId").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            entity.Property<string>("AuthMode").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
            entity.Property<int>("TimeoutSeconds").HasColumnType("INTEGER");
            entity.Property<int>("MaxOutputTokens").HasColumnType("INTEGER");
            entity.Property<bool>("AllowPrivateNetwork").HasColumnType("INTEGER");
            entity.Property<bool>("AllowInsecureHttp").HasColumnType("INTEGER");
            entity.Property<string>("LastTestJson").HasColumnType("TEXT");
            entity.HasKey("Id"); entity.ToTable("ProviderProfiles");
        });
        modelBuilder.Entity("GraphEngineering.Api.Providers.ProviderCredential", entity =>
        {
            entity.Property<Guid>("ProviderId").ValueGeneratedNever().HasColumnType("TEXT");
            entity.Property<byte[]>("Ciphertext").IsRequired().HasColumnType("BLOB");
            entity.HasKey("ProviderId"); entity.ToTable("ProviderCredentials");
            entity.HasOne("GraphEngineering.Api.Providers.ProviderRecord", null).WithOne("Credential")
                .HasForeignKey("GraphEngineering.Api.Providers.ProviderCredential", "ProviderId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
        modelBuilder.Entity("GraphEngineering.Api.Providers.ProviderRecord", entity => entity.Navigation("Credential"));
    }
}
