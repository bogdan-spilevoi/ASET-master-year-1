using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLost.ListingsService.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Categories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                NormalizedName = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Categories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Listings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Location = table.Column<string>(type: "text", nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                Colour = table.Column<string>(type: "text", nullable: true),
                Traits = table.Column<string[]>(type: "text[]", nullable: false),
                State = table.Column<int>(type: "integer", nullable: false),
                ClosureReason = table.Column<int>(type: "integer", nullable: true),
                Revision = table.Column<int>(type: "integer", nullable: false),
                ChangeSequence = table.Column<long>(type: "bigint", nullable: false),
                PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Listings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ListingCategories",
            columns: table => new
            {
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingCategories", x => new { x.ListingId, x.CategoryId });
                table.ForeignKey(
                    name: "FK_ListingCategories_Categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "Categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ListingCategories_Listings_ListingId",
                    column: x => x.ListingId,
                    principalTable: "Listings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MatchDecisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LostListingId = table.Column<Guid>(type: "uuid", nullable: false),
                FoundListingId = table.Column<Guid>(type: "uuid", nullable: false),
                LostRevision = table.Column<int>(type: "integer", nullable: false),
                FoundRevision = table.Column<int>(type: "integer", nullable: false),
                State = table.Column<int>(type: "integer", nullable: false),
                DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MatchDecisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_MatchDecisions_Listings_FoundListingId",
                    column: x => x.FoundListingId,
                    principalTable: "Listings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MatchDecisions_Listings_LostListingId",
                    column: x => x.LostListingId,
                    principalTable: "Listings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Photos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BlobKey = table.Column<string>(type: "text", nullable: false),
                Format = table.Column<string>(type: "text", nullable: false),
                ByteSize = table.Column<long>(type: "bigint", nullable: false),
                Revision = table.Column<int>(type: "integer", nullable: false),
                ListingId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Photos", x => x.Id);
                table.ForeignKey(
                    name: "FK_Photos_Listings_ListingId",
                    column: x => x.ListingId,
                    principalTable: "Listings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Recoveries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MatchDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false),
                ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Recoveries", x => x.Id);
                table.ForeignKey(
                    name: "FK_Recoveries_MatchDecisions_MatchDecisionId",
                    column: x => x.MatchDecisionId,
                    principalTable: "MatchDecisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CredibilityEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RecoveryId = table.Column<Guid>(type: "uuid", nullable: false),
                BeneficiaryId = table.Column<Guid>(type: "uuid", nullable: false),
                HelpedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Points = table.Column<int>(type: "integer", nullable: false),
                AwardedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CredibilityEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_CredibilityEntries_Recoveries_RecoveryId",
                    column: x => x.RecoveryId,
                    principalTable: "Recoveries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Categories_NormalizedName",
            table: "Categories",
            column: "NormalizedName");

        migrationBuilder.CreateIndex(
            name: "IX_CredibilityEntries_BeneficiaryId",
            table: "CredibilityEntries",
            column: "BeneficiaryId");

        migrationBuilder.CreateIndex(
            name: "IX_CredibilityEntries_RecoveryId",
            table: "CredibilityEntries",
            column: "RecoveryId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ListingCategories_CategoryId",
            table: "ListingCategories",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_Listings_AuthorId",
            table: "Listings",
            column: "AuthorId");

        migrationBuilder.CreateIndex(
            name: "IX_Listings_State_Type",
            table: "Listings",
            columns: ["State", "Type"]);

        migrationBuilder.CreateIndex(
            name: "IX_MatchDecisions_FoundListingId",
            table: "MatchDecisions",
            column: "FoundListingId");

        migrationBuilder.CreateIndex(
            name: "IX_MatchDecisions_LostListingId",
            table: "MatchDecisions",
            column: "LostListingId");

        migrationBuilder.CreateIndex(
            name: "IX_Photos_ListingId",
            table: "Photos",
            column: "ListingId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Recoveries_MatchDecisionId",
            table: "Recoveries",
            column: "MatchDecisionId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CredibilityEntries");

        migrationBuilder.DropTable(
            name: "ListingCategories");

        migrationBuilder.DropTable(
            name: "Photos");

        migrationBuilder.DropTable(
            name: "Recoveries");

        migrationBuilder.DropTable(
            name: "Categories");

        migrationBuilder.DropTable(
            name: "MatchDecisions");

        migrationBuilder.DropTable(
            name: "Listings");
    }
}
