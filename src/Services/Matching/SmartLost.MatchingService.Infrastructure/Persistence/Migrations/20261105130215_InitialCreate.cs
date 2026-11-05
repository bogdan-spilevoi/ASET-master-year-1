using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLost.MatchingService.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    private static readonly string[] _indexColumns1 = ["State", "NextAttemptAtUtc"];

    private static readonly string[] _indexColumns2 = ["ListingId", "PhotoId", "PhotoRevision", "ModelVersion", "PreprocessingVersion"];

    private static readonly string[] _indexColumns3 = ["PhotoId", "ModelVersion", "PreprocessingVersion"];

    private static readonly string[] _indexColumns4 = ["Type", "Eligible"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EmbeddingPreparationTasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                PhotoRevision = table.Column<int>(type: "integer", nullable: false),
                ModelVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PreprocessingVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmbeddingPreparationTasks", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ImageEmbeddings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                PhotoRevision = table.Column<int>(type: "integer", nullable: false),
                ModelVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PreprocessingVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Vector = table.Column<float[]>(type: "real[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImageEmbeddings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SearchListings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SourceRevision = table.Column<int>(type: "integer", nullable: false),
                ChangeSequence = table.Column<long>(type: "bigint", nullable: false),
                Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                CurrentPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                CurrentPhotoRevision = table.Column<int>(type: "integer", nullable: true),
                CategoryKeys = table.Column<string[]>(type: "text[]", nullable: false),
                Colour = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Traits = table.Column<string[]>(type: "text[]", nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Eligible = table.Column<bool>(type: "boolean", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SearchListings", x => x.Id);
                table.CheckConstraint("CK_SearchListings_CurrentPhoto", "(\"CurrentPhotoId\" IS NULL) = (\"CurrentPhotoRevision\" IS NULL)");
            });

        migrationBuilder.CreateIndex(
            name: "IX_EmbeddingPreparationTasks_State_NextAttemptAtUtc",
            table: "EmbeddingPreparationTasks",
            columns: _indexColumns1);

        migrationBuilder.CreateIndex(
            name: "UX_EmbeddingPreparationTasks_Target",
            table: "EmbeddingPreparationTasks",
            columns: _indexColumns2,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ImageEmbeddings_PhotoId_ModelVersion_PreprocessingVersion",
            table: "ImageEmbeddings",
            columns: _indexColumns3);

        migrationBuilder.CreateIndex(
            name: "UX_ImageEmbeddings_Target",
            table: "ImageEmbeddings",
            columns: _indexColumns2,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SearchListings_Type_Eligible",
            table: "SearchListings",
            columns: _indexColumns4);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EmbeddingPreparationTasks");

        migrationBuilder.DropTable(
            name: "ImageEmbeddings");

        migrationBuilder.DropTable(
            name: "SearchListings");
    }
}
