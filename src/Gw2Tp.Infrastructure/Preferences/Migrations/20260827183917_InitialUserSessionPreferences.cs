using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gw2Tp.Infrastructure.Preferences.Migrations
{
    /// <inheritdoc />
    public partial class InitialUserSessionPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSessionPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    CapitalLimitCopper = table.Column<long>(type: "INTEGER", nullable: true),
                    MinimumProfitCopper = table.Column<long>(type: "INTEGER", nullable: true),
                    RiskPreference = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StrategyPreference = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AllocationPercent = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessionPreferences", x => x.Id);
                    table.CheckConstraint("CK_UserSessionPreferences_AllocationPercent", "AllocationPercent BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_UserSessionPreferences_CapitalLimitCopper", "CapitalLimitCopper IS NULL OR (CapitalLimitCopper >= 0 AND CapitalLimitCopper <= 9007199254740991)");
                    table.CheckConstraint("CK_UserSessionPreferences_MinimumProfitCopper", "MinimumProfitCopper IS NULL OR (MinimumProfitCopper >= 0 AND MinimumProfitCopper <= 9007199254740991)");
                    table.CheckConstraint("CK_UserSessionPreferences_RiskPreference", "RiskPreference IN ('all', 'normal', 'reduced')");
                    table.CheckConstraint("CK_UserSessionPreferences_StrategyPreference", "StrategyPreference IN ('all', 'market-flip')");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSessionPreferences");
        }
    }
}
