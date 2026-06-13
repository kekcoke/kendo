# Skill: Resilience Pattern Implementation (.NET / Polly)

## Objective
Wrap all volatile external calls (databases, downstream APIs, caches) in protective fault-tolerance policies using the `Polly` NuGet package.

## Execution Directives
1. **Dependency:** Ensure `Polly` and `Microsoft.Extensions.Http.Polly` are added to the `.csproj`.
2. **Circuit Breakers:** Implement `IAsyncPolicy` circuit breakers in the `Program.cs` (or `Startup.cs`) dependency injection container. Configure it to break the circuit after 3 consecutive failures, with a 30-second duration of break.
3. **Retries with Jitter:** Implement exponential backoff for transient HTTP faults (HTTP 5xx or 408) using Polly's `WaitAndRetryAsync`, adding jitter to prevent thundering herd problems.
4. **Async Offloading:** For intra-service communication that does not require an immediate synchronous response, bypass Polly and push a message to RabbitMQ/Azure Service Bus instead.