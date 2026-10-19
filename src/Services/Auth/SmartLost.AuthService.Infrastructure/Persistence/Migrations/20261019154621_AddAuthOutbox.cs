using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLost.AuthService.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAuthOutbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "IdentityVersion",
            table: "Users",
            type: "bigint",
            nullable: false,
            defaultValue: 1L);

        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                Payload = table.Column<string>(type: "text", nullable: false),
                OccurredOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ProcessedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DeadLetteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_ProcessedOn_DeadLetteredAt_NextAttemptAt",
            table: "OutboxMessages",
            columns: ["ProcessedOn", "DeadLetteredAt", "NextAttemptAt"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OutboxMessages");

        migrationBuilder.DropColumn(
            name: "IdentityVersion",
            table: "Users");
    }
}
