#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

public class TodoServiceTests
{
    private static TodoService NewSvc(out ITodoRepository repo)
    {
        repo = new InMemoryTodoRepository();
        return new TodoService(repo);
    }

    [Fact]
    public void Add_Valid_MinimumFields()
    {
        var svc = NewSvc(out _);

        var created = svc.Add(new TodoCreate(
            Title: "Write tests",
            Priority: null,
            DueDate: null,
            Tags: null,
            Notes: null));

        created.Id.Should().NotBeEmpty();
        created.Title.Should().Be("Write tests");
        created.Completed.Should().BeFalse();
        created.Priority.Should().Be(Priority.Medium);
    }

    [Fact]
    public void Add_TrimsTitle_AndRejectsTooLong()
    {
        var svc = NewSvc(out _);

        var t = svc.Add(new TodoCreate("  Trim me  ", null, null, null, null));
        t.Title.Should().Be("Trim me");

        Action tooLong = () => svc.Add(new TodoCreate(new string('a', 101), null, null, null, null));
        tooLong.Should().Throw<ArgumentException>().WithMessage("*Title too long*");
    }

    [Fact]
    public void Add_DuplicateTitle_IsConflict()
    {
        var svc = NewSvc(out _);
        svc.Add(new TodoCreate("Buy milk", null, null, null, null));
        Action dup = () => svc.Add(new TodoCreate("buy MILK", null, null, null, null));
        dup.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate title*");
    }

    [Fact]
    public void Update_TitleToDuplicate_IsConflict()
    {
        var svc = NewSvc(out _);
        var a = svc.Add(new TodoCreate("A", null, null, null, null));
        svc.Add(new TodoCreate("B", null, null, null, null));

        Action act = () => svc.Update(a.Id, new TodoUpdate(Title: "b", Priority: null, DueDate: null, Tags: null, Notes: null));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate title*");
    }

    [Fact]
    public void CleanTags_SanitizesAndLimits()
    {
        var svc = NewSvc(out _);
        var created = svc.Add(new TodoCreate(
            Title: "Tags",
            Priority: Priority.High,
            DueDate: null,
            Tags: new[] { "  #alpha ", "b@d tag", "UPPER", "looooooooooooooooooooong", "ok-1", "extra" },
            Notes: null));

        created.Tags.Length.Should().Be(5);                   // max 5
        created.Tags.Should().Contain("alpha");
        created.Tags.Should().Contain("b-d-tag");             // sanitized
        created.Tags.Should().Contain("UPPER");
        created.Tags.Should().Contain("looooooooooooooooooo"); // trimmed to 20
        created.Tags.Should().Contain("ok-1");
    }

    [Fact]
    public void Query_Filter_ByPriority_Status_SortByDue()
    {
        var svc = NewSvc(out _);
        var today = DateTime.Today;

        svc.Add(new TodoCreate("Low old", Priority.Low,  today.AddDays(-1), new[] { "d1" }, null));
        svc.Add(new TodoCreate("High soon", Priority.High, today.AddDays(2), new[] { "d2" }, null));
        var mid = svc.Add(new TodoCreate("Medium none", Priority.Medium, null, null, null));
        svc.Complete(mid.Id);

        var res = svc.Query(query: null,
                            priorities: new[] { Priority.High, Priority.Medium },
                            status: StatusFilter.Active,
                            dueBefore: today.AddDays(10),
                            dueAfter: today.AddDays(-10),
                            sort: SortKey.DueDate).ToList();

        res.Select(r => r.Title).Should().Equal("High soon");
    }

    [Fact]
    public void Bulk_Complete_And_Delete_Mixed()
    {
        var svc = NewSvc(out _);
        var a = svc.Add(new TodoCreate("A", null, null, null, null));
        var b = svc.Add(new TodoCreate("B", null, null, null, null));

        var result = svc.Bulk(new BulkRequest("complete", new[] { a.Id, b.Id }));
        result.Succeeded.Should().Be(2);

        var result2 = svc.Bulk(new BulkRequest("delete", new[] { a.Id, Guid.NewGuid() }));
        result2.Requested.Should().Be(2);
        result2.Succeeded.Should().Be(1);
        result2.Failed.Should().Be(1);
    }

    [Fact]
    public void Delete_Locked_Throws()
    {
        var svc = NewSvc(out var repo);
        var t = svc.Add(new TodoCreate("Lock me", null, null, null, null));

        // make it locked in the same repo
        var locked = new Todo(t.Id, t.Title, t.Completed, Locked: true, t.Priority, t.DueDate, t.Tags, t.Notes);
        repo.Upsert(locked);

        Action act = () => svc.Delete(t.Id);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Locked*");
    }

    // --- Validation ---

    [Fact]
    public void Add_EmptyTitle_Throws()
    {
        var svc = NewSvc(out _);
        Action emptyStr = () => svc.Add(new TodoCreate("", null, null, null, null));
        Action whitespace = () => svc.Add(new TodoCreate("   ", null, null, null, null));
        emptyStr.Should().Throw<ArgumentException>().WithMessage("*Title required*");
        whitespace.Should().Throw<ArgumentException>().WithMessage("*Title required*");
    }

    [Fact]
    public void Add_Notes_TruncatedAt500Chars()
    {
        var svc = NewSvc(out _);
        var longNotes = new string('x', 600);
        var t = svc.Add(new TodoCreate("Note test", null, null, null, longNotes));
        t.Notes.Length.Should().Be(500);
    }

    // --- Idempotency ---

    [Fact]
    public void Complete_IsIdempotent()
    {
        var svc = NewSvc(out _);
        var t = svc.Add(new TodoCreate("Do twice", null, null, null, null));
        var first = svc.Complete(t.Id);
        var second = svc.Complete(t.Id);
        first.Completed.Should().BeTrue();
        second.Completed.Should().BeTrue();
        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public void Uncomplete_IsIdempotent()
    {
        var svc = NewSvc(out _);
        var t = svc.Add(new TodoCreate("Toggle test", null, null, null, null));
        svc.Complete(t.Id);
        var first = svc.Uncomplete(t.Id);
        var second = svc.Uncomplete(t.Id);
        first.Completed.Should().BeFalse();
        second.Completed.Should().BeFalse();
    }

    [Fact]
    public void Complete_Then_Uncomplete_IsReversible()
    {
        var svc = NewSvc(out _);
        var t = svc.Add(new TodoCreate("Reversible", null, null, null, null));
        svc.Complete(t.Id).Completed.Should().BeTrue();
        svc.Uncomplete(t.Id).Completed.Should().BeFalse();
        svc.Complete(t.Id).Completed.Should().BeTrue();
    }

    // --- Update edge cases ---

    [Fact]
    public void Update_SameTitle_IsAllowed()
    {
        var svc = NewSvc(out _);
        var t = svc.Add(new TodoCreate("Same Title", null, null, null, null));
        Action act = () => svc.Update(t.Id, new TodoUpdate(Title: "Same Title", null, null, null, null));
        act.Should().NotThrow();
    }

    [Fact]
    public void Update_NonExistent_Throws()
    {
        var svc = NewSvc(out _);
        Action act = () => svc.Update(Guid.NewGuid(), new TodoUpdate(Title: "X", null, null, null, null));
        act.Should().Throw<KeyNotFoundException>();
    }

    // --- Query / search / sort ---

    [Fact]
    public void Query_SearchByTitle_ReturnsMatch()
    {
        var svc = NewSvc(out _);
        svc.Add(new TodoCreate("Buy groceries", null, null, null, null));
        svc.Add(new TodoCreate("Call dentist", null, null, null, null));

        var res = svc.Query("grocer", null, StatusFilter.All, null, null, SortKey.None).ToList();

        res.Should().ContainSingle().Which.Title.Should().Be("Buy groceries");
    }

    [Fact]
    public void Query_SearchByTag_ReturnsMatch()
    {
        var svc = NewSvc(out _);
        svc.Add(new TodoCreate("Tagged item", null, null, new[] { "urgent" }, null));
        svc.Add(new TodoCreate("Untagged item", null, null, null, null));

        var res = svc.Query("urgent", null, StatusFilter.All, null, null, SortKey.None).ToList();

        res.Should().ContainSingle().Which.Title.Should().Be("Tagged item");
    }

    [Fact]
    public void Query_SortByPriority_OrdersHighFirst()
    {
        var svc = NewSvc(out _);
        svc.Add(new TodoCreate("Low item", Priority.Low, null, null, null));
        svc.Add(new TodoCreate("High item", Priority.High, null, null, null));
        svc.Add(new TodoCreate("Medium item", Priority.Medium, null, null, null));

        var res = svc.Query(null, null, StatusFilter.All, null, null, SortKey.Priority).ToList();

        res.Select(t => t.Priority).Should().Equal(Priority.High, Priority.Medium, Priority.Low);
    }

    // --- Bulk edge cases ---

    [Fact]
    public void Bulk_EmptyIds_ReturnsZeroCounts()
    {
        var svc = NewSvc(out _);
        var result = svc.Bulk(new BulkRequest("complete", Array.Empty<Guid>()));
        result.Requested.Should().Be(0);
        result.Succeeded.Should().Be(0);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public void Delete_NotFound_Throws()
    {
        var svc = NewSvc(out _);
        Action act = () => svc.Delete(Guid.NewGuid());
        act.Should().Throw<KeyNotFoundException>();
    }
}
