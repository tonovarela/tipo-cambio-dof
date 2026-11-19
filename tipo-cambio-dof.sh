#!/usr/bin/env bash
# Ejecuta tipo-cambio-dof con docker-compose.prod.yml. Pensado para el crontab (cronie) de CentOS,
# lun–vie 13:00 CDMX:
#
#   CRON_TZ=America/Mexico_City
#   0 13 * * 1-5  /opt/tipo-cambio-dof/tipo-cambio-dof.sh
#
# Ejecutarlo directo (./tipo-cambio-dof.sh, con chmod +x) o con bash; si se llama con sh, se relanza con bash.
# Requiere bash ≥ 4.2, flock (util-linux) y Docker con Compose (docker-compose-plugin o docker-compose).
# El usuario del crontab debe poder usar docker (root o miembro del grupo docker).
#
# Debe estar en la misma carpeta que docker-compose.prod.yml y .env.
# Sin argumentos corre "diario". Cualquier argumento se pasa tal cual a la app:
#   ./tipo-cambio-dof.sh crear-tabla
#   ./tipo-cambio-dof.sh historico --desde 2024-01-01
#
# Bitácora: un archivo por día, logs/tipo-cambio-dof-AAAA-MM-DD.log junto al script (fecha CDMX).
# Corridas del mismo día se agregan al mismo archivo. Se borran los de más de TIPO_CAMBIO_LOG_DIAS
# días (90 por defecto; 0 = no borrar). Carpeta configurable con TIPO_CAMBIO_LOG_DIR.
# Si se corre a mano en una terminal, la salida también se ve en pantalla.
# Código de salida: el de la app (0 ok, 1 error, 2 con alertas, 64 argumentos inválidos),
# o 75 si ya hay otra corrida en curso.
# Con "sh tipo-cambio-dof.sh" (dash, ksh, etc.) se relanza con bash.
if [ -z "${BASH_VERSION:-}" ]; then exec bash "$0" "$@"; fi

set -uo pipefail

# cron arranca con un PATH mínimo; docker puede estar en /usr/bin o /usr/local/bin.
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:${PATH:-}"

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE="$DIR/docker-compose.prod.yml"
LOG_DIR="${TIPO_CAMBIO_LOG_DIR:-$DIR/logs}"
LOG_DIAS="${TIPO_CAMBIO_LOG_DIAS:-90}"
# Fecha y hora en CDMX, igual que el "hoy" de la app, aunque el servidor esté en UTC.
export TZ="${TZ:-America/Mexico_City}"
LOG="$LOG_DIR/tipo-cambio-dof-$(date +%F).log"
LOCK="${TIPO_CAMBIO_LOCK:-/tmp/tipo-cambio-dof.lock}"

log() { printf '%s [tipo-cambio-dof.sh] %s\n' "$(date '+%Y-%m-%d %H:%M:%S %z')" "$*"; }

principal() {
    # Evita dos corridas a la vez: "diario" puede durar hasta las 16:00 por los reintentos.
    if command -v flock >/dev/null 2>&1; then
        exec 9>"$LOCK"
        if ! flock -n 9; then
            log "Ya hay una corrida en curso (lock $LOCK); se omite."
            return 75
        fi
    else
        log "Aviso: no está flock; no se evita que dos corridas se encimen."
    fi

    local archivo
    for archivo in "$COMPOSE" "$DIR/.env"; do
        if [ ! -f "$archivo" ]; then
            log "Falta $archivo"
            return 1
        fi
    done

    # Compose v2 (plugin docker-compose-plugin) o, si no está, el binario docker-compose v1.
    local compose
    if docker compose version >/dev/null 2>&1; then
        compose=(docker compose)
    elif command -v docker-compose >/dev/null 2>&1; then
        compose=(docker-compose)
    else
        log "No se encontró 'docker compose' ni 'docker-compose'. En CentOS: yum install docker-compose-plugin"
        return 1
    fi

    log "Inicio: ${*:-diario}"
    local codigo=0
    "${compose[@]}" -p tipo-cambio-dof -f "$COMPOSE" --project-directory "$DIR" run --rm -T app "$@" || codigo=$?
    log "Fin: código de salida $codigo"
    return "$codigo"
}

mkdir -p "$LOG_DIR"

# Retención: borra las bitácoras diarias más viejas que LOG_DIAS días.
if [[ "$LOG_DIAS" =~ ^[0-9]+$ ]] && [ "$LOG_DIAS" -gt 0 ]; then
    find "$LOG_DIR" -maxdepth 1 -type f -name 'tipo-cambio-dof-*.log' -mtime +"$LOG_DIAS" -delete 2>/dev/null
fi

# Sin process substitution (>(...)): no existe en bash < 5.1 cuando corre como sh (modo POSIX).
if [ -t 1 ]; then
    principal "$@" 2>&1 | tee -a "$LOG"
    codigo=${PIPESTATUS[0]}
else
    principal "$@" >>"$LOG" 2>&1
    codigo=$?
fi
exit "$codigo"
