# SPEC 01 — Actualización de sistemas legados con el tipo de cambio publicado hoy en el DOF

> **Estado:** Implementado
> **Depende de:** —
> **Fecha:** 2026-11-19
> **Objetivo:** Al terminar con éxito la corrida `diario`, escribir el valor publicado hoy en el DOF en `FIN_MovtoMoeda` (Metrics) y `dbo.Mon` (Lito) con las sentencias de `src/TipoCambioDof/Datos/Actualizacion.sql`.

## Por qué existe este spec

Dos sistemas internos guardan su propio tipo de cambio y hoy se capturan a mano.
Las sentencias ya están esbozadas (comentadas) en `Actualizacion.sql`.
Este spec las vuelve parte de la corrida diaria sin romper sus garantías: idempotencia, alertas y códigos de salida.

## Alcance

**Entra:**

- Al final de `diario`, cuando la verificación de cobertura sale bien, se toma el valor de la fila de `dbo.tipo_cambio` con `fecha_publicacion_dof = hoy` (es decir, `fecha_aplicacion = hoy + 1` = `ProcesoDiario.FechaCobertura`).
- Upsert en `{BD_METRICS}.dbo.FIN_MovtoMoeda` con `IdMoeda = 2`, `Base = 1`, `DataMovtoMoeda = hoy` (fecha de publicación DOF) y `ValorMoedaDia = valor`.
- `UPDATE` de `{BD_LITO}.dbo.Mon` (`TipoCambio`, `UltimoCambio = GETDATE()`) para `Moneda = 'Dolares'`, si el valor cambió o si todavía no se actualizó hoy.
- `Actualizacion.sql` pasa a ser recurso embebido (nombre lógico `Actualizacion.sql`). Es un solo batch idempotente con parámetros `@valor` y `@fecha`.
- Bandera `ACTUALIZAR_LEGADOS` y nombres de BD `BD_METRICS` y `BD_LITO`, todos por variable de entorno.
- Documentación en README §11 (variables de entorno), texto de `ayuda` en `Comando.cs`, `.env.example` y `docker-compose.yml`.

**Fuera de alcance (para specs futuros):**

- Comando manual aparte (p. ej. `actualizar-legados --fecha`). Para repetir se vuelve a correr `diario`, que es idempotente.
- Ejecutarlo desde `historico` o rellenar fechas pasadas en los sistemas legados.
- Detectar feriados sin publicación DOF (SF43718). En esos días se escribe el valor aplicable de todas formas.
- Una cadena de conexión distinta para los sistemas legados.
- Otras monedas.

## Modelo de datos

No se crean tablas ni se cambia `Esquema.sql`. Las tablas destino ya existen y no las administra esta app.

Configuración nueva en `Configuracion` (leída en `Configuracion.Desde`):

```csharp
public bool ActualizarLegados { get; init; }        // ACTUALIZAR_LEGADOS, default false. Acepta true/false.
public string? BdMetrics { get; init; }             // BD_METRICS, p. ej. "MetricsBETA". Sin default.
public string? BdLito { get; init; }                // BD_LITO, p. ej. "Lito_jess". Sin default.
```

Reglas de validación (`ConfiguracionInvalidaException` → exit 1), solo si `ActualizarLegados` es `true`:

- `BD_METRICS` y `BD_LITO` son obligatorios.
- Cada nombre debe cumplir `^[A-Za-z_][A-Za-z0-9_]{0,127}$`. Se insertan en el SQL entre corchetes, así que este filtro es la defensa contra inyección.
- `DIAS_ADELANTE` debe ser ≥ 1. Con 0 la corrida no consulta la publicación DOF de hoy.

Contrato de `Datos/Actualizacion.sql`:

- Marcadores `{BD_METRICS}` y `{BD_LITO}`, reemplazados por `[nombre]` antes de ejecutar.
- Parámetros: `@valor DECIMAL(12,6)` y `@fecha DATE` (fecha de publicación DOF = hoy).
- `FIN_MovtoMoeda`: el match es `IdMoeda = 2 AND CAST(DataMovtoMoeda AS DATE) = @fecha`, con `UPDLOCK, HOLDLOCK`. Si no hay fila se hace `INSERT`. Si la hay con otro valor, `UPDATE ValorMoedaDia`. Si no, nada.
- `Mon`: si existe `Moneda = 'Dolares'` con `TipoCambio <> @valor OR TipoCambio IS NULL OR UltimoCambio IS NULL OR CAST(UltimoCambio AS DATE) < @fecha`, se hace `UPDATE ... WHERE Moneda = 'Dolares'`. Si no, nada.
- **Ningún `UPDATE` se ejecuta si no hay nada que cambiar.** El cambio se detecta antes con `IF EXISTS`, no con un filtro en el `WHERE` del `UPDATE`. Los triggers `AFTER UPDATE` se disparan aunque el `UPDATE` afecte 0 filas, y el de `Mon` falla en ese caso (ver Riesgos).
- Al final devuelve una fila `SELECT @movto AS movto, @mon AS mon`, donde:
  - `movto` ∈ `INSERT` | `UPDATE` | `SIN_CAMBIO`
  - `mon` ∈ `UPDATE` | `SIN_CAMBIO` | `NO_EXISTE`

Resultado en C#:

```csharp
public sealed record ResultadoLegados(string Movto, string Mon);
```

## Plan de implementación

1. **Configuración.** Agregar `ActualizarLegados`, `BdMetrics` y `BdLito` a `Configuracion`, más un método `ExigirLegados()` con las reglas de validación de arriba. En `Program.cs`, el caso `Diario` lo llama cuando la bandera está activa. Tests en `tests/ConfiguracionTests.cs`: bandera apagada por defecto, nombres faltantes, nombre inválido (`"x];DROP"`), `DIAS_ADELANTE=0`.
2. **Script.** Reescribir `Datos/Actualizacion.sql` según el contrato: un batch, sin `GO`, con marcadores y parámetros, y la transacción la abre el código. Agregar `<EmbeddedResource Include="Datos/Actualizacion.sql" LogicalName="Actualizacion.sql" />` al `.csproj`.
3. **Repositorio.** Crear `Datos/RepositorioLegados.cs` con:
   - `internal static string PrepararSql(string plantilla, string bdMetrics, string bdLito)`, que hace el reemplazo de marcadores. Test en `tests/RepositorioLegadosTests.cs`: reemplaza ambos, no deja marcadores y el recurso embebido existe.
   - `Task<ResultadoLegados> ActualizarAsync(DateOnly fechaPublicacionDof, decimal valor, CancellationToken ct)`, que corre el script en una `SqlTransaction` propia con la misma `SQL_CONNECTION_STRING`.
   - Registrarlo como singleton en `Program.cs`.
4. **Lectura del valor.** En `RepositorioTipoCambio`, agregar `Task<decimal?> ValorAsync(DateOnly fechaAplicacion, ct)` y reescribir `ExisteAsync` sobre él. No cambia su comportamiento.
5. **Integración en `ProcesoDiario`.** En la rama "Cobertura OK", si `cfg.ActualizarLegados` está activa:
   - obtener `valor = ValorAsync(objetivo)` y llamar `ActualizarAsync(hoy, valor)`;
   - registrar en el log `movto` y `mon` junto con fecha y valor;
   - si `mon = NO_EXISTE`, enviar alerta con `Alertador` (exit 2) sin insertar en `Mon`.

   Si la cobertura no se alcanza a las 16:00, no se escribe nada en los sistemas legados (la alerta actual ya cubre ese caso). Una `SqlException` aquí se propaga al `catch` existente de `Program.cs`: log, alerta y exit 1.
6. **Documentación.** Agregar las tres variables a la tabla de README §11, a la `ayuda` de `Comando.cs`, a `.env.example` y al `environment` de `docker-compose.yml`. Mencionar que el usuario de SQL necesita permisos de `SELECT/INSERT/UPDATE` sobre `FIN_MovtoMoeda` y `SELECT/UPDATE` sobre `Mon`.

## Criterios de aceptación

- [ ] `dotnet build` y `dotnet test` pasan, con `TreatWarningsAsErrors` activo.
- [ ] Con `ACTUALIZAR_LEGADOS` sin definir, `diario` se comporta igual que antes y no toca `FIN_MovtoMoeda` ni `Mon`.
- [ ] Con `ACTUALIZAR_LEGADOS=true` y falta `BD_METRICS` o `BD_LITO`, `diario` sale con código 1 antes de llamar a Banxico.
- [ ] Con `ACTUALIZAR_LEGADOS=true` y `BD_LITO="x];DROP"`, `diario` sale con código 1 por nombre inválido.
- [ ] Con `ACTUALIZAR_LEGADOS=true` y `DIAS_ADELANTE=0`, `diario` sale con código 1.
- [ ] Al correr `diario` el 2026-11-19 con cobertura OK, existe exactamente una fila en `FIN_MovtoMoeda` con `IdMoeda = 2`, fecha 2026-11-19 y `ValorMoedaDia` igual a `dbo.tipo_cambio.valor` de `fecha_aplicacion = 2026-11-20`, y el log dice `movto=INSERT`.
- [ ] En la misma corrida, `Mon.TipoCambio` (`Moneda = 'Dolares'`) queda con ese valor y `UltimoCambio` se actualiza.
- [ ] Repetir `diario` el mismo día sin cambio de valor no agrega filas a `FIN_MovtoMoeda`, no cambia `Mon.UltimoCambio` y registra `movto=SIN_CAMBIO mon=SIN_CAMBIO`.
- [ ] Si Banxico revisa el valor y se repite `diario`, se actualiza `ValorMoedaDia` de la fila existente (`movto=UPDATE`) y también `Mon`.
- [ ] Si el valor de hoy es igual al de ayer, se inserta la fila de hoy en `FIN_MovtoMoeda` (`movto=INSERT`), `Mon.UltimoCambio` pasa a hoy (`mon=UPDATE`) y el trigger agrega la fila del día en `MonHist`.
- [ ] Si no existe la fila `Moneda = 'Dolares'` en `Mon`, se envía una alerta y la corrida sale con código 2. `FIN_MovtoMoeda` sí queda escrito.
- [ ] Si el usuario de SQL no tiene permisos sobre la BD legada, la corrida envía alerta y sale con código 1, y `dbo.tipo_cambio` conserva lo sincronizado.
- [ ] Si a las 16:00 no hay cobertura, no se escribe nada en `FIN_MovtoMoeda` ni en `Mon`.

## Decisiones

- **Sí:** `DataMovtoMoeda` = fecha de publicación DOF (hoy). Es lo que pide el requerimiento.
- **No:** fecha de aplicación (hoy + 1).
- **Sí:** upsert en `FIN_MovtoMoeda` por `(IdMoeda, fecha)`. Es idempotente ante corridas repetidas y recoge revisiones de Banxico.
- **No:** `INSERT` incondicional como en el borrador del script, porque duplica filas al repetir la corrida.
- **Sí:** el match compara `CAST(DataMovtoMoeda AS DATE)`. Es tolerante si la columna es `DATETIME` con hora.
- **Sí:** `UPDATE` de `Mon` si cambia el valor **o** si `UltimoCambio` es de un día anterior a `@fecha`. Así hay un `UltimoCambio` y una fila en `MonHist` por día aunque el monto se repita (p. ej. un feriado bancario), como con la captura manual. Repetir la corrida el mismo día no mueve `UltimoCambio`.
- **No:** `UPDATE` de `Mon` solo si cambia el valor (versión anterior del spec). Si el monto se repetía, `UltimoCambio` se quedaba con la fecha de ayer y no quedaba fila del día en `MonHist`, y Lito depende de ambos día por día.
- **Sí:** el cambio de valor se detecta con `IF EXISTS` antes del `UPDATE`, en `Mon` y en `FIN_MovtoMoeda`. Así ningún trigger se dispara con `inserted` vacío. Se descubrió en la primera prueba contra `LITO_jess`.
- **No:** filtrar con `AND TipoCambio <> @valor` en el `WHERE` del `UPDATE`. Ejecuta un `UPDATE` de 0 filas que dispara el trigger de `Mon`, y este falla.
- **No:** corregir el trigger de `Mon`. Es un sistema que esta app no administra.
- **Sí:** misma instancia y misma `SQL_CONNECTION_STRING`, con nombres de tres partes.
- **No:** segunda cadena de conexión. No hace falta hoy.
- **Sí:** bandera `ACTUALIZAR_LEGADOS`, apagada por defecto, y nombres de BD por variable de entorno. La imagen se publica en Docker Hub y `MetricsBETA` es de pruebas, así que pasar a producción no debe requerir reconstruir la imagen.
- **Sí:** `BD_METRICS` y `BD_LITO` no tienen default. Así no se escribe por accidente en una BD equivocada.
- **Sí:** el nombre de BD se valida con regex y se pone entre corchetes. No se puede parametrizar un identificador en SQL Server.
- **No:** SQL dinámico con `QUOTENAME` dentro del script. Es más complejo y no aporta frente a la validación en C#.
- **Sí:** solo en `diario`, después de la verificación de cobertura. Sin valor confirmado no hay nada que escribir.
- **No:** comando manual aparte; `diario` ya es idempotente.
- **Sí:** transacción propia para los sistemas legados, separada del upsert de `dbo.tipo_cambio`. Una falla en un sistema ajeno no debe deshacer los datos propios.
- **Sí:** si falla la escritura en los sistemas legados, la corrida sale con código 1 y alerta, igual que cualquier `SqlException`.
- **No:** exit 2. Una escritura fallida no es un simple aviso.
- **Sí:** en un feriado bancario entre semana se escribe de todos modos.
- **No:** consultar SF43718 para detectar publicación. Es una llamada extra y más lógica por un caso raro.
- **Sí:** `DIAS_ADELANTE=0` con la bandera activa es error de configuración. Con ese valor nunca se consulta la publicación DOF de hoy.
- **Sí:** si falta la fila `'Dolares'` en `Mon`, se alerta (exit 2) y no se inserta. Esta app no es dueña de esa tabla, y una fila insertada por ella podría tener columnas obligatorias desconocidas.

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Tipos reales de `ValorMoedaDia`, `DataMovtoMoeda` y `TipoCambio` desconocidos (precisión, `DATETIME` vs `DATE`). | Parámetros tipados con conversión implícita en SQL Server. `CAST(... AS DATE)` en el match. Revisar el esquema real antes de la primera corrida en producción. |
| `FIN_MovtoMoeda` puede tener columnas `NOT NULL` adicionales o triggers. | El borrador ya inserta solo esas cuatro columnas. Si falla, `SqlException` → alerta y exit 1, sin afectar `dbo.tipo_cambio`. |
| Ya hay filas duplicadas `(IdMoeda=2, fecha)` en `FIN_MovtoMoeda` por capturas manuales previas. | El `UPDATE` afectaría a todas. Se acepta, porque deja todas con el valor correcto. |
| Falta de permisos del usuario de SQL en las BD legadas. | Documentado en README §11. Se detecta en la primera corrida con exit 1 y alerta. |
| `UltimoCambio = GETDATE()` usa la hora local del servidor SQL, no UTC. Se compara contra `@fecha`, que es la fecha de hoy en la CDMX. | Es lo que hace el sistema legado hoy. No se cambia. La corrida va de 13:00 a 16:00 hora CDMX, así que la fecha coincide aunque el servidor esté en UTC (19:00–22:00). |
| `dbo.Mon` tiene un trigger `AFTER UPDATE` que inserta en `MonHist`. Con un `UPDATE` de 0 filas falla con "No se puede insertar el valor NULL en la columna 'Moneda', tabla 'LITO_jess.dbo.MonHist'", y eso deshace también `FIN_MovtoMoeda`. | Solo se ejecuta el `UPDATE` cuando hay un valor distinto (`IF EXISTS` previo). Cuando sí se actualiza, el trigger recibe exactamente una fila, igual que con el `UPDATE` manual de siempre. |

## Lo que **no** entra en este spec

- Comando manual `actualizar-legados`.
- Escritura desde `historico` o relleno de fechas pasadas.
- Detección de feriados sin publicación DOF.
- Cadena de conexión separada para los sistemas legados.
- Monedas distintas a USD.

Cada uno de esos, si se llega a necesitar, va en su propio spec.
