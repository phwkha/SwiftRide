# Configuration and business metrics

Secrets are supplied through environment variables or an external configuration provider (such as a vault). They have no built-in defaults. Docker Compose requires nonempty values in `.env` for `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD`, `RABBITMQ_DEFAULT_PASS`, `SWIFTRIDE_JWT_SECRET`, and `SWIFTRIDE_DEMO_PASSWORD`. JWT secrets require at least 32 UTF-8 bytes. The demo identity store uses the explicitly configured demo password; it is not a production identity provider.

When running with `dotnet run`, supply `ConnectionStrings__TripDbConnection` or `ConnectionStrings__PaymentDbConnection`, `ConnectionStrings__RabbitMq` for each backend, and `Jwt__Secret` / `DemoIdentity__Password` for the gateway. Compose variable names are mapped to these configuration keys by Compose; ASP.NET does not automatically load `.env`.

Matching checks existing driver reservations before reserving. MongoDB additionally enforces one non-null string `ReservedTripId` per driver reservation through `ux_driver_reserved_trip`. Existing databases with duplicate reservations must resolve those duplicates before index initialization can succeed. The index is created during service startup; a real MongoDB integration check is needed to validate migration against deployed data.

The `SwiftRide.Metrics` meter is already exported through OpenTelemetry:

- `swiftride.matching.results`: counter tagged with `outcome=success|failure|duplicate|cancelled`. Success means a driver offer was published, not that the driver accepted.
- `swiftride.matching.duration`: histogram in seconds for matching attempts (initial duplicate/cancelled checks excluded).
- `swiftride.payment.results`: counter tagged with `outcome=success|failure|duplicate`; each call, including a retry, is an attempt.

Calculate matching success rate as successful attempts / (successful + failed attempts), average matching duration as histogram sum / count, and payment error rate as failed attempts / (successful + failed attempts). Exclude duplicate outcomes from rate denominators. These are attempt metrics, not final per-trip settlement metrics. No rider, driver or trip identifiers are metric tags.
