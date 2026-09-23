using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EventPilot.Web.Pages;

public class ResetPasswordModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ResetPasswordModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [BindProperty(SupportsGet = true)] public string Email { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";

    [BindProperty] public string NewPassword { get; set; } = "";

    public bool InvalidLink => string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token);
    public string? Message { get; set; }
    public string? Error { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (InvalidLink)
        {
            Error = "Invalid reset link.";
            return Page();
        }

        var client = _httpClientFactory.CreateClient("LocalApi");

        var resp = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { email = Email, token = Token, newPassword = NewPassword }, ct);

        if (!resp.IsSuccessStatusCode)
        {
            Error = "Reset failed.";
            return Page();
        }

        var body = await resp.Content.ReadFromJsonAsync<MessageDto>(cancellationToken: ct);
        Message = body?.Message ?? "Password has been reset.";
        return Page();
    }

    private sealed class MessageDto
    {
        public string? Message { get; set; }
    }
}
