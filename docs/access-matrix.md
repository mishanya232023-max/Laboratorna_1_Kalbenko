# Матриця доступу до ресурсу Incident (baseline 2-A, ЛР 4)

Рішення сервера приймається для комбінації **суб'єкт + дія + ресурс + контекст**.
Суб'єкт береться лише з verified `principal` (`HttpContext.User`), а не з body, URL, query чи заголовків.
Наявність cookie і валідний login не дають права на довільний identifier.

Ролі: `Reporter` (звичайний користувач), `Analyst` (привілейована), `Administrator`
(у цьому baseline не має права на зміст інцидентів «за замовчуванням»; поводиться як `Reporter`).

## Таблиця

| Дія | Анонім | Reporter | Analyst | Administrator | Власник має значення? | Реалізація / тест |
|---|---|---|---|---|---|---|
| Перегляд списку `GET /api/incidents` | deny, 401 | allow лише власні | allow усі (для аналізу) | allow лише власні | так для Reporter/Administrator | `IncidentQueries.GetListAsync` + `VisibleTo`; A-05 |
| Перегляд деталей `GET /api/incidents/{id}` | deny, 401 | owner — allow; non-owner — deny, 404 | allow для аналізу | owner — allow; non-owner — deny, 404 | так | `GetDetailsAsync` + `VisibleTo`; A-02, A-03, A-03R |
| Створення `POST /api/incidents` | deny, 401 | allow for self | allow | allow | сервер призначає власника з principal | успадковано з ЛР 3 |
| Update content `PUT /api/incidents/{id}` | deny, 401 | owner — allow; non-owner — deny, 404 | deny, 403 (читання не дає права змінювати) | owner — allow; non-owner — 404 | так | `IncidentCommands.UpdateContentAsync`; A-03R, A-04, A-08a |
| Delete `DELETE /api/incidents/{id}` | deny, 401 | owner — allow лише при status New; інакше 409; non-owner — 404 | deny, 403 | як Reporter | так | `IncidentCommands.DeleteAsync`; A-03R, A-04 |
| Triage `POST /api/incidents/{id}/triage` (New → Triaged) | deny, 401 | deny, 403 (non-owner — 404) | allow лише для New, інакше 409 | deny, 403 (non-owner — 404) | ні для Analyst | `IncidentCommands.TriageAsync`; A-06, A-07, A-09 |
| Зміна severity/status через звичайний update | deny, 401 | deny (поля відсутні в DTO) | deny | deny | — | `UpdateIncidentRequest`; A-08a |
| Child `GET /api/incident-comments/{id}` (звичайний коментар) | deny, 401 | allow, якщо parent incident доступний | allow | як Reporter | так (через parent) | `GetCommentAsync`; A-12 |
| Child: internal comment | deny, 401 | deny, 404 | allow після перевірки parent | deny, 404 | ні | `GetCommentAsync`; A-12 |

## Політика відповідей

| Стан запиту | Відповідь |
|---|---|
| Клієнт не автентифікований | `401 Unauthorized` (без HTML redirect) |
| Ресурс невідомий **або** чужий і невидимий для суб'єкта | `404 Not Found` — однаковий status, `Content-Type` і body (`Problem Details`, відрізняється лише `traceId`) |
| Ресурс видимий (Analyst), але дія заборонена ролі/правилу | `403 Forbidden` + audit-подія `authorization.denied` |
| Дія дозволена ролі, але не підходить поточному стану (не New) | `409 Conflict` |

Обґрунтування 404: сам факт існування чужого інциденту зменшує видимість ресурсу для зловмисника
(не можна перебирати identifier і відрізняти «існує, але чужий» від «не існує»).
Query scoping за власником природно реалізує цю політику для details, update, delete, triage і child.
Policy 404 застосовується однаково до всіх цих маршрутів. `403` повертається лише коли суб'єкт
законно бачить ресурс (Analyst) і йому відмовлено у привілейованій дії.

## Серверні рішення

- Власник береться з `ClaimTypes.NameIdentifier` у `HttpContext.User` (`IncidentActor.From`).
- Правила зібрані в одному місці: `IncidentAccess` (видимість `VisibleTo`, `CanEditContent`, `CanDelete`, `CanTriage`).
- `UpdateIncidentRequest` містить лише `Title` і `Description`; `OwnerUserId`, role, status у контракті немає.
- Наступний статус при triage визначає сервер; клієнт його не передає.
- Audit denied-події (`authorization.denied`) містять лише server-verified subject id, дію, id ресурсу, серверний час і trace id.

## Залишковий ризик

`GET /api/incidents/severity-summary` вимагає автентифікації, але повертає агреговані лічильники по всіх
інцидентах без обмеження за власником, а `GET /api/incidents/search` (ЛР 2) не входить до групи, яку
захищено в ЛР 4. Ці шляхи належать до відмінного рівня (A-11) і в цій роботі не змінювалися.
CSRF для cookie-authenticated змінювальних endpoint-ів залишається задокументованим боргом (ЛР 6).
