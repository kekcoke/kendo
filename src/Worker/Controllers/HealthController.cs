using Microsoft.AspNetCore.Mvc;

namespace Kendo.Worker.Controllers;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet("live")]
    [Produces("text/plain")]
    public IActionResult Live()
    {
        return Ok("Healthy");
    }

    [HttpGet("ready")]
    [Produces("text/plain")]
    public IActionResult Ready()
    {
        return Ok("Ready");
    }
}
