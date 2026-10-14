# Playbook: add a CRUD entity end to end

Example: a `Brand` screen for the bike store, in both frontends, without writing UI code.

## 1. API (demo/server/Middt.Sample.Api)
1. Entity in `Model/Database` (EF scaffolded partial + `…Extend.cs` with `[NotMapped]`/`[Query*]` search properties;
   search-only properties need `[QueryIsNoFilter]` or a `Query*` attribute). Register in the `BikeStoresContext`.
2. Repository `Repository/Bike/BrandRepository : BaseCrudRepository<Brand, BikeStoresContext>`; register in `Program.cs`.
   Views/keyless entities: override `DefaultSortProperty`.
3. Controller `BrandController(BrandRepository repository) : SampleCrudController<Brand, BrandRepository>(repository)`
   with `[Route("api/v{version:apiVersion}/[controller]")]` and `[ApiVersion("1.0")]`. Add `AuthorizeAsync` /
   `ApplyAccessFilter` overrides if rows belong to users/tenants.
4. Tests (framework behaviour is covered; add tests only for custom logic).

## 2. Screen = JSON layout (no frontend code)
1. Copy `demo/server/Middt.Sample.Api/Layouts/bike-category.json` to `bike-brand.json`; keep `"$schema"` for completion.
2. Set `key`, `title`, `menu` (title, Material icon, order), the `grid` `entity: "Brand"`, `columns`, `search`, `form`
   (field types: text, textarea, number, integer, date, datetime, checkbox, select (options/lookup), picker).
3. Restart the API (seeds are validated on start) or create it at runtime in the editor (`/ui-admin`).
4. It appears in **both** menus (Blazor :8080, Angular :8081) at `/ui/bike-brand`.

## 3. Optional typed .NET client
Only if .NET code calls the API directly: `BrandService : BaseCrudApiClient<Brand>` in `demo/shared/Middt.Sample.Common`
and `builder.Services.AddMiddtApiClient<BrandService>()`.

## 4. Verify
`/verify` (or the commands in AGENTS.md), then open both frontends: list, search, create (validation messages), edit,
delete (needs role `manager`).
