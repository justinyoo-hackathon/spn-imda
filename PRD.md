# Product Requirements Document (PRD)

## Metadata

| Field | Value |
| --- | --- |
| Document ID | PRD-002 |
| Product | Smart Parking Navigator |
| Repository | `justinyoo-hackathon/spn-imda` |
| Related Issue | Closes `justinyoo-hackathon/spn-imda#2` |
| Status | Approved |
| Version | 1.0.0 |
| Last Updated | 2026-08-19 |
| Authors | GitHub Copilot Coding Agent |
| Reviewers | Workshop participant, repository maintainers |
| Approved By | `justinyoo-hackathon` |
| Approval Date | 2026-08-19 |

## Revision History

| Version | Date | Author | Summary |
| --- | --- | --- | --- |
| 1.0.0 | 2026-08-19 | GitHub Copilot Coding Agent | Mark document approved for implementation planning |
| 0.1.0 | 2026-08-19 | GitHub Copilot Coding Agent | Initial workshop-sized MVP PRD draft |

## 1. Product Summary

Smart Parking Navigator is a mobile-first Singapore car-park finder for HDB
car parks. It combines static HDB car-park metadata with live availability from
data.gov.sg so a driver can search for a Singapore destination and quickly see
nearby parking options with current lot availability, operating constraints, and
data freshness.

This issue defines only the workshop MVP. It does not include implementation
work, deployment design, databases, MCP integrations, or agentic AI features.

## 2. Problem Statement

Drivers in Singapore can find HDB car parks, but they still need to compare
distance, availability, and operating conditions manually. A destination may
have multiple nearby car parks, some full, some unsuitable for the driver's
vehicle, and some showing stale data. The MVP should reduce that decision time
by presenting ranked nearby HDB car parks with clear freshness and availability
states.

## 3. Goals

### 3.1 Business Goal

Demonstrate a realistic, workshop-sized Smart Parking Navigator that can be
implemented from the prepared Aspire starter and Singapore public datasets.

### 3.2 User Goals

- Search for a destination in Singapore.
- See nearby HDB car parks within walking distance.
- Compare options using live lot availability and parking conditions.
- Filter out unsuitable car parks.
- Understand whether displayed data is fresh, stale, unavailable, or in error.

### 3.3 Non-Goals

The MVP does not include:

- Reservations, payments, or navigation turn-by-turn routing
- User accounts, favorites, alerts, or saved preferences
- Historical storage, forecasting, or analytics dashboards
- Non-HDB car parks
- Deployment architecture or database design
- MCP-based integrations
- Agentic AI features, which are deferred to workshop step 05

## 4. Singapore Context and Constraints

- The MVP is limited to Singapore destinations and HDB car parks.
- Destination search must return only Singapore locations.
- Nearby car parks are limited to HDB car parks within 500 metres of the chosen
  destination.
- Time-based messaging must use Singapore Standard Time (SGT).
- Real-time availability depends on the data.gov.sg Car Park Availability API.
- Static operating details come from the HDB car-park dataset.

## 5. Target Users

### 5.1 Primary User

Singapore drivers heading to an HDB-served destination who want a quick parking
recommendation before arrival.

### 5.2 Secondary User

Workshop participants and reviewers who need a focused, implementable product
scope that maps cleanly to the prepared frontend, backend, and datasets.

## 6. User Journeys

### Journey 1: Find nearby parking for a destination

1. The user enters a Singapore destination.
2. The app resolves the destination and centers the map/list on it.
3. The app shows nearby HDB car parks within 500 metres.
4. The user compares distance, available lots, occupancy, and freshness.
5. The user selects a car park to view operating details.

### Journey 2: Filter out unsuitable car parks

1. The user enables one or more filters.
2. The app updates results without showing car parks that fail the selected
   criteria.
3. The user keeps only car parks that match their current needs.

### Journey 3: Handle stale or unavailable data

1. The app cannot retrieve fresh live availability or only has older data.
2. The app keeps the last successful information if available.
3. The app clearly labels results as loading, stale, unavailable, empty, or
   error instead of implying live certainty.

## 7. MVP Scope

### 7.1 In Scope

#### Destination Search

- Search by destination/address within Singapore.
- Select a destination and use it as the reference point for nearby parking.

#### Nearby HDB Car Parks

- Show HDB car parks within 500 metres of the destination.
- Rank results using distance first, then current availability and occupancy as
  tie-breakers.

#### Live Lot Availability

- Show available and total lots using the supported lot types from the published
  API contract:
  - `C` Cars
  - `H` Heavy vehicles
  - `S` Motorcycles with side car
  - `Y` Motorcycles
- Show source update time and app-level freshness state.
- Show occupancy as a derived percentage when total lots are present.

#### Filters

- Available-only
- Vehicle type
- Night parking
- Car-park type

#### Car-Park Details

- Address
- Car-park type
- Parking system
- Short-term parking value
- Free parking value
- Night parking value
- Number of decks
- Gantry height
- Basement/underground flag

#### UX States

- Loading
- Empty results
- Fresh data (source update time within 2 minutes of current SGT)
- Stale data (source update time older than 2 minutes, but last known data is
  still available)
- Live availability unavailable with last known data shown
- Error retrieving data

### 7.2 Out of Scope

- Favorites and notifications
- Free-parking prediction or time interpretation beyond source values
- Occupancy forecasting
- External traffic or weather data
- Databases and background storage
- Azure or other deployment targets
- MCP servers or extensions
- AI parking recommendations in the product workflow

## 8. Functional Requirements

### FR-01 Destination Search

The user must be able to search for and choose a destination in Singapore.

### FR-02 Nearby Result Set

The app must show only HDB car parks located within 500 metres of the selected
destination.

### FR-03 Ranked Results

The app must present results ordered to help quick comparison, prioritizing
closer car parks and then higher current suitability based on live availability.

### FR-04 Availability by Vehicle Type

Each result must show total and available lots for supported lot types present
in the data source.

### FR-05 Car-Park Details

The user must be able to inspect operating details for a selected car park.

### FR-06 Filters

The user must be able to filter results by available-only, vehicle type, night
parking, and car-park type.

### FR-07 Freshness and Failure States

The app must indicate whether availability data is loading, fresh, stale,
unavailable, empty, or errored.

## 9. Non-Functional Requirements

- Mobile-first layout suitable for workshop demonstration.
- Google Maps credentials must remain limited to browser-side map features.
- data.gov.sg credentials must remain server-side only.
- The app must not claim live freshness when no recent availability timestamp is
  available.
- Source values from HDB data such as short-term parking and free parking must
  be displayed as provided, without inferring undocumented meanings.

## 10. Measurable Acceptance Criteria

### AC-01 Destination Search

- Given a user enters a Singapore destination, when the destination is selected,
  then the app uses that location as the basis for nearby parking results.
- Given a destination outside Singapore or no valid match, when search
  completes, then the app shows a clear no-results or invalid-destination state.

### AC-02 Nearby HDB Car Parks

- Given a valid destination, when parking results are shown, then every result
  represents an HDB car park within 500 metres of the selected destination.

### AC-03 Availability and Ranking

- Given live availability data exists, when results are shown, then each result
  includes distance, available lots, total lots, occupancy, source update time,
  and freshness state.
- Given multiple matching car parks, when results are ranked, then nearer car
  parks appear before farther ones, with live suitability used to break ties.

### AC-04 Filters

- Given the available-only filter is enabled, when results refresh, then car
  parks with zero available lots for the selected vehicle type are excluded.
- Given a vehicle type filter is selected, when results refresh, then only
  availability for that supported lot type is used for comparison.
- Given night-parking or car-park-type filters are selected, when results
  refresh, then only matching HDB records remain.

### AC-05 Car-Park Details

- Given a user opens a car-park detail view, when data is available, then the
  app shows address, car-park type, parking system, short-term parking, free
  parking, night parking, decks, gantry height, and basement flag.

### AC-06 Freshness States

- Given availability data was updated within 2 minutes of current SGT, when
  rendered, then the app labels it as fresh.
- Given availability data is older than 2 minutes but still available, when
  rendered, then the app labels it as stale and still shows the last known
  values.
- Given live retrieval fails and no last known data exists, when rendering
  results, then the app shows an unavailable or error state instead of live
  figures.

### AC-07 Scope Control

- The MVP must not require deployment design, database persistence, MCP, or
  agentic AI to satisfy the core acceptance criteria.
- Agentic AI features are explicitly deferred to workshop step 05.

## 11. Release Readiness for Workshop Step 03

This PRD is approved for implementation planning. Reviewers confirmed:

- The MVP scope is narrow enough for the workshop
- The acceptance criteria are testable
- Deferred items are clearly excluded
- The repository can proceed to `TRD.md`
