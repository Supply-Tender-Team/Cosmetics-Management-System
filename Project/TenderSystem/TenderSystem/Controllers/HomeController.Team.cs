using Microsoft.AspNetCore.Mvc;

namespace TenderSystem.Controllers;

public partial class HomeController
{
    public IActionResult Team() => View();
}
