-- =====================================================================
-- DynCore - SPs de prueba para las 4 estrategias
-- Ejecutar en wbATCCore
-- =====================================================================

-- ═══════════════════════════════════════
-- 1. QUERY: Lee comandos registrados
-- ═══════════════════════════════════════
IF OBJECT_ID('dbo.precTestQuery', 'P') IS NOT NULL DROP PROCEDURE dbo.precTestQuery
GO
CREATE PROCEDURE dbo.precTestQuery
    @pnTop INT = 10
AS
BEGIN
    SET NOCOUNT ON
    SELECT TOP (@pnTop)
        ComandoSQLId,
        ComandoSQLDesc,
        ComandoCmd,
        ConexionSQLId,
        Activo
    FROM acsComandoSQL
    WHERE Activo = 1
    ORDER BY ComandoSQLId DESC
END
GO

-- ═══════════════════════════════════════
-- 2. TRANSACTION: Simula insert con validación
-- ═══════════════════════════════════════
IF OBJECT_ID('dbo.precTestTransaction', 'P') IS NOT NULL DROP PROCEDURE dbo.precTestTransaction
GO
CREATE PROCEDURE dbo.precTestTransaction
    @psNombre NVARCHAR(100),
    @pnUsuarioId INT
AS
BEGIN
    SET NOCOUNT ON
    DECLARE @nError INT = 0, @sMensaje NVARCHAR(500) = ''

    -- Validación simple
    IF ISNULL(@psNombre, '') = ''
    BEGIN
        SET @nError = 1
        SET @sMensaje = 'El nombre es requerido'
    END
    ELSE
    BEGIN
        SET @sMensaje = 'Operación exitosa (simulada) - Nombre: ' + @psNombre + ', Usuario: ' + CAST(@pnUsuarioId AS NVARCHAR)
    END

    -- DynCore lee estas columnas para decidir commit/rollback
    SELECT @nError AS Error, @sMensaje AS Mensaje
END
GO

-- ═══════════════════════════════════════
-- 3. MULTIRESULT: Retorna 3 datasets
-- ═══════════════════════════════════════
IF OBJECT_ID('dbo.precTestMultiResult', 'P') IS NOT NULL DROP PROCEDURE dbo.precTestMultiResult
GO
CREATE PROCEDURE dbo.precTestMultiResult
AS
BEGIN
    SET NOCOUNT ON

    -- Dataset 1: info (resumen)
    SELECT
        COUNT(*) AS TotalComandos,
        SUM(CASE WHEN Activo = 1 THEN 1 ELSE 0 END) AS Activos,
        SUM(CASE WHEN Activo = 0 THEN 1 ELSE 0 END) AS Inactivos
    FROM acsComandoSQL

    -- Dataset 2: info2 (conexiones)
    SELECT
        ConexionSQLId,
        COUNT(*) AS Comandos
    FROM acsComandoSQL
    WHERE Activo = 1
    GROUP BY ConexionSQLId

    -- Dataset 3: info3 (últimos 5 comandos)
    SELECT TOP 5
        ComandoSQLId,
        ComandoSQLDesc,
        ComandoCmd
    FROM acsComandoSQL
    ORDER BY ComandoSQLId DESC
END
GO

-- ═══════════════════════════════════════
-- 4. LOOKUP: Catálogo simple para probar includes
-- ═══════════════════════════════════════
IF OBJECT_ID('dbo.precTestLookup', 'P') IS NOT NULL DROP PROCEDURE dbo.precTestLookup
GO
CREATE PROCEDURE dbo.precTestLookup
AS
BEGIN
    SET NOCOUNT ON
    SELECT DISTINCT
        ConexionSQLId AS Id,
        'Conexion ' + CAST(ConexionSQLId AS NVARCHAR) AS Nombre
    FROM acsComandoSQL
    WHERE Activo = 1
END
GO

PRINT '✓ 4 SPs de prueba creados exitosamente'
