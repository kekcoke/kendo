using Kendo.UserService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Kendo.Tests.UserService;

[Trait("Category", "Unit")]
public class HealthControllerTests
{
    private readonly HealthController _controller = new();

    [Fact]
    public void Live_Returns200OkWithHealthy()
    {
        var result = _controller.Live();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        Assert.Equal("Healthy", okResult.Value);
    }

    [Fact]
    public void Ready_Returns200OkWithReady()
    {
        var result = _controller.Ready();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        Assert.Equal("Ready", okResult.Value);
    }
}
