using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Web.Helpers;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStreamIoT.Web.Controllers;

/// <summary>Admin: who can sign in and with which role (Farmer, Agronomist, Admin).</summary>
[Authorize(Roles = UserRoles.Admin)]
public sealed class UsersController : Controller
{
    private readonly IUserRepository _users;

    public UsersController(IUserRepository users) => _users = users;

    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new UsersViewModel
        {
            Users = await _users.GetAllAsync(cancellationToken),
            CurrentUserId = User.UserId()
        });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(int id, string role, CancellationToken cancellationToken)
    {
        var canonical = UserRoles.Normalise(role);
        var user = await _users.GetAsync(id, cancellationToken);
        if (user is null || canonical is null)
        {
            TempData["Error"] = "Choose a user and a valid role.";
            return RedirectToAction(nameof(Index));
        }

        if (user.Role == UserRoles.Admin && canonical != UserRoles.Admin && await _users.CountActiveAdminsAsync(cancellationToken) <= 1)
        {
            TempData["Error"] = "Keep at least one active admin: promote someone else first.";
            return RedirectToAction(nameof(Index));
        }

        await _users.SetRoleAsync(id, canonical, cancellationToken);
        TempData["Success"] = $"{user.FullName} is now {(canonical == UserRoles.Admin ? "an" : "a")} {canonical}. The change applies at their next sign-in.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool active, CancellationToken cancellationToken)
    {
        var user = await _users.GetAsync(id, cancellationToken);
        if (user is null) return NotFound();

        if (!active && user.UserId == User.UserId())
        {
            TempData["Error"] = "You can't deactivate your own account.";
            return RedirectToAction(nameof(Index));
        }

        if (!active && user.Role == UserRoles.Admin && await _users.CountActiveAdminsAsync(cancellationToken) <= 1)
        {
            TempData["Error"] = "Keep at least one active admin.";
            return RedirectToAction(nameof(Index));
        }

        await _users.SetActiveAsync(id, active, cancellationToken);
        TempData["Success"] = active ? $"{user.FullName} can sign in again." : $"{user.FullName} can no longer sign in.";
        return RedirectToAction(nameof(Index));
    }
}
