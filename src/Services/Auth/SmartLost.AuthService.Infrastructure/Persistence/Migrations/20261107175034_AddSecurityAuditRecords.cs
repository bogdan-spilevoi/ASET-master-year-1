using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLost.AuthService.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSecurityAuditRecords : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SecurityAuditRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SubjectIdentifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                ClientIp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                FailureReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SecurityAuditRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditRecords_EventType",
            table: "SecurityAuditRecords",
            column: "EventType");

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditRecords_OccurredAtUtc",
            table: "SecurityAuditRecords",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_SecurityAuditRecords_SubjectIdentifier",
            table: "SecurityAuditRecords",
            column: "SubjectIdentifier");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SecurityAuditRecords");
    }
}
