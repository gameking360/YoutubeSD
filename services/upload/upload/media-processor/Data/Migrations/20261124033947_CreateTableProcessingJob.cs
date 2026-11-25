using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaProcessor.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableProcessingJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "processing_job",
                columns: table => new
                {
                    video_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    last_message_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    instance_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    finished_at = table.Column<DateTime>(type: "datetime(3)", nullable: true),
                    last_error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_job", x => x.video_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processing_job");
        }
    }
}
