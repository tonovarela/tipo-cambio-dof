-- Esquema de README §5 paso 4, para SQL Server. Idempotente: se puede correr varias veces.
-- Las columnas de auditoría se guardan en UTC.

IF OBJECT_ID(N'dbo.tipo_cambio', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tipo_cambio (
        fecha_aplicacion       DATE           NOT NULL,  -- fecha de SF60653
        moneda                 CHAR(3)        NOT NULL CONSTRAINT DF_tipo_cambio_moneda DEFAULT 'USD',
        valor                  DECIMAL(12,6)  NOT NULL,
        fecha_publicacion_dof  DATE           NOT NULL,  -- fecha_aplicacion - 1 día
        fuente                 VARCHAR(20)    NOT NULL CONSTRAINT DF_tipo_cambio_fuente DEFAULT 'BANXICO_SF60653',
        fecha_captura          DATETIME2(0)   NOT NULL,
        fecha_actualizacion    DATETIME2(0)   NULL,
        CONSTRAINT PK_tipo_cambio PRIMARY KEY (fecha_aplicacion, moneda)
    );

    CREATE INDEX IX_tipo_cambio_publicacion_dof
        ON dbo.tipo_cambio (moneda, fecha_publicacion_dof) INCLUDE (valor);
END;

IF OBJECT_ID(N'dbo.tipo_cambio_auditoria', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tipo_cambio_auditoria (
        id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tipo_cambio_auditoria PRIMARY KEY,
        fecha_aplicacion  DATE           NOT NULL,
        moneda            CHAR(3)        NOT NULL,
        valor_anterior    DECIMAL(12,6)  NOT NULL,
        valor_nuevo       DECIMAL(12,6)  NOT NULL,
        fecha_cambio      DATETIME2(0)   NOT NULL CONSTRAINT DF_tipo_cambio_auditoria_fecha DEFAULT SYSUTCDATETIME()
    );
END;
