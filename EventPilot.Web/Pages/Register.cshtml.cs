using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EventPilot.Web.Pages;

public class RegisterModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public RegisterModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [BindProperty, Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = "";

    [BindProperty, Required, StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = "";

    [BindProperty, Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";

    public string? Error { get; set; }
    public string? Success { get; set; }
    public bool IsBusy { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        IsBusy = true;

        var client = _httpClientFactory.CreateClient("LocalApi");

        var resp = await client.PostAsJsonAsync("/api/users", new
        {
            email = Email.Trim(),
            password = Password,
            fullName = FullName.Trim()
        }, ct);

        if (resp.StatusCode == HttpStatusCode.Created)
        {
            Success = "Account created successfully. You can now login.";
            return RedirectToPage("/Login");
        }

        var text = await resp.Content.ReadAsStringAsync(ct);

        Error = string.IsNullOrWhiteSpace(text)
            ? "Registration failed."
            : $"Registration failed: {text}";

        return Page();
    }
}
