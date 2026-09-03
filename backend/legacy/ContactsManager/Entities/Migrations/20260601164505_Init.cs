using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Entities.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.CountryId);
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NewsLetter = table.Column<bool>(type: "bit", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.PersonId);
                });

            migrationBuilder.InsertData(
                table: "Countries",
                columns: new[] { "CountryId", "CountryName" },
                values: new object[,]
                {
                    { new Guid("4a91b323-6902-4d3e-b147-3a2f6990c254"), "Norway" },
                    { new Guid("7c9e6645-3677-448a-95b7-511b41f17491"), "Japan" },
                    { new Guid("99c6a23d-8d1e-4e90-95b6-03b576c75f71"), "Australia" },
                    { new Guid("a1b2c3d4-e5f6-47a8-b9c0-d1e2f3a4b5c6"), "Canada" },
                    { new Guid("f2345b12-1111-4a55-89cc-5521aabbccdd"), "Brazil" }
                });

            migrationBuilder.InsertData(
                table: "Persons",
                columns: new[] { "PersonId", "Address", "CountryId", "DateOfBirth", "Gender", "Name", "NewsLetter", "email", "phone" },
                values: new object[,]
                {
                    { new Guid("12345678-90ab-cdef-1234-567890abcdef"), null, new Guid("7c9e6645-3677-448a-95b7-511b41f17491"), new DateTime(1996, 5, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), "Female", "Alice", null, null, "1111111111" },
                    { new Guid("23456789-0abc-def1-2345-67890abcdefa"), null, new Guid("a1b2c3d4-e5f6-47a8-b9c0-d1e2f3a4b5c6"), new DateTime(2001, 3, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), "Male", "Bob", null, null, "2222222222" },
                    { new Guid("34567890-abcd-ef12-3456-7890abcdefa1"), null, new Guid("4a91b323-6902-4d3e-b147-3a2f6990c254"), new DateTime(1991, 8, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), "Male", "Charlie", null, null, "3333333333" },
                    { new Guid("4567890a-bcde-f123-4567-890abcdefa12"), null, new Guid("99c6a23d-8d1e-4e90-95b6-03b576c75f71"), new DateTime(1998, 11, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), "Male", "David", null, null, "4444444444" },
                    { new Guid("567890ab-cdef-1234-5678-90abcdefa123"), null, new Guid("f2345b12-1111-4a55-89cc-5521aabbccdd"), new DateTime(2004, 1, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), "Female", "Eve", null, null, "5555555555" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropTable(
                name: "Persons");
        }
    }
}
