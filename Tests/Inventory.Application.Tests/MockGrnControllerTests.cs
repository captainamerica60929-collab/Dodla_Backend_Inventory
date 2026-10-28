using Inventory.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Inventory.Application.Tests;

public class MockGrnControllerTests
{
    [Fact]
    public void MockGrn_InNonDevelopment_ShouldReturnNotFound()
    {
        // Arrange
        var env = new DummyHostEnvironment { EnvironmentName = Environments.Production };
        var controller = new MockGrnController(env);

        // Act
        var result = controller.GetOrPostMockGrn(null, null, null, null, null);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void MockGrn_WithoutApiKeyHeader_ShouldReturn401Unauthorized()
    {
        // Arrange
        var env = new DummyHostEnvironment { EnvironmentName = Environments.Development };
        var controller = new MockGrnController(env)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        // Act
        var result = controller.GetOrPostMockGrn(null, null, null, null, null);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, statusResult.StatusCode);
    }

    [Fact]
    public void MockGrn_WithApiKeyHeader_ShouldReturnNestedResponseWith12Rows()
    {
        // Arrange
        var env = new DummyHostEnvironment { EnvironmentName = Environments.Development };
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = "dev-mock-key";

        var controller = new MockGrnController(env)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        // Act
        var result = controller.GetOrPostMockGrn(null, null, null, "nested", null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void MockGrn_WithPlantFilter_ShouldFilterRows()
    {
        // Arrange
        var env = new DummyHostEnvironment { EnvironmentName = Environments.Development };
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Api-Key"] = "dev-mock-key";

        var controller = new MockGrnController(env)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        // Act
        var result = controller.GetOrPostMockGrn("1000", null, null, "array", null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<List<Dictionary<string, object>>>(okResult.Value);
        Assert.All(items, item => Assert.Equal("1000", item["Plant"]));
    }
}
