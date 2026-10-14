using BookingApi;

var temp = Path.Combine(Path.GetTempPath(), "booking-api-test-" + Guid.NewGuid().ToString("N"), "bookings.json");
var store = new JsonBookingStore(temp);
var api = new BookingService(store);
var atNine = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(7));

void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var first = api.Create(new("A-001", "oil", atNine));
var second = api.Create(new("B-002", "oil", atNine));
Assert(first.Kind == ResultKind.Created && first.Value?.Bay == 1, "Первый пост");
Assert(second.Kind == ResultKind.Created && second.Value?.Bay == 2, "Второй пост");
Assert(api.Create(new("C-003", "oil", atNine)).Kind == ResultKind.Conflict, "Запрет двойной записи");
Assert(api.Create(new("C-003", "oil", atNine.AddMinutes(60))).Kind == ResultKind.Conflict,
    "Буфер 15 минут");
Assert(api.Create(new("C-003", "oil", atNine.AddMinutes(75))).Kind == ResultKind.Created,
    "Запись после буфера");
Assert(api.Create(new("D-004", "unknown", atNine)).Kind == ResultKind.Invalid, "Проверка услуги");
Assert(api.Create(new("D-004", "oil", atNine.AddHours(8).AddMinutes(15))).Kind == ResultKind.Invalid,
    "Проверка рабочих часов");
Assert(api.Reschedule(first.Value!.Id, new(atNine.AddMinutes(75))).Kind == ResultKind.Updated,
    "Перенос записи");
Assert(api.Cancel(second.Value!.Id), "Отмена записи");
Assert(api.ForDate(new(2026, 10, 19)).Count == 2, "Список на день");
Assert(new BookingService(new JsonBookingStore(temp)).ForDate(new(2026, 10, 19)).Count == 2,
    "Данные сохраняются между запусками");

var parallelStore = Path.Combine(Path.GetDirectoryName(temp)!, "parallel.json");
var parallelApi = new BookingService(new JsonBookingStore(parallelStore));
var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() =>
    parallelApi.Create(new($"P-{i}", "diagnostics", atNine)))));
Assert(results.Count(r => r.Kind == ResultKind.Created) == 2,
    "Параллельные запросы не занимают один пост дважды");

Directory.Delete(Path.GetDirectoryName(temp)!, true);
Console.WriteLine("Все проверки пройдены.");
