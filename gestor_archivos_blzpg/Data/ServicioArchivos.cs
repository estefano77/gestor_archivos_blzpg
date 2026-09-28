using Microsoft.EntityFrameworkCore;

namespace GestorArchivosBlzpg.Data;

/// <summary>Motivo por el que no se puede subir un archivo.</summary>
public enum MotivoRechazo
{
    /// <summary>Supera el maximo por archivo.</summary>
    Tamano,

    /// <summary>No cabe en la cuota de la cuenta.</summary>
    Cuota,
}

/// <summary>
/// Resultado de intentar reservar cuota y crear un archivo.
/// </summary>
/// <param name="Aceptado">Si el archivo llego a crearse.</param>
/// <param name="Motivo">Por que se rechazo, o null si se acepto.</param>
/// <param name="Archivo">El archivo creado, si se acepto.</param>
/// <param name="UsadoAntes">Espacio usado en el momento del rechazo, para el mensaje.</param>
/// <param name="Cuota">Cuota total de la cuenta, para el mensaje.</param>
public sealed record ResultadoSubida(
    bool Aceptado,
    MotivoRechazo? Motivo,
    FileItem? Archivo,
    long UsadoAntes,
    long Cuota);

public class ServicioArchivos(AppDbContext db)
{
    /// <summary>
    /// Espacio ocupado por un usuario, sumando todos sus archivos.
    /// </summary>
    public async Task<long> EspacioUsado(Guid userId, CancellationToken ct = default)
    {
        // SQL directo y no LINQ, por una razon concreta: size es bigint y
        // PostgreSQL devuelve NUMERIC al sumarla, no bigint. EF Core no sabe
        // mapear un NUMERIC a un long y falla con "no existe la columna s.Value",
        // que no menciona ni la consulta ni la tabla.
        //
        // El COALESCE hace falta porque SUM() sobre cero filas devuelve NULL, y
        // un null aqui se restaria de la cuota o daria error al convertir.
        //
        // Se usa GetDbConnection y no una conexion propia para que la
        // transaccion de la subida, que pasa por el mismo DbContext, vea
        // tambien esta consulta. Con una conexion aparte, el FOR UPDATE no la
        // protegeria y la cuota se comprobaria contra datos sueltos.
        var conexion = db.Database.GetDbConnection();
        if (conexion.State != System.Data.ConnectionState.Open)
        {
            await conexion.OpenAsync(ct);
        }

        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT COALESCE(SUM(size), 0)::bigint FROM files WHERE user_id = @userId";

        var parametro = comando.CreateParameter();
        parametro.ParameterName = "userId";
        parametro.Value = userId;
        comando.Parameters.Add(parametro);

        return Convert.ToInt64(await comando.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Bloquea la fila del usuario hasta que termine la transaccion.
    /// </summary>
    /// <remarks>
    /// Este es el punto delicate de toda la subida. Sin el, dos subidas
    /// simultaneas de la misma cuenta leen el mismo espacio usado, las dos
    /// pasan el control y las dos escriben, con lo que el usuario se queda por
    /// encima de la cuota. Ese defecto existia en la version con MongoDB, donde
    /// la suma y la insercion iban en dos pasos sin transaccion.
    /// </remarks>
    public async Task<User?> BloquearFilaUsuario(Guid userId, CancellationToken ct = default)
    {
        // FOR UPDATE retiene el bloqueo de la fila hasta el COMMIT o el
        // ROLLBACK. Quien venga despues espera, en vez de leer un valor que
        // va a quedar obsoleto en cuanto se escriba.
        //
        // Se usa FromSqlInterpolated, no concatenar: el identificador va
        // parametrizado aunque este escrito en el codigo, que es lo que evita
        // que un valor venueido de fuera se interprete como SQL.
        return await db.Users
            .FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Reserva cuota y crea la fila del archivo, todo dentro de una transaccion.
    /// </summary>
    /// <remarks>
    /// El orden de las tres operaciones no es arbitrario:
    ///
    ///   1. bloquear la fila del usuario, para que nadie mas escriba mientras
    ///      se decide;
    ///   2. sumar el espacio ya con el bloqueo puesto, y comprobar la cuota;
    ///   3. insertar y confirmar.
    ///
    /// Si se comprobara antes de bloquear, la suma seria de un valor que otra
    /// transaccion puede cambiar antes de que esta escriba, que es
    /// precisamente la carrera que se quiere evitar.
    /// </remarks>
    public async Task<ResultadoSubida> ReservarYCrear(
        Guid userId,
        FileItem archivo,
        long cuota,
        CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1. Bloquear.
        var usuario = await BloquearFilaUsuario(userId, ct);
        if (usuario is null)
        {
            await tx.RollbackAsync(ct);
            return new ResultadoSubida(false, MotivoRechazo.Cuota, null, 0, cuota);
        }

        // 2. Sumar y comprobar, ya sin nadie mas escribiendo.
        var usado = await EspacioUsado(userId, ct);

        if (usado + archivo.Size > cuota)
        {
            // Se revierte en vez de confirmar: la transaccion no ha escrito nada,
            // pero dejarlo abierto haria esperar a los demás hasta que se
            // recogiera.
            await tx.RollbackAsync(ct);
            return new ResultadoSubida(false, MotivoRechazo.Cuota, null, usado, cuota);
        }

        // 3. Escribir.
        archivo.UserId = userId;
        db.Files.Add(archivo);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ResultadoSubida(true, null, archivo, usado, cuota);
    }
}
