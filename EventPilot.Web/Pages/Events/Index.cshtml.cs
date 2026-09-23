using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EventPilot.Web.Pages.Events;

public class IndexModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;

    public IndexModel(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public List<EventListItemVm> Events { get; private set; } = new();
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("LocalApi");

            var data = await client.GetFromJsonAsync<List<EventDto>>("/api/events", ct);

            Events = (data ?? new List<EventDto>())
                .Select(x => new EventListItemVm
                {
                    Id = x.Id,
                    Title = x.Title,
                    Location = x.Location,
                    StartsAtText = x.StartsAt is null
                        ? ""
                        : DateTime.SpecifyKind(x.StartsAt.Value, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private sealed class EventDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string? Location { get; set; }
        public DateTime? StartsAt { get; set; }
    }

    public sealed class EventListItemVm
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string? Location { get; set; }
        public string StartsAtText { get; set; } = "";
    }
}
