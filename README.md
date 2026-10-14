# Atlas Rent

Aplicación web interna de Atlas Labs para gestionar alquileres: unidades, clientes, cotizaciones, reservas, contratos, facturas, cobros en USD y DOP, fianzas e informes. Sustituye a Factusol.

- **Qué hace:** [`docs/ESPECIFICACION.md`](docs/ESPECIFICACION.md)
- **Cómo está construida:** [`docs/DOC_TECNICA.md`](docs/DOC_TECNICA.md)
- **Diseño:** [`docs/DESIGN.md`](docs/DESIGN.md) y [`Atlas Rent Design System/`](Atlas%20Rent%20Design%20System/)
- **Plan y estado:** [`docs/PLAN.md`](docs/PLAN.md)
- **Cómo se trabaja (personas y agentes):** [`AGENTS.md`](AGENTS.md) y [`docs/PROCEDIMIENTOS.md`](docs/PROCEDIMIENTOS.md)

## Arranque en desarrollo

Requisitos: .NET SDK 10, Node.js 24, Docker Desktop.

```bash
docker compose -f docker-compose.dev.yml up -d
```

PostgreSQL de desarrollo en `localhost:5433`. Los nombres de las variables de entorno están en [`config/variables-entorno.example`](config/variables-entorno.example); los valores nunca van al repositorio.

Backend y frontend se crean en las tareas F1-01 y F1-13 del plan.
