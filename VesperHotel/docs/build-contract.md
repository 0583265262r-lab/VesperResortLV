# Vesper implementation contract

This is the shared integration contract for the C# API and React frontend. Build a complete local application with Docker production-like infrastructure. Keep C# readable at intermediate OOP level: ordinary classes, explicit constructors, small services, async methods, no MediatR/CQRS framework, no reflection tricks, no generic repository.

## Ownership

- Backend agent: src/ only, backend build/config/migrations/seeding. Must communicate any contract change before making it.
- Frontend agent: frontend/ only. Do not change backend or compose/docs.
- Root: infrastructure/, tests/, docs/, root scripts and integration fixes.
- Asset agent: work/assets only; root copies finalized assets into frontend/public/images.

## HTTP

JSON camelCase. All /api routes. Enum values serialized as strings. Dates ISO 8601. Rooms use YYYY-MM-DD; other starts/ends use ISO timestamps. Auth required for booking mutations. All responses use database, never pretend success. Error format: { status, code, message, traceId, errors? }. Resource kind is Room, Activity, Transportation. Booking status: Pending, Confirmed, Cancelled, Completed, Expired, Failed.

- GET /api/health -> { status, database, messagingMode }
- GET /api/catalog?kind=Room -> CatalogItem[] (kind optional lists all active items)
- GET /api/catalog/{id} -> CatalogItem including sessions and units counts
- GET /api/availability?resourceId=...&start=...&end=...&guests=2&sessionId=... -> { available, availableCount, totalAmount, currency, offerVersion, cancellationHours }
- POST /api/auth/register { email,password,firstName,lastName,phoneNumber } -> AuthResponse
- POST /api/auth/login { email,password } -> AuthResponse
- POST /api/auth/refresh -> AuthResponse; refresh via HttpOnly cookie
- POST /api/auth/logout -> 204
- GET /api/auth/me -> UserResponse
- PUT /api/auth/profile { firstName,lastName,phoneNumber } -> UserResponse
- POST /api/auth/forgot-password {email} -> generic success. Local delivery via persistent notification outbox/file, never return token in public API.
- POST /api/auth/reset-password {email,token,newPassword} -> success
- POST /api/bookings with Idempotency-Key and HoldRequest -> BookingResponse (201)
- POST /api/bookings/{id}/confirm -> BookingResponse
- POST /api/bookings/{id}/cancel { reason } -> BookingResponse
- GET /api/bookings/mine?status=...&page=1&pageSize=20 -> PagedBookings
- GET /api/bookings/{id} -> BookingResponse, ownership enforced
- GET /api/notifications -> [{ id,subject,message,createdAtUtc,isRead }]
- POST /api/notifications/{id}/read -> 204

CatalogItem: { id:string, kind:string, name:string, slug:string, description:string, tagline:string, imageUrl:string, basePrice:number, priceUnit:string, maxGuests:number, isActive:boolean, offerVersion:number, features:string[], availableUnits:number, sessions:ActivitySession[] }
ActivitySession: { id:string, startUtc:string, endUtc:string, capacity:number, reservedSeats:number, price:number }
HoldRequest: { kind:string, resourceId:string, sessionId?:string, start:string, end:string, guests:number, pickupLocation?:string, dropoffLocation?:string, expectedOfferVersion:number, notes?:string }
BookingResponse: { id:string, bookingNumber:string, kind:string, resourceId:string, resourceName:string, imageUrl:string, status:string, startUtc:string, endUtc:string, guests:number, totalAmount:number, currency:string, expiresAtUtc?:string, createdAtUtc:string, cancellationDeadlineUtc:string, pickupLocation?:string, dropoffLocation?:string, notes?:string, priceLines:[{description,quantity,unitPrice,total}], assignedUnit?:string, version:number }
PagedBookings: { items:BookingResponse[], totalCount:number, page:number, pageSize:number }
AuthResponse: { accessToken:string, user:UserResponse }
UserResponse: { id:string, email:string, firstName:string, lastName:string, phoneNumber:string, role:string }

## Admin

- GET /api/admin/dashboard?from=...&to=... -> { totalBookings, confirmedBookings, cancelledBookings, completedBookings, pendingBookings, bookedValue, roomOccupancy, activityUtilization, transportationUtilization, daily:[{date,bookings,value}], byKind:[{kind,count,value}], upcoming:BookingResponse[] }
- GET /api/admin/bookings?status=&kind=&search=&from=&to=&page=1&pageSize=20 -> PagedBookings
- GET /api/admin/resources -> CatalogItem[] (including inactive)
- POST /api/admin/resources -> accepts {kind,name,description,tagline,basePrice,priceUnit,maxGuests,imageUrl,features}; returns CatalogItem
- PUT /api/admin/resources/{id} -> accepts same editable fields plus isActive, expectedVersion; returns CatalogItem
- GET /api/admin/resources/{id}/units -> [{id,name,isActive}]
- POST /api/admin/resources/{id}/units {name} -> unit
- PUT /api/admin/units/{id} {name,isActive} -> unit
- POST /api/admin/resources/{id}/sessions {startUtc,endUtc,capacity,price} -> session
- PUT /api/admin/sessions/{id} {startUtc,endUtc,capacity,price,expectedVersion} -> session (block unsafe edits)
- GET /api/admin/users?page=1&pageSize=20&search= -> {items:UserResponse[],totalCount,page,pageSize}
- GET /api/admin/audit?page=1&pageSize=20 -> {items:[{id,action,entityType,entityId,oldValue,newValue,adminEmail,createdAtUtc,correlationId}],totalCount,page,pageSize}
- GET /api/admin/system -> {outboxPending,outboxFailed,notificationsPending,recentEvents:[{id,type,createdAtUtc,status,attempts}]}

## Business and integration

10-minute Pending hold consumes inventory. Confirmation pay-at-hotel, no card capture. Rooms one physical unit, 1–30 nights, checkin 15 checkout 11 America/Los_Angeles. Activities separate sessions with capacity. Transportation whole vehicle hourly ceil, 30-minute turnaround. Cancellation 48h rooms, 24h other; admin may cancel before completion with reason. Snapshot price and policies. Overlaps [start,end). Confirm/expire races must serialize. MySQL resource-row locking, all related mutations consistent lock order. SQLite local runnable fallback allowed and must use database write serialization, never process-only lock as production guarantee. Outbox and worker persistent, idempotent consumers. SQL aggregates for dashboard. No raw credentials in source. Environment-generated seed admin password; a .env local file is ignored. Local admin email admin@vesper.local. Development demo guest optional seeded with generated password. Default backend port 5080, frontend Vite 5173; same origin production /api reverse proxy.

Images root will provide: /images/hero.jpg, /images/suite.jpg, /images/pool.jpg. Additional catalog visuals can reuse these intentionally or use verified real URLs with sources. Do not invent remote URLs. Brand VESPER / LAS VEGAS. English interface, editorial luxury, dark ink/champagne, serif headings, custom layouts, mobile accessible, admin efficient. Do not add fake testimonials or dead links.
