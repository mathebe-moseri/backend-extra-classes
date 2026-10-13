using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace extra_classes.Controllers
{
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly extra_classes.Services.EmailService _email;

        public AuthController(IConfiguration config, extra_classes.Services.EmailService email)
        {
            _config = config;
            _email = email;
        }

        private string Conn => _config.GetConnectionString("mathebe");

        // ---------- Login ----------
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { success = false, message = "Email and password are required." });

            int id = 0; string first = "", hash = null; bool approved = false, mustChange = false;

            using (var conn = new SqlConnection(Conn))
            {
                conn.Open();
                using var cmd = new SqlCommand(
@"SELECT Id, LearnerFirstName, PasswordHash, AccessActivated, MustChangePassword
                      FROM ClassRegistrations WHERE Email = @Email", conn);
                cmd.Parameters.AddWithValue("@Email", req.Email.Trim());
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    id = r.GetInt32(0);
                    first = r.GetString(1);
                    hash = r.IsDBNull(2) ? null : r.GetString(2);
                    approved = r.GetBoolean(3);
                    mustChange = r.GetBoolean(4);
                }
            }

            // Same message for unknown email and wrong password
            if (id == 0 || hash == null || !BCrypt.Net.BCrypt.Verify(req.Password, hash))
                return Unauthorized(new { success = false, message = "Incorrect email or password." });

            if (!approved)
                return Unauthorized(new { success = false, message = "Your registration is still being verified." });

            return Ok(new { success = true, token = CreateToken(id, req.Email.Trim(), first), mustChangePassword = mustChange });
        }

        // ---------- Dashboard data ----------
        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            int id = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);

            using var conn = new SqlConnection(Conn);
            conn.Open();
            using var cmd = new SqlCommand(
                @"SELECT LearnerFirstName, LearnerSurname, Grade, Email, SchoolName
                  FROM ClassRegistrations WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return NotFound();

            return Ok(new
            {
                firstName = r.GetString(0),
                surname = r.GetString(1),
                grade = r.GetString(2),
                email = r.GetString(3),
                school = r.GetString(4)
            });
        }

        // ---------- Change password ----------
        [Authorize]
        [HttpPost("change_password")]
        public IActionResult ChangePassword([FromBody] ChangePasswordRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
                return BadRequest(new { success = false, message = "Password must be at least 8 characters." });

            int id = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);

            using var conn = new SqlConnection(Conn);
            conn.Open();
            using var cmd = new SqlCommand(
                "UPDATE ClassRegistrations SET PasswordHash = @H, MustChangePassword = 0 WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@H", BCrypt.Net.BCrypt.HashPassword(req.NewPassword));
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();

            return Ok(new { success = true });
        }

        // ---------- Admin: approve a learner (call from Postman) ----------
        [HttpPost("admin/approve_learner")]
        public IActionResult Approve([FromHeader(Name = "X-Admin-Key")] string adminKey, [FromBody] ApproveRequest req)
        {
            if (adminKey != _config["Admin:Key"])
                return Unauthorized();

            string temp = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)); // e.g. 9F3A12BC

            using var conn = new SqlConnection(Conn);
            conn.Open();
            using var cmd = new SqlCommand(
                @"UPDATE ClassRegistrations
SET PasswordHash = @H, AccessActivated = 1, PaymentVerified = 1,
                      ContractReceived = 1, ReportCardReceived = 1, MustChangePassword = 1
                  WHERE Email = @Email", conn);
            cmd.Parameters.AddWithValue("@H", BCrypt.Net.BCrypt.HashPassword(temp));
            cmd.Parameters.AddWithValue("@Email", req.Email);
            int rows = cmd.ExecuteNonQuery();

            if (rows == 0) return NotFound(new { message = "No learner with that email." });
            return Ok(new { email = req.Email, tempPassword = temp });
        }

        // ---------- Admin: record a verified document; activates the learner when all 3 are in ----------
        [HttpPost("admin/mark_document")]
        public async Task<IActionResult> MarkDocument([FromHeader(Name = "X-Admin-Key")] string? adminKey, [FromBody] MarkDocumentRequest req)
        {
            if (adminKey != _config["Admin:Key"])
                return Unauthorized();

            string? column = req.Document?.ToLower() switch
            {
                "payment" => "PaymentVerified",
                "contract" => "ContractReceived",
                "reportcard" => "ReportCardReceived",
                _ => null
            };
            if (column == null)
                return BadRequest(new { message = "Document must be payment, contract or reportcard." });

            string first = "", temp = "";
            bool allIn, alreadyActive;

            using (var conn = new SqlConnection(Conn))
            {
                conn.Open();

                using (var upd = new SqlCommand($"UPDATE ClassRegistrations SET {column} = 1 WHERE Email = @Email", conn))
                {
                    upd.Parameters.AddWithValue("@Email", req.Email);
                    if (upd.ExecuteNonQuery() == 0)
                        return NotFound(new { message = "No learner with that email." });
                }

                using (var sel = new SqlCommand(
                    @"SELECT LearnerFirstName, PaymentVerified, ContractReceived, ReportCardReceived, AccessActivated
                      FROM ClassRegistrations WHERE Email = @Email", conn))
                {
                    sel.Parameters.AddWithValue("@Email", req.Email);
                    using var r = sel.ExecuteReader();
                    r.Read();
                    first = r.GetString(0);
                    allIn = r.GetBoolean(1) && r.GetBoolean(2) && r.GetBoolean(3);
                    alreadyActive = r.GetBoolean(4);
                }

                if (!allIn)
                    return Ok(new { activated = false, message = "Document recorded. Still waiting for the other documents." });

                if (alreadyActive)
                    return Ok(new { activated = true, emailSent = false, message = "Learner is already activated." });

                temp = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));

                using (var act = new SqlCommand(
                    @"UPDATE ClassRegistrations
                      SET PasswordHash = @H, AccessActivated = 1, MustChangePassword = 1
                      WHERE Email = @Email", conn))
                {
                    act.Parameters.AddWithValue("@H", BCrypt.Net.BCrypt.HashPassword(temp));
                    act.Parameters.AddWithValue("@Email", req.Email);
                    act.ExecuteNonQuery();
                }
            }

            bool sent = await SendAccessEmail(req.Email, first, temp);

            // If the email fails, the temporary password is returned so you can pass it on yourself.
            return Ok(new { activated = true, emailSent = sent, tempPassword = sent ? null : temp });
        }

        private Task<bool> SendAccessEmail(string toEmail, string firstName, string tempPassword)
        {
            string body =
                $"Dear {firstName},\n\n" +
                "Great news! We have received and verified your proof of payment, signed contract and school report card. " +
                "Your access to Sesi Mathebe Remote Extra Classes is now active.\n\n" +
                "Your login details:\n" +
                $"Email: {toEmail}\n" +
                $"Temporary password: {tempPassword}\n\n" +
                "To log in, open our website and click the Log in button. You will be asked to choose your own password the first time you log in.\n\n" +
                "If you have any questions, reply to this email.\n\n" +
                "Best regards,\nThe SM Remote Extra Classes Team";

            return _email.SendAsync(toEmail, "Your access to Sesi Mathebe Remote Extra Classes is active", body);
        }

        private string CreateToken(int id, string email, string firstName)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                    new Claim(ClaimTypes.Email, email),
                    new Claim(ClaimTypes.Name, firstName)
                },
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public class LoginRequest { public string Email { get; set; } public string Password { get; set; } }
        public class ChangePasswordRequest { public string NewPassword { get; set; } }
        public class ApproveRequest { public string Email { get; set; } }
        public class MarkDocumentRequest { public string Email { get; set; } public string Document { get; set; } }
    }
}