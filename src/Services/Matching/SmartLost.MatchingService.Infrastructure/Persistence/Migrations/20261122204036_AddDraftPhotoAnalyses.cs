using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLost.MatchingService.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDraftPhotoAnalyses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PhotoAnalyses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                PhotoRevision = table.Column<int>(type: "integer", nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                State = table.Column<string>(type: "text", nullable: false),
                Attempt = table.Column<int>(type: "integer", nullable: false),
                AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Lease = table.Column<Guid>(type: "uuid", nullable: false),
                SuggestionsJson = table.Column<string>(type: "jsonb", nullable: true),
                ModelVersion = table.Column<string>(type: "text", nullable: true),
                Dimensions = table.Column<int>(type: "integer", nullable: true),
                ErrorCode = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PhotoAnalyses", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PhotoAnalyses_State_AvailableAt",
            table: "PhotoAnalyses",
            columns: ["State", "AvailableAt"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PhotoAnalyses");
    }
}
