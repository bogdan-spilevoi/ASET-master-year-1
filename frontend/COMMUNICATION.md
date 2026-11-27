# Web communication

```mermaid
flowchart LR
    Browser["Browser · Angular"]
    Web["smartlost-web · Nginx"]
    Auth["auth-service:80"]
    Listings["listings-service:80"]
    Browser -->|"1. Load SPA, assets and client routes"| Web
    Browser -->|"2. Submit registration or login"| Web
    Web -->|"2.1 Forward /api/auth requests"| Auth
    Browser -->|"3. Browse, filter or open a report"| Web
    Web -->|"3.1 Read categories and listings"| Listings
    Browser -->|"3.1.1 Load visible report thumbnails, then the full photo on the detail route"| Web
    Web -->|"3.1.2 GET /api/listings/{id}/photo with preview=true for cards"| Listings
    Browser -->|"2.2 Open Profile"| Web
    Web -->|"2.3 GET /api/auth/me with JWT"| Auth
    Browser -->|"2.4 Sign out from Profile"| Web
    Web -->|"2.5 POST /api/auth/logout with refresh token"| Auth
    Browser -->|"3.2 Choose a My listings filter on Profile"| Web
    Web -->|"3.3 GET /api/listings/mine with JWT, filters and pagination"| Listings
    Browser -->|"3.4 Open draft card and GET owned draft, photo and analysis"| Web
    Browser -->|"3.5 Save partial fields with PATCH or confirm DELETE on profile"| Web
    Web -->|"3.6 Forward owner JWT to draft routes"| Listings
    Browser -->|"4. Create draft with photo, edit and publish with JWT"| Web
    Web -->|"4.1 Forward body and Authorization header"| Listings
    Browser -->|"4.2 After draft creation, POST traits and embedding"| Web
    Web -->|"4.3 Forward both owner requests for outbox delivery"| Listings
    Browser -->|"4.4 Poll analysis until both results are ready or work stops"| Web
    Web -->|"4.5 Read status and Qwen suggestions from Listings"| Listings
    Browser -->|"4.6 PATCH selected or typed category, broader class and checked traits; publish on owner request"| Web
```

Nginx also routes the public Listings decision API. Matching and private photo routes remain internal. Each backend owns persistence and validation. Upstream HTTP errors reach Angular with their status, and the UI shows failures to the user. An unrecognized API route returns 404. Client-side page links resolve to the Angular entry point.
