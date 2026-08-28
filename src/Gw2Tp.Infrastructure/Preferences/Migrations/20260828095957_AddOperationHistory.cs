using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gw2Tp.Infrastructure.Preferences.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CalculationVersionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ConfigurationVersionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operations", x => x.Id);
                    table.CheckConstraint("CK_Operations_Status", "Status IN ('planned', 'in-progress', 'completed', 'cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "OperationScenarios",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationScenarios", x => x.OperationId);
                    table.CheckConstraint("CK_OperationScenarios_Kind", "Kind IN ('market-flip', 'crafting')");
                    table.ForeignKey(
                        name: "FK_OperationScenarios_Operations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "Operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationScenarios");

            migrationBuilder.DropTable(
                name: "Operations");
        }
    }
}
