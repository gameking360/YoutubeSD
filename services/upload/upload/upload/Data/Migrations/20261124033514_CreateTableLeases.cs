using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upload.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leases",
                columns: table => new
                {
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    owner = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leases", x => x.name);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "leases",
                columns: new[] { "name", "expires_at", "owner", "version" },
                values: new object[] { "outbox", new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leases");
        }
    }
}
