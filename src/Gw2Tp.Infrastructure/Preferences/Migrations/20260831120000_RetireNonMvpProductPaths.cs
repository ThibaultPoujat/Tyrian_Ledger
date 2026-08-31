using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gw2Tp.Infrastructure.Preferences.Migrations;

public partial class RetireNonMvpProductPaths : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MarketOrderBookLevels");
        migrationBuilder.DropTable(name: "MarketPriceSnapshots");
        migrationBuilder.DropTable(name: "MarketWatchlistItems");
        migrationBuilder.DropTable(name: "OperationScenarios");
        migrationBuilder.DropTable(name: "MarketOrderBookSnapshots");
        migrationBuilder.DropTable(name: "Operations");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "Retired M9 data is intentionally deleted during upgrade and cannot be restored by a reverse migration.");
}
