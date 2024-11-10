using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntityFramework.Exceptions.Common;

public abstract class ExceptionProcessorInterceptor<T> : IDbCommandInterceptor, ISaveChangesInterceptor
    where T : DbException
{
    private List<IndexDetails> uniqueIndexDetailsList;
    private List<ForeignKeyDetails> foreignKeyDetailsList;

    protected internal enum DatabaseError
    {
        UniqueConstraint,
        CannotInsertNull,
        MaxLength,
        NumericOverflow,
        ReferenceConstraint
    }

    /// <inheritdoc />
    public void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ProcessDbUpdateException(eventData, eventData.Exception as DbUpdateException);
    }

    /// <inheritdoc />
    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
        CancellationToken cancellationToken = new CancellationToken())
    {
        ProcessDbUpdateException(eventData, eventData.Exception as DbUpdateException);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        ProcessDbException(eventData, eventData.Exception as DbException);
    }

    /// <inheritdoc />
    public Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
        CancellationToken cancellationToken = new CancellationToken())
    {
        ProcessDbException(eventData, eventData.Exception as DbException);
        return Task.CompletedTask;
    }

    protected abstract DatabaseError? GetDatabaseError(T dbException);

    [StackTraceHidden]
    private void ProcessDbUpdateException(DbContextErrorEventData eventData, DbUpdateException dbUpdateException)
    {
        if (dbUpdateException == null || eventData.Exception.GetBaseException() is not T providerException) return;

        var error = GetDatabaseError(providerException);

        if (error == null) return;

        var exception = ExceptionFactory.Create(error.Value, dbUpdateException, dbUpdateException.Entries);

        switch (exception)
        {
            case UniqueConstraintException uniqueConstraint when eventData.Context != null:
                SetConstraintDetails(eventData.Context, uniqueConstraint, providerException);
                break;
            case ReferenceConstraintException referenceConstraint when eventData.Context != null:
                SetConstraintDetails(eventData.Context, referenceConstraint, providerException);
                break;
        }

        throw exception;
    }

    [StackTraceHidden]
    private void ProcessDbException(CommandErrorEventData eventData, DbException dbException)
    {
        if (dbException == null || eventData.Exception.GetBaseException() is not T providerException) return;

        var error = GetDatabaseError(providerException);

        if (error == null) return;

        var exception = ExceptionFactory.Create(error.Value, dbException);

        switch (exception)
        {
            case UniqueConstraintException uniqueConstraint when eventData.Context != null:
                SetConstraintDetails(eventData.Context, uniqueConstraint, providerException);
                break;
            case ReferenceConstraintException referenceConstraint when eventData.Context != null:
                SetConstraintDetails(eventData.Context, referenceConstraint, providerException);
                break;
        }

        throw exception;
    }

    private void SetConstraintDetails(DbContext context, UniqueConstraintException exception,
        Exception providerException)
    {
        if (uniqueIndexDetailsList == null)
        {
            var indexes = context.Model.GetEntityTypes()
                .SelectMany(x => x.GetDeclaredIndexes().Where(index => index.IsUnique));

            var mappedIndexes = indexes.SelectMany(index => index.GetMappedTableIndexes(),
                (index, tableIndex) =>
                    new IndexDetails(tableIndex.Name, tableIndex.Table.SchemaQualifiedName, index.Properties));

            var primaryKeys = context.Model.GetEntityTypes().SelectMany(x =>
            {
                var primaryKey = x.FindPrimaryKey();
                if (primaryKey is null)
                {
                    return Array.Empty<IndexDetails>();
                }

                var primaryKeyName = primaryKey.GetName();

                if (primaryKeyName is null)
                {
                    return Array.Empty<IndexDetails>();
                }

                return new[]
                    { new IndexDetails(primaryKeyName, x.GetSchemaQualifiedTableName(), primaryKey.Properties) };
            });

            uniqueIndexDetailsList = mappedIndexes
                .Union(primaryKeys)
                .ToList();
        }

        var matchingIndexes = uniqueIndexDetailsList.Where(index =>
            providerException.Message.Contains(index.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        var match = matchingIndexes.Count == 1
            ? matchingIndexes[0]
            : matchingIndexes.FirstOrDefault(index =>
                providerException.Message.Contains(index.SchemaQualifiedTableName, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            exception.ConstraintName = match.Name;
            exception.ConstraintProperties = match.Properties.Select(property => property.Name).ToList();
            exception.SchemaQualifiedTableName = match.SchemaQualifiedTableName;
        }
    }

    private void SetConstraintDetails(DbContext context, ReferenceConstraintException exception,
        Exception providerException)
    {
        if (foreignKeyDetailsList == null)
        {
            var keys = context.Model.GetEntityTypes().SelectMany(x => x.GetDeclaredForeignKeys());

            var mappedConstraints = keys.SelectMany(index => index.GetMappedConstraints(),
                (index, constraint) => new { constraint, index.Properties });

            foreignKeyDetailsList = mappedConstraints.Select(arg =>
                    new ForeignKeyDetails(arg.constraint.Name, arg.constraint.Table.SchemaQualifiedName,
                        arg.Properties))
                .ToList();
        }

        var matchingForeignKeys = foreignKeyDetailsList.Where(foreignKey =>
            providerException.Message.Contains(foreignKey.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        var match = matchingForeignKeys.Count == 1
            ? matchingForeignKeys[0]
            : matchingForeignKeys.FirstOrDefault(foreignKey =>
                providerException.Message.Contains(foreignKey.SchemaQualifiedTableName,
                    StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            exception.ConstraintName = match.Name;
            exception.ConstraintProperties = match.Properties.Select(property => property.Name).ToList();
            exception.SchemaQualifiedTableName = match.SchemaQualifiedTableName;
        }
    }
}