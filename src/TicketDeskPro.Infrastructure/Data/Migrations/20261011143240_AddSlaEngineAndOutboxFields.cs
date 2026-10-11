using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketDeskPro.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaEngineAndOutboxFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWarningSent",
                table: "SlaTimers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PausedAt",
                table: "SlaTimers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "EmailOutbox",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "SlaBreaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    BreachType = table.Column<int>(type: "integer", nullable: false),
                    BreachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EscalatedToPriority = table.Column<int>(type: "integer", nullable: false),
                    SlaTimerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlaBreaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlaBreaches_SlaTimers_SlaTimerId",
                        column: x => x.SlaTimerId,
                        principalTable: "SlaTimers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SlaBreaches_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_Status_NextAttemptAt",
                table: "EmailOutbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SlaBreaches_SlaTimerId",
                table: "SlaBreaches",
                column: "SlaTimerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlaBreaches_TicketId_BreachType",
                table: "SlaBreaches",
                columns: new[] { "TicketId", "BreachType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlaBreaches");

            migrationBuilder.DropIndex(
                name: "IX_EmailOutbox_Status_NextAttemptAt",
                table: "EmailOutbox");

            migrationBuilder.DropColumn(
                name: "IsWarningSent",
                table: "SlaTimers");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "SlaTimers");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "EmailOutbox");
        }
    }
}
