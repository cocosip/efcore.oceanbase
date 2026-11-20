using System.Data;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class ModificationProcedureTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "`UpdateRow`")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "\"UpdateRow\"")]
        public void SaveChanges_builds_ordered_procedure_parameters_in_a_transaction(
            OceanBaseCompatibilityMode mode, string procedureName)
        {
            var capture = new CaptureProcedure();
            using var context = new Context(new DbContextOptionsBuilder<Context>()
                .UseOceanBase("Server=localhost;Database=offline", mode)
                .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
                .AddInterceptors(capture).Options);
            var connection = Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());
            var row = new Row { Id = 1, Value = 2, Stamp = 4 };
            context.Attach(row);
            row.Value = 3;

            Assert.Equal(1, context.SaveChanges());
            Assert.Equal(procedureName, capture.Text);
            Assert.Equal(CommandSource.SaveChanges, capture.Source);
            Assert.Equal(CommandType.StoredProcedure, capture.Type);
            Assert.Same(Assert.Single(connection.Transactions), capture.Transaction);
            Assert.Equal(new[] { 3, 1, 2, 4 }, capture.Inputs);
            Assert.Equal(new[] { ParameterDirection.Input, ParameterDirection.Input,
                ParameterDirection.Input, ParameterDirection.Output, ParameterDirection.InputOutput }, capture.Directions);
            Assert.Equal(5, row.Stamp);
        }

        private sealed class CaptureProcedure : DbCommandInterceptor
        {
            public string? Text { get; private set; }
            public CommandSource Source { get; private set; }
            public CommandType Type { get; private set; }
            public DbTransaction? Transaction { get; private set; }
            public int[]? Inputs { get; private set; }
            public ParameterDirection[]? Directions { get; private set; }

            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result)
            {
                Text = command.CommandText;
                Source = eventData.CommandSource;
                Type = command.CommandType;
                Transaction = command.Transaction;
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                Directions = parameters.Select(parameter => parameter.Direction).ToArray();
                Inputs = parameters.Where(parameter => parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput)
                    .Select(parameter => (int)parameter.Value!).ToArray();
                Assert.Single(parameters, parameter => parameter.Direction == ParameterDirection.Output).Value = 1;
                Assert.Single(parameters, parameter => parameter.Direction == ParameterDirection.InputOutput).Value = 5;
                return InterceptionResult<int>.SuppressWithResult(-1);
            }
        }

        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var row = modelBuilder.Entity<Row>();
                row.Property(value => value.Id).ValueGeneratedNever();
                row.Property(value => value.Value).IsConcurrencyToken();
                row.Property(value => value.Stamp).ValueGeneratedOnAddOrUpdate();
                row.UpdateUsingStoredProcedure("UpdateRow", procedure => procedure
                    .HasParameter(value => value.Value)
                    .HasOriginalValueParameter(value => value.Id)
                    .HasOriginalValueParameter(value => value.Value)
                    .HasRowsAffectedParameter()
                    .HasOriginalValueParameter(value => value.Stamp, parameter => parameter.IsInputOutput()));
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public int Value { get; set; }
            public int Stamp { get; set; }
        }
    }
}
