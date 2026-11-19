-- SPEC 01. Escribe en los sistemas legados el tipo de cambio publicado hoy en el DOF.
-- Un solo batch (sin GO), idempotente. RepositorioLegados lo ejecuta dentro de su propia transacción.
-- Los marcadores de base de datos se reemplazan por [nombre] (validado en Configuracion) antes de ejecutar.
-- Parámetros: @valor DECIMAL(12,6) y @fecha DATE (fecha de publicación DOF = hoy).
-- Devuelve una fila: movto ∈ INSERT | UPDATE | SIN_CAMBIO, mon ∈ UPDATE | SIN_CAMBIO | NO_EXISTE.
--
-- Un UPDATE solo se ejecuta si hay un valor distinto: los triggers AFTER UPDATE se disparan aunque el
-- UPDATE afecte 0 filas, y el de LITO_jess.dbo.Mon (historial en MonHist) falla con 'inserted' vacío.

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

-- Lito: se actualiza si cambia el valor o si todavía no se actualizó hoy (@fecha), para que haya un
-- UltimoCambio y una fila en MonHist por día, como con la captura manual. Repetir la corrida el
-- mismo día no mueve nada.
-- Si falta la fila 'Dolares' no se inserta (la tabla no es de esta app); el código alerta.
IF NOT EXISTS (SELECT 1 FROM {BD_LITO}.dbo.Mon WITH (UPDLOCK, HOLDLOCK) WHERE Moneda = 'Dolares')
    SET @mon = 'NO_EXISTE';
ELSE IF EXISTS (SELECT 1 FROM {BD_LITO}.dbo.Mon
                WHERE Moneda = 'Dolares'
                  AND (TipoCambio <> @valor OR TipoCambio IS NULL
                       OR UltimoCambio IS NULL OR CAST(UltimoCambio AS DATE) < @fecha))
BEGIN
    UPDATE {BD_LITO}.dbo.Mon
       SET TipoCambio = @valor, UltimoCambio = GETDATE()
     WHERE Moneda = 'Dolares';
    SET @mon = 'UPDATE';
END
ELSE
    SET @mon = 'SIN_CAMBIO';

SELECT @movto AS movto, @mon AS mon;
