using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OriSync.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recovery_code_hash",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recovery_code_issued_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recovery_code_hash",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "recovery_code_issued_at",
                table: "accounts");
        }
    }
}
