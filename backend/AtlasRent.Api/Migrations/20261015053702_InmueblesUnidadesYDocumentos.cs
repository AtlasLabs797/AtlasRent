using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class InmueblesUnidadesYDocumentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inmueble",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    direccion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    propietaria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    plantilla_contrato_por_defecto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    renovacion_defecto_meses = table.Column<int>(type: "integer", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inmueble", x => x.id);
                    table.ForeignKey(
                        name: "fk_inmueble_subempresa_propietaria_id",
                        column: x => x.propietaria_id,
                        principalTable: "subempresa",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "documento_inmueble",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inmueble_id = table.Column<Guid>(type: "uuid", nullable: false),
                    categoria = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    nombre_original = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    tipo_contenido = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_inmueble", x => x.id);
                    table.ForeignKey(
                        name: "fk_documento_inmueble_inmueble_inmueble_id",
                        column: x => x.inmueble_id,
                        principalTable: "inmueble",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inmueble_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    propietaria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    facturadora_por_defecto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado_manual = table.Column<int>(type: "integer", nullable: false),
                    metros_cuadrados = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    capacidad = table.Column<int>(type: "integer", nullable: true),
                    habitaciones = table.Column<int>(type: "integer", nullable: true),
                    banos = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    medidor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    notas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unidad", x => x.id);
                    table.ForeignKey(
                        name: "fk_unidad_inmueble_inmueble_id",
                        column: x => x.inmueble_id,
                        principalTable: "inmueble",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_unidad_subempresa_facturadora_por_defecto_id",
                        column: x => x.facturadora_por_defecto_id,
                        principalTable: "subempresa",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_unidad_subempresa_propietaria_id",
                        column: x => x.propietaria_id,
                        principalTable: "subempresa",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "unidad_modalidad",
                columns: table => new
                {
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modalidad = table.Column<int>(type: "integer", nullable: false),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unidad_modalidad", x => new { x.unidad_id, x.modalidad });
                    table.ForeignKey(
                        name: "fk_unidad_modalidad_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unidad_tarifa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modalidad = table.Column<int>(type: "integer", nullable: false),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unidad_tarifa", x => x.id);
                    table.ForeignKey(
                        name: "fk_unidad_tarifa_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documento_inmueble_inmueble_id",
                table: "documento_inmueble",
                column: "inmueble_id");

            migrationBuilder.CreateIndex(
                name: "ix_documento_inmueble_inmueble_id_categoria_version",
                table: "documento_inmueble",
                columns: new[] { "inmueble_id", "categoria", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documento_inmueble_nombre_archivo",
                table: "documento_inmueble",
                column: "nombre_archivo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inmueble_nombre",
                table: "inmueble",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_inmueble_propietaria_id",
                table: "inmueble",
                column: "propietaria_id");

            migrationBuilder.CreateIndex(
                name: "ix_unidad_facturadora_por_defecto_id",
                table: "unidad",
                column: "facturadora_por_defecto_id");

            migrationBuilder.CreateIndex(
                name: "ix_unidad_inmueble_id_estado_manual",
                table: "unidad",
                columns: new[] { "inmueble_id", "estado_manual" });

            migrationBuilder.CreateIndex(
                name: "ix_unidad_nombre",
                table: "unidad",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_unidad_propietaria_id",
                table: "unidad",
                column: "propietaria_id");

            migrationBuilder.CreateIndex(
                name: "ix_unidad_tarifa_unidad_id_modalidad_moneda",
                table: "unidad_tarifa",
                columns: new[] { "unidad_id", "modalidad", "moneda" },
                unique: true);

            // Permisos de la aplicación (docs/DOC_TECNICA.md §4.1).
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE, DELETE
                    ON inmueble, unidad, unidad_modalidad, unidad_tarifa, documento_inmueble
                    TO rent_app;
                """);

            // RLS sobre documento_inmueble (docs/SEGURIDAD.md A.4 y docs/DOC_TECNICA.md §4.3): los
            // documentos de propiedad solo los leen y escriben gerente y administrador, aunque la
            // petición llegue con otro rol. Es la segunda barrera, detrás del permiso de la API.
            //
            // El archivo no está aquí: vive en la carpeta de Almacen__Documentos, fuera del
            // webroot, y se sirve por la API con el permiso inmueble.documento.
            migrationBuilder.Sql(
                """
                ALTER TABLE documento_inmueble ENABLE ROW LEVEL SECURITY;

                CREATE POLICY documento_inmueble_gerencia ON documento_inmueble
                  FOR ALL TO rent_app
                  USING (
                    COALESCE(current_setting('app.rol', true), '') IN ('admin', 'gerente')
                  )
                  WITH CHECK (
                    COALESCE(current_setting('app.rol', true), '') IN ('admin', 'gerente')
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documento_inmueble");

            migrationBuilder.DropTable(
                name: "unidad_modalidad");

            migrationBuilder.DropTable(
                name: "unidad_tarifa");

            migrationBuilder.DropTable(
                name: "unidad");

            migrationBuilder.DropTable(
                name: "inmueble");
        }
    }
}
