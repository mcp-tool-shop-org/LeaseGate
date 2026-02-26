<p align="center">
  <a href="README.ja.md">日本語</a> | <a href="README.zh.md">中文</a> | <a href="README.es.md">Español</a> | <a href="README.fr.md">Français</a> | <a href="README.hi.md">हिन्दी</a> | <a href="README.it.md">Italiano</a> | <a href="README.pt-BR.md">Português (BR)</a>
</p>

<p align="center">
  
            <img src="https://raw.githubusercontent.com/mcp-tool-shop-org/brand/main/logos/LeaseGate/readme.png"
           alt="LeaseGate" width="400">
</p>

<p align="center">
  <a href="https://github.com/mcp-tool-shop-org/LeaseGate/actions/workflows/policy-ci.yml"><img src="https://github.com/mcp-tool-shop-org/LeaseGate/actions/workflows/policy-ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow" alt="MIT License"></a>
  <a href="https://mcp-tool-shop-org.github.io/LeaseGate/"><img src="https://img.shields.io/badge/Landing_Page-live-blue" alt="Landing Page"></a>
</p>

**Plataforma de control de gobernanza de IA, centrada en el entorno local, que emite licencias de ejecución, aplica políticas y presupuestos, y genera evidencia de gobernanza con protección contra manipulaciones.**

---

## Resumen

- **Admisión de licencias:** Licencias de ejecución basadas en TTL con gobernanza multi-pool.
- **Aplicación de políticas:** Políticas YAML de GitOps con flujo de firma para la etapa/activación del paquete.
- **Auditoría con protección contra manipulaciones:** Entradas de solo escritura enlazadas por hash y recibos de lanzamiento.
- **Automatización de seguridad:** Patrones de enfriamiento, limitación y disyuntor.
- **Aislamiento de herramientas:** Sub-licencias gobernadas con prevención de inyección de comandos.
- **Modo distribuido:** Arquitectura Hub/Agente con recuperación local degradada.

---

## Estado actual: v0.1.0

Las fases 1 a 5 están implementadas, probadas y protegidas contra vulnerabilidades, incluyendo:

- Admisión de licencias y seguridad de lanzamiento basada en TTL.
- Gobernanza multi-pool (concurrencia, tasa, contexto, computación, gasto).
- Estado duradero de SQLite con recuperación de reinicio.
- Entradas de auditoría enlazadas por hash y recibos de lanzamiento.
- Flujo de firma para la etapa/activación del paquete de políticas.
- Aislamiento de herramientas con sub-licencias gobernadas.
- Modo distribuido Hub/Agente con comportamiento local degradado.
- RBAC, cuentas de servicio, cuotas jerárquicas, controles de equidad.
- Cola de aprobación con seguimiento de revisores.
- Enrutamiento de intenciones con planes de respaldo deterministas.
- Gobernanza de contexto con seguimiento resumido gobernado.
- Automatización de seguridad (enfriamiento, limitación, disyuntor).
- Exportación y verificación de evidencia de gobernanza.

### Endurecimiento de seguridad v0.1.0

- Prevención de inyección de comandos en el aislamiento de herramientas (lista de bloqueo de metacaracteres de shell + ejecución directa).
- Hashing de tokens de cuentas de servicio (SHA-256 con compatibilidad hacia atrás con texto plano).
- Escrituras de auditoría resilientes con seguimiento de fallas (sin más operaciones silenciosas de "disparar y olvidar").
- Límites de tamaño de carga útil en el formato de mensajes de canalización (límite de 16 MB).
- Registros y estado del cliente seguros para múltiples hilos (ConcurrentDictionary en todo momento).
- Conexiones de canalización con nombre concurrentes (despacho sin bloqueo del listener).
- Protección contra recorrido de rutas en todos los puntos de exportación.
- Prevención de inyección de fórmulas CSV en las exportaciones de informes.
- Límites de crecimiento ilimitado en el estado de la automatización de seguridad.
- Seguimiento y exposición de errores de recarga de políticas.
- Soporte para claves externas para la firma de recibos de gobernanza.

---

## Estructura de la solución

```text
LeaseGate.sln
src/
  LeaseGate.Protocol/     # DTOs, enums, serializer + framing
  LeaseGate.Policy/       # policy model, evaluator, GitOps loader
  LeaseGate.Audit/        # append-only hash-chained audit writer
  LeaseGate.Service/      # governor, pools, approvals, safety, tool isolation
  LeaseGate.Client/       # SDK commands + governed call wrapper
  LeaseGate.Providers/    # provider interface + adapters
  LeaseGate.Storage/      # durable SQLite-backed state
  LeaseGate.Hub/          # distributed quota and attribution control plane
  LeaseGate.Agent/        # hub-aware agent with local degraded fallback
  LeaseGate.Receipt/      # proof export + verification services
samples/
  LeaseGate.SampleCli/    # end-to-end scenarios and proof/report commands
  LeaseGate.AuditVerifier/# audit chain verification sample
tests/
  LeaseGate.Tests/        # unit/integration coverage through phase 5
policies/
  org.yml
  models.yml
  tools.yml
  workspaces/*.yml
```

---

## Cómo empezar

### Construcción y pruebas

```powershell
dotnet restore LeaseGate.sln
dotnet build LeaseGate.sln
dotnet test LeaseGate.sln
```

### Ejecución de escenarios de ejemplo

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- simulate-all
```

### Comandos operativos

```powershell
dotnet run --project samples/LeaseGate.SampleCli -- daily-report
dotnet run --project samples/LeaseGate.SampleCli -- export-proof
dotnet run --project samples/LeaseGate.SampleCli -- verify-receipt
```

---

## Instantánea de integración

Flujo de aplicación típico:

1. Cree una solicitud `AcquireLeaseRequest` con actor, organización/espacio de trabajo, intención, modelo, uso estimado, herramientas y contribuciones de contexto.
2. Adquiera a través de `LeaseGateClient.AcquireAsync(...)`.
3. Ejecute el trabajo del modelo/herramienta (o use `GovernedModelCall.ExecuteProviderCallAsync(...)`).
4. Libere a través de `LeaseGateClient.ReleaseAsync(...)` con telemetría y resultados reales.
5. Persista/verifique la evidencia del recibo cuando sea necesario.

Consulte [docs/Protocol.md](docs/Protocol.md) y [docs/Architecture.md](docs/Architecture.md).

---

## Flujo de trabajo de políticas de GitOps

El código de las políticas se encuentra en `policies/` y se carga a través de la composición YAML de GitOps.

- `org.yml` para valores predeterminados compartidos y umbrales globales.
- `models.yml` para listas de permisos de modelos y anulaciones de modelos del espacio de trabajo.
- `tools.yml` para categorías denegadas/que requieren aprobación y requisitos de revisores.
- `workspaces/*.yml` para presupuestos a nivel de espacio de trabajo y mapas de capacidades de roles.

La validación de CI y la firma de paquetes se proporcionan por:

- `.github/workflows/policy-ci.yml`
- `scripts/build-policy-bundle.ps1`

---

## Documentación

- [docs/Architecture.md](docs/Architecture.md)
- [docs/Protocol.md](docs/Protocol.md)
- [docs/Policy.md](docs/Policy.md)
- [docs/Operations.md](docs/Operations.md)
- [docs/Development.md](docs/Development.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)

---

## Licencia

[MIT](LICENSE)
