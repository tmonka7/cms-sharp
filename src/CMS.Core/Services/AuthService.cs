using System.Text;
using CMS.Core.Data;
using CMS.Core.Models;

namespace CMS.Core.Services;

/// <summary>Outcome of a sign-in attempt.</summary>
public sealed class LoginResult
{
    public LoginResult(bool success, AppUser? user, string? error)
    {
        Success = success;
        User = user;
        Error = error;
    }

    public bool Success { get; }

    public AppUser? User { get; }

    public string? Error { get; }
}

/// <summary>
/// Local account authentication. Passwords are stored as PBKDF2-HMAC-SHA256
/// hashes with a per-user salt; nothing ever leaves the machine.
/// </summary>
public sealed class AuthService
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    private readonly UserRepository _users;

    public AuthService(UserRepository users) => _users = users;

    public AppUser? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser != null;

    /// <summary>The default credentials created on a fresh install.</summary>
    public const string DefaultUsername = "admin";

    public const string DefaultPassword = "admin1234";

    /// <summary>
    /// Creates the built-in administrator on a fresh install so the product is
    /// usable out of the box.
    /// </summary>
    public void EnsureSeedUser(string username = DefaultUsername, string password = DefaultPassword)
    {
        if (_users.Count() > 0)
        {
            return;
        }

        var user = new AppUser
        {
            Username = username,
            Role = UserRole.Administrator,
            Enabled = true,
            Permissions = new HashSet<string>(
                Models.Permissions.ForRole(UserRole.Administrator),
                StringComparer.OrdinalIgnoreCase)
        };

        SetPassword(user, password);
        _users.Insert(user);
    }

    public LoginResult Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new LoginResult(false, null, "Enter a username.");
        }

        var user = _users.GetByUsername(username.Trim());

        // The same message covers both a missing account and a wrong password,
        // so the form cannot be used to enumerate usernames.
        if (user == null || !Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            return new LoginResult(false, null, "Incorrect username or password.");
        }

        if (!user.Enabled)
        {
            return new LoginResult(false, null, "This account has been disabled.");
        }

        user.LastLoginUtc = DateTime.UtcNow;
        _users.TouchLogin(user.Id);
        CurrentUser = user;

        return new LoginResult(true, user, null);
    }

    public void Logout() => CurrentUser = null;

    public bool HasPermission(string permission)
        => CurrentUser != null
            && (CurrentUser.Role == UserRole.Administrator || CurrentUser.Permissions.Contains(permission));

    public void SetPassword(AppUser user, string password)
    {
        var salt = CryptoEx.RandomBytes(SaltSize);
        var hash = CryptoEx.Pbkdf2Sha256(Encoding.UTF8.GetBytes(password), salt, Iterations, KeySize);

        user.PasswordSalt = Convert.ToBase64String(salt);
        user.PasswordHash = Convert.ToBase64String(hash);
    }

    public bool ChangePassword(AppUser user, string currentPassword, string newPassword)
    {
        if (!Verify(currentPassword, user.PasswordHash, user.PasswordSalt))
        {
            return false;
        }

        SetPassword(user, newPassword);
        _users.Update(user);
        return true;
    }

    private static bool Verify(string password, string storedHash, string storedSalt)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(storedSalt);
            var expected = Convert.FromBase64String(storedHash);
            var actual = CryptoEx.Pbkdf2Sha256(
                Encoding.UTF8.GetBytes(password ?? string.Empty),
                salt,
                Iterations,
                expected.Length);

            return CryptoEx.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
