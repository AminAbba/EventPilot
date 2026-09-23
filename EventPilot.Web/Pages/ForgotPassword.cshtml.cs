using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EventPilot.Web.Pages;

public class ForgotPasswordModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ForgotPasswordModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [BindProperty] public string Email { get; set; } = "";
    public string? Message { get; set; }
    public string? Error { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("LocalApi");

        var resp = await client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = Email }, ct);

        if (!resp.IsSuccessStatusCode)
        {
            Error = "Failed to send reset link.";
            return Page();
        }

        var body = await resp.Content.ReadFromJsonAsync<MessageDto>(cancellationToken: ct);
        Message = body?.Message ?? "If the email exists, a reset link has been sent.";
        return Page();
    }

    private sealed class MessageDto
    {
        public string? Message { get; set; }
    }
}
