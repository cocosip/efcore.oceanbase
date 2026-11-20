using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore;

namespace OceanBaseProviderFeatureProbe;

internal static partial class Scenarios
{
    internal enum Shape { Relationships, Splitting, Tph, Tpt, Tpc, Usage }

    internal sealed record Step(string Name, Shape Model, Func<Context, bool, Action<string>, Task> Run);

    internal static IReadOnlyList<Step> Steps { get; } =
    [
        new("relationships-and-keys", Shape.Relationships, CheckRelationships),
        new("owned-and-split-entities", Shape.Splitting, CheckSplitting),
        new("inheritance-tph", Shape.Tph, CheckInheritance),
        new("inheritance-tpt", Shape.Tpt, CheckInheritance),
        new("inheritance-tpc", Shape.Tpc, CheckInheritance),
        .. GetUsageSteps()
    ];

    private static async Task CheckRelationships(Context context, bool useAsync, Action<string> stage)
    {
        stage("insert generated keys and relationships");
        var owner = new Member { Code = "owner", Badge = new Badge { Text = "lead" } };
        var assignee = new Member { Code = "assignee", Manager = owner };
        var tag = new Tag { Id = Guid.NewGuid(), Name = "urgent" };
        var linkedTag = new Tag { Id = Guid.NewGuid(), Name = "manual" };
        var item = new WorkItem { Area = "support", Number = 3, Reporter = owner, Assignee = assignee, Tags = [tag] };
        context.Add(new TagLink { Item = item, Tag = linkedTag, Note = "initial" });
        context.Add(new WorkItem { Area = "support", Number = 4, Reporter = owner });
        await Save(context, useAsync);
        Assert.True(owner.Id > 0 && assignee.Id > 0);
        Assert.NotEqual(owner.Id, assignee.Id);
        Assert.Equal(owner.Id, Assert.IsType<Badge>(owner.Badge).Id);
        context.ChangeTracker.Clear();

        stage("read shared key, optional foreign key, self-reference and join payload");
        var rows = await Read(context.Set<WorkItem>().Include(row => row.Reporter).ThenInclude(row => row.Badge)
            .Include(row => row.Assignee).ThenInclude(row => row!.Manager).Include(row => row.Tags)
            .Include(row => row.Links).ThenInclude(row => row.Tag).AsSplitQuery().OrderBy(row => row.Number), useAsync);
        Assert.Equal(2, rows.Count);
        var first = rows[0];
        Assert.Equal(("support", 3), (first.Area, first.Number));
        Assert.Equal("lead", first.Reporter.Badge?.Text);
        Assert.Equal("assignee", first.Assignee?.Code);
        Assert.Same(first.Reporter, first.Assignee?.Manager);
        Assert.Equal(tag.Id, Assert.Single(first.Tags).Id);
        var link = Assert.Single(first.Links);
        Assert.Equal(linkedTag.Id, link.Tag.Id);
        Assert.Equal("initial", link.Note);
        Assert.Null(rows[1].Assignee);
        Assert.Empty(rows[1].Tags);
        Assert.Empty(rows[1].Links);

        stage("unlink relationships and update the join payload");
        first.Assignee = null;
        first.Tags.Clear();
        link.Note = "edited";
        await Save(context, useAsync);
        context.ChangeTracker.Clear();
        var updated = Assert.Single(await Read(context.Set<WorkItem>().Where(row => row.Number == 3)
            .Include(row => row.Tags).Include(row => row.Links), useAsync));
        Assert.Null(updated.AssigneeCode);
        Assert.Empty(updated.Tags);
        Assert.Equal("edited", Assert.Single(updated.Links).Note);

        stage("delete the composite-key row and verify dependent deletion");
        context.Remove(updated);
        await Save(context, useAsync);
        context.ChangeTracker.Clear();
        Assert.Equal(4, Assert.Single(await Read(context.Set<WorkItem>(), useAsync)).Number);
        Assert.Empty(await Read(context.Set<TagLink>(), useAsync));
        Assert.Equal(2, (await Read(context.Set<Tag>(), useAsync)).Count);
    }

    private static async Task CheckSplitting(Context context, bool useAsync, Action<string> stage)
    {
        stage("insert split entities and owned values");
        var document = new Document { Id = Guid.NewGuid(), Title = "document", Body = "initial" };
        var book = new Book { Id = Guid.NewGuid(), Title = "book", Info = new BookInfo { Summary = "summary" },
            Address = new Address { City = "Shanghai" }, Notes = [new Note { Number = 1, Text = "first" }] };
        context.AddRange(document, book, new Book { Id = Guid.NewGuid(), Title = "empty" });
        context.Entry(document).Property<int>("Revision").CurrentValue = 1;
        await Save(context, useAsync);
        context.ChangeTracker.Clear();

        stage("read and update split fragments, converted values and owned values");
        var read = Assert.Single(await Read(context.Set<Document>(), useAsync));
        Assert.Equal((document.Id, "document", "initial", DocumentState.Draft), (read.Id, read.Title, read.Body, read.State));
        Assert.Equal(1, context.Entry(read).Property<int>("Revision").CurrentValue);
        var books = await Read(context.Set<Book>().Include(row => row.Info).AsSplitQuery().OrderBy(row => row.Title), useAsync);
        Assert.Equal(2, books.Count);
        Assert.Equal("summary", books[0].Info?.Summary);
        Assert.Equal("Shanghai", books[0].Address?.City);
        Assert.Equal("first", Assert.Single(books[0].Notes).Text);
        Assert.Null(books[1].Info);
        Assert.Null(books[1].Address);
        Assert.Empty(books[1].Notes);
        read.Title = "renamed";
        read.Body = "revised";
        read.State = DocumentState.Published;
        context.Entry(read).Property<int>("Revision").CurrentValue = 2;
        Assert.IsType<Address>(books[0].Address).City = "Hangzhou";
        books[0].Notes.Clear();
        await Save(context, useAsync);
        context.ChangeTracker.Clear();

        stage("verify updated fragments and owned collection deletion");
        var updated = Assert.Single(await Read(context.Set<Document>(), useAsync));
        Assert.Equal(("renamed", "revised", DocumentState.Published), (updated.Title, updated.Body, updated.State));
        Assert.Equal(2, context.Entry(updated).Property<int>("Revision").CurrentValue);
        var changed = Assert.Single(await Read(context.Set<Book>().Where(row => row.Id == book.Id), useAsync));
        Assert.Equal("Hangzhou", changed.Address?.City);
        Assert.Empty(changed.Notes);
        context.Remove(updated);
        await Save(context, useAsync);
        context.ChangeTracker.Clear();
        Assert.Empty(await Read(context.Set<Document>(), useAsync));
    }

    private static async Task CheckInheritance(Context context, bool useAsync, Action<string> stage)
    {
        stage("insert concrete types with assigned keys");
        context.AddRange(new Machine { Id = 10, Name = "printer", Serial = "A-1" },
            new Vehicle { Id = 20, Name = "truck", Capacity = 5 });
        await Save(context, useAsync);
        context.ChangeTracker.Clear();

        stage("query through the base type and update and delete derived rows");
        var assets = await Read(context.Set<Asset>().OrderBy(row => row.Id), useAsync);
        Assert.Collection(assets,
            row => Assert.Equal((10, "printer", "A-1"), (row.Id, row.Name, Assert.IsType<Machine>(row).Serial)),
            row => Assert.Equal((20, "truck", 5), (row.Id, row.Name, Assert.IsType<Vehicle>(row).Capacity)));
        Assert.IsType<Machine>(assets[0]).Serial = "A-2";
        context.Remove(assets[1]);
        await Save(context, useAsync);
        context.ChangeTracker.Clear();
        Assert.Equal("A-2", Assert.IsType<Machine>(Assert.Single(await Read(context.Set<Asset>(), useAsync))).Serial);
        Assert.Empty(await Read(context.Set<Asset>().OfType<Vehicle>(), useAsync));
    }

    private static async Task Save(Context context, bool useAsync)
    {
        if (useAsync) await context.SaveChangesAsync();
        else context.SaveChanges();
    }

    private static async Task<List<TEntity>> Read<TEntity>(IQueryable<TEntity> query, bool useAsync)
        => useAsync ? await query.ToListAsync() : query.ToList();

    internal static string[] TableSuffixes(Shape shape) => shape switch
    {
        Shape.Relationships => ["M", "B", "T", "W", "J", "L"],
        Shape.Splitting => ["P", "D", "B", "N"],
        Shape.Tph => ["A"],
        Shape.Tpt => ["A", "M", "V"],
        Shape.Tpc => ["A", "V"],
        Shape.Usage => ["R"],
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static Context Create(DbContextOptions<Context> options, string root, Shape shape)
        => new(new DbContextOptionsBuilder<Context>(options).EnableServiceProviderCaching(false).Options, root, shape);

    internal sealed class Context(DbContextOptions<Context> options, string root, Shape shape) : DbContext(options)
    {
        internal string Root => root;
        public int Workspace { get; set; } = 1;

        protected override void OnModelCreating(ModelBuilder model)
        {
            switch (shape)
            {
                case Shape.Relationships: ConfigureRelationships(model); break;
                case Shape.Splitting: ConfigureSplitting(model); break;
                case Shape.Usage: ConfigureUsage(model, root, this); break;
                default: ConfigureInheritance(model); break;
            }
        }

        private void ConfigureRelationships(ModelBuilder model)
        {
            var member = model.Entity<Member>().ToTable(root + "M");
            member.Property(row => row.Code).HasMaxLength(24);
            member.HasAlternateKey(row => row.Code);
            member.HasOne(row => row.Manager).WithMany().HasForeignKey(row => row.ManagerId).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Badge>().ToTable(root + "B").Property(row => row.Text).HasMaxLength(40);
            member.HasOne(row => row.Badge).WithOne().HasForeignKey<Badge>(row => row.Id);
            model.Entity<Tag>().ToTable(root + "T").Property(row => row.Name).HasMaxLength(40);

            var item = model.Entity<WorkItem>().ToTable(root + "W");
            item.HasKey(row => new { row.Area, row.Number });
            item.Property(row => row.Area).HasMaxLength(24);
            item.HasOne(row => row.Reporter).WithMany(row => row.Reported).HasPrincipalKey(row => row.Code)
                .HasForeignKey(row => row.ReporterCode).OnDelete(DeleteBehavior.Restrict);
            item.HasOne(row => row.Assignee).WithMany().HasPrincipalKey(row => row.Code)
                .HasForeignKey(row => row.AssigneeCode).OnDelete(DeleteBehavior.Restrict);
            item.HasMany(row => row.Tags).WithMany().UsingEntity(join => join.ToTable(root + "J"));

            var link = model.Entity<TagLink>().ToTable(root + "L");
            link.HasKey(row => new { row.Area, row.Number, row.TagId });
            link.HasOne(row => row.Item).WithMany(row => row.Links).HasForeignKey(row => new { row.Area, row.Number });
            link.HasOne(row => row.Tag).WithMany().HasForeignKey(row => row.TagId);
            link.Property(row => row.Note).HasMaxLength(40);
        }

        private void ConfigureSplitting(ModelBuilder model)
        {
            var document = model.Entity<Document>().ToTable(root + "P");
            document.Property(row => row.Title).HasMaxLength(40);
            document.Property(row => row.Body).HasMaxLength(40);
            document.Property(row => row.State).HasConversion<string>().HasMaxLength(16);
            document.Property<int>("Revision").IsConcurrencyToken();
            document.SplitToTable(root + "D", fragment =>
            {
                fragment.Property(row => row.Id);
                fragment.Property(row => row.Body);
            });

            var book = model.Entity<Book>().ToTable(root + "B");
            book.Property(row => row.Title).HasMaxLength(40);
            book.HasOne(row => row.Info).WithOne().HasForeignKey<BookInfo>(row => row.Id);
            model.Entity<BookInfo>().ToTable(root + "B").Property(row => row.Summary).HasMaxLength(40);
            book.OwnsOne(row => row.Address, address => address.Property(row => row.City).HasMaxLength(40));
            book.OwnsMany(row => row.Notes, note =>
            {
                note.ToTable(root + "N");
                note.WithOwner().HasForeignKey("BookId");
                note.HasKey("BookId", nameof(Note.Number));
                note.Property(row => row.Number).ValueGeneratedNever();
                note.Property(row => row.Text).HasMaxLength(40);
            });
        }

        private void ConfigureInheritance(ModelBuilder model)
        {
            var asset = model.Entity<Asset>();
            var machine = model.Entity<Machine>();
            var vehicle = model.Entity<Vehicle>();
            asset.Property(row => row.Id).ValueGeneratedNever();
            asset.Property(row => row.Name).HasMaxLength(40);
            machine.Property(row => row.Serial).HasMaxLength(40);

            switch (shape)
            {
                case Shape.Tph:
                    asset.ToTable(root + "A").UseTphMappingStrategy();
                    break;
                case Shape.Tpt:
                    asset.ToTable(root + "A").UseTptMappingStrategy();
                    machine.ToTable(root + "M");
                    vehicle.ToTable(root + "V");
                    break;
                case Shape.Tpc:
                    asset.UseTpcMappingStrategy();
                    machine.ToTable(root + "A");
                    vehicle.ToTable(root + "V");
                    break;
            }
        }
    }

    private sealed class Member
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public int? ManagerId { get; set; }
        public Member? Manager { get; set; }
        public Badge? Badge { get; set; }
        public List<WorkItem> Reported { get; set; } = [];
    }

    private sealed class Badge
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
    }

    private sealed class WorkItem
    {
        public string Area { get; set; } = "";
        public int Number { get; set; }
        public string ReporterCode { get; set; } = "";
        public Member Reporter { get; set; } = null!;
        public string? AssigneeCode { get; set; }
        public Member? Assignee { get; set; }
        public List<Tag> Tags { get; set; } = [];
        public List<TagLink> Links { get; set; } = [];
    }

    private sealed class Tag
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class TagLink
    {
        public string Area { get; set; } = "";
        public int Number { get; set; }
        public Guid TagId { get; set; }
        public WorkItem Item { get; set; } = null!;
        public Tag Tag { get; set; } = null!;
        public string Note { get; set; } = "";
    }

    private enum DocumentState { Draft, Published }

    private sealed class Document
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public DocumentState State { get; set; }
    }

    private sealed class Book
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public BookInfo? Info { get; set; }
        public Address? Address { get; set; }
        public List<Note> Notes { get; set; } = [];
    }

    private sealed class BookInfo
    {
        public Guid Id { get; set; }
        public string Summary { get; set; } = "";
    }

    private sealed class Address { public string City { get; set; } = ""; }

    private sealed class Note
    {
        public int Number { get; set; }
        public string Text { get; set; } = "";
    }

    private abstract class Asset
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class Machine : Asset { public string Serial { get; set; } = ""; }
    private sealed class Vehicle : Asset { public int Capacity { get; set; } }
}
