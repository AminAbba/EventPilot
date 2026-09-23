using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using EventPilot.Web.Helpers;

namespace EventPilot.Web.Pages;

public class LoginModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public LoginModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("LocalApi");

        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = Email, password = Password }, ct);

        if (!resp.IsSuccessStatusCode)
        {
            Error = "Invalid credentials.";
            return Page();
        }

        var result = await resp.Content.ReadFromJsonAsync<LoginResultDto>(cancellationToken: ct);

        if (result is null || !result.Succeeded || string.IsNullOrWhiteSpace(result.AccessToken))
        {
            Error = result?.Error ?? "Login failed.";
            return Page();
        }

        HttpContext.Session.SetToken(result.AccessToken);

        client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);

        var me = await client.GetFromJsonAsync<MeDto>("/api/users/me", ct);
        if (me != null)
        {
            HttpContext.Session.SetString("DISPLAY_NAME", me.DisplayName);
        }

        return RedirectToPage("/Index");
    }

    private sealed class LoginResultDto
    {
        public bool Succeeded { get; set; }
        public string? AccessToken { get; set; }
        public string? Error { get; set; }
        public int? UserId { get; set; }
        public string? Email { get; set; }
    }

    private sealed class MeDto
    {
        public string DisplayName { get; set; } = "";
    }
}
