using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gw2Tp.Infrastructure.Preferences.Migrations;

public partial class AddMarketScanPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AnalysisQuantity",
            table: "UserSessionPreferences",
            type: "INTEGER",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "ExchangeFeeBasisPoints",
            table: "UserSessionPreferences",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExchangeFeeRounding",
            table: "UserSessionPreferences",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ListingFeeBasisPoints",
            table: "UserSessionPreferences",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ListingFeeRounding",
            table: "UserSessionPreferences",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AnalysisQuantity", table: "UserSessionPreferences");
        migrationBuilder.DropColumn(name: "ExchangeFeeBasisPoints", table: "UserSessionPreferences");
        migrationBuilder.DropColumn(name: "ExchangeFeeRounding", table: "UserSessionPreferences");
        migrationBuilder.DropColumn(name: "ListingFeeBasisPoints", table: "UserSessionPreferences");
        migrationBuilder.DropColumn(name: "ListingFeeRounding", table: "UserSessionPreferences");
    }
}
