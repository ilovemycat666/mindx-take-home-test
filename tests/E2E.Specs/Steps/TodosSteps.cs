#nullable enable
using System;
using System.Linq;
using FluentAssertions;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using TechTalk.SpecFlow;

[Binding]
public class TodosSteps
{
    private readonly ScenarioContext _ctx;
    private IWebDriver Driver => (IWebDriver)_ctx["driver"];
    private WebDriverWait Wait => new(Driver, TimeSpan.FromSeconds(6));

    public TodosSteps(ScenarioContext ctx) => _ctx = ctx;

    // We assume another step file sets the base URL & opens the page.

    [When(@"I add a todo titled ""(.*)""")]
    public void WhenIAddATodoTitled(string title)
    {
        var t = Driver.FindElement(By.CssSelector("#title"));
        t.Clear(); t.SendKeys(title);
        Driver.FindElement(By.CssSelector("[data-testid='add-btn']")).Click();
        Wait.Until(d => d.FindElements(By.CssSelector("[data-testid='todo-label']"))
                         .Any(e => SafeText(e).EndsWith(title)));
    }

    [Then(@"I should see ""(.*)"" in the list")]
    public void ThenIShouldSeeInTheList(string title)
    {
        var labels = Driver.FindElements(By.CssSelector("[data-testid='todo-label']"))
                           .Select(SafeText).ToList();
        labels.Any(x => x.EndsWith(title)).Should().BeTrue();
    }

    [When(@"I complete the todo ""(.*)""")]
    public void WhenICompleteTheTodo(string title)
    {
        FindRow(title).FindElement(By.CssSelector("[data-testid='complete-btn']")).Click();
        // After clicking Complete the list re-renders (innerHTML reset).  Query labels fresh
        // from the driver root each retry so we never hold a stale element reference.
        Wait.Until(d => d.FindElements(By.CssSelector("[data-testid='todo-label']"))
                         .Any(e => { var t = SafeText(e); return t.StartsWith("✅ ") && t.EndsWith(title); }));
    }

    [Then(@"the todo ""(.*)"" should appear completed")]
    public void ThenTheTodoShouldAppearCompleted(string title)
    {
        var row = FindRow(title);
        var text = SafeText(row.FindElement(By.CssSelector("[data-testid='todo-label']")));
        text.StartsWith("✅ ").Should().BeTrue();
    }

    [When(@"I delete the todo ""(.*)""")]
    public void WhenIDeleteTheTodo(string title)
    {
        var row = FindRow(title);
        row.FindElement(By.CssSelector("[data-testid='delete-btn']")).Click();
        Wait.Until(_ => !Driver.FindElements(By.CssSelector("[data-testid='todo-label']"))
                               .Any(e => SafeText(e).EndsWith(title)));
    }

    [Then(@"I should not see ""(.*)"" in the list")]
    public void ThenIShouldNotSeeInTheList(string title)
    {
        var labels = Driver.FindElements(By.CssSelector("[data-testid='todo-label']"))
                           .Select(SafeText).ToList();
        labels.Any(x => x.EndsWith(title)).Should().BeFalse();
    }

    // helpers
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

    private static string SafeText(IWebElement el)
    {
        try { return el.Text ?? string.Empty; }
        catch (StaleElementReferenceException) { return string.Empty; }
    }
}
