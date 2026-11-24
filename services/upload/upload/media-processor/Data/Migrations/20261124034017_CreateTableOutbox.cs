using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace MediaProcessor.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    message_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    topic = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    message_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "json", nullable: false),
                    correlation_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", nullable: false),
                    published_at = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_message_id",
                table: "outbox",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_outbox_pendentes",
                table: "outbox",
                columns: new[] { "published_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox");
        }
    }
}
