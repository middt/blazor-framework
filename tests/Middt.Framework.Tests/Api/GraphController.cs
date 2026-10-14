using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Middt.Framework.Api;
using Middt.Framework.Common.Model.Data;

namespace Middt.Framework.Tests.Api;

// A chain of types with bidirectional navigations, shaped like scaffolded EF entity graphs
// (Category -> Product -> Brand -> ... -> Category). Regression guard for OpenAPI schema depth.
#pragma warning disable CA1002, CA2227
public class GraphNode1
{
    public int Id { get; set; }
    public GraphNode2? Next { get; set; }
    public List<GraphNode12> Back { get; set; } = [];
}

public class GraphNode2
{
    public int Id { get; set; }
    public GraphNode3? Next { get; set; }
    public List<GraphNode1> Back { get; set; } = [];
}

public class GraphNode3
{
    public int Id { get; set; }
    public GraphNode4? Next { get; set; }
    public List<GraphNode2> Back { get; set; } = [];
}

public class GraphNode4
{
    public int Id { get; set; }
    public GraphNode5? Next { get; set; }
    public List<GraphNode3> Back { get; set; } = [];
}

public class GraphNode5
{
    public int Id { get; set; }
    public GraphNode6? Next { get; set; }
    public List<GraphNode4> Back { get; set; } = [];
}

public class GraphNode6
{
    public int Id { get; set; }
    public GraphNode7? Next { get; set; }
    public List<GraphNode5> Back { get; set; } = [];
}

public class GraphNode7
{
    public int Id { get; set; }
    public GraphNode8? Next { get; set; }
    public List<GraphNode6> Back { get; set; } = [];
}

public class GraphNode8
{
    public int Id { get; set; }
    public GraphNode9? Next { get; set; }
    public List<GraphNode7> Back { get; set; } = [];
}

public class GraphNode9
{
    public int Id { get; set; }
    public GraphNode10? Next { get; set; }
    public List<GraphNode8> Back { get; set; } = [];
}

public class GraphNode10
{
    public int Id { get; set; }
    public GraphNode11? Next { get; set; }
    public List<GraphNode9> Back { get; set; } = [];
}

public class GraphNode11
{
    public int Id { get; set; }
    public GraphNode12? Next { get; set; }
    public List<GraphNode10> Back { get; set; } = [];
}

public class GraphNode12
{
    public int Id { get; set; }
    public GraphNode1? Next { get; set; }
    public List<GraphNode11> Back { get; set; } = [];
}
#pragma warning restore CA1002, CA2227

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class GraphController : BaseController
{
    [HttpGet("root")]
    public ActionResult<BaseResponseDataModel<GraphNode1>> Root() => BaseResponseDataModel<GraphNode1>.Success(new GraphNode1 { Id = 1 });
}
