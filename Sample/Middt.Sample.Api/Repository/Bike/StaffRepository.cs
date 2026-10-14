using Middt.Framework.Common.Database;
using Middt.Sample.Api.Model.Database;

namespace Middt.Sample.Api.Repository.Bike;

public class StaffRepository(BikeStoresContext context) : BaseCrudRepository<Staff, BikeStoresContext>(context);
