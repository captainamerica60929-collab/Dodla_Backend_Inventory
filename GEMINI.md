# Backend (.NET 8, Clean Architecture) — scope: Backend/Inventory

Applies when working in this folder. Shared rules are in the parent GEMINI.md.
Use the `dotnet-clean-architecture`, `wms-domain-rules` and `sap-integration` skills.

## Commands (run from this folder)
- Build:  dotnet build
- Test:   dotnet test
- Run:    dotnet run --project Inventory
- Migrations: dotnet ef migrations add <Name> --project Inventory.Infrastructure --startup-project Inventory
  (review the generated migration before applying; never drop data without asking)

## Project map
- Domain: Inventory.Domain
- Application: Inventory.Application
- Infrastructure: Inventory.Infrastructure
- Api: Inventory
- Tests: Tests (to be added alongside use cases)

## Local rules
- Dependencies point inward only. No business logic in controllers.
- EF Core 8.x for writes, state transitions, and migrations; Dapper strictly for report queries.
- No MediatR; use direct command/query services for lightweight and fast execution.
- Connection string (`ConnectionStrings:Default`) and API secrets via `dotnet user-secrets`. Never write passwords, keys, or secrets into files or output.
- Enforce branch scoping using EF Core Global Query Filter on `BranchId` via `ICurrentUser` (cross-branch reports require Admin).
- Concurrency token (`rowversion`) on `StockRecord` and `Location`; conflicts return HTTP 409 ProblemDetails.
- CORS: allow only the Angular dev origin (http://localhost:4200) in Development.
- Keep Swagger on in Development. Return ProblemDetails for errors.
- Do not touch anything under ../../Frontend.
