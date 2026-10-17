---
title: Setting up a premises check
summary: Making a recurring ticket with a checklist - pass/fail lines, readings with limits, every asset of a type.
area: Site and cleaning
kind: task
pages: /Settings/Checklist
needs: Settings:Edit, Facilities.Setup
keywords: premises check checklist set up recurring ticket schedule reading limits min max unit asset type extinguisher legionella fire alarm
order: 5
---
A premises check is a **recurring ticket** with a **checklist**. The recurring ticket says when it's logged and who does it; the checklist says what's recorded each time.

## 1. Make the recurring ticket

In **Facilities → Recurring checks** (the same page as **Settings → Recurring tickets**), add one. With **Set up facilities** but not Settings, you see and add only the schedules of your own desks.

1. **Name** it, such as *Monthly water temperatures*.
2. Choose a **category** on the right desk (a Site category for a site check), the **priority**, a **requester** (often the site office), and the **team** or **technician** who does it.
3. Set how often it **repeats**, the time it's logged (**At**) and when it **Starts**.
4. Tick **Move to the next school day** if it shouldn't be logged on a weekend or a day off.

While the last ticket it logged is still open, it doesn't log another.

## 2. Add the checklist

Press **Checklist** beside the recurring ticket, and add lines one at a time. Each line has a **kind**:

| Kind | Use it for | Fill in |
| --- | --- | --- |
| Pass or fail | A yes/no check, such as *Call point 3 sounded* | What is checked |
| Reading with limits | A measured number, such as *Hot water, hall kitchen* | What is checked, the lowest and/or highest allowed value, and the unit |
| Every asset of a type | One pass/fail line per asset of that type on the register, such as every fire extinguisher | The asset type |
| Note | A free text box, never required | What to write about |

For a reading, set the **lowest allowed** for something that must be at least a value (hot water: 50 °C or more), the **highest allowed** for something that must stay under one (cold water: 20 °C or less), or both.

Use the arrows to put the lines in the order they're walked round.

## What changes affect

Each ticket gets its own copy of the checklist when it's logged. Changing or removing a line later doesn't change tickets already logged. An *every asset of a type* line lists the assets on the register at the moment the ticket is logged, so add new extinguishers to the asset register before the next check.

**Log one now** beside the recurring ticket logs a ticket straight away, to try it out.
