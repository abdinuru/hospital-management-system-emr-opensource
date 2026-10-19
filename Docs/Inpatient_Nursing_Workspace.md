# Inpatient nursing workspace

Route: `Nursing/InPatient/InPatientList`.

## Included

- Responsive patient cards and the existing table. Search by name, hospital/IP number, bed or admitting doctor. Sort by outstanding urgent messages, overdue care tasks, documented risks, then name; alternatively natural bed order.
- Optional per-user **My patients only** preference, using existing server-backed Nursing favorites. Heart buttons manage this personal list. This is not a formal nurse allocation or a server access restriction. All Patients remains available to authorized ward nurses.
- Patient context (name, hospital/IP identifiers, bed, admission date and doctor), reception status, documented risk flags and care task counts. Summary counts follow the visible filters. Missing data is shown as unavailable, not as zero.
- Existing vitals, chart, results, nursing notes, drug request, consumption, ward request, transfer, receive-patient and diet workflows.
- Durable care tasks with optional due time and completion audit; shift handover records; documented risk flags with explicit resolution. Categories cover observations, medication follow-up, fluid balance, pain, dressings, lines/catheters, nutrition, repositioning, falls, pressure injuries, isolation, investigations, education and discharge preparation.
- SBAR messages to the admission's current **AdmittingDoctorId** (the field displayed as doctor in charge). Transactional notification creation, explicit acknowledgement by recipient and replies in the existing doctor patient overview and new clinical overview. A reply notifies the original sender. This does not resolve on-call rosters or substitute for clinical orders.
- Care messages also open from the notification bell. Existing notification settings and polling frequency govern delivery. Opening the bell notification marks it read; it does **not** acknowledge the clinical message.
- Authenticated server checks on every API, nursing-summary permission for nursing actions, admission and active bed/ward checks, recipient-only acknowledgement, parameterized SQL, immutable record text and attributed completion. Doctors can view/reply only while they are the admission's assigned doctor. Nurses with existing summary permission can access ward care records as with existing ward workflows.

## Installation

1. Back up and test on a non-production copy of the Danphe database.
2. Run `Database/Nursing/Inpatient_Workspace_Setup.sql` in that database. It creates one new table and indexes; it does not execute the previously uploaded patches or alter existing clinical records.
3. Build/deploy the server and Angular client through the existing project deployment process. This checkout's project file targets .NET Framework 4.6.1 and its client uses Angular 7 / TypeScript 3.1.6.
4. Grant the existing `nursing-ip-summary-view` permission to appropriate nursing users. Existing action permissions and route guards remain in place.
5. Verify the notification bell is enabled for the doctor and nurse accounts. Configure the admission doctor correctly before messaging.

## Validation performed

`Tests/Nursing/inpatient-workspace.test.js` runs against TypeScript 3.1.6, Angular compiler 7.2.11 and moment 2.24.0. With client dependencies installed, run from the repository root:

```sh
NODE_PATH=./Code/Websites/DanpheEMR/wwwroot/DanpheApp/node_modules node Tests/Nursing/inpatient-workspace.test.js
```

It exercises actual component methods for search, optional personal-list scoping (including an empty personal list), sorting, overdue state, recipient acknowledgement, SBAR construction and duplicate-submit prevention. It checks changed TypeScript syntax and parses the affected Angular templates. `git diff --check` also passes.

No SQL Server or .NET build runtime was available in the editing environment. Full server compilation, SQL execution, browser interaction with real patient data, delivery and role-based end-to-end checks remain deployment checks; template parsing is not a full Angular production build.

## Acceptance checks on staging

- Two nurses save different My Patients lists. Turning the option on/off and refreshing preserves each user's preference; switching views does not duplicate patients.
- Nurse creates a task, handover and risk flag. Another authorized nurse sees them. Due tasks appear in the filter. Completion/resolution retains original text and actor/time. Concurrent completion returns an error to the second caller.
- Nurse sends an SBAR to a patient with an assigned doctor. That doctor gets a bell notification, opens the correct visit, explicitly acknowledges, and replies. The nurse receives the reply and can acknowledge it. An unrelated doctor cannot read, reply or close a record by changing IDs.
- No assigned doctor: message save is rejected with a useful error; do not claim delivery. Patient transferred/discharged: stale care writes are rejected and the user refreshes.
- Simulate network loss: drafts remain after failed saves, no success message is shown, and users refresh before retrying. Missing setup: care counts show unavailable and existing nursing actions still work.
- Verify vitals, prescription/administration, intake/output, care notes, investigations, diet, discharge and transfers using the existing clinical forms. Care task completion records a reminder outcome only; it never charts an administration or changes a clinical order.

## Operational limits

Times follow existing server-local Danphe time; workstation and server time zones should match. The list refreshes every 60 seconds. An open care timeline requires Refresh to see another person's changes. Timelines show the latest 500 records; doctor threads show the latest 100 messages. Risk flags are manually documented, not inferred clinical warnings. Use the established emergency call process for emergencies; message submission is not proof a doctor has seen it. Schema rollback should retain the care table for audit; reverting application code is sufficient to hide the workspace.
