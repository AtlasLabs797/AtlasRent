using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class Reservas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reserva",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prospecto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    fecha_reserva = table.Column<DateOnly>(type: "date", nullable: false),
                    entrada_prevista = table.Column<DateOnly>(type: "date", nullable: false),
                    salida_prevista = table.Column<DateOnly>(type: "date", nullable: true),
                    entrada_real = table.Column<DateOnly>(type: "date", nullable: true),
                    salida_real = table.Column<DateOnly>(type: "date", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    modalidad = table.Column<int>(type: "integer", nullable: false),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    anticipo_importe = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    anticipo_nota = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    observaciones = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                    // La columna xmin que EF pone en cada CreateTable se quita a mano: la de
                    // docs/DOC_TECNICA.md §4.2 es la que PostgreSQL ya tiene en toda tabla del
                    // esquema, y crearla como una columna más rompe el disparador de auditoría.
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reserva", x => x.id);
                    table.CheckConstraint("ck_reserva_destinatario", "(cliente_id IS NOT NULL) <> (prospecto_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_reserva_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reserva_prospecto_prospecto_id",
                        column: x => x.prospecto_id,
                        principalTable: "prospecto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reserva_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reserva_dia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reserva_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    hora_entrada = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    hora_salida = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                    // Como en reserva: xmin fuera, la da PostgreSQL sola.
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reserva_dia", x => x.id);
                    table.ForeignKey(
                        name: "fk_reserva_dia_reserva_reserva_id",
                        column: x => x.reserva_id,
                        principalTable: "reserva",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reserva_activo_estado",
                table: "reserva",
                columns: new[] { "activo", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_reserva_cliente_id",
                table: "reserva",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_created_at",
                table: "reserva",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_estado_entrada_prevista",
                table: "reserva",
                columns: new[] { "estado", "entrada_prevista" });

            migrationBuilder.CreateIndex(
                name: "ix_reserva_prospecto_id",
                table: "reserva",
                column: "prospecto_id");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_unidad_id",
                table: "reserva",
                column: "unidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_dia_reserva_id_fecha",
                table: "reserva_dia",
                columns: new[] { "reserva_id", "fecha" },
                unique: true);

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3): las tablas las crea
            // rent_migrador y 02_basedatos.sql solo declara permisos por defecto para rent_owner,
            // así que cada migración nueva concede los suyos a rent_app. Sin estas dos líneas la
            // migración aplica bien y la aplicación falla en runtime con "permission denied".
            //
            // reserva NO lleva DELETE porque una reserva no se borra nunca (AGENTS.md §6): la baja
            // es lógica (estado cancelada y activo = false) y sus días se conservan como historial.
            //
            // reserva_dia sí lo lleva, y es la excepción que hace la regla, por el mismo motivo que
            // lo lleva cotizacion_linea en F2-02: los días son la composición del documento, no el
            // documento. Al editar una reserva corta y moverla de día, sus días antiguos se borran y
            // se escriben los nuevos; sin ese permiso el PUT falla con "permission denied" en
            // runtime. Lo que no se pierde es el qué: GuardadoAuditado deja en la auditoría los días
            // que la reserva tenía antes del cambio.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON reserva TO rent_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON reserva_dia TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reserva_dia");

            migrationBuilder.DropTable(
                name: "reserva");
        }
    }
}
