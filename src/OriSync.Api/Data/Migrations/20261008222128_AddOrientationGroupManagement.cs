using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OriSync.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrientationGroupManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "orientations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "badge_color",
                table: "groups",
                type: "text",
                nullable: false,
                defaultValue: "#64748B");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deactivated_at",
                table: "groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "groups",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Normalize any pre-feature rows before enforcing the uppercase
            // display-name invariant. Historical foreign keys use group IDs,
            // so this does not alter their identity.
            migrationBuilder.Sql(
                "UPDATE groups SET name = upper(btrim(name)), normalized_name = lower(btrim(name));");

            migrationBuilder.CreateIndex(
                name: "ux_orientations_single_active",
                table: "orientations",
                column: "is_active",
                unique: true,
                filter: "is_active");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orientations_dates_match_year",
                table: "orientations",
                sql: "EXTRACT(YEAR FROM start_date) = year AND EXTRACT(YEAR FROM end_date) = year");

            migrationBuilder.AddCheckConstraint(
                name: "ck_groups_badge_color",
                table: "groups",
                sql: "badge_color ~ '^#[0-9A-F]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_groups_uppercase_name",
                table: "groups",
                sql: "name = upper(btrim(name))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_orientations_single_active",
                table: "orientations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orientations_dates_match_year",
                table: "orientations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_groups_badge_color",
                table: "groups");

            migrationBuilder.DropCheckConstraint(
                name: "ck_groups_uppercase_name",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "orientations");

            migrationBuilder.DropColumn(
                name: "badge_color",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "deactivated_at",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "groups");
        }
    }
}
