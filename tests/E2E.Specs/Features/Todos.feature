Feature: Todos E2E

  Background:
    Given the app is running at "http://localhost:5173"

  Scenario: Add, complete, and delete a todo
    Given I reset data
    And I open the Todos page
    When I add a todo titled "Buy milk"
    Then I should see "Buy milk" in the list
    When I complete the todo "Buy milk"
    Then the todo "Buy milk" should appear completed
    When I delete the todo "Buy milk"
    Then I should not see "Buy milk" in the list

  @smoke @e2e
  Scenario: Add a todo (happy path)
    Given I reset data
    And I open the Todos page
    When I add a todo titled "Buy milk"
    Then I should see "Buy milk" in the list

  @smoke
  Scenario: Create todo with priority, due date, and tags
    Given I reset data
    And I open the Todos page
    When I create a todo titled "Priority Task" with:
      | priority | dueDate | tags        |
      | High     | +3d     | work,urgent |
    Then I should see "Priority Task" in the list
    And I should see "Priority Task" with priority "High"
    And it should show a due date within 3 days

  Scenario: Overdue todo shows Overdue indicator
    Given I reset data
    And I seed todos:
      | title        | priority | dueDate |
      | Overdue Task | Medium   | -1d     |
    And I open the Todos page
    Then the todo "Overdue Task" should show as overdue

  Scenario: Edit title successfully
    Given I reset data
    And I seed todos:
      | title     |
      | Old Title |
    And I open the Todos page
    When I edit "Old Title" to title "New Title" and notes ""
    Then I should see "New Title" in the list
    And I should not see "Old Title" in the list

  Scenario: Duplicate title shows error on create
    Given I reset data
    And I seed todos:
      | title    |
      | Buy milk |
    And I open the Todos page
    When I try to add a todo titled "Buy milk"
    Then I should see an alert containing "Duplicate"
    And I should see exactly 1 todo titled "Buy milk"

  Scenario: Filter by single priority shows only matching todos
    Given I reset data
    And I seed todos:
      | title       | priority |
      | High Task   | High     |
      | Medium Task | Medium   |
      | Low Task    | Low      |
    And I open the Todos page
    When I set filter Priority to "High" and Status to "All"
    Then I should see exactly:
      | title     |
      | High Task |

  Scenario: Filter by completed status shows only completed todos
    Given I reset data
    And I seed todos:
      | title  |
      | Item A |
      | Item B |
    And I open the Todos page
    When I complete the todo "Item A"
    And I set filter Priority to "High,Medium,Low" and Status to "Completed"
    Then I should see exactly:
      | title  |
      | Item A |

  Scenario: Sort by due date shows soonest first
    Given I reset data
    And I seed todos:
      | title   | dueDate |
      | Later   | +5d     |
      | Sooner  | +1d     |
      | Soonest | +0d     |
    And I open the Todos page
    When I sort by due date
    Then I should see exactly:
      | title   |
      | Soonest |
      | Sooner  |
      | Later   |

  Scenario: Bulk complete marks all selected todos as done
    Given I reset data
    And I seed todos:
      | title  |
      | Item A |
      | Item B |
    And I open the Todos page
    When I select all todos
    And I apply bulk action "complete"
    Then both items should appear completed

  Scenario: Bulk delete removes all selected todos
    Given I reset data
    And I seed todos:
      | title    |
      | Remove 1 |
      | Remove 2 |
    And I open the Todos page
    When I select all todos
    And I apply bulk action "delete"
    Then I should not see "Remove 1" in the list
    And I should not see "Remove 2" in the list
