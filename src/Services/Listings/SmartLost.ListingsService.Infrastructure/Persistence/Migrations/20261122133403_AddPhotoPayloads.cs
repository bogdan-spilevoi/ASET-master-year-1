using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLost.ListingsService.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPhotoPayloads : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PhotoPayloads",
            columns: table => new
            {
                PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                Bytes = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PhotoPayloads", x => x.PhotoId);
                table.ForeignKey(
                    name: "FK_PhotoPayloads_Photos_PhotoId",
                    column: x => x.PhotoId,
                    principalTable: "Photos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PhotoPayloads");
    }
}
