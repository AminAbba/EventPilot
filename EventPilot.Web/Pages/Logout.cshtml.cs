using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using EventPilot.Web.Helpers;

namespace EventPilot.Web.Pages;

public class LogoutModel : PageModel
{
    public IActionResult OnPost()
    {
        HttpContext.Session.ClearToken();
        HttpContext.Session.Remove("DISPLAY_NAME");
        return RedirectToPage("/Index");
    }
}
