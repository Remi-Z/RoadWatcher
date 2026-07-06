# RoadWatch batch submission notes

Source: https://onlinereporting.yrp.ca/RoadWatch.html

## What the page allows

RoadWatch submissions are accepted through LexisNexis Desk Officer Reporting System, not a stable public API.
The start page redirects into a session-specific Apache Tapestry app.

Direct bulk POSTs are brittle because each session carries:

- `JSESSIONID`
- `_csrf`
- `dynparam`
- one or more compressed `t:formdata` hidden fields

The lazy working path is browser automation over the real page, with a manual review before each final submit.

## First page contract

The RoadWatch landing page opens:

`https://onlinereporting.yrp.ca:443/dors/app?service=external/StartReport&sp=100181100&sp=100223204`

That page rewrites `StartReport` to a screen-width route such as:

`/dors/en/startreport/SW1920;jsessionid=...`

The first active form is `prefilingQuestionsForm`.

Observed gate answers for an eligible RoadWatch report:

| Question | Field | Eligible value |
| --- | --- | --- |
| Incident occurred in York region, not including a 400-series highway | `radiogroup_0` | `Y300000183` |
| Threat of violence, hate, or bias | `radiogroup_0_0` | `N300000184` |
| Collision occurred | `radiogroup_0_1` | `N300000185` |

Submitting those answers reveals `yourselfForm`.

## Reporter form fields

Observed `yourselfForm` fields:

- `firstNameField`
- `lastNameField`
- `countryField`
- `stNo`
- `address1StNameField`
- `stType`
- `stPostDirection`
- `stAptUnit`
- `cityField`
- `state2Field`
- `zipcodeField`
- `hmPhoneField`
- `mbPhoneField`
- `emailField`
- `emailConfirmField`
- `wkPhoneField`
- `wkPhoneExtField`
- `dobFieldYearSelect`
- `dobFieldMonthSelect`
- `dobFieldDaySelect`
- `sexField`

Do not use fake identity data to inspect later pages. Fill this with the real reporter profile in the browser.

## Batch workflow

Use one input row per real incident:

```csv
id,plate,vehicle,occurred_at,address,latitude,longitude,category,narrative,evidence_folder
inc-1,ABC1234,dark sedan,2026-07-06T14:00:08-04:00,"Highway 7 near Warden",43.8570,-79.3380,FailToYield,"Driver failed to yield. Dashcam video is available on request.",C:\path\to\evidence
```

Automation should:

1. Open a fresh RoadWatch report.
2. Answer the three gate questions.
3. Fill the reporter profile from a local private profile file.
4. Fill one incident row.
5. Stop on the review page for human verification.
6. Submit only after explicit confirmation.
7. Save the confirmation page as the receipt.

## Receipt export

After final submit, capture both:

- `receipt.html`: the full confirmation page HTML.
- `receipt.png` or `receipt.pdf`: a visual copy of the confirmation page.

Name the folder from the local incident id and any confirmation/report number visible after submit:

`receipts/20260706-140008-ABC1234-confirmation-number/`

Keep videos local unless York Regional Police later asks for them. The RoadWatch page says to retain photo or video evidence and mention it in the narrative.

## Boundary

Skipped: raw HTTP batch submitter.

Add it only if the site exposes a documented API or the Tapestry tokens stop changing per session. Until then, browser automation is less code and less fragile.
