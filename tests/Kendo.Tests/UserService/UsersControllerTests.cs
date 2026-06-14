using Kendo.Shared.Messaging;
using Kendo.UserService.Controllers;
using Kendo.UserService.Data;
using Kendo.UserService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Rebus.Bus;

namespace Kendo.Tests.UserService;

[Trait("Category", "Unit")]
public class UsersControllerTests
{
    /// <summary>
    /// A pass-through resilience pipeline that executes the action directly.
    /// Used in tests to avoid mocking generic method signatures.
    /// </summary>
    private class PassThroughResiliencePipeline : Kendo.Shared.Resilience.IResiliencePipeline
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
            => action(ct);
        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
            => action(ct);
    }

    private static (UsersController controller, Mock<IBus> busMock) CreateSut(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new AppDbContext(options);
        var resilientDb = new ResilientAppDbContext(db, new PassThroughResiliencePipeline());
        var repo = new UserRepository(db, resilientDb);
        var busMock = new Mock<IBus>();
        var logger = NullLogger<UsersController>.Instance;
        var controller = new UsersController(repo, busMock.Object, logger);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return (controller, busMock);
    }

    [Fact]
    public async Task Post_Returns202Accepted_WithLocationHeader()
    {
        var (controller, _) = CreateSut(nameof(Post_Returns202Accepted_WithLocationHeader));

        var result = await controller.Create(new CreateUserRequest { Email = "user@example.com", DisplayName = "Jane Doe" }, CancellationToken.None);

        var acceptedResult = Assert.IsType<AcceptedAtActionResult>(result);
        Assert.Equal(202, acceptedResult.StatusCode);
        Assert.Equal(nameof(UsersController.GetStatus), acceptedResult.ActionName);
        Assert.NotNull(acceptedResult.RouteValues);
        Assert.Contains("id", acceptedResult.RouteValues.Keys);

        var response = Assert.IsType<UserStatusResponse>(acceptedResult.Value);
        Assert.NotEqual(Guid.Empty, response.UserId);
        Assert.Equal("pending", response.Status);
        Assert.Null(response.ProcessedAt);
    }

    [Fact]
    public async Task Post_PublishesUserCreatedEvent()
    {
        var (controller, busMock) = CreateSut(nameof(Post_PublishesUserCreatedEvent));

        await controller.Create(new CreateUserRequest { Email = "event@example.com", DisplayName = "Event Test" }, CancellationToken.None);

        busMock.Verify(b => b.Send(It.Is<UserCreatedEvent>(e => e.Email == "event@example.com"), It.IsAny<IDictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task Post_WithMissingEmail_BadRequest()
    {
        var (controller, _) = CreateSut(nameof(Post_WithMissingEmail_BadRequest));
        controller.ModelState.AddModelError("Email", "Required");

        var result = await controller.Create(new CreateUserRequest { Email = "", DisplayName = "No Email" }, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task GetStatus_Returns200_WithUserStatus()
    {
        var (controller, _) = CreateSut(nameof(GetStatus_Returns200_WithUserStatus));

        var createResult = await controller.Create(new CreateUserRequest { Email = "status@example.com", DisplayName = "Status Test" }, CancellationToken.None);
        var accepted = Assert.IsType<AcceptedAtActionResult>(createResult);
        var created = Assert.IsType<UserStatusResponse>(accepted.Value);

        var result = await controller.GetStatus(created.UserId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        var response = Assert.IsType<UserStatusResponse>(okResult.Value);
        Assert.Equal("pending", response.Status);
    }

    [Fact]
    public async Task GetStatus_Returns404_ForNonExistentUser()
    {
        var (controller, _) = CreateSut(nameof(GetStatus_Returns404_ForNonExistentUser));

        var result = await controller.GetStatus(Guid.NewGuid(), CancellationToken.None);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFoundResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(notFoundResult.Value);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task Post_Returns202_WhenBusIsNull()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nameof(Post_Returns202_WhenBusIsNull))
            .Options;
        var db = new AppDbContext(options);
        var resilientDb = new ResilientAppDbContext(db, new PassThroughResiliencePipeline());
        var repo = new UserRepository(db, resilientDb);
        var logger = NullLogger<UsersController>.Instance;
        var controller = new UsersController(repo, null!, logger);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.Create(new CreateUserRequest { Email = "nobus@example.com", DisplayName = "No Bus" }, CancellationToken.None);

        var acceptedResult = Assert.IsType<AcceptedAtActionResult>(result);
        Assert.Equal(202, acceptedResult.StatusCode);
        var response = Assert.IsType<UserStatusResponse>(acceptedResult.Value);
        Assert.Equal("pending", response.Status);
    }
}
