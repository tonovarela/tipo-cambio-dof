-- SPEC 01 y SPEC 02. Escribe en los sistemas legados el tipo de cambio publicado hoy en el DOF.
-- Un solo batch (sin GO). RepositorioLegados lo ejecuta dentro de su propia transacción.
-- Los marcadores de base de datos se reemplazan por [nombre] (validado en Configuracion) antes de ejecutar.
-- Parámetros: @valor DECIMAL(12,6) y @fecha DATE (fecha de publicación DOF = hoy).
-- Devuelve una fila: movto ∈ INSERT | UPDATE | SIN_CAMBIO, mon ∈ UPDATE | NO_EXISTE.
--
-- Nunca se ejecuta un UPDATE de 0 filas: los triggers AFTER UPDATE se disparan aunque el UPDATE no
-- afecte filas, y el de LITO_jess.dbo.Mon (historial en MonHist) falla con 'inserted' vacío.
-- En FIN_MovtoMoeda solo se actualiza si el valor cambió; en Mon, siempre que exista la fila 'Dolares'.

SET NOCOUNT ON;

DECLARE @movto VARCHAR(10), @mon VARCHAR(10);

-- Metrics: upsert por (IdMoeda, fecha). CAST por si DataMovtoMoeda es DATETIME con hora.
IF EXISTS (SELECT 1 FROM {BD_METRICS}.dbo.FIN_MovtoMoeda WITH (UPDLOCK, HOLDLOCK)
           WHERE IdMoeda = 2 AND CAST(DataMovtoMoeda AS DATE) = @fecha)
BEGIN
    IF EXISTS (SELECT 1 FROM {BD_METRICS}.dbo.FIN_MovtoMoeda
               WHERE IdMoeda = 2 AND CAST(DataMovtoMoeda AS DATE) = @fecha
                 AND (ValorMoedaDia <> @valor OR ValorMoedaDia IS NULL))
    BEGIN
        UPDATE {BD_METRICS}.dbo.FIN_MovtoMoeda
           SET ValorMoedaDia = @valor
         WHERE IdMoeda = 2 AND CAST(DataMovtoMoeda AS DATE) = @fecha;
        SET @movto = 'UPDATE';
    END
    ELSE
        SET @movto = 'SIN_CAMBIO';
END
ELSE
BEGIN
    INSERT INTO {BD_METRICS}.dbo.FIN_MovtoMoeda (IdMoeda, ValorMoedaDia, DataMovtoMoeda, Base)
    VALUES (2, @valor, @fecha, 1);
    SET @movto = 'INSERT';
END;

-- Lito (SPEC 02): se actualiza en cada corrida, aunque el valor no cambie, para que UltimoCambio
-- refleje la última escritura de esta app. Cada corrida agrega una fila en MonHist (trigger).
-- Si falta la fila 'Dolares' no se inserta (la tabla no es de esta app); el código alerta.
-- El IF NOT EXISTS previo garantiza que el UPDATE afecte exactamente una fila.
IF NOT EXISTS (SELECT 1 FROM {BD_LITO}.dbo.Mon WITH (UPDLOCK, HOLDLOCK) WHERE Moneda = 'Dolares')
    SET @mon = 'NO_EXISTE';
ELSE
BEGIN
    UPDATE {BD_LITO}.dbo.Mon
       SET TipoCambio = @valor, UltimoCambio = GETDATE()
     WHERE Moneda = 'Dolares';
    SET @mon = 'UPDATE';
END;

SELECT @movto AS movto, @mon AS mon;
