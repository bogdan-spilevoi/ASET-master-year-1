using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLost.AuthService.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddEmailConfirmation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "EmailConfirmed",
            table: "Users",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "EmailConfirmedAtUtc",
            table: "Users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "EmailConfirmationTokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmailConfirmationTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_EmailConfirmationTokens_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EmailConfirmationTokens_ExpiresAtUtc",
            table: "EmailConfirmationTokens",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_EmailConfirmationTokens_TokenHash",
            table: "EmailConfirmationTokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EmailConfirmationTokens_UserId",
            table: "EmailConfirmationTokens",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EmailConfirmationTokens");

        migrationBuilder.DropColumn(
            name: "EmailConfirmed",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "EmailConfirmedAtUtc",
            table: "Users");
    }
}
