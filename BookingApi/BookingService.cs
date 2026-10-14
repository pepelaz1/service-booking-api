using System.Text.Json;

namespace BookingApi;

public record ServiceItem(string Code, string Name, int Minutes);
public static class ServiceCatalog
{
    public static readonly ServiceItem[] All =
    [
        new("diagnostics", "Диагностика", 45),
        new("oil", "Замена масла", 60),
        new("tires", "Шиномонтаж", 90)
    ];
}

public record Booking(Guid Id, string CustomerRef, string ServiceCode,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Bay);
public record CreateBookingRequest(string CustomerRef, string ServiceCode, DateTimeOffset StartsAt);
public record RescheduleBookingRequest(DateTimeOffset StartsAt);
public enum ResultKind { Created, Updated, Invalid, Conflict, NotFound }
public record BookingResult(ResultKind Kind, Booking? Value = null, string? Error = null);

public interface IBookingStore
{
    List<Booking> Load();
    void Save(IReadOnlyList<Booking> bookings);
}

public sealed class JsonBookingStore(string path) : IBookingStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public List<Booking> Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<List<Booking>>(File.ReadAllText(path), Options) ?? []
        : [];

    public void Save(IReadOnlyList<Booking> bookings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(bookings, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class BookingService
{
    private const int BayCount = 2;
    private const int BufferMinutes = 15;
    private static readonly TimeSpan TomskOffset = TimeSpan.FromHours(7);
    private readonly IBookingStore store;
    private readonly List<Booking> bookings;
    private readonly object gate = new();

    public BookingService(IBookingStore store)
    {
        this.store = store;
        bookings = store.Load();
    }

    public IReadOnlyList<Booking> ForDate(DateOnly date)
    {
        lock (gate)
            return bookings.Where(b => DateOnly.FromDateTime(b.StartsAt.ToOffset(TomskOffset).DateTime) == date)
                .OrderBy(b => b.StartsAt).ThenBy(b => b.Bay).ToArray();
    }

    public BookingResult Create(CreateBookingRequest request)
    {
        lock (gate)
        {
            var service = ServiceCatalog.All.FirstOrDefault(s => s.Code == request.ServiceCode);
            var error = Validate(request.CustomerRef, service, request.StartsAt);
            if (error is not null) return new(ResultKind.Invalid, Error: error);

            var start = request.StartsAt.ToOffset(TomskOffset);
            var end = start.AddMinutes(service!.Minutes);
            var bay = FreeBay(start, end);
            if (bay is null) return new(ResultKind.Conflict, Error: "В это время оба поста заняты.");

            var booking = new Booking(Guid.NewGuid(), request.CustomerRef.Trim(), service.Code, start, end, bay.Value);
            bookings.Add(booking);
            store.Save(bookings);
            return new(ResultKind.Created, booking);
        }
    }

    public BookingResult Reschedule(Guid id, RescheduleBookingRequest request)
    {
        lock (gate)
        {
            var old = bookings.FirstOrDefault(b => b.Id == id);
            if (old is null) return new(ResultKind.NotFound, Error: "Запись не найдена.");
            var service = ServiceCatalog.All.Single(s => s.Code == old.ServiceCode);
            var error = Validate(old.CustomerRef, service, request.StartsAt);
            if (error is not null) return new(ResultKind.Invalid, Error: error);

            var start = request.StartsAt.ToOffset(TomskOffset);
            var end = start.AddMinutes(service.Minutes);
            var bay = FreeBay(start, end, id);
            if (bay is null) return new(ResultKind.Conflict, Error: "В это время оба поста заняты.");

            var updated = old with { StartsAt = start, EndsAt = end, Bay = bay.Value };
            bookings[bookings.IndexOf(old)] = updated;
            store.Save(bookings);
            return new(ResultKind.Updated, updated);
        }
    }

    public bool Cancel(Guid id)
    {
        lock (gate)
        {
            var removed = bookings.RemoveAll(b => b.Id == id) > 0;
            if (removed) store.Save(bookings);
            return removed;
        }
    }

    private static string? Validate(string? customerRef, ServiceItem? service, DateTimeOffset start)
    {
        if (string.IsNullOrWhiteSpace(customerRef) || customerRef.Length > 80)
            return "customerRef должен содержать от 1 до 80 символов.";
        if (service is null) return "Неизвестная услуга.";
        if (start == default) return "Укажите startsAt с часовым поясом.";
        var local = start.ToOffset(TomskOffset);
        if (local.Minute % 15 != 0 || local.Second != 0 || local.Millisecond != 0)
            return "Время начала должно быть кратно 15 минутам.";
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return "Запись доступна с понедельника по пятницу.";
        if (local.TimeOfDay < TimeSpan.FromHours(9) ||
            local.AddMinutes(service.Minutes).TimeOfDay > TimeSpan.FromHours(18))
            return "Рабочие часы: 09:00–18:00 по Томску.";
        return null;
    }

    private int? FreeBay(DateTimeOffset start, DateTimeOffset end, Guid? except = null)
    {
        for (var bay = 1; bay <= BayCount; bay++)
        {
            if (!bookings.Any(b => b.Bay == bay && b.Id != except &&
                start < b.EndsAt.AddMinutes(BufferMinutes) &&
                b.StartsAt < end.AddMinutes(BufferMinutes)))
                return bay;
        }
        return null;
    }
}
