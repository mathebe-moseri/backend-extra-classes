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
            try { return LoginCore(req); }
            catch (Exception ex) { return StatusCode(500, new { success = false, message = ex.Message }); }
        }

        private IActionResult LoginCore(LoginRequest req)
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

        // ---------- Quiz attempts: learner saves one ----------
        [Authorize]
        [HttpPost("quiz_attempts")]
        public IActionResult SaveAttempt([FromBody] SaveAttemptRequest req)
        {
            if (req == null || req.Total <= 0 || req.Correct < 0 || req.Correct > req.Total)
                return BadRequest(new { success = false, message = "Invalid attempt." });
            if (req.Details != null && req.Details.Length > 200000)
                return BadRequest(new { success = false, message = "Attempt is too large." });

            int id = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            string quiz = string.IsNullOrWhiteSpace(req.Quiz) ? "Introduction to Trigonometry" : req.Quiz.Trim();
            if (quiz.Length > 100) quiz = quiz.Substring(0, 100);

            using var conn = new SqlConnection(Conn);
            conn.Open();
            using var cmd = new SqlCommand(
                @"INSERT INTO QuizAttempts (LearnerId, Quiz, ScorePercent, Correct, Total, Details)
                  VALUES (@L, @Q, @P, @C, @T, @D)", conn);
            cmd.Parameters.AddWithValue("@L", id);
            cmd.Parameters.AddWithValue("@Q", quiz);
            cmd.Parameters.AddWithValue("@P", (int)Math.Floor(req.Correct * 100.0 / req.Total));
            cmd.Parameters.AddWithValue("@C", req.Correct);
            cmd.Parameters.AddWithValue("@T", req.Total);
            cmd.Parameters.AddWithValue("@D", (object?)req.Details ?? DBNull.Value);
            cmd.ExecuteNonQuery();

            return Ok(new { success = true });
        }

        // ---------- Quiz attempts: learner reads their own ----------
        [Authorize]
        [HttpGet("quiz_attempts")]
        public IActionResult MyAttempts()
        {
            int id = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            return Ok(ReadAttempts(id));
        }

        // ---------- Quiz progress: learner saves answered questions (works for unfinished quizzes) ----------
        private static readonly string[] ProgressQuizzes = { "all", "patterns", "measurement", "algebra" };

        [Authorize]
        [HttpPost("quiz_progress")]
        public IActionResult SaveProgress([FromBody] SaveProgressRequest req)
        {
            if (req?.Items == null || req.Items.Count == 0)
                return Ok(new { success = true });
            if (req.Items.Count > 500)
                return BadRequest(new { success = false, message = "Too many items." });

            int id = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);

            using var conn = new SqlConnection(Conn);
            conn.Open();
            using var tx = conn.BeginTransaction();

            foreach (var it in req.Items)
            {
                string quiz = (it.Quiz ?? "").Trim().ToLower();
                string key = it.Key ?? "";
                if (!ProgressQuizzes.Contains(quiz) || key.Length == 0 || key.Length > 4000 || it.Total <= 0)
                    continue;   // skip anything invalid

                string topic = (it.Topic ?? "").Trim();
                if (topic.Length > 255) topic = topic.Substring(0, 255);

                string totals = System.Text.Json.JsonSerializer.Serialize(it.Totals ?? new Dictionary<string, int>());
                if (totals.Length > 20000) continue;

                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

                using var cmd = new SqlCommand(
                    @"MERGE QuizProgress WITH (HOLDLOCK) AS t
                      USING (SELECT @L AS LearnerId, @Q AS Quiz, @H AS KeyHash) AS s
                        ON t.LearnerId = s.LearnerId AND t.Quiz = s.Quiz AND t.KeyHash = s.KeyHash
                      WHEN MATCHED THEN
                        UPDATE SET Topic = @Topic, Ok = @Ok, Total = @T, Totals = @Totals, UpdatedAt = SYSUTCDATETIME()
                      WHEN NOT MATCHED THEN
                        INSERT (LearnerId, Quiz, KeyHash, QuestionKey, Topic, Ok, Total, Totals)
                        VALUES (@L, @Q, @H, @Key, @Topic, @Ok, @T, @Totals);", conn, tx);
                cmd.Parameters.AddWithValue("@L", id);
                cmd.Parameters.AddWithValue("@Q", quiz);
                cmd.Parameters.AddWithValue("@H", hash);
                cmd.Parameters.AddWithValue("@Key", key);
                cmd.Parameters.AddWithValue("@Topic", topic);
                cmd.Parameters.AddWithValue("@Ok", it.Ok);
                cmd.Parameters.AddWithValue("@T", it.Total);
                cmd.Parameters.AddWithValue("@Totals", totals);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return Ok(new { success = true });
        }

        // ---------- Quiz progress: learner reads their own ----------
        [Authorize]
        [HttpGet("quiz_progress")]
        public IActionResult MyProgress()
        {
            int id = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            var list = new List<object>();

            using var conn = new SqlConnection(Conn);
            conn.Open();
            using var cmd = new SqlCommand(
                @"SELECT Quiz, QuestionKey, Topic, Ok, Total, Totals
                  FROM QuizProgress WHERE LearnerId = @Id ORDER BY UpdatedAt", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new
                {
                    quiz = r.GetString(0),
                    key = r.GetString(1),
                    topic = r.GetString(2),
                    ok = r.GetBoolean(3),
                    total = r.GetInt32(4),
                    totals = r.GetString(5)
                });
            return Ok(list);
        }

        // ---------- Admin: every learner's progress at a glance ----------        
        [HttpGet("admin/learner_progress")]
        public IActionResult LearnerProgress([FromHeader(Name = "X-Admin-Key")] string? adminKey)
        {
            if (adminKey != _config["Admin:Key"])
                return Unauthorized();

            var list = new List<object>();
            using var conn = new SqlConnection(Conn);
            conn.Open();
            using var cmd = new SqlCommand(
                @"SELECT c.LearnerFirstName, c.LearnerSurname, c.Grade, c.Email,
                         COUNT(a.Id), MAX(a.ScorePercent), MAX(a.TakenAt),
                         (SELECT TOP 1 x.ScorePercent FROM QuizAttempts x
                          WHERE x.LearnerId = c.Id ORDER BY x.TakenAt DESC)
                  FROM ClassRegistrations c
                  JOIN QuizAttempts a ON a.LearnerId = c.Id
                  GROUP BY c.Id, c.LearnerFirstName, c.LearnerSurname, c.Grade, c.Email
                  ORDER BY MAX(a.TakenAt) DESC", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new
                {
                    firstName = r.GetString(0),
                    surname = r.GetString(1),
                    grade = r.GetString(2),
                    email = r.GetString(3),
                    attempts = r.GetInt32(4),
                    best = r.GetInt32(5),
                    lastDate = DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc),
                    latest = r.GetInt32(7)
                });
            return Ok(list);
        }

        // ---------- Admin: one learner's attempts (with wrong answers) ----------
        [HttpGet("admin/learner_attempts")]
        public IActionResult LearnerAttempts([FromHeader(Name = "X-Admin-Key")] string? adminKey, [FromQuery] string email)
        {
            if (adminKey != _config["Admin:Key"])
                return Unauthorized();

            int id = 0;
            using (var conn = new SqlConnection(Conn))
            {
                conn.Open();
                using var cmd = new SqlCommand("SELECT Id FROM ClassRegistrations WHERE Email = @Email", conn);
                cmd.Parameters.AddWithValue("@Email", (email ?? "").Trim());
                var o = cmd.ExecuteScalar();
                if (o != null) id = Convert.ToInt32(o);
            }
            if (id == 0) return NotFound(new { message = "No learner with that email." });
            return Ok(ReadAttempts(id));
        }

        // Last 20 attempts, oldest first (so the last one is the latest)
        private List<object> ReadAttempts(int learnerId)
        {
            var list = new List<object>();
            using var conn = new SqlConnection(Conn);
            conn.Open();
            using var cmd = new SqlCommand(
                @"SELECT TOP 20 Quiz, ScorePercent, Correct, Total, TakenAt, Details
                  FROM QuizAttempts WHERE LearnerId = @Id ORDER BY TakenAt DESC", conn);
            cmd.Parameters.AddWithValue("@Id", learnerId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new
                {
                    quiz = r.GetString(0),
                    percent = r.GetInt32(1),
                    correct = r.GetInt32(2),
                    total = r.GetInt32(3),
                    date = DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc),
                    details = r.IsDBNull(5) ? null : r.GetString(5)
                });
            list.Reverse();
            return list;
        }

        // ---------- Leaderboard: how a learner compares with everyone ----------
        private static readonly (string Id, string Name, string Pattern)[] BoardTopics =
        {
            ("parts",   "Parts of a triangle",          "parts of a"),
            ("types",   "Types of triangles",           "types"),
            ("rules",   "Rules of a triangle",          "rules"),
            ("compare", "Comparing triangles",          "compar|congru|similar"),
            ("trig",    "Introduction to trigonometry", "trigonometry")
        };

        private static void Keep(Dictionary<string, int> d, string key, int value)
        {
            if (!d.TryGetValue(key, out var old) || value > old) d[key] = value;
        }

        [Authorize]
        [HttpGet("leaderboard")]
        public IActionResult Leaderboard()
        {
            int me = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            var names = new Dictionary<int, string>();
            var best = new Dictionary<int, Dictionary<string, int>>(); // learner -> topic -> best %

            using var conn = new SqlConnection(Conn);
            conn.Open();

            using (var cmd = new SqlCommand(
                "SELECT Id, LearnerFirstName, LearnerSurname FROM ClassRegistrations WHERE AccessActivated = 1", conn))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    string sn = r.GetString(2);
                    // Other learners see "Thabo M." only. For full anonymity use: "Learner"
                    names[r.GetInt32(0)] = r.GetString(1) + (sn.Length > 0 ? " " + char.ToUpper(sn[0]) + "." : "");
                }

            using (var cmd = new SqlCommand("SELECT LearnerId, ScorePercent, Details, Quiz FROM QuizAttempts", conn)) using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    int lid = r.GetInt32(0);
                    if (!names.ContainsKey(lid)) continue;
                    if (!best.ContainsKey(lid)) best[lid] = new Dictionary<string, int>();
                    var d = best[lid];
                    if (r.GetString(3) == "Introduction to Trigonometry") Keep(d, "overall", r.GetInt32(1)); if (r.IsDBNull(2)) continue;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(r.GetString(2));
                        if (!doc.RootElement.TryGetProperty("byPart", out var parts)) continue;
                        foreach (var p in parts.EnumerateArray())
                        {
                            string pn = p.GetProperty("name").GetString() ?? "";
                            int total = p.GetProperty("total").GetInt32();
                            if (total <= 0) continue;
                            int pct = (int)Math.Round(p.GetProperty("correct").GetInt32() * 100.0 / total);
                            foreach (var t in BoardTopics)
                                if (System.Text.RegularExpressions.Regex.IsMatch(pn, t.Pattern,
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                                { Keep(d, t.Id, pct); break; }
                        }
                    }
                    catch { }
                }

            object Build(string id, string name)
            {
                var rows = best
                    .Where(kv => kv.Value.ContainsKey(id))
                    .Select(kv => new { Id = kv.Key, Score = kv.Value[id] })
                    .OrderByDescending(x => x.Score).ThenBy(x => x.Id).ToList();
                int n = rows.Count;
                int RankOf(int s) => rows.Count(x => x.Score > s) + 1;

                var board = rows.Take(5)
                    .Select(x => new { rank = RankOf(x.Score), name = names[x.Id], score = x.Score, isMe = x.Id == me })
                    .ToList();
                var mine = rows.FirstOrDefault(x => x.Id == me);
                if (mine != null && !board.Any(b => b.isMe))
                    board.Add(new { rank = RankOf(mine.Score), name = names[mine.Id], score = mine.Score, isMe = true });

                int beaten = mine == null ? 0 : rows.Count(x => x.Score < mine.Score);
                return new
                {
                    id,
                    name,
                    participants = n,
                    average = n == 0 ? 0 : (int)Math.Round(rows.Average(x => x.Score)),
                    top = n == 0 ? 0 : rows[0].Score,
                    myScore = mine == null ? (int?)null : mine.Score,
                    rank = mine == null ? (int?)null : RankOf(mine.Score),
                    beatPercent = mine != null && n > 1 ? (int)Math.Round(beaten * 100.0 / (n - 1)) : (int?)null,
                    board
                };
            }

            return Ok(new
            {
                overall = Build("overall", "Overall"),
                topics = BoardTopics.Select(t => Build(t.Id, t.Name)).ToList()
            });
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
        public class ProgressItem { public string? Quiz { get; set; } public string? Key { get; set; } public string? Topic { get; set; } public bool Ok { get; set; } public int Total { get; set; } public Dictionary<string, int>? Totals { get; set; } }
        public class SaveProgressRequest { public List<ProgressItem>? Items { get; set; } }
        public class SaveAttemptRequest { public string? Quiz { get; set; } public int Correct { get; set; } public int Total { get; set; } public string? Details { get; set; } }
    }
}