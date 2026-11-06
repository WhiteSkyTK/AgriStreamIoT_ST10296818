using System.Security.Claims;
using AgriStreamIoT.Shared.Constants;
using AgriStreamIoT.Web.Helpers;
using AgriStreamIoT.Web.Interfaces;
using AgriStreamIoT.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AgriStreamIoT.Web.Controllers;

/// <summary>Sign-in, registration and profile. Users and roles live in Azure SQL (dbo.Users).</summary>
public sealed class AccountController : Controller
{
    private readonly IUserRepository _users;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IUserRepository users, ILogger<AccountController> logger)
    {
        _users = users;
        _logger = logger;
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        AppUser? user;
        try
        {
            user = await _users.FindByEmailAsync(model.Email, cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "Sign-in failed: Azure SQL unavailable.");
            ModelState.AddModelError(string.Empty, ex.Number is 40613 or 40197 or 40501
                ? "The database is waking up (Azure SQL serverless). Try again in 30–60 seconds."
                : "The database cannot be reached right now. Check the FarmSql connection string and firewall.");
            return View(model);
        }

        if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "That email and password don't match an account.");
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account is deactivated. Ask a farm administrator to reactivate it.");
            return View(model);
        }

        await SignInAsync(user, model.RememberMe);
        await _users.RecordLoginAsync(user.UserId, cancellationToken);
        _logger.LogInformation("Sign-in: {Email} ({Role}).", user.Email, user.Role);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        return View(new RegisterViewModel());
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        // Admins are promoted by another admin, never self-registered.
        var role = UserRoles.SelfRegister.FirstOrDefault(r => string.Equals(r, model.Role, StringComparison.OrdinalIgnoreCase));
        if (role is null) ModelState.AddModelError(nameof(model.Role), "Choose Farmer or Agronomist.");
        if (!ModelState.IsValid) return View(model);

        if (await _users.FindByEmailAsync(model.Email, cancellationToken) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists. Sign in instead.");
            return View(model);
        }

        var user = new AppUser
        {
            FullName = model.FullName,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            PasswordHash = PasswordHasher.Hash(model.Password),
            Role = role!,
            IsActive = true
        };

        try
        {
            user.UserId = await _users.CreateAsync(user, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists. Sign in instead.");
            return View(model);
        }

        await SignInAsync(user, rememberMe: false);
        _logger.LogInformation("New {Role} registered: {Email}.", user.Role, user.Email);
        TempData["Success"] = $"Welcome to AgriStream, {user.FirstName}. Your {user.Role.ToLowerInvariant()} account is ready.";
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return await SignOutAndGoHome();
        ViewBag.Password = new ChangePasswordViewModel();
        return View(ToSettings(user));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(SettingsViewModel model, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return await SignOutAndGoHome();

        if (!ModelState.IsValid)
        {
            ViewBag.Password = new ChangePasswordViewModel();
            var reshow = ToSettings(user);
            reshow.FullName = model.FullName;
            reshow.PhoneNumber = model.PhoneNumber;
            return View(reshow);
        }

        await _users.UpdateProfileAsync(user.UserId, model.FullName, model.PhoneNumber, cancellationToken);
        user.FullName = model.FullName.Trim();
        await SignInAsync(user, rememberMe: false);   // refresh the name shown in the header
        TempData["Success"] = "Profile saved.";
        return RedirectToAction(nameof(Settings));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null) return await SignOutAndGoHome();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault()
                                ?? "Check the password fields.";
            return RedirectToAction(nameof(Settings));
        }

        if (!PasswordHasher.Verify(model.CurrentPassword, user.PasswordHash))
        {
            TempData["Error"] = "Your current password is not correct.";
            return RedirectToAction(nameof(Settings));
        }

        await _users.UpdatePasswordAsync(user.UserId, PasswordHasher.Hash(model.NewPassword), cancellationToken);
        TempData["Success"] = "Password changed.";
        return RedirectToAction(nameof(Settings));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    // ── helpers ──────────────────────────────────────────────────────

    private async Task SignInAsync(AppUser user, bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.GivenName, user.FullName),
            new(ClaimTypes.Role, user.Role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = rememberMe, AllowRefresh = true });
    }

    private async Task<AppUser?> CurrentUserAsync(CancellationToken cancellationToken)
    {
        var id = User.UserId();
        return id is null ? null : await _users.GetAsync(id.Value, cancellationToken);
    }

    private async Task<IActionResult> SignOutAndGoHome()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    private static SettingsViewModel ToSettings(AppUser user) => new()
    {
        FullName = user.FullName,
        PhoneNumber = user.PhoneNumber,
        Email = user.Email,
        Role = user.Role,
        CreatedAtUtc = user.CreatedAtUtc,
        LastLoginUtc = user.LastLoginUtc
    };
}
