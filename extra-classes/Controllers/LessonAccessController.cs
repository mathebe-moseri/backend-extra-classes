using System;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace extra_classes.Controllers
{
    // Learners log in with their registered email + a 6-digit code sent to that email.
    // Only learners who have a row in the Enrollments table can log in or watch videos.
    //
    // Endpoints:
    //   POST /api/auth/request-code        { "email": "..." }
    //   POST /api/auth/verify-code         { "email": "...", "code": "123456" }
    //   GET  /api/lessons/{id}/video-url   (header: X-Session-Token)
    [ApiController]
    [Route("api")]
    public class LessonAccessController : ControllerBase
    {
        private const int CodeMinutes = 10;        // how long a login code works
        private const int MaxAttempts = 5;         // wrong guesses allowed per code
        private const int MaxCodesPerHour = 5;     // codes one learner can request per hour
        private const int SessionHours = 8;        // how long a login lasts
        private const int VideoLinkMinutes = 90;   // must be longer than your longest lesson

        private readonly IConfiguration _configuration;

        public LessonAccessController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private SqlConnection Db()
        {
            return new SqlConnection(_configuration.GetConnectionString("mathebe"));
        }

        // ---------------- 1. Ask for a login code ----------------
        [HttpPost("auth/request-code")]
        public async Task<IActionResult> RequestCode([FromBody] EmailRequest req)
        {
            // Same answer whether or not the email exists, so strangers can't test which emails are registered.
            const string generic = "If this email belongs to an approved learner, a login code has been sent. It expires in 10 minutes.";

            string email = (req?.Email ?? "").Trim();
            if (email.Length == 0)
                return BadRequest(new { success = false, message = "Please enter your email address." });

            string code;
            Learner learner;

            using (var conn = Db())
            {
                await conn.OpenAsync();

                learner = await FindApprovedLearner(conn, email);
                if (learner == null)
                    return Ok(new { success = true, message = generic });

                int recent = await Scalar(conn,
                    "SELECT COUNT(*) FROM LoginCodes WHERE RegistrationId = @Id AND CreatedAt > @Since",
                    ("@Id", learner.Id), ("@Since", DateTime.UtcNow.AddHours(-1)));
                if (recent >= MaxCodesPerHour)
                    return Ok(new { success = true, message = generic });

                // Cancel any older unused codes, then store the new one (only its hash is saved).
                await Exec(conn,
                    "UPDATE LoginCodes SET Used = 1 WHERE RegistrationId = @Id AND Used = 0",
                    ("@Id", learner.Id));

                code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");

                await Exec(conn,
                    "INSERT INTO LoginCodes (RegistrationId, CodeHash, ExpiresAt) VALUES (@Id, @Hash, @Expires)",
                    ("@Id", learner.Id),
                    ("@Hash", Sha256(code + ":" + learner.Id)),
                    ("@Expires", DateTime.UtcNow.AddMinutes(CodeMinutes)));
            }

            string body =
                $"Hi {learner.FirstName},\n\n" +
                $"Your Sesi Mathebe login code is: {code}\n\n" +
                $"It expires in {CodeMinutes} minutes. If you did not ask for this code, you can ignore this email.\n\n" +
                $"The Sesi Mathebe Team";

            bool sent = await SendEmail(learner.Email, "Your Sesi Mathebe login code", body);
            if (!sent)
                Console.WriteLine($"Login code could not be emailed to registration {learner.Id}.");

            return Ok(new { success = true, message = generic });
        }

        // ---------------- 2. Check the code and log in ----------------
        [HttpPost("auth/verify-code")]
        public async Task<IActionResult> VerifyCode([FromBody] VerifyRequest req)
        {
            const string fail = "That code is not correct or has expired. Please request a new one.";

            string email = (req?.Email ?? "").Trim();
            string code = (req?.Code ?? "").Trim();
            if (email.Length == 0 || code.Length == 0)
                return Unauthorized(new { success = false, message = fail });

            using (var conn = Db())
            {
                await conn.OpenAsync();

                Learner learner = await FindApprovedLearner(conn, email);
                if (learner == null)
                    return Unauthorized(new { success = false, message = fail });

                int codeId = 0;
                int attempts = 0;
                string storedHash = null;

                using (var cmd = new SqlCommand(
                    @"SELECT TOP 1 Id, CodeHash, Attempts FROM LoginCodes
                      WHERE RegistrationId = @Id AND Used = 0 AND ExpiresAt > @Now
                      ORDER BY Id DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", learner.Id);
                    cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow);
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        if (await rd.ReadAsync())
                        {
                            codeId = rd.GetInt32(0);
                            storedHash = rd.GetString(1);
                            attempts = rd.GetInt32(2);
                        }
                    }
                }

                if (codeId == 0 || attempts >= MaxAttempts)
                    return Unauthorized(new { success = false, message = fail });

                // Count this try before checking it.
                await Exec(conn, "UPDATE LoginCodes SET Attempts = Attempts + 1 WHERE Id = @C", ("@C", codeId));

                string candidate = Sha256(code + ":" + learner.Id);
                bool correct = CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(storedHash));
                if (!correct)
                    return Unauthorized(new { success = false, message = fail });

                // Correct: use up the code and start a login session.
                await Exec(conn, "UPDATE LoginCodes SET Used = 1 WHERE Id = @C", ("@C", codeId));
                await Exec(conn, "DELETE FROM Sessions WHERE ExpiresAt < @Now", ("@Now", DateTime.UtcNow));

                string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .Replace('+', '-').Replace('/', '_').TrimEnd('=');

                await Exec(conn,
                    "INSERT INTO Sessions (RegistrationId, TokenHash, ExpiresAt) VALUES (@R, @H, @E)",
                    ("@R", learner.Id),
                    ("@H", Sha256(token)),
                    ("@E", DateTime.UtcNow.AddHours(SessionHours)));

                return Ok(new { success = true, token, firstName = learner.FirstName, email = learner.Email });
            }
        }

        // ---------------- 3. Get a short-lived link to watch a lesson ----------------
        [HttpGet("lessons/{lessonId:int}/video-url")]
        public async Task<IActionResult> GetVideoUrl(int lessonId)
        {
            string token = Request.Headers["X-Session-Token"].ToString();
            if (string.IsNullOrWhiteSpace(token))
                return Unauthorized(new { message = "Please log in." });

            using (var conn = Db())
            {
                await conn.OpenAsync();

                // Who is logged in?
                int registrationId = 0;
                string email = "";
                using (var cmd = new SqlCommand(
                    @"SELECT s.RegistrationId, r.Email
                      FROM Sessions s
                      JOIN ClassRegistrations r ON r.Id = s.RegistrationId
                      WHERE s.TokenHash = @H AND s.ExpiresAt > @Now", conn))
                {
                    cmd.Parameters.AddWithValue("@H", Sha256(token.Trim()));
                    cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow);
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        if (await rd.ReadAsync())
                        {
                            registrationId = rd.GetInt32(0);
                            email = Convert.ToString(rd[1]);
                        }
                    }
                }
                if (registrationId == 0)
                    return Unauthorized(new { message = "Your session has expired. Please log in again." });

                // Which video is this lesson?
                int courseId = 0;
                string videoKey = null;
                using (var cmd = new SqlCommand("SELECT CourseId, VideoKey FROM Lessons WHERE Id = @Id", conn))
                {
                    cmd.Parameters.AddWithValue("@Id", lessonId);
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        if (await rd.ReadAsync())
                        {
                            courseId = rd.GetInt32(0);
                            videoKey = rd.GetString(1);
                        }
                    }
                }
                if (videoKey == null)
                    return NotFound(new { message = "Lesson not found." });

                // Is this learner approved for this course?
                int approved = await Scalar(conn,
                    "SELECT COUNT(*) FROM Enrollments WHERE RegistrationId = @R AND CourseId = @C",
                    ("@R", registrationId), ("@C", courseId));
                if (approved == 0)
                    return StatusCode(403, new { message = "You do not have access to this lesson." });

                return Ok(new { url = CreateVideoLink(videoKey), watermark = email });
            }
        }

        // ---------------- Helpers ----------------

        // A learner counts as approved only if they have at least one Enrollments row.
        private static async Task<Learner> FindApprovedLearner(SqlConnection conn, string email)
        {
            using (var cmd = new SqlCommand(
                @"SELECT TOP 1 r.Id, r.LearnerFirstName, r.Email
                  FROM ClassRegistrations r
                  WHERE r.Email = @Email
                    AND EXISTS (SELECT 1 FROM Enrollments e WHERE e.RegistrationId = r.Id)
                  ORDER BY r.Id DESC", conn))
            {
                cmd.Parameters.AddWithValue("@Email", email);
                using (var rd = await cmd.ExecuteReaderAsync())
                {
                    if (!await rd.ReadAsync()) return null;
                    return new Learner
                    {
                        Id = rd.GetInt32(0),
                        FirstName = Convert.ToString(rd[1]),
                        Email = Convert.ToString(rd[2])
                    };
                }
            }
        }

        private string CreateVideoLink(string videoKey)
        {
            var s3 = new AmazonS3Client(
                _configuration["R2:AccessKeyId"],
                _configuration["R2:SecretAccessKey"],
                new AmazonS3Config
                {
                    ServiceURL = $"https://{_configuration["R2:AccountId"]}.r2.cloudflarestorage.com",
                    AuthenticationRegion = "auto",
                    ForcePathStyle = true
                });

            return s3.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _configuration["R2:Bucket"],
                Key = videoKey,
                Verb = HttpVerb.GET,
                Protocol = Protocol.HTTPS,
                Expires = DateTime.UtcNow.AddMinutes(VideoLinkMinutes)
            });
        }

        private async Task<bool> SendEmail(string to, string subject, string body)
        {
            try
            {
                string from = _configuration["Gmail:Address"] ?? "mathebemoseri@gmail.com";
                string appPassword = _configuration["Gmail:AppPassword"];
                if (string.IsNullOrEmpty(appPassword))
                {
                    Console.WriteLine("Email error: Gmail:AppPassword is not configured.");
                    return false;
                }

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Sesi Mathebe", from));
                message.To.Add(MailboxAddress.Parse(to));
                message.Subject = subject;
                message.Body = new TextPart("plain") { Text = body };

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(from, appPassword);
                await smtp.SendAsync(message);
                await smtp.DisconnectAsync(true);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Email error: {ex.Message}");
                return false;
            }
        }

        private static string Sha256(string text)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }

        private static async Task<int> Exec(SqlConnection conn, string sql, params (string Name, object Value)[] ps)
        {
            using (var cmd = new SqlCommand(sql, conn))
            {
                foreach (var p in ps) cmd.Parameters.AddWithValue(p.Name, p.Value);
                return await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task<int> Scalar(SqlConnection conn, string sql, params (string Name, object Value)[] ps)
        {
            using (var cmd = new SqlCommand(sql, conn))
            {
                foreach (var p in ps) cmd.Parameters.AddWithValue(p.Name, p.Value);
                return Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
        }

        // ---------------- Models ----------------
        public class EmailRequest
        {
            public string Email { get; set; }
        }

        public class VerifyRequest
        {
            public string Email { get; set; }
            public string Code { get; set; }
        }

        private class Learner
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
            public string Email { get; set; }
        }
    }
}
