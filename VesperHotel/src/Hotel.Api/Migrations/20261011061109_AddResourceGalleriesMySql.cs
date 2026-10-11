using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hotel.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceGalleriesMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GalleryImagesJson",
                table: "Resources",
                type: "longtext",
                nullable: true);

            migrationBuilder.Sql("UPDATE Resources SET GalleryImagesJson = '[]' WHERE GalleryImagesJson IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "GalleryImagesJson",
                table: "Resources",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GalleryImagesJson",
                table: "Resources");
        }
    }
}
