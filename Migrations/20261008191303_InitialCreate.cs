using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

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
                name: "Piezas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Categoria = table.Column<string>(type: "text", nullable: false),
                    StockFisico = table.Column<int>(type: "integer", nullable: false),
                    Reservado = table.Column<int>(type: "integer", nullable: false),
                    StockMinimo = table.Column<int>(type: "integer", nullable: false),
                    TiempoReposicion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Piezas", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Piezas",
                columns: new[] { "Id", "Categoria", "Nombre", "Reservado", "Sku", "StockFisico", "StockMinimo", "TiempoReposicion" },
                values: new object[,]
                {
                    { 1, "Husillos CNC", "Rodamiento Cerámico Híbrido 7008-C", 6, "BAL-7008C", 8, 4, "3 semanas" },
                    { 2, "Sellado", "Sello Laberíntico Viton 45mm", 2, "SEL-VT45", 2, 3, "5 días" },
                    { 3, "Instrumentación", "Sensor Térmico PT100 Calibrado", 3, "TH-PT100", 1, 2, "12 días" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Piezas");
        }
    }
}
