using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartLost.ListingsService.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ReusableCategoryCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Categories_NormalizedName",
            table: "Categories");

        migrationBuilder.AddColumn<string>(
            name: "DraftBroadClass",
            table: "Listings",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DraftCategoryName",
            table: "Listings",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BroadClass",
            table: "Categories",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "MergedIntoId",
            table: "Categories",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CategoryCatalogState",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CategoryCatalogState", x => x.Id);
            });

        migrationBuilder.InsertData(
            table: "CategoryCatalogState",
            columns: ["Id", "Version"],
            values: new object[] { 1, 0L });

        // Older databases permitted duplicate names. Preserve every distinct listing assignment.
        migrationBuilder.Sql("""
            CREATE TEMP TABLE category_duplicates ON COMMIT DROP AS
            SELECT "Id" AS old_id, first_value("Id") OVER (PARTITION BY "NormalizedName" ORDER BY "Id") AS keep_id
            FROM "Categories";
            INSERT INTO "ListingCategories" ("ListingId", "CategoryId")
                SELECT x."ListingId", d.keep_id FROM "ListingCategories" x
                JOIN category_duplicates d ON d.old_id = x."CategoryId" AND d.old_id <> d.keep_id
                ON CONFLICT DO NOTHING;
            DELETE FROM "ListingCategories" x USING category_duplicates d
                WHERE x."CategoryId" = d.old_id AND d.old_id <> d.keep_id;
            DELETE FROM "Categories" c USING category_duplicates d
                WHERE c."Id" = d.old_id AND d.old_id <> d.keep_id;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Categories_MergedIntoId",
            table: "Categories",
            column: "MergedIntoId");

        migrationBuilder.CreateIndex(
            name: "IX_Categories_NormalizedName",
            table: "Categories",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Categories_Categories_MergedIntoId",
            table: "Categories",
            column: "MergedIntoId",
            principalTable: "Categories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
            CREATE PROCEDURE merge_listing_category(source_id uuid, target_id uuid)
            LANGUAGE plpgsql AS $merge$
            BEGIN
                LOCK TABLE "Categories" IN EXCLUSIVE MODE;
                LOCK TABLE "ListingCategories" IN SHARE ROW EXCLUSIVE MODE;
                IF source_id = target_id OR
                    (SELECT count(*) FROM "Categories" WHERE "Id" IN (source_id, target_id) AND "MergedIntoId" IS NULL) <> 2 THEN
                    RAISE EXCEPTION 'Choose two distinct active categories';
                END IF;
                INSERT INTO "ListingCategories" ("ListingId", "CategoryId")
                    SELECT "ListingId", target_id FROM "ListingCategories" WHERE "CategoryId" = source_id
                    ON CONFLICT DO NOTHING;
                DELETE FROM "ListingCategories" WHERE "CategoryId" = source_id;
                UPDATE "Categories" SET "BroadClass" = (SELECT "BroadClass" FROM "Categories" WHERE "Id" = source_id)
                    WHERE "Id" = target_id AND "BroadClass" IS NULL;
                UPDATE "Categories" SET "MergedIntoId" = target_id
                    WHERE "Id" = source_id OR "MergedIntoId" = source_id;
                UPDATE "CategoryCatalogState" SET "Version" = "Version" + 1 WHERE "Id" = 1;
            END;
            $merge$;
            REVOKE ALL ON PROCEDURE merge_listing_category(uuid, uuid) FROM PUBLIC;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE merge_listing_category(uuid, uuid);");
        migrationBuilder.DropForeignKey(
            name: "FK_Categories_Categories_MergedIntoId",
            table: "Categories");

        migrationBuilder.DropTable(
            name: "CategoryCatalogState");

        migrationBuilder.DropIndex(
            name: "IX_Categories_MergedIntoId",
            table: "Categories");

        migrationBuilder.DropIndex(
            name: "IX_Categories_NormalizedName",
            table: "Categories");

        migrationBuilder.DropColumn(
            name: "DraftBroadClass",
            table: "Listings");

        migrationBuilder.DropColumn(
            name: "DraftCategoryName",
            table: "Listings");

        migrationBuilder.DropColumn(
            name: "BroadClass",
            table: "Categories");

        migrationBuilder.DropColumn(
            name: "MergedIntoId",
            table: "Categories");

        migrationBuilder.CreateIndex(
            name: "IX_Categories_NormalizedName",
            table: "Categories",
            column: "NormalizedName");
    }
}
