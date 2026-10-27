using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoltGrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStationBootInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirmwareVersion",
                table: "ChargingStations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastBootAt",
                table: "ChargingStations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirmwareVersion",
                table: "ChargingStations");

            migrationBuilder.DropColumn(
                name: "LastBootAt",
                table: "ChargingStations");
        }
    }
}
