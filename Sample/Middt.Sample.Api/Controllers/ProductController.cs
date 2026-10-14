using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Sample.Api.Model.Database;
using Middt.Sample.Api.Repository.Bike;

namespace Middt.Sample.Api.Controllers;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class ProductController(ProductRepository repository) : BaseCrudControllerWithAuth<Product, ProductRepository>(repository);
