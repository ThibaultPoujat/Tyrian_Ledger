using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gw2Tp.Infrastructure.Preferences.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationHistoryCreatedAtSortKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUtcTicks",
                table: "Operations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtcTicks",
                table: "Operations");
        }
    }
}
