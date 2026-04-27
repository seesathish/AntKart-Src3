using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AK.Notification.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "notifications");
        }
    }
}
