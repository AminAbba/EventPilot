using Microsoft.Data.SqlClient;

namespace EventPilot.Infrastructure.Persistence;

public static class SqlServerErrors
{
    public static bool IsUniqueConstraintViolation(Exception ex)
    {
        var sqlEx = ex as SqlException ?? ex.InnerException as SqlException;
        if (sqlEx is null) return false;

        // 2627 and 2601 are duplicate key errors.
        return sqlEx.Number is 2627 or 2601;
    }
}
