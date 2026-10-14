using BookingApi;

var builder = WebApplication.CreateBuilder(args);
var dataPath = Environment.GetEnvironmentVariable("BOOKING_DATA")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "bookings.json");
builder.Services.AddSingleton(new BookingService(new JsonBookingStore(dataPath)));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/services", () => Results.Ok(ServiceCatalog.All));
app.MapGet("/bookings", (DateOnly? date, BookingService service) =>
    date is null ? Results.BadRequest(new { error = "Укажите date=YYYY-MM-DD." })
                 : Results.Ok(service.ForDate(date.Value)));

app.MapPost("/bookings", (CreateBookingRequest request, BookingService service) =>
{
    var result = service.Create(request);
    return result.Kind switch
    {
        ResultKind.Created => Results.Created($"/bookings/{result.Value!.Id}", result.Value),
        ResultKind.Conflict => Results.Conflict(new { error = result.Error }),
        _ => Results.BadRequest(new { error = result.Error })
    };
});

app.MapPatch("/bookings/{id:guid}", (Guid id, RescheduleBookingRequest request, BookingService service) =>
{
    var result = service.Reschedule(id, request);
    return result.Kind switch
    {
        ResultKind.Updated => Results.Ok(result.Value),
        ResultKind.NotFound => Results.NotFound(new { error = result.Error }),
        ResultKind.Conflict => Results.Conflict(new { error = result.Error }),
        _ => Results.BadRequest(new { error = result.Error })
    };
});

app.MapDelete("/bookings/{id:guid}", (Guid id, BookingService service) =>
    service.Cancel(id) ? Results.NoContent() : Results.NotFound());

app.Run();
