using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AlmacenTaller.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_workbench = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.location_id);
                });

            migrationBuilder.CreateTable(
                name: "parts",
                columns: table => new
                {
                    part_id = table.Column<long>(type: "bigint", nullable: false),
                    sku = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    family = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    part_group = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subgroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parts", x => x.part_id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_balances",
                columns: table => new
                {
                    part_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    on_hand = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reserved = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_balances", x => new { x.part_id, x.location_id });
                    table.ForeignKey(
                        name: "FK_inventory_balances_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "locations",
                        principalColumn: "location_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inventory_balances_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "part_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "locations",
                columns: new[] { "location_id", "active", "code", "created_at", "name" },
                values: new object[] { 100L, true, "ALM-PRINCIPAL", new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc), "Almacén Central" });

            migrationBuilder.InsertData(
                table: "parts",
                columns: new[] { "part_id", "active", "created_at", "family", "name", "part_group", "sku", "subgroup", "unit", "updated_at" },
                values: new object[,]
                {
                    { 1L, true, new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc), "Husillos CNC", "Rodamiento Cerámico Híbrido 7008-C", "Mecánico", "BAL-7008C", "Rodamientos", "pz", new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2L, true, new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc), "Sellado", "Sello Laberíntico Viton 45mm", "Mecánico", "SEL-VT45", "Sellos", "pz", new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3L, true, new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc), "Instrumentación", "Sensor Térmico PT100 Calibrado", "Eléctrico", "TH-PT100", "Sensores", "pz", new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "inventory_balances",
                columns: new[] { "location_id", "part_id", "on_hand", "reserved", "updated_at" },
                values: new object[,]
                {
                    { 100L, 1L, 8, 6, new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 100L, 2L, 2, 2, new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 100L, 3L, 1, 3, new DateTime(2026, 10, 9, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_location_id",
                table: "inventory_balances",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_locations_code",
                table: "locations",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_balances");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "parts");
        }
    }
}
