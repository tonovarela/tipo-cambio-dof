# SPEC 02 — Actualizar `Mon.UltimoCambio` en cada corrida de `diario`

> **Estado:** Implementado
> **Depende de:** SPEC 01
> **Fecha:** 2026-11-19
> **Objetivo:** Cada corrida de `diario` con cobertura OK debe poner `UltimoCambio = GETDATE()` en `{BD_LITO}.dbo.Mon` (`Moneda = 'Dolares'`), aunque el tipo de cambio no haya cambiado.

## Por qué existe este spec

En SPEC 01, el `UPDATE` de `Mon` se ejecuta solo si cambió el valor o si `UltimoCambio` es de un día anterior.
Por eso, si la corrida se repite el mismo día, `UltimoCambio` se queda con la hora de la primera corrida.
Se necesita que `UltimoCambio` refleje la última vez que esta app escribió en Lito.

La "fecha de registro" de `Mon` es la columna `UltimoCambio`, que ya existe. Este spec no agrega columnas: solo cambia cuándo se escribe.

## Alcance

**Entra:**

- En `Datos/Actualizacion.sql`, el bloque de Lito ejecuta `UPDATE ... SET TipoCambio = @valor, UltimoCambio = GETDATE() WHERE Moneda = 'Dolares'` siempre que exista la fila `'Dolares'`. Se elimina la condición de "valor distinto o no actualizado hoy".
- El resultado `mon` queda en `UPDATE` | `NO_EXISTE`. `SIN_CAMBIO` ya no se devuelve para `mon`.
- Actualizar los comentarios de `Actualizacion.sql`, el comentario de `ResultadoLegados` en `Datos/RepositorioLegados.cs` y la descripción de README §11.

**Fuera de alcance (para specs futuros):**

- Columnas de fecha de registro en `FIN_MovtoMoeda`. Su lógica de SPEC 01 (`INSERT` | `UPDATE` | `SIN_CAMBIO`) no cambia.
- Crear o alterar columnas en `Mon` o en cualquier tabla legada.
- Evitar filas repetidas en `MonHist` cuando `diario` se corre varias veces el mismo día.
- Cambiar la zona horaria de `UltimoCambio` (sigue siendo `GETDATE()`, hora local del servidor SQL).
- Escribir en `Mon` desde `historico`.

## Modelo de datos

Este spec no introduce estructuras de datos nuevas. Reutiliza `Mon.UltimoCambio`, `Actualizacion.sql` y `ResultadoLegados` de SPEC 01.

Cambio en el contrato de `Datos/Actualizacion.sql` (bloque `Mon`):

- Si no existe `Moneda = 'Dolares'` (con `UPDLOCK, HOLDLOCK`): `@mon = 'NO_EXISTE'` y no se ejecuta ningún `UPDATE`. Igual que en SPEC 01.
- Si existe: `UPDATE {BD_LITO}.dbo.Mon SET TipoCambio = @valor, UltimoCambio = GETDATE() WHERE Moneda = 'Dolares'` y `@mon = 'UPDATE'`.
- La regla de SPEC 01 se mantiene: no se ejecuta un `UPDATE` sin haber confirmado antes con `IF EXISTS` que la fila existe. Así el trigger de `Mon` siempre recibe una fila en `inserted`.

Valores de `mon` en `ResultadoLegados`:

```csharp
// mon ∈ UPDATE | NO_EXISTE   (antes: UPDATE | SIN_CAMBIO | NO_EXISTE)
public sealed record ResultadoLegados(string Movto, string Mon);
```

## Plan de implementación

1. **Script.** En `Datos/Actualizacion.sql`, reemplazar el `ELSE IF EXISTS (... TipoCambio <> @valor ... CAST(UltimoCambio AS DATE) < @fecha)` y su `ELSE SET @mon = 'SIN_CAMBIO'` por un `ELSE` que siempre hace el `UPDATE` y pone `@mon = 'UPDATE'`. Actualizar el comentario de cabecera (valores de `mon`) y el del bloque de Lito.
2. **Repositorio.** En `Datos/RepositorioLegados.cs`, actualizar el `<summary>` de `ResultadoLegados` a `mon ∈ UPDATE|NO_EXISTE`. Sin cambios de código: `ProcesoDiario` ya solo distingue `NO_EXISTE`.
3. **Test.** En `tests/TipoCambioDof.Tests/RepositorioLegadosTests.cs`, agregar un test que lea la plantilla embebida y verifique que el bloque de `Mon` ya no contiene la condición `CAST(UltimoCambio AS DATE) < @fecha`.
4. **Documentación.** En README §11, cambiar la descripción de `BD_LITO.dbo.Mon` (líneas "si el valor cambió o si todavía no se actualizó hoy…") por: se actualiza `TipoCambio` y `UltimoCambio = GETDATE()` en cada corrida de `diario` con cobertura OK, y cada corrida agrega una fila en `MonHist` por el trigger.

## Criterios de aceptación

- [ ] `dotnet build` y `dotnet test` pasan, con `TreatWarningsAsErrors` activo.
- [ ] Al correr `diario` con cobertura OK y `ACTUALIZAR_LEGADOS=true`, `Mon.UltimoCambio` (`Moneda = 'Dolares'`) queda con la hora de esa corrida y el log dice `mon=UPDATE`.
- [ ] Al repetir `diario` el mismo día sin cambio de valor, `Mon.UltimoCambio` avanza a la hora de la segunda corrida y el log dice `mon=UPDATE`.
- [ ] En esa segunda corrida, `FIN_MovtoMoeda` no cambia y el log dice `movto=SIN_CAMBIO`.
- [ ] Al repetir `diario` el mismo día sin cambio de valor, la corrida termina con código 0 (el trigger de `Mon` no falla).
- [ ] Si no existe la fila `Moneda = 'Dolares'`, el comportamiento es el de SPEC 01: alerta, exit 2 y `mon=NO_EXISTE`.
- [ ] Con `ACTUALIZAR_LEGADOS` sin definir, `diario` no toca `Mon`.
- [ ] Si a las 16:00 no hay cobertura, `Mon.UltimoCambio` no cambia.

## Decisiones

- **Sí:** la "fecha de registro" es `Mon.UltimoCambio`. Es la columna que ya existe y que la captura manual usa.
- **No:** agregar una columna nueva en `Mon`. Esta app no administra esa tabla.
- **Sí:** `UPDATE` de `Mon` en cada corrida con cobertura OK, aunque el valor sea igual. `UltimoCambio` debe reflejar la última escritura de esta app.
- **No:** mantener la regla de SPEC 01 ("valor distinto o no actualizado hoy"). Deja `UltimoCambio` con la hora de la primera corrida del día.
- **No:** actualizar solo cuando cambia el valor. Ya se descartó en SPEC 01 porque Lito necesita un `UltimoCambio` por día.
- **Sí:** `UltimoCambio = GETDATE()`, hora local del servidor SQL. Es consistente con lo que ya se escribe hoy y con la captura manual.
- **No:** `SYSUTCDATETIME()` ni la fecha DOF sin hora. Mezclaría convenciones en la misma columna.
- **Sí:** `mon` pasa a `UPDATE` | `NO_EXISTE`. Siempre se escribe cuando la fila existe, así que `SIN_CAMBIO` ya no aplica.
- **No:** un valor nuevo (p. ej. `FECHA`) para distinguir "solo cambió la fecha" de "cambió el valor". El valor anterior no se usa en ninguna lógica, y `movto` de `FIN_MovtoMoeda` ya indica si el valor cambió.
- **Sí:** este spec modifica el comportamiento de SPEC 01 para `Mon`. El criterio de SPEC 01 "Repetir `diario` el mismo día sin cambio de valor … no cambia `Mon.UltimoCambio` y registra `mon=SIN_CAMBIO`" queda reemplazado por los de este spec. SPEC 01 no se edita.
- **Sí:** solo `Mon`. `FIN_MovtoMoeda` queda fuera porque la petición es solo sobre `Mon`.

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Cada corrida de `diario` dispara el trigger `AFTER UPDATE` de `Mon` y agrega una fila en `MonHist`, aunque el valor no cambie. Repetir la corrida el mismo día deja varias filas del día en `MonHist`. | Se acepta. Lo normal es una corrida por día (13:00, y `diario` termina al confirmar cobertura). Las repeticiones manuales son raras. Evitarlo va en otro spec si llega a molestar. |
| Un `UPDATE` con valor igual podría afectar a otros procesos de Lito que reaccionen a cambios en `Mon`. | El `UPDATE` es el mismo que la captura manual. No se conocen otros consumidores además del trigger de `MonHist`. |
| El `UPDATE` de 0 filas hace fallar el trigger de `Mon` (ver SPEC 01). | Se conserva el `IF NOT EXISTS ... 'Dolares'` previo. El `UPDATE` solo corre cuando la fila existe, así que siempre afecta exactamente una fila. |

## Lo que **no** entra en este spec

- Fecha de registro en `FIN_MovtoMoeda`.
- Columnas nuevas en tablas legadas.
- Evitar filas repetidas en `MonHist` por corridas repetidas.
- Cambiar `GETDATE()` por UTC.
- Escritura en `Mon` desde `historico`.

Cada uno de esos, si se llega a necesitar, va en su propio spec.
