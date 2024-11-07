﻿using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EntityFramework.Exceptions.PostgreSQL;

class PostgresExceptionProcessorInterceptor : ExceptionProcessorInterceptor<PostgresException>
{
    protected override DatabaseError? GetDatabaseError(PostgresException dbException)
    {
        return dbException.SqlState switch
        {
            PostgresErrorCodes.StringDataRightTruncation => DatabaseError.MaxLength,
            PostgresErrorCodes.NumericValueOutOfRange => DatabaseError.NumericOverflow,
            PostgresErrorCodes.NotNullViolation => DatabaseError.CannotInsertNull,
            PostgresErrorCodes.UniqueViolation => DatabaseError.UniqueConstraint,
            PostgresErrorCodes.ForeignKeyViolation => DatabaseError.ReferenceConstraint,
            _ => null
        };
    }
}