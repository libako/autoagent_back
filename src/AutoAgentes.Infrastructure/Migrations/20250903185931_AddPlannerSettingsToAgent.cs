using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoAgentes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannerSettingsToAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowUnknownTools",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedToolsCsv",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedToolsCsv",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlannerMaxSteps",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlannerMaxTokens",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannerStop",
                table: "Agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PlannerTemperature",
                table: "Agents",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PlannerTopP",
                table: "Agents",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowUnknownTools",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "AllowedToolsCsv",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "BlockedToolsCsv",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PlannerMaxSteps",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PlannerMaxTokens",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PlannerStop",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PlannerTemperature",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "PlannerTopP",
                table: "Agents");
        }
    }
}
