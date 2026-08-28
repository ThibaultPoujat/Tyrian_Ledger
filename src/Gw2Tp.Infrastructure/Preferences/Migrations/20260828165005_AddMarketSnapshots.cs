using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gw2Tp.Infrastructure.Preferences.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketOrderBookSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CapturedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FormatVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketOrderBookSnapshots", x => x.Id);
                    table.CheckConstraint("CK_MarketOrderBookSnapshots_FormatVersion", "FormatVersion > 0");
                    table.CheckConstraint("CK_MarketOrderBookSnapshots_ItemId", "ItemId > 0");
                });

            migrationBuilder.CreateTable(
                name: "MarketPriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CapturedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsWhitelisted = table.Column<bool>(type: "INTEGER", nullable: false),
                    BuyQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    BuyUnitPriceCopper = table.Column<int>(type: "INTEGER", nullable: false),
                    SellQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    SellUnitPriceCopper = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketPriceSnapshots", x => x.Id);
                    table.CheckConstraint("CK_MarketPriceSnapshots_BuyQuantity", "BuyQuantity >= 0");
                    table.CheckConstraint("CK_MarketPriceSnapshots_BuyUnitPriceCopper", "BuyUnitPriceCopper >= 0");
                    table.CheckConstraint("CK_MarketPriceSnapshots_FormatVersion", "FormatVersion > 0");
                    table.CheckConstraint("CK_MarketPriceSnapshots_ItemId", "ItemId > 0");
                    table.CheckConstraint("CK_MarketPriceSnapshots_SellQuantity", "SellQuantity >= 0");
                    table.CheckConstraint("CK_MarketPriceSnapshots_SellUnitPriceCopper", "SellUnitPriceCopper >= 0");
                });

            migrationBuilder.CreateTable(
                name: "MarketWatchlistItems",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    SamplingClass = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketWatchlistItems", x => x.ItemId);
                    table.CheckConstraint("CK_MarketWatchlistItems_ItemId", "ItemId > 0");
                    table.CheckConstraint("CK_MarketWatchlistItems_SamplingClass", "SamplingClass IN ('watchlist', 'background')");
                });

            migrationBuilder.CreateTable(
                name: "MarketOrderBookLevels",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Listings = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPriceCopper = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketOrderBookLevels", x => new { x.SnapshotId, x.Side, x.Position });
                    table.CheckConstraint("CK_MarketOrderBookLevels_Listings", "Listings >= 0");
                    table.CheckConstraint("CK_MarketOrderBookLevels_Position", "Position >= 0");
                    table.CheckConstraint("CK_MarketOrderBookLevels_Quantity", "Quantity >= 0");
                    table.CheckConstraint("CK_MarketOrderBookLevels_Side", "Side IN ('buy', 'sell')");
                    table.CheckConstraint("CK_MarketOrderBookLevels_UnitPriceCopper", "UnitPriceCopper >= 0");
                    table.ForeignKey(
                        name: "FK_MarketOrderBookLevels_MarketOrderBookSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "MarketOrderBookSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketOrderBookSnapshots_ItemId_CapturedAtUtcTicks_Id",
                table: "MarketOrderBookSnapshots",
                columns: new[] { "ItemId", "CapturedAtUtcTicks", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceSnapshots_ItemId_CapturedAtUtcTicks_Id",
                table: "MarketPriceSnapshots",
                columns: new[] { "ItemId", "CapturedAtUtcTicks", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketOrderBookLevels");

            migrationBuilder.DropTable(
                name: "MarketPriceSnapshots");

            migrationBuilder.DropTable(
                name: "MarketWatchlistItems");

            migrationBuilder.DropTable(
                name: "MarketOrderBookSnapshots");
        }
    }
}
