using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketDeskPro.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixSlaPolicyIndexAndSystemTransitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TicketStatusHistories_Users_ChangedById",
                table: "TicketStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_SlaPolicies_Priority",
                table: "SlaPolicies");

            migrationBuilder.AlterColumn<Guid>(
                name: "ChangedById",
                table: "TicketStatusHistories",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_SlaPolicies_Priority",
                table: "SlaPolicies",
                column: "Priority",
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.AddForeignKey(
                name: "FK_TicketStatusHistories_Users_ChangedById",
                table: "TicketStatusHistories",
                column: "ChangedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TicketStatusHistories_Users_ChangedById",
                table: "TicketStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_SlaPolicies_Priority",
                table: "SlaPolicies");

            migrationBuilder.AlterColumn<Guid>(
                name: "ChangedById",
                table: "TicketStatusHistories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlaPolicies_Priority",
                table: "SlaPolicies",
                column: "Priority",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TicketStatusHistories_Users_ChangedById",
                table: "TicketStatusHistories",
                column: "ChangedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
