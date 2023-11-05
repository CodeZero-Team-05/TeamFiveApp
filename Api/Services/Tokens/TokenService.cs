using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TeamFive.DataStorage;
using TeamFive.DataTransfer.Tokens;
using TeamFive.Models;

namespace TeamFive.Services.Tokens;
public class TokenService : ITokenService
{
    private readonly DBContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<TokenService> _logger;

    public TokenService(DBContext context, IConfiguration config, ILogger<TokenService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    static string GenerateRefreshToken()
    {
        using var rng = RandomNumberGenerator.Create();
        byte[] byteToken = new byte[32]; //128 bit
        rng.GetBytes(byteToken);
        return Convert.ToBase64String(byteToken);
    }

    public string GenerateAccessToken(User user)
    {
        string? encKey = _config["Jwt:SecretKey"];
        if (string.IsNullOrEmpty(encKey) || encKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt Secret is Invalid or Missing.");
        }

        byte[] key = Encoding.ASCII.GetBytes(encKey);

        List<Claim> claims = new()
        {
            new(ClaimTypes.Name, Convert.ToString(user.UserId)),
            new Claim("roles", Convert.ToString((int)user.Role!.RoleType)!)
        };

        JwtSecurityTokenHandler handler = new();
        SecurityTokenDescriptor tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Audience = _config["Jwt:Audience"],
            Issuer = _config["Jwt:Issuer"],
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256Signature)
        };
        SecurityToken token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    public async Task<TokensDto?> CreateTokensDtoAsync(int userId)
    {
        RefreshToken rft = new()
        {
            Value = GenerateRefreshToken(),
            UserId = userId
        };

        User? user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null || user.Role == null)
        {
            return null;
        }

        try
        {
            string jwt = GenerateAccessToken(user);
            await DeactivateTokensForUserAsync(userId);
            await _context.RefreshTokens.AddAsync(rft);
            await _context.SaveChangesAsync();
            return new TokensDto(rft, jwt);
        }
        catch (Exception e)
        {
            _logger.LogError("{Message}", e.Message);
            return null;
        }
    }

    public async Task<bool> DeactivateTokensForUserAsync(int id)
    {
        var tokens = await _context.RefreshTokens
            .Where(t => t.UserId == id)
            .ToListAsync();
        try
        {
            foreach (var t in tokens)
            {
                t.IsActive = false;
                t.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError("Error deactivating fetched tokens for user. {Message}", e.Message);
            return false;
        }
    }

    public async Task<TokensDto?> DoRefreshActionAsync(RefreshRequestDto refreshRequest)
    {
        RefreshToken? check = await _context.RefreshTokens
            .Include(t=>t.User)
            .Where(t => t.Value == refreshRequest.Token)
            .FirstOrDefaultAsync();
        if (check == null || check.User == null)
        {
            return null;
        }
        TokensDto? tokensDto = await CreateTokensDtoAsync(check.User.UserId);
        if (tokensDto == null)
        {
            return null;
        }
        return tokensDto;
    }

    public int GetIdClaimFromHeaderValue(HttpRequest request)
    {
        AuthenticationHeaderValue? header = null;
        try
        {
            header = AuthenticationHeaderValue.Parse(request.Headers["Authorization"]);
        }
        catch (FormatException)
        {
            Console.WriteLine("Authorization header is malformed.");
            return -1;
        }
        catch (ArgumentNullException)
        {
            Console.WriteLine("Authorization header is missing.");
            return -1;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return -1;
        }
        string? token = ExtractTokenFromHeaders(header);
        if (token == null)
        {
            return -1;
        }
        int? claim = GetIdClaimFromAccessToken(token);
        if (claim == null)
        {
            return -1;
        }
        return (int)claim;
    }

    static string? ExtractTokenFromHeaders(AuthenticationHeaderValue header)
    {
        if (string.IsNullOrEmpty(header.Parameter))
        {
            return null;
        }
        return header.Parameter;
    }

    static int? GetIdClaimFromAccessToken(string jwt)
    {
        JwtSecurityTokenHandler handler = new();
        JwtSecurityToken? token = handler.ReadJwtToken(jwt);
        if (token == null)
        {
            Console.WriteLine("Handler failed to read token in GetClaimFromAccessToken");
            return null;
        }
        var claims = token.Claims;
        Claim? userIdClaim = claims.Where(c => c.Type == "unique_name").FirstOrDefault();
        if (userIdClaim == null)
        {
            Console.WriteLine("Failed to extract userIdClaim from token.");
            return null;
        }
        if (int.TryParse(userIdClaim.Value, out int id))
        {
            return id;
        }
        else
        {
            Console.WriteLine("Failed to parse int from claim.value");
            return null;
        }
    }

    // static List<int> GetRoleClaimsFromAccessToken(string jwt)
    // {
    //     JwtSecurityTokenHandler handler = new();
    //     JwtSecurityToken? token = handler.ReadJwtToken(jwt);
    //     if (token == null)
    //     {
    //         Console.WriteLine("Handler failed to read token in GetRoleClaimsFromAccessToken");
    //         return new();
    //     }
    //     var claims = token.Claims;
    //     var rolesClaims = claims
    //         .Where(c => c.Type == "roles")
    //         .Select(c=>int.Parse(c.Value))
    //         .ToList();
    //     return rolesClaims;
    // }
}
