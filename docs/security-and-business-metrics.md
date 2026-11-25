# Configuration and business metrics

Secrets are supplied through environment variables or an external configuration provider (such as a vault). Docker Compose requires nonempty values in `.env` for `MSSQL_SA_PASSWORD`, `POSTGRES_PASSWORD`, `SWIFTRIDE_JWT_SECRET`, and `SWIFTRIDE_DEMO_PASSWORD`. JWT secrets require at least 32 UTF-8 bytes. The demo identity store uses the explicitly configured demo password; it is not a production identity provider.

When running with `dotnet run`, supply `ConnectionStrings__TripDbConnection` or `ConnectionStrings__PaymentDbConnection`, `Kafka__BootstrapServers=localhost:9092` for each backend, and `Jwt__Secret` for each service and `DemoIdentity__Password` for the gateway. Compose variable names are mapped to these configuration keys by Compose; ASP.NET does not automatically load `.env`.

Trip, Matching, and Payment read [kafka-topics.json](../src/Shared.Messaging/kafka-topics.json) at startup. The Kafka Admin API creates missing topics with each topic's partition count, replication factor and settings. It synchronizes the declared `configs` keys on existing topics. Changes to an existing partition count or replica assignment are administered through Kafka. Use `kafka:29092` inside Compose. The local listener binds to loopback on port `9092`. Kafka producers use the trip ID as the key for business events. The Trip and Payment outboxes retain their message IDs and mark events processed after the broker acknowledges delivery. Use consumer-group lag to measure backlog and `swiftride.consumer-failures.v1` to inspect failed deliveries.

Replica startup uses a Redis lock per service while initializing its database. The Trip and Payment outbox workers use database transaction locks to let one replica publish each polling batch. Payment applies balance changes, ledger entries and outbox events in one database transaction. Use the same Kafka consumer group for every replica of a service.

Matching checks existing driver reservations before reserving. MongoDB additionally enforces one non-null string `ReservedTripId` per driver reservation through `ux_driver_reserved_trip`. Existing databases with duplicate reservations must resolve those duplicates before index initialization can succeed. The index is created during service startup; a real MongoDB integration check is needed to validate migration against deployed data.

The `SwiftRide.Metrics` meter is already exported through OpenTelemetry:

- `swiftride.matching.results`: counter tagged with `outcome=success|failure|duplicate|cancelled`. Success means a driver offer was published, not that the driver accepted.
- `swiftride.matching.duration`: histogram in seconds for matching attempts (initial duplicate/cancelled checks excluded).
- `swiftride.payment.results`: counter tagged with `outcome=success|failure|duplicate`; each call, including a retry, is an attempt.

Calculate matching success rate as successful attempts / (successful + failed attempts), average matching duration as histogram sum / count, and payment error rate as failed attempts / (successful + failed attempts). Exclude duplicate outcomes from rate denominators. These are attempt metrics, not final per-trip settlement metrics. No rider, driver or trip identifiers are metric tags.
