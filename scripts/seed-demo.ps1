param(
    [string]$BaseUrl = 'http://localhost:5080',
    [string]$Date = '2026-10-19'
)

$existing = @(Invoke-RestMethod "$BaseUrl/bookings?date=$Date")
foreach ($reference in @('DEMO-001', 'DEMO-002')) {
    if ($existing.customerRef -contains $reference) { continue }
    $body = @{
        customerRef = $reference
        serviceCode = 'oil'
        startsAt = "${Date}T10:00:00+07:00"
    } | ConvertTo-Json
    Invoke-RestMethod "$BaseUrl/bookings" -Method Post -ContentType 'application/json' -Body $body | Out-Null
}

Invoke-RestMethod "$BaseUrl/bookings?date=$Date" | Format-Table customerRef, serviceCode, startsAt, bay
