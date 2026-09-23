using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GraphEngineering.Api.Persistence.Migrations;

[DbContext(typeof(WorkflowDbContext))]
public sealed class WorkflowDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => RunMigrationModel.Build(modelBuilder);
}
