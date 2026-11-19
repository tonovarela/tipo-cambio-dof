# Tipo de cambio USD/MXN desde la API de Banxico (serie SF60653)

Flujo para obtener diariamente el tipo de cambio peso-dólar (FIX) que se publica en el
Diario Oficial de la Federación (DOF), usando la API SIE de Banco de México y la serie
**SF60653**, y registrarlo en el sistema interno.

> La implementación (app de consola .NET 10 empaquetada en Docker) está descrita en la
> [sección 11](#11-aplicación-de-consola-net-10--docker).

---

## 1. Conceptos: un mismo valor, tres fechas

El tipo de cambio FIX lo **determina** Banco de México y el DOF solo lo **publica**. Un mismo
valor recibe una fecha distinta según la etapa de su ciclo:

| Etapa | Cuándo ocurre | Dónde se consulta |
|---|---|---|
| Determinación | Día hábil bancario *D*, a partir de las 12:00 | Serie `SF43718` |
| Publicación en DOF | Siguiente día hábil bancario | `dof.gob.mx` |
| Liquidación (aplicación) | Día siguiente a la publicación en el DOF | **Serie `SF60653`** |

La serie **SF60653** tiene un valor **para cada día del calendario** (periodicidad de 7 días).
En fines de semana y festivos repite el último tipo de cambio publicado. Por eso es la serie
recomendada para este sistema.

### Ejemplo real (noviembre 2026)

| Fecha | DOF publicó | SF60653 (fecha de liquidación) |
|---|---|---|
| jue 12/11 | 18.6192 | 18.6678 |
| vie 13/11 | 18.6188 | 18.6192 |
| sáb 14/11 | — | 18.6188 |
| dom 15/11 | — | 18.6188 |
| lun 16/11 (festivo) | — | 18.6188 |
| mar 17/11 | 18.5525 | 18.6188 |
| mié 18/11 | 18.4677 | 18.5525 |
| jue 19/11 | 18.6723 | 18.4677 |
| vie 20/11 | *19.0270* | 18.6723 |
| sáb 21/11 | — | 19.0270 |

Regla que se observa: **el valor que el DOF publica el día *D* aparece en SF60653 con fecha
*D + 1***.

---

## 2. Reglas de uso

Las reglas dependen de la pregunta que se quiere contestar.

### 2.1 Tipo de cambio aplicable a una fecha (caso principal)

> **Usar SF60653 tal cual:** `tipo_cambio_aplicable(X) = SF60653[X]`

- Funciona para **cualquier día**, incluidos fines de semana y festivos.
- Ya incorpora la regla "se usa el publicado en el DOF el día anterior; si ese día no hubo
  publicación, el último publicado" (art. 20 del CFF). La interpretación fiscal debe
  confirmarse con Contabilidad.

### 2.2 Replicar lo que publicó el DOF en una fecha

> **Usar SF60653 del día siguiente:** `valor_publicado_dof(D) = SF60653[D + 1]`

- *D + 1* es un día **calendario**, no hábil. No hace falta un calendario de festivos.
- **Solo es válido si *D* fue día de publicación** (día hábil bancario). Si *D* es sábado,
  domingo o festivo, la fórmula devuelve el último valor publicado, aunque ese día el DOF no
  publicó nada.
- Si hace falta distinguir los días de publicación, ver la sección 7.

---

## 3. Requisitos

- **Token de consulta** de 64 caracteres. Es gratuito y se solicita en
  <https://www.banxico.org.mx/SieAPIRest/service/v1/token>.
  - Guardarlo en una variable de entorno o en un gestor de secretos (por ejemplo
    `BANXICO_TOKEN`).
  - **Nunca** guardarlo en el código ni en el repositorio.
- **TLS 1.3**: la API lo exige desde marzo de 2023. Verifica que el runtime del servidor lo
  soporte.
- Salida HTTPS hacia `www.banxico.org.mx`.

---

## 4. Endpoint

**URL base:** `https://www.banxico.org.mx/SieAPIRest/service/v1`

| Uso | Endpoint | Límite |
|---|---|---|
| **Rango de fechas (recomendado)** | `GET /series/SF60653/datos/{fechaIni}/{fechaFin}` | 200 consultas / 5 min · 10,000 / día |
| Último dato disponible | `GET /series/SF60653/datos/oportuno` | 80 consultas / min · 40,000 / día |

- Las fechas de la URL van en formato `yyyy-MM-dd`.
- **Headers:**
  ```
  Accept: application/json
  Bmx-Token: <token>
  ```
  El token también puede enviarse como `?token=`, pero se prefiere el header para que no
  quede en los logs.

### Ejemplo de consulta

```bash
curl -H "Accept: application/json" -H "Bmx-Token: $BANXICO_TOKEN" "https://www.banxico.org.mx/SieAPIRest/service/v1/series/SF60653/datos/2026-11-19/2026-11-25"
```

### Respuesta

```json
{
  "bmx": {
    "series": [
      {
        "idSerie": "SF60653",
        "titulo": "Tipo de cambio pesos por dólar E.U.A. Tipo de cambio para solventar obligaciones denominadas en moneda extranjera Fecha de liquidación",
        "datos": [
          { "fecha": "19/11/2026", "dato": "18.4677" },
          { "fecha": "20/11/2026", "dato": "18.6723" },
          { "fecha": "21/11/2026", "dato": "19.0270" },
          { "fecha": "22/11/2026", "dato": "19.0270" },
          { "fecha": "23/11/2026", "dato": "19.0270" }
        ]
      }
    ]
  }
}
```

### Cómo leer la respuesta

- `fecha` llega como **`dd/MM/yyyy`**, aunque la consulta se hace con `yyyy-MM-dd`.
- `dato` llega como **texto**. Hay que convertirlo a decimal y descartar cualquier valor que no
  sea numérico.
- Si el rango no tiene datos, la serie llega **sin la propiedad `datos`**, con HTTP 200. No es
  un error, pero tampoco hay nada que guardar.
- **Datos por adelantado:** en cuanto Banxico determina el FIX (después de las 12:00), SF60653
  ya trae el valor para los días siguientes, hasta el próximo día hábil. Por ejemplo, el
  jueves 19/11 a las 13:00 la serie ya tenía datos hasta el lunes 23/11.

### Errores observados

| Caso | Respuesta |
|---|---|
| Token inválido o ausente | HTTP 400 · `{"error":{"mensaje":"Token inválido", ...}}` |
| Fecha con formato incorrecto | HTTP 400 · `{"error":{"mensaje":"Formato de fecha incorrecto.", ...}}` |
| Serie inexistente | HTTP 302 (redirección): tratarlo como error |
| Rango sin datos | HTTP 200 sin `datos` |

---

## 5. Flujo diario

```
┌──────────────────────────┐
│ 1. Programador (L–V)     │  13:00 hora Ciudad de México
└────────────┬─────────────┘
             ▼
┌──────────────────────────┐
│ 2. GET SF60653           │  rango: [hoy − 10, hoy + 1] = hasta lo publicado hoy en el DOF
└────────────┬─────────────┘
             ▼
┌──────────────────────────┐
│ 3. Leer y validar        │  fecha dd/MM/yyyy, dato numérico, rango razonable
└────────────┬─────────────┘
             ▼
┌──────────────────────────┐
│ 4. Upsert en BD          │  clave: fecha_aplicacion + moneda
└────────────┬─────────────┘
             ▼
┌──────────────────────────┐
│ 5. Verificar cobertura   │  ¿existe lo publicado hoy en el DOF? si no → reintento / alerta
└──────────────────────────┘
```

### Paso 1 · Programación

- Correr de **lunes a viernes a las 13:00** (hora de la Ciudad de México). Banxico da a conocer
  el FIX a partir de las 12:00.
- Si en el paso 5 falta el dato, **reintentar cada 30 minutos hasta las 16:00**. Si para
  entonces sigue sin aparecer, enviar una alerta.
- Correr también en fines de semana no causa ningún problema: el upsert hace que repetir una
  corrida no tenga efecto.

### Paso 2 · Consulta

- Pedir siempre un **rango** (`hoy − 10` a `hoy + 1`), no solo el día actual. Así:
  - Si una corrida falla, la siguiente rellena los días faltantes.
  - Se recogen revisiones si Banxico corrige algún valor.
- El rango termina en **lo publicado hoy en el DOF**. Por la regla *D + 1*, ese valor tiene
  fecha de aplicación `hoy + 1` en SF60653; por eso el fin del rango es `hoy + 1` y no `hoy`. No se
  guardan valores posteriores, aunque SF60653 ya los tenga (ver "Datos por adelantado" en la
  sección 4).
  - Ejemplo: el jueves 19/11 se consulta hasta el 20/11 (18.6723), que es el valor que el DOF
    publicó el 19/11.
  - Los días sin publicación (fin de semana o puente) se guardan en la siguiente corrida que los
    incluya.
- Timeout sugerido: 30 segundos. En errores de red o HTTP 5xx, reintentar 3 veces con espera
  creciente (por ejemplo 1 s, 5 s y 30 s).

### Paso 3 · Validación

Por cada elemento de `datos`:

1. Convertir `fecha` de `dd/MM/yyyy` a fecha.
2. Convertir `dato` a decimal. Si no es numérico, **omitirlo** y registrarlo en el log.
3. Verificar que esté en un rango razonable, por ejemplo **10.0000 a 40.0000**. Si no, enviar
   una alerta y no guardar.
4. Si la variación contra el día anterior es mayor al 5 %, **enviar una alerta**, pero sí
   guardar el valor: el movimiento puede ser real.

### Paso 4 · Persistencia (upsert)

```sql
CREATE TABLE tipo_cambio (
    fecha_aplicacion       DATE           NOT NULL,  -- fecha de SF60653
    moneda                 CHAR(3)        NOT NULL DEFAULT 'USD',
    valor                  DECIMAL(12,6)  NOT NULL,
    fecha_publicacion_dof  DATE           NOT NULL,  -- fecha_aplicacion − 1 día
    fuente                 VARCHAR(20)    NOT NULL DEFAULT 'BANXICO_SF60653',
    fecha_captura          DATETIME       NOT NULL,
    fecha_actualizacion    DATETIME       NULL,
    PRIMARY KEY (fecha_aplicacion, moneda)
);
```

- Si la fila no existe: **insertarla**.
- Si existe con el **mismo valor**: no hacer nada.
- Si existe con un **valor distinto**: actualizarla, llenar `fecha_actualizacion` y registrar
  el cambio en el log de auditoría.

### Paso 5 · Verificación de cobertura

Después de guardar, debe existir la fila del valor **publicado hoy en el DOF**
(`fecha_publicacion_dof = hoy`, o sea `fecha_aplicacion = hoy + 1`). Si no existe a las 16:00 de
un día hábil, enviar una alerta.

> Lo que el DOF publica hoy es el FIX que Banxico determinó el día hábil anterior. Por eso esta
> verificación detecta que Banxico o la consulta se atrasaron, no que falte el FIX que se
> determina hoy (ese aparece como `hoy + 2` o más, y queda fuera del rango a propósito).

---

## 6. Consultas desde el sistema interno

**Tipo de cambio aplicable a una fecha** (caso 2.1):

```sql
SELECT valor
FROM tipo_cambio
WHERE moneda = 'USD'
  AND fecha_aplicacion = :fecha;
```

**Valor publicado por el DOF en una fecha** (caso 2.2; solo es válido en días de publicación):

```sql
SELECT valor
FROM tipo_cambio
WHERE moneda = 'USD'
  AND fecha_publicacion_dof = :fecha;
```

---

## 7. Opcional: distinguir los días de publicación en el DOF

SF60653 no indica si un día hubo publicación. Si hace falta saberlo, por ejemplo para marcar
la columna `es_publicacion_dof`, hay dos opciones:

- **Calendario de días hábiles bancarios** (lo publica Banxico cada año).
- **Serie `SF43718`** (FIX por fecha de determinación): solo tiene datos en días hábiles
  bancarios, que son los mismos días en que el DOF publica. En noviembre de 2026, las fechas
  de SF43718 y las del histórico del DOF coincidieron una a una. Así, *D* fue día de
  publicación si existe `SF43718[D]`.

  Se pueden consultar ambas series en una sola llamada:
  `/series/SF43718,SF60653/datos/{ini}/{fin}`.

---

## 8. Carga histórica inicial

- La serie SF60653 tiene datos desde **14/11/1991**.
- Cargar por **bloques de un año**, por ejemplo `/datos/2020-01-01/2020-12-31`, con el mismo
  proceso de los pasos 3 y 4.
- Hacer una pausa de 1 a 2 segundos entre llamadas para no acercarse al límite de 200
  consultas cada 5 minutos.

---

## 9. Validación cruzada con el DOF (opcional, recomendada al inicio)

Durante las primeras semanas, comparar el valor de cada día hábil contra el histórico
publicado por el DOF:

```
https://dof.gob.mx/indicadores_detalle.php?cod_tipo_indicador=158&dfecha=DD/MM/AAAA&hfecha=DD/MM/AAAA
```

Se debe cumplir siempre `DOF[D] = SF60653[D + 1]`. Una diferencia indica un problema en el
cálculo de fechas o una corrección del lado de Banxico.

---

## 10. Referencias

- Documentación de la API SIE: <https://www.banxico.org.mx/SieAPIRest/service/v1/>
- Especificación OpenAPI: <https://www.banxico.org.mx/SieAPIRest/service/v2/api-docs>
- Catálogo de series de tipo de cambio (cuadro CF102):
  <https://www.banxico.org.mx/SieInternet/consultarDirectorioInternetAction.do?sector=6&accion=consultarCuadro&idCuadro=CF102&locale=es>
- Reglas del FIX: Circular 3/2012 de Banco de México, Título Tercero, Capítulo V.

---

## 11. Aplicación de consola (.NET 10 + Docker)

Implementa las secciones 4, 5 y 8 sobre **SQL Server**. El contenedor **corre una vez y termina**:
la programación (L–V 13:00 hora CDMX) la hace un programador externo, y los reintentos cada 30
minutos hasta las 16:00 ocurren dentro de la misma corrida.

```
src/TipoCambioDof/          App de consola
  Banxico/                  Cliente HTTP y lectura de la respuesta SIE
  Dominio/Validador.cs      Rango razonable y variación diaria (paso 3)
  Datos/                    Esquema.sql (embebido) y upsert con MERGE (paso 4);
                            Actualizacion.sql (embebido) para los sistemas legados
  Procesos/                 Corrida diaria (pasos 1–5) y carga histórica (sección 8)
  Alertas/Alertador.cs      Log + webhook opcional
tests/TipoCambioDof.Tests/  Pruebas unitarias (xUnit)
```

### Comandos

| Comando | Qué hace |
|---|---|
| `diario` (por defecto) | Consulta SF60653 `[hoy − 10, hoy + 1]`, es decir, hasta lo publicado hoy en el DOF. Valida, hace upsert y verifica que exista ese valor. Si falta, reintenta cada 30 min hasta las 16:00 y luego alerta. |
| `historico [--desde yyyy-MM-dd] [--hasta yyyy-MM-dd]` | Fechas de **aplicación** (SF60653). Carga por bloques de un año, con 1.5 s de pausa entre llamadas. Por defecto va del 1991-11-14 a hoy + 1 (lo publicado hoy en el DOF); un `--hasta` posterior se recorta a ese límite. |
| `crear-tabla` | Crea `dbo.tipo_cambio` y `dbo.tipo_cambio_auditoria` si no existen (idempotente). |
| `ayuda` | Muestra la ayuda. |

**Códigos de salida:** `0` OK · `1` error (configuración, API o BD) · `2` terminó con alertas
(valor fuera de rango, variación > 5 % o falta el valor de cobertura) · `64` argumentos inválidos.

### Variables de entorno

| Variable | Default | Descripción |
|---|---|---|
| `BANXICO_TOKEN` | — | Token SIE de 64 caracteres. Requerido para `diario` e `historico`. |
| `SQL_CONNECTION_STRING` | — | Cadena de conexión de SQL Server. Requerida. |
| `ALERT_WEBHOOK_URL` | — | Opcional. Cada alerta se envía por POST como `{"text": "..."}` (Slack / Teams). |
| `DIAS_ATRAS` / `DIAS_ADELANTE` | `10` / `1` | Rango en fechas de aplicación: `[hoy − DIAS_ATRAS, hoy + DIAS_ADELANTE]`. `1` = hasta lo publicado hoy en el DOF. `0` = solo hasta la fecha de aplicación de hoy (lo publicado ayer en el DOF). |
| `VALOR_MINIMO` / `VALOR_MAXIMO` | `10` / `40` | Rango razonable; fuera de él no se guarda. |
| `VARIACION_MAXIMA_PCT` | `5` | Variación diaria que dispara alerta (sí se guarda). |
| `REINTENTO_MINUTOS` / `HORA_LIMITE` | `30` / `16:00` | Reintentos de la verificación de cobertura (hora CDMX). |
| `PAUSA_HISTORICO_MS` | `1500` | Pausa entre bloques de la carga histórica. |
| `ACTUALIZAR_LEGADOS` | `false` | `true` = al confirmar la cobertura, `diario` escribe lo publicado hoy en el DOF en los sistemas legados (ver abajo). Requiere `DIAS_ADELANTE` ≥ 1. |
| `BD_METRICS` / `BD_LITO` | — | Requeridas si `ACTUALIZAR_LEGADOS=true`. Nombres de las bases de datos (p. ej. `MetricsBETA` / `Lito_jess`), en la misma instancia que `SQL_CONNECTION_STRING`. Solo letras, dígitos y `_`. |

#### Sistemas legados (`ACTUALIZAR_LEGADOS=true`)

Al final de `diario`, cuando ya existe el valor con `fecha_publicacion_dof` = hoy, se ejecuta
`Datos/Actualizacion.sql` en una transacción propia (separada de `dbo.tipo_cambio`):

- `BD_METRICS.dbo.FIN_MovtoMoeda`: upsert de `IdMoeda = 2`, `Base = 1`, `DataMovtoMoeda` = hoy
  (fecha de publicación DOF), `ValorMoedaDia` = valor. Repetir la corrida no duplica filas.
- `BD_LITO.dbo.Mon`: `TipoCambio` y `UltimoCambio = GETDATE()` de `Moneda = 'Dolares'` en cada
  corrida de `diario` con cobertura OK, aunque el valor no cambie, así `UltimoCambio` refleja la
  última escritura. Cada corrida agrega una fila en `MonHist` por el trigger de `Mon`. Si esa fila
  no existe se envía una alerta (código 2) y no se inserta.

Si la escritura falla (permisos, base inexistente, etc.) la corrida alerta y termina con código 1;
`dbo.tipo_cambio` conserva lo sincronizado. El usuario de SQL necesita `SELECT/INSERT/UPDATE` sobre
`FIN_MovtoMoeda` y `SELECT/UPDATE` sobre `Mon`.

Diferencias contra el esquema de la sección 5:

- Las columnas de fecha y hora son `DATETIME2(0)`. `fecha_captura` y `fecha_actualizacion` de
  `tipo_cambio` se llenan con `GETDATE()` (hora local del servidor SQL). `fecha_cambio` de
  `tipo_cambio_auditoria` sigue en **UTC** (`SYSUTCDATETIME()`).
- Se agrega la tabla `tipo_cambio_auditoria`, donde queda cada valor que Banxico revise.

> **Carga histórica antes de ~1999:** el tipo de cambio estaba por debajo de 10 pesos, así que con
> el rango por defecto esos valores se descartan. Para cargarlos usa, por ejemplo,
> `-e VALOR_MINIMO=2`. Espera además alertas de variación en 1994–1995, que fue una devaluación real.

### Desarrollo local (sin Docker)

Requiere el SDK de .NET 10. Fuera de un contenedor, la app carga el `.env` de la raíz del repo
(`cp .env.example .env`). Las variables de entorno reales tienen prioridad sobre el `.env`. En
Docker ese archivo se ignora: no se copia a la imagen y la app no lo busca si detecta
`DOTNET_RUNNING_IN_CONTAINER`.

```bash
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~ValidadorTests"          # una clase de pruebas
dotnet run --project src/TipoCambioDof -- historico --desde 2026-11-01 --hasta 2026-11-30
```

Sin el SDK 10 instalado, las pruebas se pueden correr en un contenedor:

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
```

Correr la imagen local contra la base de datos de `SQL_CONNECTION_STRING`:

```bash
cp .env.example .env          # llenar BANXICO_TOKEN y SQL_CONNECTION_STRING
docker compose run --rm app crear-tabla
docker compose run --rm app   # corrida diaria
```

Dentro del contenedor, `localhost` es el propio contenedor. Si SQL Server corre en tu máquina,
usa `Server=host.docker.internal,1433` en la cadena de conexión.

### Publicar en Docker Hub

La imagen es multi-arquitectura (`linux/amd64` y `linux/arm64`) y corre con un usuario sin
privilegios. Sustituye `<usuario>` por tu cuenta u organización de Docker Hub:

```bash
docker login
docker buildx build --platform linux/amd64,linux/arm64 \
  -t <usuario>/tipo-cambio-dof:1.0.0 -t <usuario>/tipo-cambio-dof:latest --push .
```

### Ejecutar la imagen

```bash
docker run --rm \
  -e BANXICO_TOKEN -e SQL_CONNECTION_STRING -e ALERT_WEBHOOK_URL \
  <usuario>/tipo-cambio-dof:latest            # = diario

docker run --rm -e BANXICO_TOKEN -e SQL_CONNECTION_STRING \
  <usuario>/tipo-cambio-dof:latest historico --desde 2020-01-01
```

Programación con **cron** en el host (servidor en hora CDMX). Se fija la versión (`1.0.0`) en lugar
de `latest`, para que una publicación nueva no cambie lo que corre sin actualizar el cron:

```cron
0 13 * * 1-5  docker run --rm --env-file /etc/tipo-cambio-dof.env tonovarela/tipo-cambio-dof:1.0.0 >> /var/log/tipo-cambio-dof.log 2>&1
```

Producción con **Docker Compose** (`docker-compose.prod.yml`). Usa la imagen publicada `1.0.0`, sin
compilar, con sistema de archivos de solo lectura y sin privilegios. En el servidor solo hacen falta
ese archivo y un `.env` junto a él (copiado de `.env.example`, con `chmod 600`):

```bash
mkdir -p /opt/tipo-cambio-dof && cd /opt/tipo-cambio-dof
# copiar aquí docker-compose.prod.yml y .env
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml run --rm app crear-tabla   # una sola vez
docker compose -f docker-compose.prod.yml run --rm app               # prueba manual de diario
```

El contenedor corre una vez y termina, así que la programación va en el crontab del host con
`tipo-cambio-dof.sh`, que se copia a la misma carpeta (`chmod +x`). El script:

- corre `docker compose -f docker-compose.prod.yml run --rm -T app` con los argumentos que reciba
  (sin argumentos, `diario`);
- escribe la salida con fecha y hora en un archivo por día, `logs/tipo-cambio-dof-AAAA-MM-DD.log`
  (fecha CDMX; carpeta configurable con `TIPO_CAMBIO_LOG_DIR`), y borra los de más de 90 días
  (`TIPO_CAMBIO_LOG_DIAS`, `0` = conservar todos);
- usa `flock` para que dos corridas no se encimen; si ya hay una, sale con código 75;
- termina con el código de salida de la app (0, 1, 2 o 64).

```bash
./tipo-cambio-dof.sh crear-tabla                    # una sola vez
./tipo-cambio-dof.sh                                # prueba manual de diario
./tipo-cambio-dof.sh historico --desde 2024-01-01
```

El script está pensado para **CentOS** (cronie, bash ≥ 4, `flock` de util-linux). Usa `docker compose`
(paquete `docker-compose-plugin`) y, si no está, el binario `docker-compose`. Entrada de `crontab -e`
de root o de un usuario del grupo `docker`. `CRON_TZ` hace que cronie la evalúe en hora CDMX aunque
el servidor esté en otra zona:

```cron
CRON_TZ=America/Mexico_City
0 13 * * 1-5  /opt/tipo-cambio-dof/tipo-cambio-dof.sh
```

Para cambiar de versión, edita `image:` en `docker-compose.prod.yml` y corre `pull`.
