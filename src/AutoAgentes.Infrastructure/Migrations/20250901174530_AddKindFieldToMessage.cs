using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoAgentes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKindFieldToMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Messages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Messages");
        }
    }
}
