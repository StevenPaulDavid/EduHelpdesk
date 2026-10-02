namespace EduHelpdesk.Tests;

// The service catalogue: items under a ticket category, picked in the staff portal. The portal page itself is exercised
// by hand; these cover the rules that keep the data honest.
public class ServiceCatalogueTests
{
    [Fact]
    public void A_new_system_starts_with_a_catalogue_under_its_own_categories()
    {
        using var test = new TestStore();
        var items = test.Store.ServiceItems;

        Assert.NotEmpty(items);
        Assert.All(items, x => Assert.Contains(x.Category, test.Store.Categories, StringComparer.OrdinalIgnoreCase));
        Assert.All(items.Where(x => x.DefaultPriority.Length > 0), x => Assert.Contains(x.DefaultPriority, test.Store.Priorities));
    }

    [Fact]
    public void Items_are_checked_before_they_are_saved()
    {
        using var test = new TestStore();
        var store = test.Store;
        var before = store.ServiceItems.Count;

        Assert.Equal("Select a valid category.", store.AddServiceItem("Nonsense", "Thing", ""));
        Assert.Equal("Enter a name for the catalogue item.", store.AddServiceItem("Hardware", "   ", ""));
        Assert.Equal("Select a valid priority.", store.AddServiceItem("Hardware", "Thing", "Sooner"));
        Assert.Contains("too long", store.AddServiceItem("Hardware", new string('x', HelpdeskStore.MaxServiceItemNameLength + 1), ""));
        Assert.Equal(before, store.ServiceItems.Count);

        Assert.Equal("Catalogue item added.", store.AddServiceItem("hardware", "  Dead pixel  ", "low"));
        var added = store.ServiceItems.Single(x => x.Name == "Dead pixel");
        Assert.Equal("Hardware", added.Category);
        Assert.Equal("Low", added.DefaultPriority);

        // The same name is fine under another category, not under the same one.
        Assert.Contains("already has", store.AddServiceItem("Hardware", "DEAD PIXEL", ""));
        Assert.Equal("Catalogue item added.", store.AddServiceItem("Software", "Dead pixel", ""));
    }

    [Fact]
    public void FindServiceItem_only_matches_inside_the_category_it_was_posted_with()
    {
        using var test = new TestStore();
        var store = test.Store;
        store.AddServiceItem("Hardware", "Dead pixel", "");

        Assert.NotNull(store.FindServiceItem("Hardware", "dead pixel"));
        Assert.Null(store.FindServiceItem("Software", "Dead pixel"));
        Assert.Null(store.FindServiceItem("Hardware", ""));
        Assert.Null(store.FindServiceItem(null, null));
    }

    [Fact]
    public void Renaming_an_item_follows_through_to_its_tickets_but_deleting_it_leaves_their_wording()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Priya Shah");
        store.AddServiceItem("Hardware", "Dead pixel", "");
        var item = store.ServiceItems.Single(x => x.Name == "Dead pixel");
        var number = store.AddTicket(new TicketRecord(0, "Dead pixel", "", requester.Id, [], null, "Normal", "Open", "Hardware", DateTime.UtcNow, null) { SubCategory = "Dead pixel" });

        Assert.Equal("Catalogue item saved.", store.UpdateServiceItem(item.Id, "Hardware", "Screen fault", "High"));
        Assert.Equal("Screen fault", store.Tickets.Single(x => x.Number == number).SubCategory);

        Assert.Equal("Catalogue item deleted.", store.DeleteServiceItem(item.Id));
        Assert.Equal("Screen fault", store.Tickets.Single(x => x.Number == number).SubCategory);
        Assert.Equal("Catalogue item was not found.", store.DeleteServiceItem(item.Id));
    }

    [Fact]
    public void A_category_with_catalogue_items_cannot_be_deleted_and_a_rename_moves_them()
    {
        using var test = new TestStore();
        var store = test.Store;

        Assert.Contains("service catalogue", store.DeleteTicketOption("Category", "Network"));
        Assert.Contains("Network", store.Categories);

        store.UpdateTicketOption("Category", "Network", "Connectivity");
        Assert.DoesNotContain(store.ServiceItems, x => x.Category == "Network");
        Assert.Contains(store.ServiceItems, x => x.Category == "Connectivity");
    }

    [Fact]
    public void Deleting_a_priority_clears_it_as_a_starting_priority_and_renaming_it_follows()
    {
        using var test = new TestStore();
        var store = test.Store;
        store.AddServiceItem("Hardware", "Dead pixel", "Urgent");
        store.AddServiceItem("Hardware", "Cracked case", "Low");

        store.UpdateTicketOption("Priority", "Low", "Minor");
        Assert.Equal("Minor", store.ServiceItems.Single(x => x.Name == "Cracked case").DefaultPriority);

        store.DeleteTicketOption("Priority", "Urgent");
        Assert.Equal("", store.ServiceItems.Single(x => x.Name == "Dead pixel").DefaultPriority);
    }

    [Fact]
    public void Moving_a_ticket_to_another_category_drops_its_sub_category_but_the_same_category_keeps_it()
    {
        using var test = new TestStore();
        var store = test.Store;
        var ticket = new TicketRecord(0, "Dead pixel", "", Guid.NewGuid(), [], null, "Normal", "Open", "Hardware", DateTime.UtcNow, null) { SubCategory = "Dead pixel" };

        Assert.Equal("Dead pixel", store.WithCategory(ticket, "hardware").SubCategory);
        Assert.Equal("", store.WithCategory(ticket, "Software").SubCategory);
    }

    [Fact]
    public void The_catalogue_and_a_tickets_sub_category_survive_a_restart_and_save_only_the_difference()
    {
        using var test = new TestStore();
        var store = test.Store;
        var requester = test.AddRequester("Priya Shah");

        var before = store.RecentSaveTimings().Count;
        store.AddServiceItem("Hardware", "Dead pixel", "High");
        var number = store.AddTicket(new TicketRecord(0, "Dead pixel", "", requester.Id, [], null, "High", "Open", "Hardware", DateTime.UtcNow, null) { SubCategory = "Dead pixel" });
        Assert.All(store.RecentSaveTimings().Skip(before), x => Assert.False(x.FullRewrite));
        Assert.Null(store.CompareDatabaseWithMemory());

        var reopened = test.Reopen();
        Assert.Equal("High", reopened.ServiceItems.Single(x => x.Name == "Dead pixel").DefaultPriority);
        Assert.Equal("Dead pixel", reopened.Tickets.Single(x => x.Number == number).SubCategory);
        Assert.Null(reopened.CompareDatabaseWithMemory());
    }

    [Fact]
    public void The_portal_offers_shown_items_and_shown_templates_together_by_category()
    {
        using var test = new TestStore();
        var store = test.Store;
        foreach (var item in store.ServiceItems.ToList()) store.DeleteServiceItem(item.Id);

        store.AddServiceItem("Hardware", "Dead pixel", "", "Stuck dot on the screen");
        store.AddServiceItem("Hardware", "Hidden thing", "", null, showInPortal: false);
        store.AddTicketTemplate("New starter laptop", "Request", "Laptop for a new starter", "", "Hardware", "Normal", null, null, "Login and email too", showInPortal: true);
        store.AddTicketTemplate("Technician only", "Incident", "", "", "Hardware", "Normal", null, null);
        store.AddTicketTemplate("Install a program", "Request", "", "", "Software", "Low", null, null, showInPortal: true);

        var hardware = store.PortalChoices("hardware");
        Assert.Equal(["Dead pixel", "New starter laptop"], hardware.Select(x => x.Label));
        Assert.Equal("Stuck dot on the screen", hardware[0].HelperLine);
        Assert.Equal(HelpdeskStore.PortalChoice.ItemKind, hardware[0].Kind);
        Assert.Equal(HelpdeskStore.PortalChoice.TemplateKind, hardware[1].Kind);
        Assert.Equal("Login and email too", hardware[1].HelperLine);

        // Categories in their own order, only those with something to press.
        Assert.Equal([("Hardware", 2), ("Software", 1)], store.PortalCategories());
    }

    [Fact]
    public void A_template_is_only_offered_while_it_is_shown_and_its_category_exists()
    {
        using var test = new TestStore();
        var store = test.Store;
        store.AddTicketTemplate("Shown", "Request", "", "", "Software", "Low", null, null, showInPortal: true);
        store.AddTicketTemplate("Hidden", "Request", "", "", "Software", "Low", null, null);
        var shown = store.TicketTemplates.Single(x => x.Name == "Shown");
        var hidden = store.TicketTemplates.Single(x => x.Name == "Hidden");

        Assert.NotNull(store.FindPortalTemplate(shown.Id));
        Assert.Null(store.FindPortalTemplate(hidden.Id));
        Assert.NotNull(store.FindPortalTemplateByName("software", "shown"));
        Assert.Null(store.FindPortalTemplateByName("Hardware", "Shown"));

        // A rename follows the template, so its button does not vanish.
        store.UpdateTicketOption("Category", "Software", "Programs");
        Assert.Equal("Programs", store.GetTicketTemplate(shown.Id)!.Category);
        Assert.NotNull(store.FindPortalTemplate(shown.Id));
        Assert.Contains(store.PortalChoices("Programs"), x => x.Label == "Shown");
    }

    [Fact]
    public void Helper_lines_have_a_limit_and_templates_and_items_keep_both_new_settings_through_a_restart()
    {
        using var test = new TestStore();
        var store = test.Store;
        var tooLong = new string('x', HelpdeskStore.MaxHelperLineLength + 1);
        Assert.Contains("helper line is too long", store.AddServiceItem("Hardware", "Dead pixel", "", tooLong));
        Assert.Contains("helper line is too long", store.AddTicketTemplate("T", "Request", "", "", "Hardware", "Normal", null, null, tooLong));

        store.AddServiceItem("Hardware", "Dead pixel", "", "  Stuck dot  ", showInPortal: false);
        store.AddTicketTemplate("New starter laptop", "Request", "", "", "Hardware", "Normal", null, null, "Login and email", showInPortal: true);
        Assert.Null(store.CompareDatabaseWithMemory());

        var reopened = test.Reopen();
        var item = reopened.ServiceItems.Single(x => x.Name == "Dead pixel");
        Assert.Equal("Stuck dot", item.HelperLine);
        Assert.False(item.ShowInPortal);
        var template = reopened.TicketTemplates.Single(x => x.Name == "New starter laptop");
        Assert.Equal("Login and email", template.HelperLine);
        Assert.True(template.ShowInPortal);
    }

    [Fact]
    public void A_database_from_before_the_portal_buttons_keeps_its_items_visible_and_its_templates_hidden()
    {
        using var test = new TestStore();
        test.Store.AddTicketTemplate("Old template", "Incident", "", "", "Hardware", "Normal", null, null);
        // Put the file back as the previous release left it: the same tables without the two new columns.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={test.DatabasePath};Pooling=False"))
        {
            connection.Open();
            foreach (var sql in new[]
            {
                "ALTER TABLE ServiceItems DROP COLUMN HelperLine;", "ALTER TABLE ServiceItems DROP COLUMN ShowInPortal;",
                "ALTER TABLE TicketTemplates DROP COLUMN HelperLine;", "ALTER TABLE TicketTemplates DROP COLUMN ShowInPortal;"
            })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var reopened = test.Reopen();
        Assert.NotEmpty(reopened.ServiceItems);
        Assert.All(reopened.ServiceItems, x => Assert.True(x.ShowInPortal));
        Assert.All(reopened.TicketTemplates, x => Assert.False(x.ShowInPortal));
        Assert.NotEmpty(reopened.PortalCategories());
        Assert.Null(reopened.CompareDatabaseWithMemory());
    }

    [Fact]
    public void Every_icon_and_colour_is_distinct_and_every_built_in_look_uses_real_ones()
    {
        Assert.Equal(PortalLook.Icons.Count, PortalLook.Icons.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(PortalLook.Colors.Count, PortalLook.Colors.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(PortalLook.Icons, x => Assert.False(string.IsNullOrWhiteSpace(x.Shapes)));
        Assert.All(PortalLook.Colors, x => Assert.Matches("^#[0-9a-f]{6}$", x.Hex));
        Assert.NotNull(PortalLook.FindIcon(PortalLook.DefaultIcon));
        Assert.NotNull(PortalLook.FindColor(PortalLook.DefaultColor));
        foreach (var category in new[] { "Hardware", "Software", "Account", "Network", "Classroom AV", "Other", "Onboarding", "Something new" })
        {
            var (icon, color) = PortalLook.DefaultFor(category);
            Assert.NotNull(PortalLook.FindIcon(icon));
            Assert.NotNull(PortalLook.FindColor(color));
        }
        // An unknown key never reaches the page as anything but the folder icon and the default colour.
        Assert.Contains("<svg", PortalLook.Svg("<script>").Value);
        Assert.DoesNotContain("script", PortalLook.Svg("<script>").Value);
        Assert.Equal(PortalLook.Hex(PortalLook.DefaultColor), PortalLook.Hex("red; background:url(x)"));
    }

    [Fact]
    public void A_category_tile_has_a_built_in_look_until_one_is_chosen_and_the_choice_is_checked_and_kept()
    {
        using var test = new TestStore();
        var store = test.Store;

        Assert.Equal(new CategoryStyle("projector", "pink"), store.StyleFor("Classroom AV"));
        Assert.Equal(new CategoryStyle(PortalLook.DefaultIcon, PortalLook.DefaultColor), store.StyleFor("Nonsense"));

        Assert.Equal("Select a valid category.", store.SetCategoryStyle("Nonsense", "lock", "red"));
        Assert.Equal("Choose one of the icons.", store.SetCategoryStyle("Hardware", "<b>", "red"));
        Assert.Equal("Choose one of the colours.", store.SetCategoryStyle("Hardware", "lock", "#ff0000"));
        Assert.Equal(new CategoryStyle("laptop", "blue"), store.StyleFor("Hardware"));

        Assert.Equal("Hardware tile saved.", store.SetCategoryStyle("hardware", "KEY", "Gold"));
        Assert.Equal(new CategoryStyle("key", "gold"), store.StyleFor("Hardware"));
        Assert.Null(store.CompareDatabaseWithMemory());
        Assert.Equal(new CategoryStyle("key", "gold"), test.Reopen().StyleFor("Hardware"));
    }

    [Fact]
    public void A_category_tile_look_follows_a_rename_and_goes_with_a_delete()
    {
        using var test = new TestStore();
        var store = test.Store;
        store.SetCategoryStyle("Hardware", "mouse", "red");
        store.SetCategoryStyle("Other", "gear", "green");

        store.UpdateTicketOption("Category", "Hardware", "Equipment");
        Assert.Equal(new CategoryStyle("mouse", "red"), store.StyleFor("Equipment"));

        // "Other" has no tickets or items, so it can go - and its look must not come back if a new one reuses the name.
        Assert.Equal("Category deleted.", store.DeleteTicketOption("Category", "Other"));
        store.AddTicketOption("Category", "Other");
        Assert.Equal(PortalLook.DefaultFor("Other").Icon, store.StyleFor("Other").Icon);
        Assert.Null(store.CompareDatabaseWithMemory());
        Assert.Equal(new CategoryStyle("mouse", "red"), test.Reopen().StyleFor("Equipment"));
    }

    [Fact]
    public void Categories_move_up_and_down_and_keep_their_order_after_a_restart()
    {
        using var test = new TestStore();
        var store = test.Store;
        var before = store.Categories.ToList();

        Assert.Null(store.MoveCategory(before[0], -1)); // already first: nothing happens, nothing to say
        Assert.Equal(before, store.Categories);
        Assert.Null(store.MoveCategory(before[^1], 1));
        Assert.Equal("Category was not found.", store.MoveCategory("Nonsense", 1));

        Assert.Null(store.MoveCategory(before[2], -1));
        var expected = before.ToList();
        (expected[1], expected[2]) = (expected[2], expected[1]);
        Assert.Equal(expected, store.Categories);
        Assert.Null(store.CompareDatabaseWithMemory());
        Assert.Equal(expected, test.Reopen().Categories);
        // The tiles follow: the portal lists categories in that order.
        Assert.Equal(expected.Where(c => test.Store.PortalChoices(c).Count > 0), test.Store.PortalCategories().Select(x => x.Category));
    }

    [Fact]
    public void Portal_buttons_can_be_ordered_by_hand_with_items_and_templates_sharing_one_order()
    {
        using var test = new TestStore();
        var store = test.Store;
        foreach (var item in store.ServiceItems.ToList()) store.DeleteServiceItem(item.Id);
        store.AddServiceItem("Hardware", "Alpha", "");
        store.AddServiceItem("Hardware", "Charlie", "");
        store.AddTicketTemplate("Bravo template", "Request", "", "", "Hardware", "Normal", null, null, showInPortal: true);
        string Labels() => string.Join(",", store.PortalChoices("Hardware").Select(x => x.Label));

        // Added one after another, they sit in that order - not alphabetically.
        Assert.Equal("Alpha,Charlie,Bravo template", Labels());

        var bravo = store.PortalChoices("Hardware").Single(x => x.Kind == HelpdeskStore.PortalChoice.TemplateKind);
        Assert.Null(store.MovePortalChoice("Hardware", bravo.Kind, bravo.Key, -1));
        Assert.Equal("Alpha,Bravo template,Charlie", Labels());
        Assert.Null(store.MovePortalChoice("Hardware", bravo.Kind, bravo.Key, -1));
        Assert.Equal("Bravo template,Alpha,Charlie", Labels());
        Assert.Null(store.MovePortalChoice("Hardware", bravo.Kind, bravo.Key, -1)); // at the top already
        Assert.Equal("Bravo template,Alpha,Charlie", Labels());
        Assert.Equal("That button was not found.", store.MovePortalChoice("Hardware", "item", "Nonsense", 1));
        Assert.Equal("That button was not found.", store.MovePortalChoice("Hardware", "template", Guid.NewGuid().ToString(), 1));

        // Editing keeps its place; hiding and showing again sends it to the end; so does moving category and back.
        var alpha = store.ServiceItems.Single(x => x.Name == "Alpha");
        store.UpdateServiceItem(alpha.Id, "Hardware", "Alpha (renamed)", "High", "A hint", true);
        Assert.Equal("Bravo template,Alpha (renamed),Charlie", Labels());
        store.UpdateServiceItem(alpha.Id, "Hardware", "Alpha (renamed)", "High", "A hint", false);
        Assert.Equal("Bravo template,Charlie", Labels());
        store.UpdateServiceItem(alpha.Id, "Hardware", "Alpha (renamed)", "High", "A hint", true);
        Assert.Equal("Bravo template,Charlie,Alpha (renamed)", Labels());

        Assert.Null(store.CompareDatabaseWithMemory());
        var reopened = test.Reopen();
        Assert.Equal("Bravo template,Charlie,Alpha (renamed)", string.Join(",", reopened.PortalChoices("Hardware").Select(x => x.Label)));
    }

    [Fact]
    public void A_new_button_joins_the_end_of_its_category_and_buttons_in_other_categories_are_untouched()
    {
        using var test = new TestStore();
        var store = test.Store;
        foreach (var item in store.ServiceItems.ToList()) store.DeleteServiceItem(item.Id);
        store.AddServiceItem("Hardware", "Zulu", "");
        store.AddServiceItem("Software", "Yankee", "");
        var yankee = store.PortalChoices("Software").Single();
        store.AddServiceItem("Software", "Alpha", "");
        Assert.Equal(["Yankee", "Alpha"], store.PortalChoices("Software").Select(x => x.Label));
        Assert.Null(store.MovePortalChoice("Software", yankee.Kind, yankee.Key, 1));
        Assert.Equal(["Alpha", "Yankee"], store.PortalChoices("Software").Select(x => x.Label));
        Assert.Equal(["Zulu"], store.PortalChoices("Hardware").Select(x => x.Label));
    }

    [Fact]
    public void A_database_from_before_ordering_and_tile_looks_opens_alphabetically_with_built_in_looks()
    {
        using var test = new TestStore();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={test.DatabasePath};Pooling=False"))
        {
            connection.Open();
            foreach (var sql in new[]
            {
                "ALTER TABLE ServiceItems DROP COLUMN PortalOrder;", "ALTER TABLE TicketTemplates DROP COLUMN PortalOrder;", "DROP TABLE CategoryStyles;"
            })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var reopened = test.Reopen();
        var hardware = reopened.PortalChoices("Hardware").Select(x => x.Label).ToList();
        Assert.NotEmpty(hardware);
        Assert.Equal(hardware.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), hardware);
        Assert.Equal(new CategoryStyle("laptop", "blue"), reopened.StyleFor("Hardware"));
        Assert.Null(reopened.CompareDatabaseWithMemory());
    }

    [Fact]
    public void A_database_from_before_the_catalogue_opens_with_blank_sub_categories_and_an_empty_catalogue()
    {
        using var test = new TestStore();
        var requester = test.AddRequester("Priya Shah");
        var number = test.AddTicket(requester.Id);
        foreach (var item in test.Store.ServiceItems.ToList()) test.Store.DeleteServiceItem(item.Id);

        var reopened = test.Reopen();
        Assert.Empty(reopened.ServiceItems);
        Assert.Equal("", reopened.Tickets.Single(x => x.Number == number).SubCategory);
    }
}
