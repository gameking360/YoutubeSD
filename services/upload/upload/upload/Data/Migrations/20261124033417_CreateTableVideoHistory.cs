using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Upload.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableVideoHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "video_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    video_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    from_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    detail = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    message_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    instance_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    at = table.Column<DateTime>(type: "datetime(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_video_history", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_video_history_video",
                table: "video_history",
                columns: new[] { "video_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "video_history");
        }
    }
}
