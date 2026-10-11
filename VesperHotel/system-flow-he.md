# הפלואו המלא של מערכת VESPER

המסמך מתאר את המערכת לפי הקוד הקיים: איזו פונקציה מופעלת, מי מפעיל אותה, אילו בדיקות היא עושה, ומה נשמר במסד הנתונים.

## 1. התמונה הכללית

בקשה רגילה עוברת במסלול הבא:

```text
עמוד React
    ↓ api() או send()
Nginx ב-Docker
    ↓ /api
ASP.NET Middleware
    ↓ Authentication + Authorization
Controller
    ↓
Service
    ↓
HotelDbContext
    ↓
MySQL או SQLite
```

פעולה שיוצרת אירוע ממשיכה גם ברקע:

```text
Service שומר Booking וגם OutboxEvent באותה טרנזקציה
    ↓
HotelWorker קורא את Outbox
    ↓
Kafka
    ↓
Consumer ב-HotelWorker
    ↓
Inbox מונע עיבוד כפול
    ↓
Notification נשמרת ונמסרת
    ↓
האירוע נשלח גם ל-Elasticsearch
```

## 2. מה קורה כשהשרת עולה

נקודת הכניסה היא `Hotel.Api/Program.cs`.

1. `WebApplication.CreateBuilder(args)` קורא הגדרות מ-`appsettings`, ממשתני סביבה ומ-Docker.
2. הקוד קורא את `Database:Provider` ואת `ConnectionStrings:HotelDatabase`.
3. `AddDbContext<HotelDbContext>` רושם את החיבור למסד הנתונים.
4. אם ה-Provider הוא `MySql`, מופעל `UseMySQL`. אחרת מופעל `UseSqlite`.
5. `AddIdentityCore<HotelUser>` רושם את מערכת המשתמשים, הסיסמאות, התפקידים והנעילה אחרי ניסיונות כושלים.
6. `AddAuthentication().AddJwtBearer()` מגדיר כיצד לבדוק JWT שמגיע בכותרת `Authorization`.
7. `AddAuthorization()` מאפשר להשתמש ב-`[Authorize]` וב-`[Authorize(Roles = "Admin")]`.
8. השירותים `AuthService`, `BookingService`, `CatalogService` ו-`AdminService` נרשמים כ-Scoped.
9. `AddHttpClient<WeatherService>` יוצר `WeatherService` יחד עם `HttpClient` שמכוון ל-Open-Meteo.
10. `AddControllers()` מפעיל Controllers ו-Model Binding.
11. `AddJsonOptions()` גורם ל-Enums להישלח כטקסט, למשל `Room`, במקום מספר.
12. מוגדר טיפול אחיד בשגיאות ולידציה.
13. מוגדר CORS עבור כתובת הפרונט.
14. מוגדר Rate Limiter עבור פעולות התחברות.
15. במצב מקומי `AddHostedService<LocalHotelWorker>()` מפעיל Worker בלי Kafka.
16. `builder.Build()` בונה את האפליקציה ואת מיכל ההזרקות.
17. ה-Middleware מופעל לפי הסדר: `ApiErrors`, ‏CORS, ‏Rate Limiter, ‏Authentication, ‏Authorization.
18. `MapControllers()` מחבר את כתובות ה-API לפונקציות ב-Controllers.
19. `DatabaseSeeder.InitializeAsync(app.Services)` מכין את מסד הנתונים ואת נתוני ההדגמה.
20. `RunAsync()` מתחיל להאזין לבקשות.

### הפלואו של DatabaseSeeder

`DatabaseSeeder.InitializeAsync()` יוצר Scope חדש ומקבל ממנו:

- `HotelDbContext`
- `IConfiguration`
- `RoleManager`
- `UserManager`

לאחר מכן:

1. ב-SQLite מופעלות `EnsureCreatedAsync()` ופקודת WAL.
2. ב-MySQL מופעלת `MigrateAsync()`.
3. נוצרים התפקידים `Admin` ו-`Guest` אם אינם קיימים.
4. נוצר משתמש האדמין מהערכים שב-`.env`.
5. נוצרת רשומת `system` שמשמשת לנעילה כללית.
6. אם הקטלוג ריק, מופעלות `AddRoom`, ‏`AddActivity` ו-`AddTransport`.
7. `AddDemoRoomGalleriesAsync()` משלים את גלריות התמונות.

## 3. Dependency Injection — הזרקת תלויות

Dependency Injection אומר שמחלקה מצהירה בבנאי מה היא צריכה, ו-ASP.NET יוצר ומעביר לה את האובייקטים.

לדוגמה, `CatalogController` צריך שני שירותים:

```csharp
public CatalogController(
    CatalogService catalogService,
    BookingService bookingService)
{
    _catalogService = catalogService;
    _bookingService = bookingService;
}
```

אין בקוד `new CatalogService(...)`. מיכל ההזרקות רואה את הבנאי, מוצא את הרישומים ב-`Program.cs`, יוצר את השירותים ומעביר אותם ל-Controller.

### מה רשום במיכל

| רישום | מה מוזרק | Lifetime |
| --- | --- | --- |
| `AddDbContext<HotelDbContext>` | חיבור EF Core למסד | Scoped |
| `AddIdentityCore<HotelUser>` | `UserManager`, `RoleManager`, `SignInManager` | Scoped |
| `AddScoped<AuthService>` | פעולות משתמש והתחברות | Scoped |
| `AddScoped<BookingService>` | זמינות והזמנות | Scoped |
| `AddScoped<CatalogService>` | קריאת הקטלוג | Scoped |
| `AddScoped<AdminService>` | פעולות מנהל | Scoped |
| `AddMemoryCache` | `IMemoryCache` | Singleton פנימי |
| `AddHttpClient<WeatherService>` | WeatherService ו-HttpClient | Typed client |
| `AddHostedService<LocalHotelWorker>` | עבודת רקע מקומית | Singleton |

### מה פירוש Scoped

בכל בקשת HTTP נוצר Scope חדש. בתוך אותה בקשה:

```text
Controller
  ├── BookingService
  └── HotelDbContext
```

ה-Controller והשירות מקבלים את אותו `HotelDbContext` של הבקשה. בסיום הבקשה ה-Scope נסגר וה-DbContext משוחרר.

### איך נבנה AdminController

הבנאי מבקש:

```text
AdminService
CatalogService
BookingService
AuthService
HotelDbContext
```

המיכל בונה כל שירות. אם שירות מבקש `HotelDbContext`, הוא מקבל את אותו DbContext של הבקשה. כך אפשר לבצע קריאות לשירות ולמסד בלי לפתוח חיבורים ידנית.

### למה Worker משתמש ב-IServiceScopeFactory

Worker חי כל עוד התהליך פועל ולכן הוא Singleton. אסור לשמור בתוכו DbContext אחד לאורך כל חיי התהליך. במקום זאת:

1. `IServiceScopeFactory.CreateScope()` יוצר Scope זמני.
2. `GetRequiredService<HotelDbContext>()` מקבל DbContext חדש.
3. ה-Worker מבצע פעולה.
4. בסוף בלוק `using` ה-Scope וה-DbContext נסגרים.

אותו תהליך משמש גם כאשר ה-Worker צריך `BookingService`.

### הזרקה בפונקציית Health

ב-`MapGet("/api/health", async (HotelDbContext db) => ...)` ההזרקה מתבצעת ישירות לפרמטר של הפונקציה. ASP.NET מזהה ש-`HotelDbContext` הוא Service רשום ומעביר אותו.

## 4. Pipeline של כל בקשת API

כל בקשה נכנסת לפי הסדר הבא:

1. `ApiErrors.InvokeAsync()` מתחיל מדידת זמן ומוסיף `X-Correlation-ID`.
2. CORS בודק האם כתובת הפרונט מורשית.
3. Rate Limiter מגביל בקשות ל-Endpoints של Auth.
4. Authentication קורא את ה-JWT ומייצר `User` עם Claims.
5. Authorization בודק `[Authorize]` ותפקיד `Admin`.
6. Model Binding ממיר JSON למחלקת Request.
7. Data Annotations כמו `[Required]` ו-`[EmailAddress]` נבדקות.
8. ה-Controller מופעל.
9. ה-Controller קורא ל-Service או ל-DbContext.
10. התוצאה הופכת ל-JSON.
11. `ApiErrors` רושם Status, זמן ו-Trace ID.

אם נזרקת `HotelException`, ‏`ApiErrors` מחזיר את ה-Status וה-Code שנקבעו בה. שגיאת Concurrency מוחזרת כ-409. שגיאה לא מוכרת מוחזרת כ-500 ונכתבת ללוג.

## 5. הפלואו המשותף בפרונט

כל המסכים נטענים מתוך `main.tsx`:

```text
BrowserRouter
    ↓
AuthProvider
    ↓
App
    ↓
Routes והעמוד המתאים
```

### api()

`api<T>()` ב-`api.ts` היא הפונקציה המרכזית לכל בקשות הפרונט:

1. יוצרת Headers.
2. אם יש Body, מוסיפה `Content-Type: application/json`.
3. אם קיים Access Token בזיכרון, מוסיפה `Authorization: Bearer ...`.
4. מפעילה `fetch('/api' + path)`.
5. מוסיפה `credentials: 'include'` כדי לשלוח את Cookie של ה-Refresh Token.
6. אם התקבל 401, מפעילה `refreshToken()` פעם אחת.
7. אם הרענון הצליח, שולחת שוב את הבקשה המקורית.
8. אם התקבלה שגיאה, יוצרת `ApiError` עם message, status ו-code.
9. אם התקבלה הצלחה, ממירה את ה-JSON לסוג `T`.

`send<T>()` היא פונקציית עזר שקוראת ל-`api<T>()` עם Method ו-Body.

### AuthProvider

כאשר React עולה:

1. `AuthProvider` מפעיל `POST /auth/refresh`.
2. הדפדפן שולח את Refresh Cookie.
3. אם השרת מחזיר Access Token, `accept()` שומר אותו בזיכרון ושומר את המשתמש ב-State.
4. `Guard` מאפשר כניסה למסכים פרטיים רק אם יש משתמש.
5. במסכי אדמין `Guard admin` בודק גם `user.role === 'Admin'`.

## 6. הצגת קטלוג

### רשימת חדרים, פעילויות או תחבורה

```text
CatalogPage
  → useApi('/catalog?kind=Room')
  → api()
  → GET /api/catalog
  → CatalogController.GetCatalog()
  → CatalogService.ListAsync()
  → HotelDbContext.Resources
  → CatalogItem[]
```

`CatalogService.ListAsync()`:

1. מתחיל שאילתה על `Resources`.
2. מסנן משאבים פעילים, אלא אם אדמין ביקש גם לא פעילים.
3. מסנן לפי `ResourceKind` אם נשלח Kind.
4. טוען Units ו-Sessions.
5. מחשב כמה מקומות שמורים בכל Session.
6. `Map()` ממיר Entity של מסד הנתונים ל-`CatalogItem` של ה-API.
7. `ReadStringList()` ממיר את ה-JSON של Features ושל GalleryImages לרשימות.

### פרטי משאב וגלריית חדר

```text
DetailPage
  → GET /api/catalog/{id}
  → CatalogController.GetResource()
  → CatalogService.GetAsync()
  → CatalogService.Map()
  → DetailPage
  → RoomGallery
```

`RoomGallery` משתמש ב-`galleryImages` שכבר הגיעו מהשרת. פתיחת תמונה מתבצעת בפרונט בלבד ואינה יוצרת בקשת API נוספת.

## 7. בדיקת זמינות

כאשר המשתמש לוחץ על Check availability:

```text
BookingPanel.check()
  → request()
  → GET /api/availability
  → CatalogController.CheckAvailability()
  → BookingService.AvailabilityAsync()
  → BookingRules.Period()
  → BookingRules.ValidateGuests()
  → AllocateAsync()
  → AvailabilityResponse
```

`request()` בפרונט מכין נתונים שונים לפי סוג המשאב:

- חדר: תאריך כניסה ותאריך יציאה.
- פעילות: מזהה Session והזמנים שלו.
- תחבורה: שעות התחלה וסיום, איסוף ויעד.

`AvailabilityAsync()`:

1. מחפש משאב פעיל.
2. `BookingRules.Period()` ממיר את התאריכים ל-UTC.
3. בחדר נקבעות שעת כניסה 15:00 ושעת יציאה 11:00 לפי Las Vegas.
4. בפעילות ובתחבורה נבדק שהסיום אחרי ההתחלה.
5. בתחבורה מתווספות 30 דקות ל-`InventoryEndUtc`.
6. `ValidateGuests()` בודקת את מספר האורחים.
7. `AllocateAsync()` בודקת מלאי קיים.
8. מוחזרים זמינות, כמות פנויה, מחיר, גרסת הצעה ומדיניות ביטול.

`AvailabilityAsync()` אינה שומרת דבר. זו בדיקה להצגה בלבד. בזמן יצירת ההזמנה מתבצעת בדיקה נוספת בתוך טרנזקציה.

## 8. הרשמה

```text
AuthPage.submit()
  → send('/auth/register')
  → AuthController.Register()
  → AuthService.RegisterAsync()
  → UserManager.CreateAsync()
  → UserManager.AddToRoleAsync('Guest')
  → CreateSessionAsync()
  → SaveChanges + Commit
  → AuthController.SaveRefreshCookie()
  → AuthProvider.accept()
```

`RegisterAsync()`:

1. פותחת טרנזקציה דרך `DatabaseWrite.BeginAsync()`.
2. יוצרת `HotelUser` מהבקשה.
3. `UserManager.CreateAsync()` בודק את כללי הסיסמה ושומר Hash, לא את הסיסמה עצמה.
4. המשתמש מקבל Role בשם `Guest`.
5. `CreateSessionAsync()` יוצר Access Token ו-Refresh Token.
6. Hash של ה-Refresh Token נשמר ב-`RefreshSessions`.
7. השינויים נשמרים והטרנזקציה מבצעת Commit.
8. ה-Controller שומר את ה-Refresh Token ב-HttpOnly Cookie.
9. ה-Access Token ופרטי המשתמש חוזרים ל-React.

## 9. התחברות אדמין או אורח

```text
AuthPage או AdminLoginPage
  → POST /api/auth/login
  → AuthController.Login()
  → AuthService.LoginAsync()
  → UserManager.FindByEmailAsync()
  → SignInManager.CheckPasswordSignInAsync()
  → CreateSessionAsync()
  → Access Token + Refresh Cookie
```

`CheckPasswordSignInAsync()` בודק את Hash הסיסמה ומטפל בנעילה לאחר חמישה ניסיונות כושלים. ה-JWT כולל:

- מזהה משתמש.
- כתובת אימייל.
- Role של `Admin` או `Guest`.
- Security Stamp.

בכניסת אדמין הפרונט בודק שהתפקיד שחזר הוא `Admin`. בהמשך גם השרת בודק `[Authorize(Roles = "Admin")]`; בדיקת הפרונט לבדה אינה ההגנה.

## 10. רענון התחברות ויציאה

### Refresh

```text
AuthProvider או api() אחרי 401
  → POST /api/auth/refresh + Cookie
  → AuthController.Refresh()
  → AuthService.RefreshAsync()
  → Hash(token)
  → חיפוש RefreshSession
  → ביטול Session ישן
  → CreateSessionAsync()
  → Cookie ו-Access Token חדשים
```

### Logout

```text
AuthProvider.logout()
  → POST /api/auth/logout
  → AuthService.LogoutAsync()
  → RefreshSession.RevokedAtUtc = now
  → מחיקת Cookie
  → ניקוי המשתמש וה-Access Token מה-State
```

## 11. פרופיל ואיפוס סיסמה

### פרופיל

`ProfilePage.save()` → `PUT /auth/profile` → `AuthController.UpdateProfile()` → `AuthService.ProfileAsync()` → עדכון `HotelUser` דרך `UserManager.UpdateAsync()`.

### שכחתי סיסמה

`ForgotPasswordPage.submit()` → `POST /auth/forgot-password` → `AuthService.ForgotAsync()`.

אם המשתמש קיים:

1. `GeneratePasswordResetTokenAsync()` יוצר Token.
2. נבנה קישור לעמוד Reset Password.
3. אירוע `PasswordReset` נשמר ב-Outbox.
4. ה-Worker יהפוך אותו להתראה וימסור אותה.

התגובה לפרונט זהה גם אם האימייל אינו קיים, כדי לא לחשוף אילו משתמשים רשומים.

### איפוס סיסמה

`ResetPasswordPage.submit()` → `POST /auth/reset-password` → `AuthService.ResetAsync()`.

1. המשתמש נמצא לפי אימייל.
2. `ResetPasswordAsync()` בודק את ה-Token ומעדכן את Hash הסיסמה.
3. כל ה-Refresh Sessions הפעילים מבוטלים.
4. המשתמש צריך להתחבר מחדש.

## 12. יצירת הזמנה — Hold

זה הפלואו החשוב ביותר במערכת.

```text
BookingPanel.reserve()
  → POST /api/bookings
  → Header: Idempotency-Key
  → BookingsController.CreateHold()
  → BookingService.HoldAsync()
  → DatabaseWrite.BeginAsync()
  → ReplayAsync()
  → בדיקת Resource ו-OfferVersion
  → BookingRules
  → AllocateAsync()
  → יצירת Booking
  → AddEvent('BookingHeld')
  → SaveReplay()
  → SaveChangesAsync()
  → CommitAsync()
  → מעבר למסך ההזמנה
```

### בפרונט

1. `BookingPanel` יוצר `Idempotency-Key` באמצעות `crypto.randomUUID()`.
2. שינוי של תאריך, אורחים או פרטי נסיעה יוצר מפתח חדש.
3. `reserve()` בודקת שיש משתמש מחובר.
4. היא שולחת `HoldRequest` עם `expectedOfferVersion` שקיבלה מבדיקת הזמינות.

### ב-HoldAsync

1. `ValidateKey()` בודקת שהמפתח קיים ואורכו עד 120 תווים.
2. נוצר Hash של תוכן הבקשה.
3. `DatabaseWrite.BeginAsync()` פותחת טרנזקציה ונועלת את המשתמש ואת המשאב.
4. `ReplayAsync()` בודקת אם אותה פעולה עם אותו מפתח כבר נשמרה.
5. אם זו בקשה חוזרת זהה, מוחזרת התוצאה המקורית.
6. אם אותו מפתח הגיע עם תוכן אחר, מוחזרת שגיאת 409.
7. המשאב נטען ונבדק שהוא פעיל.
8. נבדק שסוג המשאב בבקשה מתאים למשאב.
9. `ExpectedOfferVersion` נבדק מול `OfferVersion` כדי למנוע הזמנה לפי מחיר ישן.
10. נבדקים האורחים והתאריכים.
11. בתחבורה נבדקים מקום איסוף ויעד.
12. `AllocateAsync()` בודקת שוב את המלאי בתוך הנעילה.
13. נוצר `RoomBooking`, ‏`ActivityBooking` או `TransportationBooking`.
14. המחיר והפרטים מועתקים לתוך ההזמנה, כדי ששינוי מחיר עתידי לא ישנה אותה.
15. `ExpiresAtUtc` נקבע לעשר דקות או לתחילת השירות, המוקדם מביניהם.
16. נקבע מועד הביטול: 48 שעות לחדר ו-24 שעות לשאר.
17. ההזמנה מתווספת ל-`Bookings` בסטטוס `Pending`.
18. `AddEvent()` מוסיפה `BookingHeld` ל-Outbox.
19. `SaveReplay()` שומרת את התוצאה בטבלת Idempotency.
20. `SaveChangesAsync()` שומרת הזמנה, Outbox ו-Idempotency יחד.
21. `CommitAsync()` מאשרת את הטרנזקציה.

## 13. אישור הזמנה

```text
BookingPage.confirm()
  → POST /bookings/{id}/confirm
  → BookingsController.ConfirmBooking()
  → ChangeStatus('confirm')
  → BookingService.TransitionAsync()
```

`TransitionAsync()`:

1. מוצאת את `ResourceId` ומוודאת שההזמנה שייכת למשתמש או שהוא Admin.
2. פותחת טרנזקציה עם נעילה.
3. מטפלת ב-Idempotency אם התקבל מפתח.
4. טוענת את ההזמנה.
5. אם ה-Hold פג, משנה ל-`Expired`, מוסיפה אירוע ושומרת.
6. באישור נבדק שהשירות עוד לא התחיל.
7. נבדק שהסטטוס הוא `Pending` או כבר `Confirmed`.
8. הסטטוס משתנה ל-`Confirmed`.
9. זמן הפקיעה נמחק.
10. `Version` גדל.
11. נוסף `BookingConfirmed` ל-Outbox.
12. השינויים נשמרים ומתבצע Commit.

אין סליקה. אישור ההזמנה משמעו שהמקום שמור והתשלום יבוצע במלון.

## 14. ביטול הזמנה

```text
CancelBookingModal.cancel()
  → POST /bookings/{id}/cancel
  → BookingsController.CancelBooking()
  → ChangeStatus('cancel')
  → BookingService.TransitionAsync()
```

הפונקציה בודקת:

1. ההזמנה שייכת למשתמש או שהמשתמש Admin.
2. הסטטוס מאפשר ביטול.
3. ההזמנה עדיין לא הסתיימה.
4. משתמש רגיל נמצא לפני `CancellationDeadlineUtc`.
5. אדמין שמבטל חייב לספק סיבה.

לאחר מכן הסטטוס הופך ל-`Cancelled`, נוסף `BookingCancelled` ל-Outbox, ובפעולת אדמין נוספת גם רשומת `Audit`.

## 15. צפייה בהזמנות

### הזמנות המשתמש

`BookingsPage` → `GET /bookings/mine` → `BookingsController.GetMyBookings()` → `BookingService.ListAsync(userId, ...)`.

### הזמנה בודדת

`BookingPage` → `GET /bookings/{id}` → `BookingService.GetAsync()`.

השאילתה כוללת תנאי בעלות. Admin יכול לצפות בכל הזמנה.

### רשימת הזמנות לאדמין

`AdminBookings` → `GET /admin/bookings` → `AdminController.GetBookings()` → `BookingService.ListAsync(null, filters...)`.

`ListAsync()` בונה `IQueryable`, מוסיפה `Where` רק עבור הפילטרים שנשלחו, סופרת תוצאות ומפעילה `Skip` ו-`Take` לעימוד.

## 16. התראות

### הצגת התראות

`NotificationsPage` → `GET /notifications` → `NotificationsController.GetNotifications()` → שאילתה ישירה על `Notifications` לפי UserId.

### סימון כנקרא

`markRead()` → `POST /notifications/{id}/read` → `MarkAsRead()`.

השרת מחפש התראה לפי Id וגם לפי UserId, משנה `IsRead` ושומר. תנאי UserId מונע ממשתמש לסמן התראה של משתמש אחר.

## 17. מזג האוויר

קיימים שני מסלולים בקוד הנוכחי.

### המסלול שמוצג בדף הבית

`WeatherWidget` ב-`PublicPages.tsx` מפעיל `fetch()` ישירות אל Open-Meteo ומציג את התוצאה. הקריאה אינה עוברת דרך הבקאנד.

### Endpoint שקיים בבקאנד

```text
GET /api/weather
  → WeatherController.GetWeather()
  → WeatherService.GetLasVegasAsync()
  → IMemoryCache
  → HttpClient
  → Open-Meteo
```

`WeatherService`:

1. מחפש `las-vegas-weather` ב-Cache.
2. אם נמצא ערך, מחזיר אותו בלי קריאת רשת.
3. אחרת שולח בקשה ל-Open-Meteo.
4. ממיר את Weather Code לטקסט באמצעות `WeatherCondition()`.
5. שומר את התוצאה ב-Cache ל-15 דקות.
6. אם השירות החיצוני אינו זמין, ה-Controller מחזיר 503.

## 18. ניהול משאבים

### יצירה או עדכון

`ResourceEditor.save()` → `POST /admin/resources` או `PUT /admin/resources/{id}` → `AdminService.SaveResourceAsync()`.

ה-Service:

1. בודק Kind, Features, תמונת שער וגלריה.
2. פותח טרנזקציה ונועל את המשאב.
3. ביצירה יוצר `CatalogResource` חדש.
4. בעדכון בודק `ExpectedVersion` מול `OfferVersion`.
5. מונע שינוי של Kind קיים.
6. ממיר מחיר לסנטים באמצעות `BookingRules.ToCents()`.
7. שומר Features וגלריה כ-JSON.
8. ביצירה מייצר Slug.
9. מוסיף רשומת Audit.
10. שומר ומחזיר `CatalogItem` מעודכן.

## 19. ניהול יחידות מלאי

חדר פיזי או רכב פיזי מיוצגים על ידי `ResourceUnit`.

`UnitManager.save()` → `POST /admin/resources/{id}/units` או `PUT /admin/units/{id}` → `AdminService.SaveUnitAsync()`.

1. נמצא המשאב של היחידה.
2. נפתחת טרנזקציה על המשאב.
3. פעילות נדחית כי פעילות משתמשת ב-Sessions.
4. בכיבוי יחידה נבדק שאין לה הזמנה פעילה.
5. השם והסטטוס נשמרים.
6. נוסף Audit.

## 20. ניהול Sessions של פעילויות

`SessionManager.save()` → `POST /admin/resources/{id}/sessions` או `PUT /admin/sessions/{id}` → `AdminService.SaveSessionAsync()`.

1. נבדקים תאריכי התחלה וסיום.
2. נמצא המשאב ונבדק שהוא Activity.
3. מחושב מספר המקומות שכבר נשמרו.
4. בעדכון נבדקת Version.
5. Session שיש בו הזמנות אינו יכול לעבור שעה או לרדת מתחת למספר המקומות השמורים.
6. המחיר מומר לסנטים.
7. `OfferVersion` של המשאב גדל.
8. נוסף Audit והמידע נשמר.

## 21. Dashboard, משתמשים, Audit ומצב מערכת

- `GET /admin/dashboard` → `AdminService.DashboardAsync()` מחשבת מספר הזמנות, שווי הזמנות, תפוסת חדרים, ניצול פעילויות ותחבורה.
- `GET /admin/users` → `AuthService.ListUsersAsync()` מחפשת משתמשים ומחזירה תפקידים.
- `GET /admin/audit` → שאילתה ישירה על `Audit` עם עימוד.
- `GET /admin/system` → סופרת אירועי Outbox והתראות ממתינות ומחזירה אירועים אחרונים.

כל ה-Endpoints האלה מוגנים ב-`[Authorize(Roles = "Admin")]`.

## 22. DatabaseWrite והטרנזקציה

`DatabaseWrite.BeginAsync()` מרכזת את הנעילה:

### SQLite

נפתחת טרנזקציית `BEGIN IMMEDIATE` ברמת Serializable. SQLite מאפשר כותב אחד ולכן הנעילה מתקבלת בתחילת הפעולה.

### MySQL

1. נפתחת טרנזקציה ברמת `ReadCommitted`.
2. אם נשלח UserId, מופעל `SELECT ... FOR UPDATE` על המשתמש.
3. אם נשלח ResourceId, מופעל `SELECT ... FOR UPDATE` על המשאב.
4. כל בקשה מתחרה חייבת להמתין לשחרור אותה נעילה.

`CommitAsync()` מאשרת את השינויים. אם הקוד יוצא לפני Commit, Dispose מבטל את הטרנזקציה.

## 23. Outbox, Kafka, Inbox והתראות

### למה Outbox נשמר יחד עם ההזמנה

אם ההזמנה נשמרה אבל Kafka לא זמין, האירוע עדיין קיים במסד. ה-Worker יכול לנסות שוב מאוחר יותר.

### PublishOneAsync

1. יוצר Scope ומקבל DbContext.
2. פותח טרנזקציה.
3. בוחר אירוע Outbox שעדיין לא פורסם.
4. מסמן אותו `Processing` ומגדיל Attempts.
5. שומר Commit ומשחרר את הנעילה.
6. שולח `EventEnvelope` ל-Kafka.
7. ב-Scope חדש מסמן `Published`, ‏`Pending` או `Failed`.
8. בכישלון קובע זמן ניסיון הבא עם Backoff.

### ConsumeAsync ו-Inbox

1. Kafka Consumer מקבל EventEnvelope.
2. `ConsumeAsync()` פותחת Scope וטרנזקציה.
3. `ConsumeInTransactionAsync()` בודקת אם Id האירוע כבר קיים ב-Inbox.
4. אם קיים, האירוע כבר עובד ולא נוצרת התראה נוספת.
5. אם אינו קיים, נוצרת `Notification` ונוספת רשומת `InboxEvent`.
6. רק לאחר הצלחה מתבצע Commit ל-Kafka Offset.

### DeliverOneAsync

1. בוחרת התראה שלא נמסרה.
2. מציבה Lease של דקה כדי ש-Worker אחר לא ייקח אותה במקביל.
3. מוצאת את אימייל המשתמש.
4. במימוש ההדגמה כותבת קובץ JSON לתיקיית Notifications.
5. מסמנת `DeliveredAtUtc` ומנקה את ה-Lease.

### IndexOneAsync

1. בוחרת אירוע Published שעדיין לא אונדקס.
2. שולחת מסמך ל-`vesper-events` ב-Elasticsearch.
3. בהצלחה מעדכנת `IndexedAtUtc`.
4. בכישלון קובעת ניסיון נוסף.
5. כשל ב-Elasticsearch אינו מבטל הזמנה ואינו חוסם התראה.

### SweepAsync

1. מחפשת Holds שפגו והזמנות שאושרו והסתיימו.
2. לכל Resource יוצרת Scope חדש.
3. מפעילה `BookingService.SweepResourceAsync()`.
4. Pending שפג הופך ל-`Expired`.
5. Confirmed שהסתיים הופך ל-`Completed`.
6. נוסף אירוע מתאים ל-Outbox.

## 24. LocalHotelWorker לעומת HotelWorker

בפיתוח מקומי `LocalHotelWorker` מבצע את אותו רעיון בלי Kafka ו-Elasticsearch:

```text
SweepBookingsAsync()
ProcessOutboxAsync()
DeliverNotificationAsync()
```

ב-Docker `Workers:RunInApi=false`, ולכן ה-API אינו מפעיל אותו. פרויקט `Hotel.Worker` מופעל כתהליך נפרד ומשתמש ב-Kafka וב-Elasticsearch.

## 25. טבלת Endpoint → פונקציות

| Endpoint | Controller | Service או פעולה |
| --- | --- | --- |
| `GET /api/health` | Program | `Database.CanConnectAsync()` |
| `GET /api/catalog` | `GetCatalog` | `CatalogService.ListAsync` |
| `GET /api/catalog/{id}` | `GetResource` | `CatalogService.GetAsync` |
| `GET /api/availability` | `CheckAvailability` | `BookingService.AvailabilityAsync` |
| `POST /api/auth/register` | `Register` | `AuthService.RegisterAsync` |
| `POST /api/auth/login` | `Login` | `AuthService.LoginAsync` |
| `POST /api/auth/refresh` | `Refresh` | `AuthService.RefreshAsync` |
| `POST /api/auth/logout` | `Logout` | `AuthService.LogoutAsync` |
| `GET /api/auth/me` | `GetCurrentUser` | `AuthService.MeAsync` |
| `PUT /api/auth/profile` | `UpdateProfile` | `AuthService.ProfileAsync` |
| `POST /api/auth/forgot-password` | `ForgotPassword` | `AuthService.ForgotAsync` |
| `POST /api/auth/reset-password` | `ResetPassword` | `AuthService.ResetAsync` |
| `POST /api/bookings` | `CreateHold` | `BookingService.HoldAsync` |
| `GET /api/bookings/mine` | `GetMyBookings` | `BookingService.ListAsync` |
| `GET /api/bookings/{id}` | `GetBooking` | `BookingService.GetAsync` |
| `POST /api/bookings/{id}/confirm` | `ConfirmBooking` | `BookingService.TransitionAsync` |
| `POST /api/bookings/{id}/cancel` | `CancelBooking` | `BookingService.TransitionAsync` |
| `GET /api/notifications` | `GetNotifications` | שאילתת DbContext |
| `POST /api/notifications/{id}/read` | `MarkAsRead` | עדכון DbContext |
| `GET /api/weather` | `GetWeather` | `WeatherService.GetLasVegasAsync` |
| `GET /api/admin/dashboard` | `GetDashboard` | `AdminService.DashboardAsync` |
| `GET /api/admin/bookings` | `GetBookings` | `BookingService.ListAsync` |
| `GET /api/admin/resources` | `GetResources` | `CatalogService.ListAsync` |
| `POST/PUT /api/admin/resources` | Create/Update Resource | `AdminService.SaveResourceAsync` |
| `POST/PUT units` | Create/Update Unit | `AdminService.SaveUnitAsync` |
| `POST/PUT sessions` | Create/Update Session | `AdminService.SaveSessionAsync` |
| `GET /api/admin/users` | `GetUsers` | `AuthService.ListUsersAsync` |
| `GET /api/admin/audit` | `GetAudit` | שאילתת DbContext |
| `GET /api/admin/system` | `GetSystemStatus` | שאילתות Outbox ו-Notifications |

## 26. הסבר קצר שמתאים להגנה

אפשר להסביר את המערכת כך:

> הפרונט שולח בקשות דרך פונקציה מרכזית בשם api. הבקשה מגיעה ל-Controller, שתפקידו לקבל HTTP ולזהות את המשתמש. ה-Controller מעביר את הפעולה ל-Service, שבו נמצאים החוקים העסקיים. ה-Service משתמש ב-HotelDbContext כדי לקרוא ולכתוב למסד. ביצירת הזמנה נפתחת טרנזקציה וננעל המלאי, הזמינות נבדקת שוב, ונשמרים יחד ההזמנה, מפתח Idempotency ואירוע Outbox. Worker נפרד קורא את האירוע, מפרסם אותו ב-Kafka, משתמש ב-Inbox כדי למנוע עיבוד כפול, יוצר התראה ושולח את פרטי האירוע גם ל-Elasticsearch. Dependency Injection יוצר את ה-Controllers, השירותים וה-DbContext ומעביר כל תלות דרך הבנאי.

