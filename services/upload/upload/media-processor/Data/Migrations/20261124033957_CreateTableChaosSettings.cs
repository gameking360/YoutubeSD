using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaProcessor.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableChaosSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chaos_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    latency_ms = table.Column<int>(type: "int", nullable: true),
                    failure_rate = table.Column<double>(type: "double", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chaos_settings", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "chaos_settings",
                columns: new[] { "id", "failure_rate", "latency_ms", "updated_at" },
                values: new object[] { 1, null, null, new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chaos_settings");
        }
    }
}
