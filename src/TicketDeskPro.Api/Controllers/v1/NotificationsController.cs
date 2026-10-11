using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDeskPro.Application.DTOs.Admin;
using TicketDeskPro.Application.Services.Admin;
using TicketDeskPro.Shared.Wrappers;

namespace TicketDeskPro.Api.Controllers.V1;

/// <summary>Own notification preferences (US-7.03).</summary>
[ApiController]
[Authorize]
[Route("api/v1/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationPreferenceService _preferences;

    public NotificationsController(INotificationPreferenceService preferences) => _preferences = preferences;

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct) =>
        Ok(ApiResponse<NotificationPreferenceDto>.Ok(await _preferences.GetMineAsync(ct)));

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences(NotificationPreferenceDto request, CancellationToken ct) =>
        Ok(ApiResponse<NotificationPreferenceDto>.Ok(await _preferences.UpdateMineAsync(request, ct)));
}
