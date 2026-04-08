#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using FluentAssertions;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using TechTalk.SpecFlow;

[Binding]
public class AdvancedTodosSteps
{
    private readonly ScenarioContext _ctx;
    private IWebDriver Driver => (IWebDriver)_ctx["driver"];
    private WebDriverWait Wait => new(Driver, TimeSpan.FromSeconds(6));

    public AdvancedTodosSteps(ScenarioContext ctx) => _ctx = ctx;

    // ---------- Setup / Navigation ----------
    [Given(@"the app is running at ""(.*)""")]
    public void GivenTheAppIsRunningAt(string baseUrl) => _ctx["baseUrl"] = baseUrl.TrimEnd('/');

    [Given(@"I open the Todos page")]
    public void GivenIOpenTheTodosPage()
    {
        Driver.Navigate().GoToUrl(BaseUrl());
        Wait.Until(d => Exists(d, "[data-testid='new-title']"));
    }

    // ---------- Dev seed/reset ----------
    [Given(@"I reset data")]
    public async Task GivenIResetData() => await new TestApi(BaseUrl()).ResetAsync();

    [Given(@"I have a todo seeded titled ""(.*)""")]
    public async Task GivenIHaveATodoSeededTitled(string title)
    {
        var api = new TestApi(BaseUrl());
        await api.ResetAsync();
        await api.SeedAsync(new List<TodoCreateReq> { new(title) });
    }

    [Given(@"I seed todos:")]
    public async Task GivenISeedTodos(Table table)
    {
        var items = new List<TodoCreateReq>();
        foreach (var row in table.Rows)
        {
            var title = row.GetValueOrDefault("title") ?? row.Values.First();
            var priority = row.GetValueOrDefault("priority");
            var notes = row.GetValueOrDefault("notes");
            var tagsCsv = row.GetValueOrDefault("tags");
            string[]? tags = !string.IsNullOrWhiteSpace(tagsCsv)
                ? tagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null;

            DateTime? due = null;
            var dueStr = row.GetValueOrDefault("dueDate");
            if (!string.IsNullOrWhiteSpace(dueStr)) due = ParseRelativeOrIsoDate(dueStr!);

            items.Add(new TodoCreateReq(title!, priority, due, tags, notes));
        }
        await new TestApi(BaseUrl()).SeedAsync(items);
    }

    // ---------- Create with details ----------
    [When(@"I create a todo titled ""(.*)"" with:")]
    public void WhenICreateATodoTitledWith(string title, Table table)
    {
        string? priority = table.Get("priority");
        string? due = table.Get("dueDate");
        string? tags = table.Get("tags");
        string? notes = table.Get("notes");

        Type("#title", title);
        Select("#priority", priority ?? "Medium");
        if (!string.IsNullOrWhiteSpace(due)) SetDate("#due", ParseRelativeOrIsoDate(due!).Date);
        if (!string.IsNullOrWhiteSpace(tags)) Type("#tags", tags);
        if (!string.IsNullOrWhiteSpace(notes)) Type("#notes", notes);

        Click("[data-testid='add-btn']");
        WaitForRow(title);
    }

    // ---------- Edit (cancel prompt; use API for determinism) ----------
    [When(@"I edit ""(.*)"" to title ""(.*)"" and notes ""(.*)""")]
    public async Task WhenIEditToTitleAndNotes(string oldTitle, string newTitle, string notes)
    {
        // click Edit then dismiss any prompt
        var row = FindRow(oldTitle);
        row.FindElement(By.XPath(".//button[normalize-space()='Edit']")).Click();
        TryDismissAlert();

        var api = new TestApi(BaseUrl());
        var id = await api.TryGetIdByTitleAsync(oldTitle) ?? throw new Exception("Item not found");
        await api.UpdateAsync(id, new TodoUpdateReq(Title: newTitle, Notes: notes));

        // Refresh so the frontend picks up the API change
        Driver.Navigate().Refresh();
        Wait.Until(d => d.FindElements(By.CssSelector("#list li")).Any());
        WaitForRow(newTitle);
    }

    // ---------- Search / Filters / Bulk ----------
    [When(@"I search for ""(.*)"" and select all items")]
    public void WhenISearchForAndSelectAllItems(string query)
    {
        SetValue("#q", query);
        Click("#apply");
        Wait.Until(_ => Driver.FindElements(By.CssSelector("#list li")).Any());
        foreach (var cb in Driver.FindElements(By.CssSelector("input[type='checkbox'][data-id]")))
            if (!cb.Selected) cb.Click();
    }

    [When(@"I set filter Priority to ""(.*)"" and Status to ""(.*)""")]
    public void WhenISetFilterPriorityToAndStatusTo(string priosCsv, string status)
    {
        foreach (var cb in Driver.FindElements(By.CssSelector(".prio")))
            if (cb.Selected) cb.Click();

        var wanted = priosCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var cb in Driver.FindElements(By.CssSelector(".prio")))
            if (wanted.Contains(cb.GetAttribute("value")) && !cb.Selected) cb.Click();

        Select("#status", status);
        Click("#apply");
        Wait.Until(_ => true);
    }

    [When(@"I apply bulk action ""(.*)""")]
    public void WhenIApplyBulkAction(string op)
    {
        if (op.Equals("complete", StringComparison.OrdinalIgnoreCase))
        {
            Click("#bulk-complete");
            // Wait until every visible todo label shows the completed prefix
            Wait.Until(d =>
            {
                var labels = d.FindElements(By.CssSelector("[data-testid='todo-label']"));
                return labels.Count > 0 && labels.All(e => SafeText(e).StartsWith("✅ "));
            });
        }
        else
        {
            Click("#bulk-delete");
            // Wait until the list is empty
            Wait.Until(d => !d.FindElements(By.CssSelector("[data-testid='todo-label']")).Any());
        }
    }

    // ---------- Assertions ----------
    [Then(@"both items should appear completed")]
    public void ThenBothItemsShouldAppearCompleted()
    {
        var labels = Driver.FindElements(By.CssSelector("[data-testid='todo-label']")).Select(SafeText).ToList();
        labels.Should().NotBeEmpty();
        labels.Should().OnlyContain(t => t.StartsWith("✅ "));
    }

    [Then(@"""(.*)"" should appear completed")]
    public void ThenTitleShouldAppearCompleted(string title)
    {
        var labels = Driver.FindElements(By.CssSelector("[data-testid='todo-label']")).Select(SafeText).ToList();
        labels.Any(t => t.StartsWith("✅ ") && t.EndsWith(title)).Should().BeTrue();
    }

    [Then(@"I should see ""(.*)"" with priority ""(.*)""")]
    public void ThenIShouldSeeWithPriority(string title, string priority)
    {
        var row = FindRow(title);
        var badge = row.FindElement(By.CssSelector(".badge"));
        badge.Text.Trim().Should().Be(priority);
    }

    [Then(@"it should show a due date within (\d+) days")]
    public void ThenItShouldShowADueDateWithinDays(int days)
    {
        var chips = Driver.FindElements(By.CssSelector(".chip")).Select(SafeText).ToList();
        chips.Should().NotBeEmpty();
        var ok = chips.Any(c =>
        {
            if (DateTime.TryParse(c.Split('(')[0].Trim(), out var d))
            {
                var diff = (d.Date - DateTime.Today).TotalDays;
                return diff >= 0 && diff <= days;
            }
            return false;
        });
        ok.Should().BeTrue();
    }

    [Then(@"I should see exactly:")]
    public void ThenIShouldSeeExactly(Table table)
    {
        var expected = table.Rows.Select(r => r.Values.First().Trim()).ToList();

        // After a filter/sort the list re-renders asynchronously: old elements go stale and
        // new ones are appended.  Wait until the DOM settles to exactly the expected count
        // with all labels readable before asserting.
        Wait.Until(d =>
        {
            var els = d.FindElements(By.CssSelector("[data-testid='todo-label']"));
            return els.Count == expected.Count && els.All(e => !string.IsNullOrEmpty(SafeText(e)));
        });

        var actual = Driver.FindElements(By.CssSelector("[data-testid='todo-label']"))
                           .Select(SafeText).Select(t => t.Replace("✅ ", "")).ToList();
        actual.Should().Equal(expected);
    }

    // ---------- Additional steps ----------

    [When(@"I try to add a todo titled ""(.*)""")]
    public void WhenITryToAddATodoTitled(string title)
    {
        Type("#title", title);
        Click("[data-testid='add-btn']");
    }

    [Then(@"I should see an alert containing ""(.*)""")]
    public void ThenIShouldSeeAnAlertContaining(string expected)
    {
        var alert = Wait.Until(d =>
        {
            try { return d.SwitchTo().Alert(); }
            catch (OpenQA.Selenium.NoAlertPresentException) { return null; }
        });
        alert!.Text.Should().Contain(expected);
        alert.Accept();
    }

    [Then(@"I should see exactly (\d+) todo titled ""(.*)""")]
    public void ThenIShouldSeeExactlyNTodoTitled(int count, string title)
    {
        var matches = Driver.FindElements(By.CssSelector("[data-testid='todo-label']"))
                            .Select(SafeText)
                            .Where(t => t.Replace("✅ ", "").Equals(title, StringComparison.OrdinalIgnoreCase))
                            .ToList();
        matches.Count.Should().Be(count);
    }

    [Then(@"the todo ""(.*)"" should show as overdue")]
    public void ThenTheTodoShouldShowAsOverdue(string title)
    {
        var row = FindRow(title);
        var chip = row.FindElement(By.CssSelector(".chip"));
        chip.Text.Should().Contain("Overdue");
    }

    [When(@"I sort by due date")]
    public void WhenISortByDueDate()
    {
        new SelectElement(Driver.FindElement(By.CssSelector("#sort"))).SelectByValue("due");
        Click("#apply");
        // Wait for list to repopulate after async fetch.
        // The subsequent ThenIShouldSeeExactly step waits for the exact count + readable text,
        // so no sleep is needed here.
        Wait.Until(d => d.FindElements(By.CssSelector("#list li")).Any());
    }

    [When(@"I select all todos")]
    public void WhenISelectAllTodos()
    {
        Wait.Until(d => d.FindElements(By.CssSelector("input[type='checkbox'][data-id]")).Any());
        foreach (var cb in Driver.FindElements(By.CssSelector("input[type='checkbox'][data-id]")))
            if (!cb.Selected) cb.Click();
    }

    // ---------- Helpers ----------
    private string BaseUrl() => (string)(_ctx.TryGetValue("baseUrl", out var v) ? v! : "http://localhost:5173");

    private static bool Exists(IWebDriver d, string css)
    {
        try { return d.FindElement(By.CssSelector(css)).Displayed; }
        catch { return false; }
    }

    private void Click(string css)
    {
        Wait.Until(d => Exists(d, css));
        Driver.FindElement(By.CssSelector(css)).Click();
    }

    private void Type(string css, string text)
    {
        var el = Driver.FindElement(By.CssSelector(css));
        el.Clear(); el.SendKeys(text);
    }

    private void SetValue(string css, string text)
    {
        var el = Driver.FindElement(By.CssSelector(css));
        el.Clear(); el.SendKeys(text);
    }

    private void Select(string css, string visible)
    {
        var sel = new SelectElement(Driver.FindElement(By.CssSelector(css)));
        sel.SelectByText(visible, true);
    }

    private void SetDate(string css, DateTime date)
    {
        // Chrome date inputs have separate month/day/year segments; SendKeys is unreliable.
        // Use JavaScript to set .value directly so the frontend reads the correct string.
        var el = Driver.FindElement(By.CssSelector(css));
        ((OpenQA.Selenium.IJavaScriptExecutor)Driver)
            .ExecuteScript("arguments[0].value = arguments[1];", el, date.ToString("yyyy-MM-dd"));
    }

    private IWebElement FindRow(string title)
    {
        Wait.Until(d => d.FindElements(By.CssSelector("#list li")).Any());
        foreach (var li in Driver.FindElements(By.CssSelector("#list li")))
        {
            var label = SafeText(li.FindElement(By.CssSelector("[data-testid='todo-label']")));
            if (label.EndsWith(title)) return li;
        }
        throw new Exception($"Row with title '{title}' not found");
    }

    private void WaitForRow(string title)
    {
        Wait.Until(d => d.FindElements(By.CssSelector("[data-testid='todo-label']"))
                         .Any(e => SafeText(e).EndsWith(title)));
    }

    private void TryDismissAlert()
    {
        try { Driver.SwitchTo().Alert().Dismiss(); } catch { }
    }

    private static string SafeText(IWebElement el)
    {
        try { return el.Text ?? string.Empty; }
        catch (StaleElementReferenceException) { return string.Empty; }
    }

    private static DateTime ParseRelativeOrIsoDate(string s)
    {
        s = s.Trim();
        if (s.StartsWith("+") || s.StartsWith("-"))
        {
            var sign = s[0] == '+' ? 1 : -1;
            var numStr = new string(s.Skip(1).TakeWhile(char.IsDigit).ToArray());
            var unit = new string(s.Skip(1 + numStr.Length).ToArray()).ToLowerInvariant();
            var val = int.Parse(numStr);
            return unit.StartsWith("d") ? DateTime.Today.AddDays(sign * val) : DateTime.Today;
        }
        return DateTime.Parse(s);
    }
}

// Table helpers
public static class TableExt
{
    public static string? Get(this Table table, string key)
    {
        var row = table.Rows.FirstOrDefault(r => r.ContainsKey(key));
        return row != null ? row[key] : null;
    }

    public static string? GetValueOrDefault(this TableRow row, string key)
        => row.ContainsKey(key) ? row[key] : null;
}
