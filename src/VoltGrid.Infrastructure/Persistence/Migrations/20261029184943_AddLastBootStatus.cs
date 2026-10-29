using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoltGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLastBootStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastBootStatus",
                table: "ChargingStations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastBootStatus",
                table: "ChargingStations");
        }
    }
}
