using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Middt.Framework.Model.Workflow;
using Middt.Framework.Workflow.Persistence;

namespace Middt.Identity;

/// <summary>
/// Workflow-backed <see cref="IUserStore{TUser}"/> storing <see cref="MiddtIdentityUser"/> records as JSON inside
/// <see cref="WorkflowDbContext.Instances"/> (<c>WorkflowKey = "idp-user"</c>). No relational <c>AspNet*</c> tables are created.
/// </summary>
internal sealed class WorkflowUserStore(WorkflowDbContext db, IDataProtectionProvider protection) :
    IQueryableUserStore<MiddtIdentityUser>,
    IUserPasswordStore<MiddtIdentityUser>,
    IUserEmailStore<MiddtIdentityUser>,
    IUserRoleStore<MiddtIdentityUser>,
    IUserLockoutStore<MiddtIdentityUser>,
    IUserSecurityStampStore<MiddtIdentityUser>,
    IUserTwoFactorStore<MiddtIdentityUser>,
    IUserAuthenticatorKeyStore<MiddtIdentityUser>,
    IUserTwoFactorRecoveryCodeStore<MiddtIdentityUser>,
    IUserClaimStore<MiddtIdentityUser>,
    IUserPhoneNumberStore<MiddtIdentityUser>,
    IUserLoginStore<MiddtIdentityUser>,
    IUserAuthenticationTokenStore<MiddtIdentityUser>
{
    internal const string WorkflowKey = "idp-user";
    private const string AuthenticatorKeyPurpose = "Middt.Identity.WorkflowUserStore.AuthenticatorKey";
    private const string RecoveryCodesPurpose = "Middt.Identity.WorkflowUserStore.RecoveryCodes";
    private const string UserTokensPurpose = "Middt.Identity.WorkflowUserStore.UserTokens";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector keyProtector = protection.CreateProtector(AuthenticatorKeyPurpose);
    private readonly IDataProtector codesProtector = protection.CreateProtector(RecoveryCodesPurpose);
    private readonly IDataProtector tokensProtector = protection.CreateProtector(UserTokensPurpose);
    private readonly ConditionalWeakTable<MiddtIdentityUser, UserExtras> userExtras = new();
    private readonly Dictionary<string, UserExtras> extrasById = new(StringComparer.Ordinal);

    private sealed class UserExtras
    {
        public List<string> Roles { get; set; } = [];
        public List<ClaimRecord> Claims { get; set; } = [];
        public List<LoginRecord> Logins { get; set; } = [];
        public List<TokenRecord> Tokens { get; set; } = [];
        public string? AuthenticatorKey { get; set; }
        public List<string> RecoveryCodes { get; set; } = [];
    }

    internal sealed record ClaimRecord(string Type, string Value);

    private sealed record LoginRecord(string LoginProvider, string ProviderKey, string? ProviderDisplayName);

    private sealed record TokenRecord(string LoginProvider, string Name, string? Value);

    private sealed class UserDocument
    {
        public string Id { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string? NormalizedUserName { get; set; }
        public string? Email { get; set; }
        public string? NormalizedEmail { get; set; }
        public bool EmailConfirmed { get; set; }
        public string? PasswordHash { get; set; }
        public string? SecurityStamp { get; set; }
        public string? ConcurrencyStamp { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneNumberConfirmed { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public bool LockoutEnabled { get; set; }
        public int AccessFailedCount { get; set; }
        public string? DisplayName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
        public List<string> Roles { get; set; } = [];
        public List<ClaimRecord> Claims { get; set; } = [];
        public List<LoginRecord> Logins { get; set; } = [];
        public string? TokensProtected { get; set; }
        public string? AuthenticatorKeyProtected { get; set; }
        public string? RecoveryCodesProtected { get; set; }
    }

    public IQueryable<MiddtIdentityUser> Users
    {
        get
        {
            var instances = db.Instances.AsNoTracking()
                .Where(x => x.WorkflowKey == WorkflowKey)
                .ToList();
            var list = instances.Select(Deserialize).ToList();
            return new WorkflowAsyncQueryable<MiddtIdentityUser>(list.AsQueryable());
        }
    }

    public void Dispose()
    {
    }

    public Task<string> GetUserIdAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.Id);
    }

    public Task<string?> GetUserNameAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.UserName);
    }

    public Task SetUserNameAsync(MiddtIdentityUser user, string? userName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.NormalizedUserName);
    }

    public Task SetNormalizedUserNameAsync(MiddtIdentityUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var id = Guid.TryParse(user.Id, out var parsed) && parsed != Guid.Empty ? parsed : db.NewId();
        user.Id = id.ToString("D");
        user.ConcurrencyStamp ??= Guid.NewGuid().ToString("D");
        var now = user.CreatedAt == default ? DateTimeOffset.UtcNow : user.CreatedAt;
        user.CreatedAt = now;

        var instance = new WorkflowInstance
        {
            Id = id,
            WorkflowKey = WorkflowKey,
            WorkflowVersion = 1,
            BusinessKey = user.NormalizedUserName ?? user.Id,
            State = IsLocked(user) ? "locked" : "active",
            Status = WorkflowInstanceStatus.Active,
            StartedBy = user.NormalizedEmail,
            Data = Serialize(user, GetExtras(user)),
            Revision = 1,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Instances.Add(instance);
        await db.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var instance = await FindInstanceByIdAsync(user.Id, cancellationToken);
        if (instance is null)
        {
            return IdentityResult.Failed(new IdentityError { Code = "OptimisticConcurrencyFailure", Description = "Kullanıcı bulunamadı." });
        }

        user.ConcurrencyStamp = Guid.NewGuid().ToString("D");
        instance.BusinessKey = user.NormalizedUserName ?? user.Id;
        instance.StartedBy = user.NormalizedEmail;
        instance.State = IsLocked(user) ? "locked" : "active";
        instance.Data = Serialize(user, GetExtras(user));
        instance.Revision++;
        instance.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var instance = await FindInstanceByIdAsync(user.Id, cancellationToken);
        if (instance is not null)
        {
            db.Instances.Remove(instance);
            await db.SaveChangesAsync(cancellationToken);
        }

        extrasById.Remove(user.Id);
        return IdentityResult.Success;
    }

    public async Task<MiddtIdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var instance = await FindInstanceByIdAsync(userId, cancellationToken);
        return instance is null ? null : Deserialize(instance);
    }

    public async Task<MiddtIdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            return null;
        }

        var instance = await db.Instances
            .FirstOrDefaultAsync(x => x.WorkflowKey == WorkflowKey && x.BusinessKey == normalizedUserName, cancellationToken);
        if (instance is not null)
        {
            return Deserialize(instance);
        }

        var all = await db.Instances.Where(x => x.WorkflowKey == WorkflowKey).ToListAsync(cancellationToken);
        return all.Select(Deserialize).FirstOrDefault(u => string.Equals(u.NormalizedUserName, normalizedUserName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MiddtIdentityUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return null;
        }

        var instance = await db.Instances
            .FirstOrDefaultAsync(x => x.WorkflowKey == WorkflowKey && x.StartedBy == normalizedEmail, cancellationToken);
        if (instance is not null)
        {
            return Deserialize(instance);
        }

        var all = await db.Instances.Where(x => x.WorkflowKey == WorkflowKey).ToListAsync(cancellationToken);
        return all.Select(Deserialize).FirstOrDefault(u => string.Equals(u.NormalizedEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase));
    }

    // ───────────────────────────── Password ─────────────────────────────

    public Task SetPasswordHashAsync(MiddtIdentityUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.PasswordHash);
    }

    public Task<bool> HasPasswordAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
    }

    // ───────────────────────────── Email ─────────────────────────────

    public Task SetEmailAsync(MiddtIdentityUser user, string? email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.Email);
    }

    public Task<bool> GetEmailConfirmedAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.EmailConfirmed);
    }

    public Task SetEmailConfirmedAsync(MiddtIdentityUser user, bool confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedEmailAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.NormalizedEmail);
    }

    public Task SetNormalizedEmailAsync(MiddtIdentityUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    // ───────────────────────────── Roles ─────────────────────────────

    public async Task AddToRoleAsync(MiddtIdentityUser user, string roleName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        var canonical = await ResolveRoleNameAsync(roleName, cancellationToken);
        var extras = GetExtras(user);
        if (!extras.Roles.Any(r => string.Equals(r, canonical, StringComparison.OrdinalIgnoreCase) || string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase)))
        {
            extras.Roles.Add(canonical);
        }
    }

    public Task RemoveFromRoleAsync(MiddtIdentityUser user, string roleName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var extras = GetExtras(user);
        extras.Roles.RemoveAll(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task<IList<string>> GetRolesAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        IList<string> roles = [.. GetExtras(user).Roles];
        return Task.FromResult(roles);
    }

    public Task<bool> IsInRoleAsync(MiddtIdentityUser user, string roleName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var inRole = GetExtras(user).Roles.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(inRole);
    }

    public async Task<IList<MiddtIdentityUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .ToListAsync(cancellationToken);
        return [.. instances.Select(Deserialize).Where(u => GetExtras(u).Roles.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase)))];
    }

    private async Task<string> ResolveRoleNameAsync(string normalizedOrRawRoleName, CancellationToken cancellationToken)
    {
        var roleInstance = await db.Instances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowKey == WorkflowRoleStore.WorkflowKey && x.BusinessKey == normalizedOrRawRoleName, cancellationToken);
        if (roleInstance is not null && WorkflowRoleStore.TryGetRoleName(roleInstance.Data) is { Length: > 0 } name)
        {
            return name;
        }

        return normalizedOrRawRoleName;
    }

    // ───────────────────────────── Lockout ─────────────────────────────

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.LockoutEnd);
    }

    public Task SetLockoutEndDateAsync(MiddtIdentityUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task<bool> GetLockoutEnabledAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.LockoutEnabled);
    }

    public Task SetLockoutEnabledAsync(MiddtIdentityUser user, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    // ───────────────────────────── Security Stamp ─────────────────────────────

    public Task SetSecurityStampAsync(MiddtIdentityUser user, string stamp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.SecurityStamp);
    }

    // ───────────────────────────── Two-Factor & Authenticator Key ─────────────────────────────

    public Task SetTwoFactorEnabledAsync(MiddtIdentityUser user, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<bool> GetTwoFactorEnabledAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.TwoFactorEnabled);
    }

    public Task SetAuthenticatorKeyAsync(MiddtIdentityUser user, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        GetExtras(user).AuthenticatorKey = key;
        return Task.CompletedTask;
    }

    public Task<string?> GetAuthenticatorKeyAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(GetExtras(user).AuthenticatorKey);
    }

    public Task ReplaceCodesAsync(MiddtIdentityUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        GetExtras(user).RecoveryCodes = [.. recoveryCodes];
        return Task.CompletedTask;
    }

    public Task<bool> RedeemCodeAsync(MiddtIdentityUser user, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var redeemed = GetExtras(user).RecoveryCodes.Remove(code);
        return Task.FromResult(redeemed);
    }

    public Task<int> CountCodesAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(GetExtras(user).RecoveryCodes.Count);
    }

    // ───────────────────────────── Claims ─────────────────────────────

    public Task<IList<Claim>> GetClaimsAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        IList<Claim> claims = [.. GetExtras(user).Claims.Select(c => new Claim(c.Type, c.Value))];
        return Task.FromResult(claims);
    }

    public Task AddClaimsAsync(MiddtIdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var list = GetExtras(user).Claims;
        foreach (var claim in claims)
        {
            list.Add(new ClaimRecord(claim.Type, claim.Value));
        }

        return Task.CompletedTask;
    }

    public Task ReplaceClaimAsync(MiddtIdentityUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var list = GetExtras(user).Claims;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Type == claim.Type && list[i].Value == claim.Value)
            {
                list[i] = new ClaimRecord(newClaim.Type, newClaim.Value);
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveClaimsAsync(MiddtIdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var list = GetExtras(user).Claims;
        foreach (var claim in claims)
        {
            list.RemoveAll(c => c.Type == claim.Type && c.Value == claim.Value);
        }

        return Task.CompletedTask;
    }

    public async Task<IList<MiddtIdentityUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
    {
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .ToListAsync(cancellationToken);
        return [.. instances.Select(Deserialize).Where(u => GetExtras(u).Claims.Any(c => c.Type == claim.Type && c.Value == claim.Value))];
    }

    // ───────────────────────────── External Logins (IUserLoginStore) ─────────────────────────────

    public Task AddLoginAsync(MiddtIdentityUser user, UserLoginInfo login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(login);
        var logins = GetExtras(user).Logins;
        logins.RemoveAll(l => string.Equals(l.LoginProvider, login.LoginProvider, StringComparison.Ordinal)
                           && string.Equals(l.ProviderKey, login.ProviderKey, StringComparison.Ordinal));
        logins.Add(new LoginRecord(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName));
        return Task.CompletedTask;
    }

    public Task RemoveLoginAsync(MiddtIdentityUser user, string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        GetExtras(user).Logins.RemoveAll(l =>
            string.Equals(l.LoginProvider, loginProvider, StringComparison.Ordinal) &&
            string.Equals(l.ProviderKey, providerKey, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public Task<IList<UserLoginInfo>> GetLoginsAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        IList<UserLoginInfo> logins = [.. GetExtras(user).Logins.Select(l => new UserLoginInfo(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName))];
        return Task.FromResult(logins);
    }

    public async Task<MiddtIdentityUser?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        var instances = await db.Instances.AsNoTracking()
            .Where(x => x.WorkflowKey == WorkflowKey)
            .ToListAsync(cancellationToken);
        return instances.Select(Deserialize).FirstOrDefault(u =>
            GetExtras(u).Logins.Any(l =>
                string.Equals(l.LoginProvider, loginProvider, StringComparison.Ordinal) &&
                string.Equals(l.ProviderKey, providerKey, StringComparison.Ordinal)));
    }

    // ───────────────────────────── Authentication Tokens (IUserAuthenticationTokenStore) ─────────────────────────────

    public Task SetTokenAsync(MiddtIdentityUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var tokens = GetExtras(user).Tokens;
        tokens.RemoveAll(t => string.Equals(t.LoginProvider, loginProvider, StringComparison.Ordinal)
                           && string.Equals(t.Name, name, StringComparison.Ordinal));
        tokens.Add(new TokenRecord(loginProvider, name, value));
        return Task.CompletedTask;
    }

    public Task RemoveTokenAsync(MiddtIdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        GetExtras(user).Tokens.RemoveAll(t =>
            string.Equals(t.LoginProvider, loginProvider, StringComparison.Ordinal) &&
            string.Equals(t.Name, name, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public Task<string?> GetTokenAsync(MiddtIdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var token = GetExtras(user).Tokens.FirstOrDefault(t =>
            string.Equals(t.LoginProvider, loginProvider, StringComparison.Ordinal) &&
            string.Equals(t.Name, name, StringComparison.Ordinal));
        return Task.FromResult(token?.Value);
    }

    // ───────────────────────────── Phone Number ─────────────────────────────

    public Task SetPhoneNumberAsync(MiddtIdentityUser user, string? phoneNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<string?> GetPhoneNumberAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.PhoneNumber);
    }

    public Task<bool> GetPhoneNumberConfirmedAsync(MiddtIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Task.FromResult(user.PhoneNumberConfirmed);
    }

    public Task SetPhoneNumberConfirmedAsync(MiddtIdentityUser user, bool confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    // ───────────────────────────── Serialization Helpers ─────────────────────────────

    private async Task<WorkflowInstance?> FindInstanceByIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(userId, out var id))
        {
            return await db.Instances.FirstOrDefaultAsync(x => x.Id == id && x.WorkflowKey == WorkflowKey, cancellationToken);
        }

        var all = await db.Instances.Where(x => x.WorkflowKey == WorkflowKey).ToListAsync(cancellationToken);
        return all.FirstOrDefault(x => JsonSerializer.Deserialize<UserDocument>(x.Data, JsonOptions)?.Id == userId);
    }

    private UserExtras GetExtras(MiddtIdentityUser user)
    {
        if (userExtras.TryGetValue(user, out var existing))
        {
            return existing;
        }

        if (!string.IsNullOrEmpty(user.Id) && extrasById.TryGetValue(user.Id, out var byId))
        {
            userExtras.AddOrUpdate(user, byId);
            return byId;
        }

        var created = new UserExtras();
        userExtras.AddOrUpdate(user, created);
        if (!string.IsNullOrEmpty(user.Id))
        {
            extrasById[user.Id] = created;
        }

        return created;
    }

    private string Serialize(MiddtIdentityUser user, UserExtras extras)
    {
        var doc = new UserDocument
        {
            Id = user.Id,
            UserName = user.UserName,
            NormalizedUserName = user.NormalizedUserName,
            Email = user.Email,
            NormalizedEmail = user.NormalizedEmail,
            EmailConfirmed = user.EmailConfirmed,
            PasswordHash = user.PasswordHash,
            SecurityStamp = user.SecurityStamp,
            ConcurrencyStamp = user.ConcurrencyStamp,
            PhoneNumber = user.PhoneNumber,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            LockoutEnd = user.LockoutEnd,
            LockoutEnabled = user.LockoutEnabled,
            AccessFailedCount = user.AccessFailedCount,
            DisplayName = user.DisplayName,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Roles = [.. extras.Roles],
            Claims = [.. extras.Claims],
            Logins = [.. extras.Logins],
            TokensProtected = extras.Tokens.Count == 0 ? null : tokensProtector.Protect(JsonSerializer.Serialize(extras.Tokens, JsonOptions)),
            AuthenticatorKeyProtected = string.IsNullOrEmpty(extras.AuthenticatorKey) ? null : keyProtector.Protect(extras.AuthenticatorKey),
            RecoveryCodesProtected = extras.RecoveryCodes.Count == 0 ? null : codesProtector.Protect(string.Join(';', extras.RecoveryCodes)),
        };

        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    private MiddtIdentityUser Deserialize(WorkflowInstance instance)
    {
        var doc = JsonSerializer.Deserialize<UserDocument>(instance.Data, JsonOptions) ?? new UserDocument();
        var id = string.IsNullOrEmpty(doc.Id) ? instance.Id.ToString("D") : doc.Id;
        var user = new MiddtIdentityUser
        {
            Id = id,
            UserName = doc.UserName,
            NormalizedUserName = doc.NormalizedUserName,
            Email = doc.Email,
            NormalizedEmail = doc.NormalizedEmail,
            EmailConfirmed = doc.EmailConfirmed,
            PasswordHash = doc.PasswordHash,
            SecurityStamp = doc.SecurityStamp,
            ConcurrencyStamp = doc.ConcurrencyStamp ?? instance.Revision.ToString(CultureInfo.InvariantCulture),
            PhoneNumber = doc.PhoneNumber,
            PhoneNumberConfirmed = doc.PhoneNumberConfirmed,
            TwoFactorEnabled = doc.TwoFactorEnabled,
            LockoutEnd = doc.LockoutEnd,
            LockoutEnabled = doc.LockoutEnabled,
            AccessFailedCount = doc.AccessFailedCount,
            DisplayName = doc.DisplayName,
            CreatedAt = doc.CreatedAt == default ? new DateTimeOffset(DateTime.SpecifyKind(instance.CreatedAt, DateTimeKind.Utc)) : doc.CreatedAt,
            LastLoginAt = doc.LastLoginAt,
        };

        var extras = new UserExtras
        {
            Roles = [.. doc.Roles],
            Claims = [.. doc.Claims],
            Logins = [.. doc.Logins],
            Tokens = UnprotectOrNull(tokensProtector, doc.TokensProtected) is { Length: > 0 } tokensJson
                ? JsonSerializer.Deserialize<List<TokenRecord>>(tokensJson, JsonOptions) ?? []
                : [],
            AuthenticatorKey = UnprotectOrNull(keyProtector, doc.AuthenticatorKeyProtected),
            RecoveryCodes = UnprotectOrNull(codesProtector, doc.RecoveryCodesProtected) is { Length: > 0 } codes
                ? [.. codes.Split(';', StringSplitOptions.RemoveEmptyEntries)]
                : [],
        };

        userExtras.AddOrUpdate(user, extras);
        extrasById[id] = extras;
        return user;
    }

    private static string? UnprotectOrNull(IDataProtector protector, string? protectedData)
    {
        if (string.IsNullOrEmpty(protectedData))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(protectedData);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLocked(MiddtIdentityUser user) =>
        user.LockoutEnabled && user.LockoutEnd is { } until && until > DateTimeOffset.UtcNow;
}

/// <summary>
/// Workflow-backed <see cref="IRoleStore{TRole}"/> and <see cref="IRoleClaimStore{TRole}"/> storing <see cref="IdentityRole"/>
/// records as JSON inside <see cref="WorkflowDbContext.Instances"/> (<c>WorkflowKey = "idp-role"</c>).
/// </summary>
internal sealed class WorkflowRoleStore(WorkflowDbContext db) :
    IQueryableRoleStore<IdentityRole>,
    IRoleClaimStore<IdentityRole>
{
    internal const string WorkflowKey = "idp-role";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConditionalWeakTable<IdentityRole, List<WorkflowUserStore.ClaimRecord>> roleClaims = new();
    private readonly Dictionary<string, List<WorkflowUserStore.ClaimRecord>> claimsById = new(StringComparer.Ordinal);

    private sealed class RoleDocument
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? NormalizedName { get; set; }
        public string? ConcurrencyStamp { get; set; }
        public List<WorkflowUserStore.ClaimRecord> Claims { get; set; } = [];
    }

    public IQueryable<IdentityRole> Roles
    {
        get
        {
            var instances = db.Instances.AsNoTracking()
                .Where(x => x.WorkflowKey == WorkflowKey)
                .ToList();
            return new WorkflowAsyncQueryable<IdentityRole>(instances.Select(Deserialize).ToList().AsQueryable());
        }
    }

    public void Dispose()
    {
    }

    public async Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        var id = Guid.TryParse(role.Id, out var parsed) && parsed != Guid.Empty ? parsed : db.NewId();
        role.Id = id.ToString("D");
        role.ConcurrencyStamp ??= Guid.NewGuid().ToString("D");
        var now = DateTime.UtcNow;

        var instance = new WorkflowInstance
        {
            Id = id,
            WorkflowKey = WorkflowKey,
            WorkflowVersion = 1,
            BusinessKey = role.NormalizedName ?? role.Name ?? role.Id,
            State = "active",
            Status = WorkflowInstanceStatus.Active,
            Data = Serialize(role, GetRoleClaims(role)),
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Instances.Add(instance);
        await db.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        var instance = await FindInstanceByIdAsync(role.Id, cancellationToken);
        if (instance is null)
        {
            return IdentityResult.Failed(new IdentityError { Code = "OptimisticConcurrencyFailure", Description = "Rol bulunamadı." });
        }

        role.ConcurrencyStamp = Guid.NewGuid().ToString("D");
        instance.BusinessKey = role.NormalizedName ?? role.Name ?? role.Id;
        instance.Data = Serialize(role, GetRoleClaims(role));
        instance.Revision++;
        instance.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        var instance = await FindInstanceByIdAsync(role.Id, cancellationToken);
        if (instance is not null)
        {
            db.Instances.Remove(instance);
            await db.SaveChangesAsync(cancellationToken);
        }

        claimsById.Remove(role.Id);
        return IdentityResult.Success;
    }

    public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        return Task.FromResult(role.Id);
    }

    public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        return Task.FromResult(role.Name);
    }

    public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        return Task.FromResult(role.NormalizedName);
    }

    public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        var instance = await FindInstanceByIdAsync(roleId, cancellationToken);
        return instance is null ? null : Deserialize(instance);
    }

    public async Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedRoleName))
        {
            return null;
        }

        var instance = await db.Instances
            .FirstOrDefaultAsync(x => x.WorkflowKey == WorkflowKey && x.BusinessKey == normalizedRoleName, cancellationToken);
        if (instance is not null)
        {
            return Deserialize(instance);
        }

        var all = await db.Instances.Where(x => x.WorkflowKey == WorkflowKey).ToListAsync(cancellationToken);
        return all.Select(Deserialize).FirstOrDefault(r => string.Equals(r.NormalizedName, normalizedRoleName, StringComparison.OrdinalIgnoreCase));
    }

    public Task<IList<Claim>> GetClaimsAsync(IdentityRole role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        IList<Claim> claims = [.. GetRoleClaims(role).Select(c => new Claim(c.Type, c.Value))];
        return Task.FromResult(claims);
    }

    public Task AddClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(claim);
        var list = GetRoleClaims(role);
        if (!list.Any(c => c.Type == claim.Type && c.Value == claim.Value))
        {
            list.Add(new WorkflowUserStore.ClaimRecord(claim.Type, claim.Value));
        }

        return Task.CompletedTask;
    }

    public Task RemoveClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(claim);
        GetRoleClaims(role).RemoveAll(c => c.Type == claim.Type && c.Value == claim.Value);
        return Task.CompletedTask;
    }

    internal static string? TryGetRoleName(string json) =>
        JsonSerializer.Deserialize<RoleDocument>(json, JsonOptions)?.Name;

    private List<WorkflowUserStore.ClaimRecord> GetRoleClaims(IdentityRole role)
    {
        if (roleClaims.TryGetValue(role, out var existing))
        {
            return existing;
        }

        if (!string.IsNullOrEmpty(role.Id) && claimsById.TryGetValue(role.Id, out var byId))
        {
            roleClaims.AddOrUpdate(role, byId);
            return byId;
        }

        var created = new List<WorkflowUserStore.ClaimRecord>();
        roleClaims.AddOrUpdate(role, created);
        if (!string.IsNullOrEmpty(role.Id))
        {
            claimsById[role.Id] = created;
        }

        return created;
    }

    private async Task<WorkflowInstance?> FindInstanceByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(roleId, out var id))
        {
            return await db.Instances.FirstOrDefaultAsync(x => x.Id == id && x.WorkflowKey == WorkflowKey, cancellationToken);
        }

        var all = await db.Instances.Where(x => x.WorkflowKey == WorkflowKey).ToListAsync(cancellationToken);
        return all.FirstOrDefault(x => JsonSerializer.Deserialize<RoleDocument>(x.Data, JsonOptions)?.Id == roleId);
    }

    private static string Serialize(IdentityRole role, List<WorkflowUserStore.ClaimRecord> claims) =>
        JsonSerializer.Serialize(new RoleDocument
        {
            Id = role.Id,
            Name = role.Name,
            NormalizedName = role.NormalizedName,
            ConcurrencyStamp = role.ConcurrencyStamp,
            Claims = [.. claims],
        }, JsonOptions);

    private IdentityRole Deserialize(WorkflowInstance instance)
    {
        var doc = JsonSerializer.Deserialize<RoleDocument>(instance.Data, JsonOptions) ?? new RoleDocument();
        var id = string.IsNullOrEmpty(doc.Id) ? instance.Id.ToString("D") : doc.Id;
        var role = new IdentityRole
        {
            Id = id,
            Name = doc.Name,
            NormalizedName = doc.NormalizedName,
            ConcurrencyStamp = doc.ConcurrencyStamp ?? instance.Revision.ToString(CultureInfo.InvariantCulture),
        };
        var claims = new List<WorkflowUserStore.ClaimRecord>(doc.Claims);
        roleClaims.AddOrUpdate(role, claims);
        claimsById[id] = claims;
        return role;
    }
}

/// <summary>
/// Wraps an in-memory <see cref="IQueryable{T}"/> with <see cref="IAsyncEnumerable{T}"/> and <see cref="IAsyncQueryProvider"/>
/// so callers using EF Core async LINQ extensions (<c>CountAsync</c>, <c>LongCountAsync</c>, <c>ToListAsync</c>) work seamlessly.
/// </summary>
internal sealed class WorkflowAsyncQueryable<T>(IQueryable<T> inner) : IOrderedQueryable<T>, IAsyncEnumerable<T>
{
    public Type ElementType => inner.ElementType;

    public Expression Expression => inner.Expression;

    public IQueryProvider Provider => new WorkflowAsyncQueryProvider(inner.Provider);

    public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => inner.GetEnumerator();

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        foreach (var item in inner)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    private sealed class WorkflowAsyncQueryProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression)
        {
            var elementType = expression.Type.GetGenericArguments().FirstOrDefault() ?? typeof(object);
            var queryableType = typeof(WorkflowAsyncQueryable<>).MakeGenericType(elementType);
            return (IQueryable)Activator.CreateInstance(queryableType, inner.CreateQuery(expression))!;
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new WorkflowAsyncQueryable<TElement>(inner.CreateQuery<TElement>(expression));

        public object? Execute(Expression expression) => inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resultType = typeof(TResult);
            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var innerResultType = resultType.GetGenericArguments()[0];
                var executeMethod = typeof(IQueryProvider)
                    .GetMethods()
                    .Single(m => m.Name == nameof(IQueryProvider.Execute) && m.IsGenericMethodDefinition)
                    .MakeGenericMethod(innerResultType);
                var value = executeMethod.Invoke(inner, [expression]);
                return (TResult)typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(innerResultType)
                    .Invoke(null, [value])!;
            }

            return Execute<TResult>(expression);
        }
    }
}
