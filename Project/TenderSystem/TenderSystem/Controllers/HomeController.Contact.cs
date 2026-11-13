using Microsoft.AspNetCore.Mvc;

namespace TenderSystem.Controllers;

// OWNER: Abdallah (Contact Us). Same controller as HomeController, split in its own file so there are no merge conflicts.
public partial class HomeController
{
    // GET /Home/Contact. Add the POST action (form with validation) and a ContactViewModel in Models/ when you build the page.
    public IActionResult Contact() => View();
}
